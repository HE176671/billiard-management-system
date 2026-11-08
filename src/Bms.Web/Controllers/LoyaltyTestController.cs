using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Admin")]
public class LoyaltyTestController : Controller
{
    private const string PlaceholderEmailDomain = "@noemail.bms.local";

    private readonly ApplicationDbContext _context;
    private readonly ILoyaltyService _loyaltyService;
    private readonly IPointLedgerService _ledger;
    private readonly ITierEvaluationService _tierEval;
    private readonly ICheckoutService _checkout;

    public LoyaltyTestController(
        ApplicationDbContext context,
        ILoyaltyService loyaltyService,
        IPointLedgerService ledger,
        ITierEvaluationService tierEval,
        ICheckoutService checkout)
    {
        _context = context;
        _loyaltyService = loyaltyService;
        _ledger = ledger;
        _tierEval = tierEval;
        _checkout = checkout;
    }

    private string? CurrentActorId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /LoyaltyTest hoặc /CashierSimulator
    [HttpGet("/LoyaltyTest")]
    [HttpGet("/LoyaltyTest/Index")]
    [HttpGet("/CashierSimulator")]
    [HttpGet("/CashierSimulator/Index")]
    public async Task<IActionResult> Index(
        string? customerId = null,
        string? customerMode = null,
        int? sessionId = null,
        decimal playtimeAmount = 300000m,
        decimal fandBAmount = 80000m,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string? referenceCode = null,
        string paymentMethod = "VietQR",
        decimal? cashReceived = null)
    {
        var roleId = await _context.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync();

        var customers = await _context.Users
            .AsNoTracking()
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
            .OrderBy(u => u.FullName)
            .Select(u => new CustomerSelectItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                Email = u.Email != null && !u.Email.EndsWith(PlaceholderEmailDomain) ? u.Email : null,
                DateOfBirth = u.DateOfBirth,
                CardCode = u.EmployeeCode,
                TierName = u.Tier != null ? u.Tier.TierName : "Member",
                RewardPoints = u.RewardPoints
            })
            .ToListAsync();

        // Tải danh sách phiên chơi từ bảng PlaySessions + Orders/OrderDetails (Tích hợp vận hành bàn & thực đơn F&B)
        var activeSessions = await LoadCheckoutSessionsAsync(sessionId);
        CheckoutSessionItemViewModel? selectedSession = null;
        var sessionOrderLines = new List<SessionOrderLineViewModel>();

        if (sessionId.HasValue && sessionId.Value > 0)
        {
            selectedSession = activeSessions.FirstOrDefault(s => s.SessionId == sessionId.Value);
            if (selectedSession != null)
            {
                playtimeAmount = selectedSession.PlaytimeAmount;
                fandBAmount = selectedSession.FandBAmount;
            }
            sessionOrderLines = await LoadSessionOrderLinesAsync(sessionId.Value);
        }

        string resolvedMode = string.Equals(customerMode, "guest", StringComparison.OrdinalIgnoreCase)
            ? "guest"
            : "member";

        if (resolvedMode == "member" && string.IsNullOrEmpty(customerId) && customers.Count > 0)
        {
            customerId = customers[0].Id;
        }
        else if (resolvedMode == "guest")
        {
            customerId = null;
        }

        SelectedCustomerDetailViewModel? detail = null;
        var availableVouchers = new List<CustomerVoucher>();
        CheckoutLoyaltyPreview? preview = null;
        var refCode = string.IsNullOrWhiteSpace(referenceCode)
            ? $"HD-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : referenceCode.Trim();
        var pendingIntent = await _context.PaymentIntents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReferenceCode == refCode && x.Status == PaymentIntent.StatusPending);

        if (resolvedMode == "member" && !string.IsNullOrEmpty(customerId))
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Tier)
                .FirstOrDefaultAsync(u => u.Id == customerId);

            if (user != null)
            {
                var spend12M = await _tierEval.GetSpendLast12MonthsAsync(customerId);
                var transactions = await _context.PointTransactions
                    .AsNoTracking()
                    .Where(t => t.CustomerId == customerId)
                    .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                    .Take(10)
                    .Select(t => new PointTransactionRowViewModel
                    {
                        CreatedAt = t.CreatedAt,
                        TransactionType = t.TransactionType,
                        Amount = t.Amount,
                        BalanceAfter = t.BalanceAfter,
                        ReferenceCode = t.ReferenceCode,
                        Reason = t.Reason
                    })
                    .ToListAsync();

                detail = new SelectedCustomerDetailViewModel
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    PhoneNumber = user.PhoneNumber,
                    Email = user.Email != null && !user.Email.EndsWith(PlaceholderEmailDomain) ? user.Email : null,
                    DateOfBirth = user.DateOfBirth,
                    CardCode = user.EmployeeCode,
                    TierName = user.Tier?.TierName ?? "Member",
                    PointMultiplier = user.Tier?.PointMultiplier ?? 1,
                    DiscountPercent = user.Tier?.DiscountPercent ?? 0,
                    RewardPoints = user.RewardPoints,
                    TotalSpend = Math.Max(user.TotalSpend, spend12M),
                    Spend12Months = Math.Max(user.TotalSpend, spend12M),
                    LastPaidDate = user.LastPaidDate,
                    RecentTransactions = transactions
                };

                var now = DateTime.UtcNow;
                availableVouchers = await _context.CustomerVouchers.AsNoTracking()
                    .Include(v => v.RewardItem)
                        .ThenInclude(r => r!.MinTier)
                    .Where(v => v.CustomerId == customerId
                             && v.RewardItemId != null
                             && v.RewardItem != null
                             && v.RewardItem.IsActive
                             && v.Status == CustomerVoucher.StatusActive
                             && v.ExpiresAtUtc > now)
                    .OrderBy(v => v.ExpiresAtUtc)
                    .ToListAsync();

                preview = await _loyaltyService.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
                    CustomerId: customerId,
                    ReferenceCode: refCode,
                    InvoiceId: null,
                    PlaytimeAmount: playtimeAmount,
                    FandBAmount: fandBAmount,
                    PointsToRedeem: pointsToRedeem,
                    VoucherCode: voucherCode,
                    CashierId: CurrentActorId));
            }
        }

        var customerNotifications = new List<SimulatedNotificationItemViewModel>();
        if (!string.IsNullOrEmpty(customerId))
        {
            customerNotifications = await _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == customerId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new SimulatedNotificationItemViewModel
                {
                    Id = n.Id,
                    Title = n.Title,
                    Content = n.Content,
                    Type = n.Type,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();
        }

        decimal finalAmt = preview?.FinalAmount ?? Math.Max(0m, playtimeAmount + fandBAmount);
        string normalizedMethod = string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase) ? "Cash" : "VietQR";

        var vm = new LoyaltyTestViewModel
        {
            CustomerMode = resolvedMode,
            SelectedCustomerId = customerId,
            Customers = customers,
            CustomerDetail = detail,
            SelectedSessionId = sessionId,
            ActiveSessions = activeSessions,
            SelectedSession = selectedSession,
            SessionOrderLines = sessionOrderLines,
            ReferenceCode = refCode,
            PlaytimeAmount = playtimeAmount,
            FandBAmount = fandBAmount,
            PointsToRedeem = pointsToRedeem,
            VoucherCode = voucherCode,
            AvailableVouchers = availableVouchers,
            Preview = preview,
            PaymentMethod = normalizedMethod,
            CashReceived = cashReceived ?? finalAmt,
            CustomerNotifications = customerNotifications,
            HasPendingTransfer = pendingIntent != null,
            PendingTransferExpiresAtUtc = pendingIntent?.ExpiresAtUtc
        };

        return View(vm);
    }

    // GET /CashierSimulator/Preview (AJAX JSON cho receipt live preview — hỗ trợ cả Hội viên & Khách lẻ)
    [HttpGet("/CashierSimulator/Preview")]
    [HttpGet("/LoyaltyTest/Preview")]
    public async Task<IActionResult> Preview(
        string? customerId,
        decimal playtimeAmount,
        decimal fandBAmount,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string? referenceCode = null,
        string customerMode = "member")
    {
        decimal subtotal = Math.Max(0m, playtimeAmount) + Math.Max(0m, fandBAmount);

        if (string.Equals(customerMode, "guest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(customerId))
        {
            var guestErrors = new List<string>();
            if (playtimeAmount < 0 || fandBAmount < 0)
            {
                guestErrors.Add("Tiền giờ chơi và tiền F&B phải là số từ 0 VND trở lên (không được nhập số âm).");
            }
            if (!string.IsNullOrWhiteSpace(voucherCode))
            {
                guestErrors.Add($"Khách lẻ không thể sử dụng mã voucher hội viên ({voucherCode.Trim().ToUpperInvariant()}). Vui lòng chọn chế độ Khách hội viên.");
            }
            return Json(new
            {
                isValid = guestErrors.Count == 0,
                errors = guestErrors,
                tierName = "Khách lẻ",
                tierDiscountPercent = 0m,
                subtotal,
                tierDiscountAmount = 0m,
                voucherDiscountAmount = 0m,
                pointsDiscountAmount = 0m,
                maxRedeemablePoints = 0,
                finalAmount = subtotal,
                pointsToEarn = 0,
                willUpgradeTier = false,
                nextTierName = (string?)null,
                spend12MonthsAfter = 0m,
                amountNeededForNextTier = 0m
            });
        }

        var res = await _loyaltyService.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId,
            ReferenceCode: referenceCode ?? "PREVIEW",
            InvoiceId: null,
            PlaytimeAmount: playtimeAmount,
            FandBAmount: fandBAmount,
            PointsToRedeem: pointsToRedeem,
            VoucherCode: string.IsNullOrWhiteSpace(voucherCode) ? null : voucherCode,
            CashierId: CurrentActorId));

        var user = await _context.Users.AsNoTracking()
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.Id == customerId);
        var spend12M = user != null ? Math.Max(user.TotalSpend, await _tierEval.GetSpendLast12MonthsAsync(user.Id)) : 0m;
        var nowUtc = DateTime.UtcNow;
        decimal userTierThreshold = user?.Tier?.SpendThreshold ?? 0m;
        var rawVouchers = user != null
            ? await _context.CustomerVouchers.AsNoTracking()
                .Include(v => v.RewardItem)
                    .ThenInclude(r => r!.MinTier)
                .Where(v => v.CustomerId == user.Id
                         && v.RewardItemId != null
                         && v.RewardItem != null
                         && v.RewardItem.IsActive
                         && v.Status == CustomerVoucher.StatusActive
                         && v.ExpiresAtUtc > nowUtc)
                .OrderBy(v => v.ExpiresAtUtc)
                .ToListAsync()
            : new List<CustomerVoucher>();

        var vouchers = rawVouchers
            .GroupBy(v => v.RewardItem!.Code)
            .Select(group => group.OrderBy(v => v.ExpiresAtUtc).First())
            .Select(v =>
        {
            var minTier = v.RewardItem?.MinTier;
            bool eligible = minTier == null || userTierThreshold >= minTier.SpendThreshold;
            string tierNote = minTier != null
                ? (eligible ? $" · Hạng {minTier.TierName}+" : $" · ⚠️ Yêu cầu Hạng {minTier.TierName} (Chưa đủ hạng)")
                : "";
            return new
            {
                code = v.RewardItem!.Code,
                minTierName = minTier?.TierName,
                isEligible = eligible,
                label = $"{v.RewardItem.Code} — {v.RewardItem.Name} ({(v.RewardItem.DiscountValue.HasValue ? "-" + VnFormat.Money(v.RewardItem.DiscountValue.Value) : VnFormat.RewardTypeLabel(v.RewardItem.RewardType))}{tierNote})"
            };
        }).ToList();

        return Json(new
        {
            isValid = res.IsValid,
            errors = res.Errors,
            customerId = user?.Id ?? customerId,
            customerName = user?.FullName ?? "—",
            customerPhone = user?.PhoneNumber ?? "—",
            rewardPoints = user?.RewardPoints ?? 0,
            spend12Months = spend12M,
            pointMultiplier = user?.Tier?.PointMultiplier ?? 1,
            tierBadgeStyle = VnFormat.TierBadgeStyle(res.TierName),
            availableVouchers = vouchers,
            tierName = res.TierName,
            tierDiscountPercent = res.TierDiscountPercent,
            subtotal = playtimeAmount + fandBAmount,
            tierDiscountAmount = res.TierDiscountAmount,
            voucherDiscountAmount = res.VoucherDiscountAmount,
            pointsDiscountAmount = res.PointsDiscountAmount,
            maxRedeemablePoints = res.MaxRedeemablePoints,
            finalAmount = res.FinalAmount,
            pointsToEarn = res.PointsToEarn,
            willUpgradeTier = res.WillUpgradeTier,
            nextTierName = res.NextTierName,
            spend12MonthsAfter = res.Spend12MonthsAfter,
            amountNeededForNextTier = res.AmountNeededForNextTier
        });
    }

    // POST /CashierSimulator/CommitCheckout (Thanh toán trọn gói F3: Hội viên hoặc Khách lẻ, VietQR hoặc Tiền mặt)
    [HttpPost("/CashierSimulator/CommitCheckout")]
    [HttpPost("/LoyaltyTest/CommitCheckout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CommitCheckout(
        string? customerId,
        string referenceCode,
        decimal playtimeAmount,
        decimal fandBAmount,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string customerMode = "member",
        string paymentMethod = "VietQR",
        decimal? cashReceived = null,
        int? sessionId = null)
    {
        bool isGuest = string.Equals(customerMode, "guest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(customerId);
        bool isCash = string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);
        bool hasFractionalMoney = playtimeAmount != decimal.Truncate(playtimeAmount)
            || fandBAmount != decimal.Truncate(fandBAmount)
            || (cashReceived.HasValue && cashReceived.Value != decimal.Truncate(cashReceived.Value));

        if (!ModelState.IsValid || hasFractionalMoney || playtimeAmount < 0 || fandBAmount < 0 || pointsToRedeem < 0 || (cashReceived.HasValue && cashReceived.Value < 0))
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ: Tiền giờ chơi, tiền F&B, số điểm đổi và tiền khách đưa phải là số từ 0 trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
            return RedirectToAction(nameof(Index), new { customerId, customerMode, sessionId, playtimeAmount = Math.Max(0m, playtimeAmount), fandBAmount = Math.Max(0m, fandBAmount), paymentMethod });
        }

        if ((playtimeAmount + fandBAmount) <= 0)
        {
            TempData["Error"] = "Tổng tiền giờ chơi và dịch vụ ăn uống phải lớn hơn 0 VND.";
            return RedirectToAction(nameof(Index), new { customerId, customerMode, sessionId, playtimeAmount, fandBAmount, paymentMethod });
        }

        if (isGuest && !string.IsNullOrWhiteSpace(voucherCode))
        {
            TempData["Error"] = $"Khách lẻ không thể sử dụng mã voucher hội viên ({voucherCode.Trim().ToUpperInvariant()}).";
            return RedirectToAction(nameof(Index), new { customerMode = "guest", sessionId, playtimeAmount, fandBAmount, paymentMethod, referenceCode });
        }

        var command = new CheckoutCommand(
            isGuest ? null : customerId,
            referenceCode,
            sessionId,
            playtimeAmount,
            fandBAmount,
            pointsToRedeem,
            voucherCode,
            CurrentActorId,
            cashReceived);

        CheckoutPaymentResult result = isCash
            ? await _checkout.CompleteCashAsync(command)
            : await _checkout.CreateTransferIntentAsync(command);

        if (!result.Success)
        {
            TempData["Error"] = result.Message;
        }
        else if (result.Pending)
        {
            TempData["Warning"] = result.Message;
        }
        else if (result.Duplicate)
        {
            TempData["Warning"] = result.Message;
        }
        else
        {
            TempData["Success"] = $"{result.Message} Số tiền: {VnFormat.Money(result.FinalAmount)}. Tiền trả lại khách: {VnFormat.Money(result.ChangeAmount)}.";
        }

        return RedirectToAction(nameof(Index), new
        {
            customerId,
            customerMode = "member",
            playtimeAmount,
            fandBAmount,
            pointsToRedeem = result.Pending ? pointsToRedeem : 0,
            voucherCode = result.Pending ? voucherCode : null,
            paymentMethod,
            referenceCode = result.ReferenceCode ?? referenceCode
        });
    }

    // ── Helpers tích hợp Vận hành bàn (PlaySessions) & Thực đơn F&B (Orders/OrderDetails) ──

    private async Task<List<CheckoutSessionItemViewModel>> LoadCheckoutSessionsAsync(int? includeSessionId = null)
    {
        try
        {
            int targetId = includeSessionId ?? 0;
            return await _context.Database.SqlQuery<CheckoutSessionItemViewModel>($@"
                ;WITH RankedSessions AS (
                    SELECT
                        s.Id AS SessionId,
                        ISNULL(t.TableCode, CONCAT(N'Bàn #', s.TableId)) AS TableCode,
                        CAST(NULL AS nvarchar(450)) AS CustomerId,
                        CAST(NULL AS nvarchar(256)) AS CustomerName,
                        s.StartAtUtc AS StartAtUtc,
                        s.EndAtUtc AS EndAtUtc,
                        s.HourlyRateSnapshot AS HourlyRateSnapshot,
                        CASE
                            WHEN OBJECT_ID(N'dbo.fn_CalcPlaytimeAmount', N'FN') IS NOT NULL
                                 AND dbo.fn_CalcPlaytimeAmount(s.Id, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) IS NOT NULL
                            THEN dbo.fn_CalcPlaytimeAmount(s.Id, ISNULL(s.EndAtUtc, SYSUTCDATETIME()))
                            WHEN s.PlaytimeAmount IS NOT NULL AND s.PlaytimeAmount > 0 THEN s.PlaytimeAmount
                            ELSE CAST(ROUND(
                                (CASE WHEN DATEDIFF(MINUTE, s.StartAtUtc, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) < 1
                                      THEN 1
                                      ELSE DATEDIFF(MINUTE, s.StartAtUtc, ISNULL(s.EndAtUtc, SYSUTCDATETIME()))
                                 END / 60.0) * s.HourlyRateSnapshot, -3) AS decimal(18,2))
                        END AS PlaytimeAmount,
                        ISNULL((
                            SELECT SUM(od.Subtotal)
                            FROM dbo.Orders o
                            INNER JOIN dbo.OrderDetails od ON od.OrderId = o.Id
                            WHERE o.SessionId = s.Id AND o.Status = 'Served'
                        ), 0) AS FandBAmount,
                        s.Status AS Status,
                        ROW_NUMBER() OVER (
                            PARTITION BY s.TableId
                            ORDER BY CASE WHEN s.Status IN ('Active', 'Open') THEN 0 ELSE 1 END, s.StartAtUtc DESC, s.Id DESC
                        ) AS Rn
                    FROM dbo.PlaySessions s
                    LEFT JOIN dbo.BilliardTables t ON t.Id = s.TableId
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.SessionId = s.Id AND i.Status = 'Paid')
                )
                SELECT TOP 20
                    SessionId, TableCode, CustomerId, CustomerName,
                    StartAtUtc, EndAtUtc, HourlyRateSnapshot,
                    PlaytimeAmount, FandBAmount, Status
                FROM RankedSessions
                WHERE Rn = 1 OR SessionId = {targetId}
                ORDER BY TableCode ASC, SessionId DESC
            ").ToListAsync();
        }
        catch
        {
            return new List<CheckoutSessionItemViewModel>();
        }
    }

    private async Task<List<SessionOrderLineViewModel>> LoadSessionOrderLinesAsync(int sessionId)
    {
        try
        {
            return await _context.Database.SqlQuery<SessionOrderLineViewModel>($@"
                SELECT
                    p.Name AS ProductName,
                    SUM(od.Quantity) AS Quantity,
                    od.UnitPrice AS UnitPrice,
                    SUM(od.Subtotal) AS Subtotal
                FROM dbo.Orders o
                INNER JOIN dbo.OrderDetails od ON od.OrderId = o.Id
                INNER JOIN dbo.Products p ON p.Id = od.ProductId
                WHERE o.SessionId = {sessionId} AND o.Status = 'Served'
                GROUP BY p.Name, od.UnitPrice
                ORDER BY p.Name
            ").ToListAsync();
        }
        catch
        {
            return new List<SessionOrderLineViewModel>();
        }
    }

    // POST /LoyaltyTest/SimulatePayment (Giữ lại cho tương thích cũ)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulatePayment(string customerId, decimal finalAmount)
    {
        var result = await _loyaltyService.CommitCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId,
            ReferenceCode: $"SIM-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            InvoiceId: null,
            PlaytimeAmount: finalAmount,
            FandBAmount: 0m,
            PointsToRedeem: 0,
            VoucherCode: null,
            CashierId: CurrentActorId));

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SimulateRedeem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateRedeem(string customerId, int pointsToRedeem, decimal billAmount)
    {
#pragma warning disable CS0618
        var result = await _loyaltyService.RedeemPointsForBillAsync(customerId, pointsToRedeem, billAmount);
#pragma warning restore CS0618
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/TriggerExpirationScan
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerExpirationScan(string? customerId)
    {
        int count = await _loyaltyService.ProcessPointExpirationsAsync();
        TempData["Success"] = count > 0
            ? $"Đã quét và thu hồi điểm thưởng cho {count} hồ sơ (>100 ngày không giao dịch)."
            : "Đã quét xong: Không có hồ sơ nào không giao dịch trên 100 ngày.";

        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SimulateAging (Dùng IPointLedgerService thay vì gán trực tiếp)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateAging(string customerId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == customerId);
        if (user == null) return NotFound();

        if (user.RewardPoints == 0)
        {
            await _ledger.PostAsync(
                customerId: user.Id,
                delta: 120,
                type: PointTransaction.Bonus,
                referenceCode: $"SIM-AGING-{DateTime.UtcNow:yyyyMMddHHmmss}",
                invoiceId: null,
                reason: "Cấp điểm giả lập để kiểm thử thu hồi điểm >100 ngày",
                actorId: CurrentActorId);
        }

        await _context.Entry(user).ReloadAsync();
        user.LastPaidDate = DateTime.UtcNow.AddDays(-105);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"[ĐÃ GIẢ LẬP] Đặt LastPaidDate của {user.FullName} về 105 ngày trước (Điểm hiện có: {user.RewardPoints}). Bạn có thể bấm 'Quét hết hạn điểm' ngay để kiểm tra kết quả!";
        return RedirectToAction(nameof(Index), new { customerId });
    }
}
