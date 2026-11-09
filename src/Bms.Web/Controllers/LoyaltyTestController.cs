using Bms.Web.Filters;
using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff")]
public class LoyaltyTestController : Controller
{
    private const string PlaceholderEmailDomain = "@noemail.bms.local";

    private readonly ApplicationDbContext _context;
    private readonly ILoyaltyService _loyaltyService;
    private readonly IPointLedgerService _ledger;
    private readonly ITierEvaluationService _tierEval;

    public LoyaltyTestController(
        ApplicationDbContext context,
        ILoyaltyService loyaltyService,
        IPointLedgerService ledger,
        ITierEvaluationService tierEval)
    {
        _context = context;
        _loyaltyService = loyaltyService;
        _ledger = ledger;
        _tierEval = tierEval;
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
        var activeSessions = await LoadCheckoutSessionsAsync();
        CheckoutSessionItemViewModel? selectedSession = null;
        var sessionOrderLines = new List<SessionOrderLineViewModel>();

        if (sessionId.HasValue && sessionId.Value > 0)
        {
            selectedSession = activeSessions.FirstOrDefault(s => s.SessionId == sessionId.Value);
            if (selectedSession != null)
            {
                playtimeAmount = selectedSession.PlaytimeAmount;
                fandBAmount = selectedSession.FandBAmount;
                if (string.IsNullOrEmpty(customerId) && !string.IsNullOrEmpty(selectedSession.CustomerId) && customerMode != "guest")
                {
                    customerId = selectedSession.CustomerId;
                }
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
            CustomerNotifications = customerNotifications
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
                         && v.Status == CustomerVoucher.StatusActive
                         && v.ExpiresAtUtc > nowUtc)
                .OrderBy(v => v.ExpiresAtUtc)
                .ToListAsync()
            : new List<CustomerVoucher>();

        var vouchers = rawVouchers.Select(v =>
        {
            var minTier = v.RewardItem?.MinTier;
            bool eligible = minTier == null || userTierThreshold >= minTier.SpendThreshold;
            string tierNote = minTier != null
                ? (eligible ? $" · Hạng {minTier.TierName}+" : $" · ⚠️ Yêu cầu Hạng {minTier.TierName} (Chưa đủ hạng)")
                : "";
            return new
            {
                code = v.Code,
                minTierName = minTier?.TierName,
                isEligible = eligible,
                label = $"{v.Code} — {v.Title} ({(v.DiscountValue.HasValue ? "-" + VnFormat.Money(v.DiscountValue.Value) : VnFormat.RewardTypeLabel(v.VoucherType))}{tierNote})"
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
    [RequireOpenShift] // TV5 - BR-23: chưa mở ca thì không thu tiền
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
        string methodLabel = isCash ? "Tiền mặt" : "Chuyển khoản VietQR";
        string dbPaymentMethod = isCash ? "Cash" : "Transfer";

        if (!ModelState.IsValid || playtimeAmount < 0 || fandBAmount < 0 || pointsToRedeem < 0 || (cashReceived.HasValue && cashReceived.Value < 0))
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ: Tiền giờ chơi, tiền F&B, số điểm đổi và tiền khách đưa phải là số từ 0 trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
            return RedirectToAction(nameof(Index), new { customerId, customerMode, sessionId, playtimeAmount = Math.Max(0m, playtimeAmount), fandBAmount = Math.Max(0m, fandBAmount), paymentMethod });
        }

        if ((playtimeAmount + fandBAmount) <= 0)
        {
            TempData["Error"] = "Tổng tiền giờ chơi và dịch vụ ăn uống phải lớn hơn 0 VND.";
            return RedirectToAction(nameof(Index), new { customerId, customerMode, sessionId, playtimeAmount, fandBAmount, paymentMethod });
        }

        if (isGuest)
        {
            if (!string.IsNullOrWhiteSpace(voucherCode))
            {
                TempData["Error"] = $"Khách lẻ không thể sử dụng mã voucher hội viên ({voucherCode.Trim().ToUpperInvariant()}).";
                return RedirectToAction(nameof(Index), new { customerMode = "guest", sessionId, playtimeAmount, fandBAmount, paymentMethod, referenceCode });
            }
            decimal guestFinal = playtimeAmount + fandBAmount;
            decimal actualCash = cashReceived ?? guestFinal;
            if (isCash && actualCash < guestFinal)
            {
                TempData["Error"] = $"Số tiền khách đưa ({VnFormat.Money(actualCash)}) nhỏ hơn tổng cần thanh toán ({VnFormat.Money(guestFinal)}). Vui lòng nhập lại số tiền khách đưa.";
                return RedirectToAction(nameof(Index), new { customerMode = "guest", sessionId, playtimeAmount, fandBAmount, paymentMethod = "Cash", cashReceived = actualCash, referenceCode });
            }

            decimal changeAmt = isCash ? (actualCash - guestFinal) : 0m;
            await RecordInvoiceAndPaymentAsync(sessionId, null, playtimeAmount, fandBAmount, 0m, guestFinal, dbPaymentMethod);

            string cashSummary = isCash
                ? $" (Khách đưa: {VnFormat.Money(actualCash)} · Tiền thừa trả khách: {VnFormat.Money(changeAmt)})"
                : $" (Đã xác nhận thanh toán qua mã VietQR số tiền {VnFormat.Money(guestFinal)})";

            TempData["Success"] = $"Đã thanh toán thành công hóa đơn {referenceCode} cho Khách lẻ bằng hình thức {methodLabel}: {VnFormat.Money(guestFinal)}{cashSummary}.";
            return RedirectToAction(nameof(Index), new
            {
                customerMode = "guest",
                playtimeAmount,
                fandBAmount,
                paymentMethod,
                referenceCode = $"HD-{DateTime.UtcNow:yyyyMMddHHmmss}"
            });
        }

        // Luồng Khách hội viên: kiểm tra trước FinalAmount nếu thanh toán bằng Tiền mặt
        var preview = await _loyaltyService.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId!,
            ReferenceCode: referenceCode,
            InvoiceId: null,
            PlaytimeAmount: playtimeAmount,
            FandBAmount: fandBAmount,
            PointsToRedeem: pointsToRedeem,
            VoucherCode: string.IsNullOrWhiteSpace(voucherCode) ? null : voucherCode,
            CashierId: CurrentActorId));

        if (!preview.IsValid)
        {
            TempData["Error"] = string.Join(" ", preview.Errors);
            return RedirectToAction(nameof(Index), new { customerId, customerMode = "member", sessionId, playtimeAmount, fandBAmount, pointsToRedeem, voucherCode, paymentMethod, cashReceived, referenceCode });
        }

        decimal memberCashReceived = cashReceived ?? preview.FinalAmount;
        if (isCash && memberCashReceived < preview.FinalAmount)
        {
            TempData["Error"] = $"Số tiền khách đưa ({VnFormat.Money(memberCashReceived)}) nhỏ hơn tổng thanh toán ({VnFormat.Money(preview.FinalAmount)}). Vui lòng nhập đủ số tiền khách đưa.";
            return RedirectToAction(nameof(Index), new { customerId, customerMode = "member", sessionId, playtimeAmount, fandBAmount, pointsToRedeem, voucherCode, paymentMethod = "Cash", cashReceived = memberCashReceived, referenceCode });
        }

        // Thanh toán bằng bất kỳ hình thức nào (VietQR hoặc Tiền mặt) đều cộng điểm và xét hạng cho hội viên
        var result = await _loyaltyService.CommitCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId!,
            ReferenceCode: referenceCode,
            InvoiceId: null,
            PlaytimeAmount: playtimeAmount,
            FandBAmount: fandBAmount,
            PointsToRedeem: pointsToRedeem,
            VoucherCode: string.IsNullOrWhiteSpace(voucherCode) ? null : voucherCode,
            CashierId: CurrentActorId));

        if (!result.Success)
        {
            TempData["Error"] = result.Message;
        }
        else if (result.Duplicate)
        {
            TempData["Warning"] = result.Message;
        }
        else
        {
            decimal totalDiscount = (playtimeAmount + fandBAmount) - result.FinalAmount;
            await RecordInvoiceAndPaymentAsync(sessionId, customerId, playtimeAmount, fandBAmount, totalDiscount, result.FinalAmount, dbPaymentMethod);

            decimal memberChange = isCash ? (memberCashReceived - result.FinalAmount) : 0m;
            string paymentDetail = isCash
                ? $" [Hình thức: Tiền mặt · Khách đưa: {VnFormat.Money(memberCashReceived)} · Trả lại khách: {VnFormat.Money(memberChange)}]"
                : $" [Hình thức: Chuyển khoản VietQR · Số tiền: {VnFormat.Money(result.FinalAmount)}]";

            TempData["Success"] = result.Message + paymentDetail;
        }

        return RedirectToAction(nameof(Index), new
        {
            customerId,
            customerMode = "member",
            playtimeAmount,
            fandBAmount,
            pointsToRedeem = 0,
            paymentMethod,
            referenceCode
        });
    }

    // ── Helpers tích hợp Vận hành bàn (PlaySessions) & Thực đơn F&B (Orders/OrderDetails) ──

    private async Task<List<CheckoutSessionItemViewModel>> LoadCheckoutSessionsAsync()
    {
        try
        {
            return await _context.Database.SqlQuery<CheckoutSessionItemViewModel>($@"
                SELECT TOP 20
                    s.Id AS SessionId,
                    ISNULL(t.TableCode, CONCAT(N'Bàn #', s.TableId)) AS TableCode,
                    s.CustomerId AS CustomerId,
                    u.FullName AS CustomerName,
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
                        WHERE o.SessionId = s.Id AND o.Status <> 'Cancelled'
                    ), 0) AS FandBAmount,
                    s.Status AS Status
                FROM dbo.PlaySessions s
                LEFT JOIN dbo.BilliardTables t ON t.Id = s.TableId
                LEFT JOIN dbo.AspNetUsers u ON u.Id = s.CustomerId
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.SessionId = s.Id AND i.Status = 'Paid')
                ORDER BY CASE WHEN s.Status IN ('Active', 'Open') THEN 0 ELSE 1 END, s.StartAtUtc DESC, s.Id DESC
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
                WHERE o.SessionId = {sessionId} AND o.Status <> 'Cancelled'
                GROUP BY p.Name, od.UnitPrice
                ORDER BY p.Name
            ").ToListAsync();
        }
        catch
        {
            return new List<SessionOrderLineViewModel>();
        }
    }

    private async Task RecordInvoiceAndPaymentAsync(
        int? sessionId,
        string? customerId,
        decimal totalPlaytime,
        decimal totalFandB,
        decimal discountAmount,
        decimal finalAmount,
        string dbPaymentMethod)
    {
        try
        {
            int resolvedSessionId = sessionId ?? 0;
            var actorId = CurrentActorId;
            if (string.IsNullOrEmpty(actorId))
            {
                actorId = await _context.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
            }
            if (string.IsNullOrEmpty(actorId)) return;

            if (resolvedSessionId <= 0)
            {
                var tableId = (await _context.Database
                    .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.BilliardTables ORDER BY Id")
                    .ToListAsync()).FirstOrDefault();
                if (tableId <= 0) return;

                resolvedSessionId = (await _context.Database.SqlQuery<int>($@"
                    INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
                    OUTPUT INSERTED.Id AS [Value]
                    VALUES ({tableId}, {customerId}, {actorId}, {actorId}, DATEADD(hour, -1, SYSUTCDATETIME()), SYSUTCDATETIME(), {totalPlaytime}, {totalPlaytime}, 'Closed');
                ").ToListAsync()).FirstOrDefault();
            }
            else
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    IF OBJECT_ID(N'dbo.PlaySessionTableSegments', N'U') IS NOT NULL
                    BEGIN
                        UPDATE dbo.PlaySessionTableSegments
                        SET EndAtUtc = SYSUTCDATETIME()
                        WHERE SessionId = {resolvedSessionId} AND EndAtUtc IS NULL;
                    END;

                    UPDATE dbo.PlaySessions
                    SET Status = 'Closed',
                        EndAtUtc = ISNULL(EndAtUtc, SYSUTCDATETIME()),
                        ClosedById = ISNULL(ClosedById, {actorId}),
                        CustomerId = ISNULL(CustomerId, {customerId}),
                        PlaytimeAmount = {totalPlaytime}
                    WHERE Id = {resolvedSessionId};

                    UPDATE t
                    SET t.Status = 'Available'
                    FROM dbo.BilliardTables t
                    INNER JOIN dbo.PlaySessions s ON s.TableId = t.Id
                    WHERE s.Id = {resolvedSessionId} AND t.Status = 'InUse';
                ");
            }

            if (resolvedSessionId <= 0) return;

            var existingInvoiceId = (await _context.Database
                .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.Invoices WHERE SessionId = {resolvedSessionId}")
                .ToListAsync()).FirstOrDefault();

            int invoiceId = existingInvoiceId;
            if (invoiceId <= 0)
            {
                invoiceId = (await _context.Database.SqlQuery<int>($@"
                    INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
                    OUTPUT INSERTED.Id AS [Value]
                    VALUES ({resolvedSessionId}, {customerId}, {totalPlaytime}, {totalFandB}, {discountAmount}, 'Paid', SYSUTCDATETIME());
                ").ToListAsync()).FirstOrDefault();
            }
            else
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.Invoices
                    SET CustomerId = {customerId},
                        TotalPlaytime = {totalPlaytime},
                        TotalFandB = {totalFandB},
                        DiscountAmount = {discountAmount},
                        Status = 'Paid'
                    WHERE Id = {invoiceId};
                ");
            }

            if (invoiceId > 0)
            {
                var activeShiftId = await _context.WorkShifts.AsNoTracking()
                    .Where(s => s.Status == WorkShift.StatusActive) // BR-23: ca chung của quán
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, WorkShiftId, ReceivedById, TransactionTimeUtc)
                    VALUES ({invoiceId}, {dbPaymentMethod}, {finalAmount}, {activeShiftId}, {actorId}, SYSUTCDATETIME());
                ");
            }
        }
        catch
        {
            // Không làm gián đoạn luồng tính điểm nếu DB đang chạy trong ngữ cảnh giả lập thiếu bảng phụ
        }
    }

    // POST /LoyaltyTest/SimulatePayment (Giữ lại cho tương thích cũ)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireOpenShift]
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
