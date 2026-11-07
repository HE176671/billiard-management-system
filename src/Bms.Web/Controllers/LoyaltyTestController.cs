using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager,Admin")]
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
                    TotalSpend = user.TotalSpend,
                    Spend12Months = spend12M,
                    LastPaidDate = user.LastPaidDate,
                    RecentTransactions = transactions
                };

                var now = DateTime.UtcNow;
                availableVouchers = await _context.CustomerVouchers.AsNoTracking()
                    .Where(v => v.CustomerId == customerId
                             && v.Status == CustomerVoucher.StatusActive
                             && v.ExpiresAtUtc > now
                             && v.VoucherType != RewardItemEntity.TypeGift)
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

        var recentReviews = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.Customer)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(10)
            .Select(r => new SimulatedReviewItemViewModel
            {
                Id = r.Id,
                SessionId = r.SessionId,
                CustomerId = r.CustomerId,
                CustomerName = r.Customer != null ? r.Customer.FullName : "Khách vãng lai",
                Rating = r.Rating,
                Comment = r.Comment,
                AdminReply = r.AdminReply,
                CreatedAtUtc = r.CreatedAtUtc
            })
            .ToListAsync();

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
            RecentReviews = recentReviews,
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
            return Json(new
            {
                isValid = true,
                errors = Array.Empty<string>(),
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

        return Json(new
        {
            isValid = res.IsValid,
            errors = res.Errors,
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
        string methodLabel = isCash ? "Tiền mặt" : "Chuyển khoản VietQR";
        string dbPaymentMethod = isCash ? "Cash" : "Transfer";

        if (playtimeAmount < 0 || fandBAmount < 0 || (playtimeAmount + fandBAmount) <= 0)
        {
            TempData["Error"] = "Tổng tiền giờ chơi và dịch vụ ăn uống phải lớn hơn 0 ₫.";
            return RedirectToAction(nameof(Index), new { customerId, customerMode, sessionId, playtimeAmount, fandBAmount, paymentMethod });
        }

        if (isGuest)
        {
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
                        WHEN s.PlaytimeAmount IS NOT NULL AND s.PlaytimeAmount > 0 THEN s.PlaytimeAmount
                        ELSE CAST(ROUND(
                            (CASE WHEN DATEDIFF(MINUTE, s.StartAtUtc, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) < 15
                                  THEN 15
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
                LEFT JOIN dbo.Users u ON u.Id = s.CustomerId
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.SessionId = s.Id AND i.Status = 'Paid')
                ORDER BY CASE WHEN s.Status = 'Open' THEN 0 ELSE 1 END, s.StartAtUtc DESC, s.Id DESC
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
                    UPDATE dbo.PlaySessions
                    SET Status = 'Closed',
                        EndAtUtc = ISNULL(EndAtUtc, SYSUTCDATETIME()),
                        ClosedById = ISNULL(ClosedById, {actorId}),
                        CustomerId = ISNULL(CustomerId, {customerId}),
                        PlaytimeAmount = {totalPlaytime}
                    WHERE Id = {resolvedSessionId};
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
                    .Where(s => s.StaffId == actorId && s.Status == WorkShift.StatusActive)
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

    // POST /LoyaltyTest/SimulateReview
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateReview(string customerId, int sessionId, byte rating, string? comment)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            TempData["Error"] = "Vui lòng chọn khách hàng gửi đánh giá.";
            return RedirectToAction(nameof(Index));
        }

        if (sessionId <= 0)
        {
            TempData["Error"] = "Mã phiên (SessionId) phải là số nguyên dương lớn hơn 0.";
            return RedirectToAction(nameof(Index), new { customerId });
        }

        if (rating < 1 || rating > 5)
        {
            TempData["Error"] = "Đánh giá sao phải nằm trong khoảng từ 1 đến 5 sao.";
            return RedirectToAction(nameof(Index), new { customerId });
        }

        var customer = await _context.Users.FirstOrDefaultAsync(u => u.Id == customerId);
        if (customer == null)
        {
            TempData["Error"] = "Không tìm thấy khách hàng được chọn.";
            return RedirectToAction(nameof(Index));
        }

        var sessionExists = (await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.PlaySessions WHERE Id = {sessionId}")
            .ToListAsync()).FirstOrDefault() > 0;

        if (!sessionExists)
        {
            var tableId = (await _context.Database
                .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.BilliardTables ORDER BY Id")
                .ToListAsync()).FirstOrDefault();
            if (tableId == 0) tableId = 1;

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;
                SET IDENTITY_INSERT dbo.PlaySessions ON;
                INSERT INTO dbo.PlaySessions (Id, TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
                VALUES ({sessionId}, {tableId}, {customerId}, {customerId}, {customerId}, DATEADD(hour, -2, SYSUTCDATETIME()), SYSUTCDATETIME(), 60000, 120000, 'Closed');
                SET IDENTITY_INSERT dbo.PlaySessions OFF;
            ");
        }

        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.SessionId == sessionId);
        if (review != null)
        {
            review.CustomerId = customerId;
            review.Rating = rating;
            review.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            review.AdminReply = null;
            review.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            review = new Review
            {
                SessionId = sessionId,
                CustomerId = customerId,
                Rating = rating,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                AdminReply = null,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Reviews.Add(review);
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"[ĐÃ GIẢ LẬP] Đã tạo đánh giá {rating}★ cho phiên #{sessionId} của khách {customer.FullName}!";
        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SeedSampleReviews
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedSampleReviews(string? customerId)
    {
        var customers = await _context.Users
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Customer")))
            .Take(5)
            .ToListAsync();

        if (customers.Count == 0)
            customers = await _context.Users.Take(5).ToListAsync();

        var samples = new[]
        {
            (SessionId: 901, Rating: (byte)5, Comment: "Bàn xịn, bi chạy cực chuẩn, cơ bóng mới cứng. Phục vụ nước nhanh, 10/10!"),
            (SessionId: 902, Rating: (byte)4, Comment: "Quán rộng rãi, nhạc vừa phải. Bàn êm nhưng điều hòa hơi lạnh một chút."),
            (SessionId: 903, Rating: (byte)3, Comment: "Dịch vụ tạm ổn, giờ cao điểm gọi đồ ăn hơi lâu."),
            (SessionId: 904, Rating: (byte)2, Comment: "Đầu lơ cơ hơi mòn, nhân viên phục vụ chưa được niềm nở."),
            (SessionId: 905, Rating: (byte)1, Comment: "Bàn bên cạnh làm ồn quá, khách phàn nàn nhưng nhân viên chưa hỗ trợ kịp.")
        };

        var tableId = (await _context.Database
            .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.BilliardTables ORDER BY Id")
            .ToListAsync()).FirstOrDefault();
        if (tableId == 0) tableId = 1;

        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var custId = customers.Count > 0 ? customers[i % customers.Count].Id : (customerId ?? "demo-customer-01");
            int hoursAgo = -(i + 1);

            var sessionExists = (await _context.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.PlaySessions WHERE Id = {s.SessionId}")
                .ToListAsync()).FirstOrDefault() > 0;

            if (!sessionExists)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    SET ANSI_NULLS ON;
                    SET QUOTED_IDENTIFIER ON;
                    SET IDENTITY_INSERT dbo.PlaySessions ON;
                    INSERT INTO dbo.PlaySessions (Id, TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
                    VALUES ({s.SessionId}, {tableId}, {custId}, {custId}, {custId}, DATEADD(hour, {hoursAgo}, SYSUTCDATETIME()), SYSUTCDATETIME(), 60000, 120000, 'Closed');
                    SET IDENTITY_INSERT dbo.PlaySessions OFF;
                ");
            }

            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.SessionId == s.SessionId);
            if (review != null)
            {
                review.CustomerId = custId;
                review.Rating = s.Rating;
                review.Comment = s.Comment;
                review.AdminReply = null;
                review.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30 * (i + 1));
            }
            else
            {
                review = new Review
                {
                    SessionId = s.SessionId,
                    CustomerId = custId,
                    Rating = s.Rating,
                    Comment = s.Comment,
                    AdminReply = null,
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30 * (i + 1))
                };
                _context.Reviews.Add(review);
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã khởi tạo thành công 5 đánh giá mẫu đa dạng từ 1★ đến 5★!";
        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/ClearReviews
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearReviews(string? customerId)
    {
        _context.Reviews.RemoveRange(_context.Reviews);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã xóa toàn bộ dữ liệu đánh giá trong hệ thống.";
        return RedirectToAction(nameof(Index), new { customerId });
    }
}
