using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Customer,Staff,Manager,Admin")]
public class WalletController(ApplicationDbContext db, IWalletService wallet, ILogger<WalletController> logger, IConfiguration configuration) : Controller
{
    private readonly BankTransferSettings transferSettings = new(configuration);
    [HttpGet]
    public async Task<IActionResult> Index(string? customerId, int page = 1, string? requestId = null, string? referenceCode = null, string paymentMethod = "Cash")
    {
        bool canTopUp = User.IsInRole("Staff") || User.IsInRole("Manager") || User.IsInRole("Admin");
        if (!canTopUp) customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(customerId)) return RedirectToAction("Index", "CustomerManagement");
        var user = await db.Users.AsNoTracking().Include(u => u.Tier).SingleOrDefaultAsync(u => u.Id == customerId);
        var roleId = await db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync();
        if (user == null || !await db.UserRoles.AnyAsync(r => r.UserId == customerId && r.RoleId == roleId)) return NotFound();
        page = Math.Clamp(page, 1, 100000);
        var rows = await db.WalletTransactions.AsNoTracking().Where(w => w.CustomerId == customerId)
            .OrderByDescending(w => w.Id).Skip((page - 1) * 25).Take(26).ToListAsync();
        var transfers = await db.WalletTopUpIntents.AsNoTracking().Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.Id).Take(10).ToListAsync();
        var selected = referenceCode == null ? null : await db.WalletTopUpIntents.AsNoTracking()
            .SingleOrDefaultAsync(i => i.CustomerId == customerId && i.ReferenceCode == referenceCode);
        var actorIds = rows.Select(r => r.CreatedById)
            .Concat(transfers.Select(i => i.ConfirmedById ?? "")).Append(selected?.ConfirmedById ?? "").Distinct().ToList();
        var actorNames = canTopUp ? await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName) : new Dictionary<string, string>();
        return View(new WalletViewModel
        {
            TransferEnabled = TransferEnabled,
            AutomaticConfirmationConfigured = transferSettings.CanConfirmAutomatically,
            BankCode = transferSettings.BankCode,
            AccountNumber = transferSettings.AccountNumber,
            AccountName = transferSettings.AccountName,
            QrBaseUrl = transferSettings.QrBaseUrl,
            SelectedTransfer = selected, RecentTransfers = transfers,
            PaymentMethod = paymentMethod == "Transfer" || selected != null ? "Transfer" : "Cash",
            RequestId = Guid.TryParseExact(requestId, "N", out var request) ? request.ToString("N") : Guid.NewGuid().ToString("N"),
            CustomerId = user.Id, CustomerName = user.FullName, Balance = user.WalletBalance,
            TierName = user.Tier?.TierName ?? "Member", RewardPoints = user.RewardPoints,
            PointMultiplier = user.Tier?.PointMultiplier ?? 1, CanTopUp = canTopUp && user.IsActive,
            ActorNames = actorNames, Transactions = rows.Take(25).ToList(), Page = page, HasNextPage = rows.Count > 25
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Staff,Manager,Admin")]
    public async Task<IActionResult> TopUp(string customerId, decimal amount, string requestId, bool receivedCash, string paymentMethod = "Cash")
    {
        if (!ModelState.IsValid || (paymentMethod != "Cash" && paymentMethod != "Transfer") || (paymentMethod == "Cash" && !receivedCash))
        {
            TempData["Error"] = "Nhập số tiền hợp lệ và xác nhận đã nhận đủ tiền mặt.";
            return RedirectToAction(nameof(Index), new { customerId, requestId, paymentMethod });
        }
        try
        {
            if (paymentMethod == "Transfer" && !TransferEnabled)
            {
                TempData["Error"] = "Chưa cấu hình ngân hàng và số tài khoản nhận tiền để tạo QR.";
                return RedirectToAction(nameof(Index), new { customerId, requestId, paymentMethod });
            }
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = paymentMethod == "Transfer"
                ? await wallet.CreateTransferIntentAsync(customerId, amount, requestId, actorId)
                : await wallet.TopUpAsync(customerId, amount, requestId, actorId);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            if (result.Success) return RedirectToAction(nameof(Index), new { customerId, referenceCode = result.ReferenceCode, paymentMethod });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không thể nạp ví. RequestId={RequestId}", requestId);
            TempData["Error"] = "Chưa ghi nhận phiếu nạp. Vui lòng kiểm tra lịch sử trước khi thử lại.";
        }
        return RedirectToAction(nameof(Index), new { customerId, requestId, paymentMethod });
    }

    private bool TransferEnabled => transferSettings.CanCreateQr;

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Staff,Manager,Admin")]
    public async Task<IActionResult> ConfirmTransfer(string referenceCode, bool receivedTransfer)
    {
        var reference = (referenceCode ?? "").Trim().ToUpperInvariant();
        var intent = await db.WalletTopUpIntents.AsNoTracking().SingleOrDefaultAsync(i => i.ReferenceCode == reference);
        if (intent == null) return NotFound();
        if (!ModelState.IsValid || !receivedTransfer)
        {
            TempData["Error"] = "Kiểm tra tiền vào tài khoản nhận và số tiền thực nhận, rồi tích ô xác nhận.";
        }
        else
        {
            try
            {
                var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
                var result = await wallet.ConfirmTransferManuallyAsync(reference, intent.ExpectedAmount, actorId);
                TempData[result.Success ? "Success" : "Error"] = result.Message;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể xác nhận nạp chuyển khoản. ReferenceCode={ReferenceCode}", reference);
                TempData["Error"] = "Chưa xác nhận được giao dịch. Kiểm tra lịch sử trước khi thử lại.";
            }
        }
        return RedirectToAction(nameof(Index), new { customerId = intent.CustomerId, referenceCode = reference, paymentMethod = "Transfer" });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TransferStatus(string referenceCode)
    {
        var query = db.WalletTopUpIntents.AsNoTracking().Where(i => i.ReferenceCode == referenceCode);
        if (!(User.IsInRole("Staff") || User.IsInRole("Manager") || User.IsInRole("Admin")))
        {
            var ownId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            query = query.Where(i => i.CustomerId == ownId);
        }
        var intent = await query.SingleOrDefaultAsync();
        if (intent == null) return NotFound();
        var status = intent.Status == PaymentIntent.StatusExpired ? PaymentIntent.StatusPending : intent.Status;
        return Json(new { status });
    }
}
