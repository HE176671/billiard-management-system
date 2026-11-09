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
        decimal playtimeAmount = 0m,
        decimal fandBAmount = 0m,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string? referenceCode = null,
        string paymentMethod = "VietQR",
        decimal? cashReceived = null,
        bool cashMode = false,
        decimal? switchCashReceived = null,
        string? switchReason = null,
        bool useRewardPoints = false)
    {
        if (!useRewardPoints) pointsToRedeem = 0;
        // A persisted reference owns the customer, session and receipt snapshot.
        // Query-string values must not rewrite a pending or completed checkout.
        var draft = string.IsNullOrWhiteSpace(referenceCode) ? null : await _context.CheckoutDrafts.AsNoTracking()
            .SingleOrDefaultAsync(d => d.ReferenceCode == referenceCode);
        var selectedIntent = string.IsNullOrWhiteSpace(referenceCode) ? null : await _context.PaymentIntents.AsNoTracking()
            .SingleOrDefaultAsync(i => i.ReferenceCode == referenceCode);
        if (draft?.Status == CheckoutDraft.Superseded)
            return RedirectToAction(nameof(Index), new { referenceCode = draft.ReplacementReference });
        if (!string.IsNullOrWhiteSpace(referenceCode) && draft == null && selectedIntent == null)
            return NotFound("Phiếu không tồn tại. Mở Thanh toán & Tích điểm để bắt đầu giao dịch mới.");
        if (draft != null || selectedIntent != null)
        {
            customerId = draft != null ? draft.CustomerId : selectedIntent!.CustomerId;
            customerMode = customerId == null ? "guest" : "member";
            sessionId = draft != null ? draft.SessionId : selectedIntent!.SessionId;
            playtimeAmount = draft != null ? draft.PlaytimeAmount : selectedIntent!.PlaytimeAmount;
            fandBAmount = draft != null ? draft.FandBAmount : selectedIntent!.FandBAmount;
            pointsToRedeem = draft != null ? draft.PointsToRedeem : selectedIntent!.PointsToRedeem;
            voucherCode = draft != null ? draft.VoucherCode : selectedIntent!.VoucherCode;
            paymentMethod = draft?.PaymentMethod ?? "VietQR";
            cashReceived = draft?.CashReceived;
        }
        var checkoutLocked = (draft != null && draft.Status != CheckoutDraft.Draft) || selectedIntent != null;
        var paid = draft?.Status == CheckoutDraft.Paid || selectedIntent?.Status == PaymentIntent.StatusConfirmed;
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
            selectedSession = draft?.IsManualEntry == true ? null : activeSessions.FirstOrDefault(s => s.SessionId == sessionId.Value);
            if (selectedSession != null && !checkoutLocked)
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
        if (string.IsNullOrWhiteSpace(referenceCode))
        {
            if (customerId != null && !customers.Any(c => c.Id == customerId))
                return BadRequest("Hội viên không hợp lệ.");
            if (sessionId.HasValue && selectedSession == null)
                return BadRequest("Phiên chơi không tồn tại hoặc đã được thanh toán. Hãy chọn phiên khác.");
            if (playtimeAmount < 0 || fandBAmount < 0 || playtimeAmount > 100_000_000 || fandBAmount > 100_000_000
                || playtimeAmount != decimal.Truncate(playtimeAmount) || fandBAmount != decimal.Truncate(fandBAmount)
                || pointsToRedeem < 0 || (voucherCode?.Length ?? 0) > 20)
                return BadRequest("Dữ liệu khởi tạo phiếu thanh toán không hợp lệ.");
            draft = new CheckoutDraft
            {
                CreatedById = CurrentActorId!, CustomerId = customerId, SessionId = sessionId,
                IsManualEntry = sessionId == null,
                PlaytimeAmount = playtimeAmount, FandBAmount = fandBAmount,
                PointsToRedeem = pointsToRedeem, VoucherCode = voucherCode,
                PaymentMethod = paymentMethod is "Cash" or "Wallet" ? paymentMethod : "VietQR",
                CashReceived = cashReceived
            };
            _context.CheckoutDrafts.Add(draft);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { referenceCode = draft.ReferenceCode });
        }
        var refCode = referenceCode.Trim();
        var pendingIntent = selectedIntent is { Status: PaymentIntent.StatusPending or PaymentIntent.StatusExpired } ? selectedIntent : null;

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
                    WalletBalance = user.WalletBalance,
                    RewardPoints = user.RewardPoints,
                    TotalSpend = user.TotalSpend,
                    Spend12Months = spend12M,
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

                if (!paid) preview = await _loyaltyService.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
                    CustomerId: customerId,
                    ReferenceCode: refCode,
                    InvoiceId: null,
                    PlaytimeAmount: playtimeAmount,
                    FandBAmount: fandBAmount,
                    PointsToRedeem: pointsToRedeem,
                    VoucherCode: voucherCode,
                    CashierId: CurrentActorId,
                    PayWithWallet: paymentMethod == "Wallet"));
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
        string normalizedMethod = paymentMethod == "Wallet" && resolvedMode == "member" ? "Wallet" : (string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase) ? "Cash" : "VietQR");
        var methodChange = await _context.PaymentMethodChanges.AsNoTracking().SingleOrDefaultAsync(c => c.ReferenceCode == refCode);

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
            CheckoutStatus = paid ? CheckoutDraft.Paid : checkoutLocked ? CheckoutDraft.Pending : CheckoutDraft.Draft,
            InvoiceId = draft?.InvoiceId ?? selectedIntent?.InvoiceId,
            StoredFinalAmount = checkoutLocked ? draft?.FinalAmount ?? selectedIntent?.ExpectedAmount : null,
            CanConfirmTransfer = pendingIntent != null,
            ConfirmationMethod = selectedIntent?.ConfirmationMethod,
            ConfirmedByName = selectedIntent?.ConfirmedById == null ? null : await _context.Users.Where(u => u.Id == selectedIntent.ConfirmedById).Select(u => u.FullName).FirstOrDefaultAsync(),
            PaidAtUtc = draft?.PaidAtUtc ?? selectedIntent?.ConfirmedAtUtc,
            EarnedPoints = paid ? await _context.PointTransactions.Where(t => t.ReferenceCode == refCode && t.TransactionType == PointTransaction.Earn).SumAsync(t => t.Amount) : 0,
            CashSwitchMode = cashMode && pendingIntent != null,
            CashSwitchReceived = switchCashReceived ?? pendingIntent?.ExpectedAmount ?? 0,
            CashSwitchReason = string.IsNullOrWhiteSpace(switchReason) ? "Ngân hàng của khách gặp lỗi" : switchReason,
            MethodChange = methodChange,
            MethodChangedByName = methodChange == null ? null : await _context.Users.Where(u => u.Id == methodChange.ChangedById).Select(u => u.FullName).FirstOrDefaultAsync(),
            RedeemedPoints = paid ? -(await _context.PointTransactions.Where(t => t.ReferenceCode == refCode && t.TransactionType == PointTransaction.Redeem).SumAsync(t => t.Amount)) : pointsToRedeem,
            PlaytimeAmount = playtimeAmount,
            FandBAmount = fandBAmount,
            PointsToRedeem = pointsToRedeem,
            VoucherCode = voucherCode,
            AvailableVouchers = availableVouchers,
            Preview = preview,
            PaymentMethod = normalizedMethod,
            CashReceived = cashReceived ?? finalAmt,
            CustomerNotifications = customerNotifications,
            HasPendingTransfer = pendingIntent != null && !paid
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
        string customerMode = "member",
        string paymentMethod = "VietQR",
        bool useRewardPoints = false)
    {
        if (!useRewardPoints) pointsToRedeem = 0;
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
            CashierId: CurrentActorId,
                    PayWithWallet: paymentMethod == "Wallet"));

        var user = await _context.Users.AsNoTracking()
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.Id == customerId);
        var spend12M = user != null ? await _tierEval.GetSpendLast12MonthsAsync(user.Id) : 0m;
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
            walletBalance = user?.WalletBalance ?? 0m,
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
        int? sessionId = null,
        bool useRewardPoints = false)
    {
        if (!useRewardPoints) pointsToRedeem = 0;
        bool isGuest = string.Equals(customerMode, "guest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(customerId);
        bool hasFractionalMoney = playtimeAmount != decimal.Truncate(playtimeAmount)
            || fandBAmount != decimal.Truncate(fandBAmount)
            || (cashReceived.HasValue && cashReceived.Value != decimal.Truncate(cashReceived.Value));

        if (!ModelState.IsValid || hasFractionalMoney || playtimeAmount < 0 || fandBAmount < 0 || pointsToRedeem < 0 || (cashReceived.HasValue && cashReceived.Value < 0))
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ: Tiền giờ chơi, tiền F&B, số điểm đổi và tiền khách đưa phải là số từ 0 trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
            return RedirectToAction(nameof(Index), new { referenceCode });
        }

        if ((playtimeAmount + fandBAmount) <= 0)
        {
            TempData["Error"] = "Tổng tiền giờ chơi và dịch vụ ăn uống phải lớn hơn 0 VND.";
            return RedirectToAction(nameof(Index), new { referenceCode });
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

        if (paymentMethod != "Cash" && paymentMethod != "VietQR" && paymentMethod != "Wallet") return BadRequest("Hình thức thanh toán không hợp lệ.");
        if (paymentMethod == "VietQR" && !new BankTransferSettings(HttpContext.RequestServices.GetRequiredService<IConfiguration>()).CanCreateQr)
        {
            TempData["Error"] = "Chưa cấu hình tài khoản nhận chuyển khoản.";
            return RedirectToAction(nameof(Index), new { referenceCode });
        }
        CheckoutPaymentResult result = await _checkout.SubmitDraftAsync(command, paymentMethod);

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
            referenceCode = result.ReferenceCode ?? referenceCode
        });
    }

    [HttpPost("/CashierSimulator/ConfirmTransfer"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmTransfer(string referenceCode, bool receivedTransfer)
    {
        if (!ModelState.IsValid || !receivedTransfer)
            TempData["Error"] = "Hãy kiểm tra tiền vào tài khoản nhận, đúng số tiền và nội dung, rồi tích ô xác nhận.";
        else
        {
            var result = await _checkout.ConfirmTransferManuallyAsync(referenceCode, CurrentActorId ?? "");
            TempData[result.Success ? "Success" : "Error"] = result.Message;
        }
        return RedirectToAction(nameof(Index), new { referenceCode });
    }

    [HttpPost("/CashierSimulator/ReopenTransfer"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ReopenTransfer(string referenceCode, bool checkedUnpaid)
    {
        if (!ModelState.IsValid || !checkedUnpaid)
        {
            TempData["Error"] = "Kiểm tra chưa nhận tiền cho QR cũ trước khi sửa thanh toán.";
            return RedirectToAction(nameof(Index), new { referenceCode });
        }
        var result = await _checkout.ReopenTransferAsync(referenceCode, CurrentActorId ?? "");
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index), new { referenceCode = result.ReferenceCode ?? referenceCode });
    }

    [HttpPost("/CashierSimulator/SwitchToCash"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchToCash(string referenceCode, decimal cashReceived, string reason, bool checkedCashAndBank)
    {
        if (!ModelState.IsValid || !checkedCashAndBank)
        {
            TempData["Error"] = "Hãy kiểm tra chưa nhận tiền chuyển khoản và xác nhận đã nhận đủ tiền mặt.";
            return RedirectToAction(nameof(Index), new { referenceCode, cashMode = true, switchCashReceived = cashReceived, switchReason = reason });
        }
        var result = await _checkout.SwitchTransferToCashAsync(referenceCode, cashReceived, reason, CurrentActorId ?? "");
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? $"{result.Message} Tiền trả lại khách: {VnFormat.Money(result.ChangeAmount)}." : result.Message;
        return RedirectToAction(nameof(Index), new { referenceCode, cashMode = !result.Success, switchCashReceived = cashReceived, switchReason = reason });
    }

    [HttpGet("/CashierSimulator/CheckoutStatus")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckoutStatus(string referenceCode)
    {
        var intent = await _context.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(i => i.ReferenceCode == referenceCode);
        if (intent == null) return NotFound();
        return Json(new { status = intent.Status == PaymentIntent.StatusExpired ? PaymentIntent.StatusPending : intent.Status });
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
                        CASE 
                            WHEN s.Status = 'Closed' AND s.PlaytimeAmount = 0 
                            THEN CONCAT(N'[Mang về] ', ISNULL(t.TableCode, CONCAT(N'Bàn #', s.TableId)))
                            ELSE ISNULL(t.TableCode, CONCAT(N'Bàn #', s.TableId)) 
                        END AS TableCode,
                        CAST(NULL AS nvarchar(450)) AS CustomerId,
                        CAST(NULL AS nvarchar(256)) AS CustomerName,
                        s.StartAtUtc AS StartAtUtc,
                        s.EndAtUtc AS EndAtUtc,
                        s.HourlyRateSnapshot AS HourlyRateSnapshot,
                        CASE
                            WHEN OBJECT_ID(N'dbo.fn_CalcPlaytimeAmount', N'FN') IS NOT NULL
                                 AND dbo.fn_CalcPlaytimeAmount(s.Id, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) IS NOT NULL
                            THEN dbo.fn_CalcPlaytimeAmount(s.Id, ISNULL(s.EndAtUtc, SYSUTCDATETIME()))
                            WHEN s.Status = 'Closed' AND s.PlaytimeAmount IS NOT NULL THEN s.PlaytimeAmount
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
        var result = await _checkout.CompleteCashAsync(new CheckoutCommand(
            customerId, $"SIM-{Guid.NewGuid():N}", null, finalAmount, 0m, 0, null, CurrentActorId));

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
