using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin,Staff")]
public class LoyaltyTestController : Controller
{
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
        decimal playtimeAmount = 300000m,
        decimal fandBAmount = 80000m,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string? referenceCode = null)
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
                CardCode = u.EmployeeCode,
                TierName = u.Tier != null ? u.Tier.TierName : null,
                RewardPoints = u.RewardPoints
            })
            .ToListAsync();

        if (string.IsNullOrEmpty(customerId) && customers.Count > 0)
        {
            customerId = customers[0].Id;
        }

        SelectedCustomerDetailViewModel? detail = null;
        var availableVouchers = new List<CustomerVoucher>();
        CheckoutLoyaltyPreview? preview = null;
        var refCode = string.IsNullOrWhiteSpace(referenceCode)
            ? $"HD-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : referenceCode.Trim();

        if (!string.IsNullOrEmpty(customerId))
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

        var vm = new LoyaltyTestViewModel
        {
            SelectedCustomerId = customerId,
            Customers = customers,
            CustomerDetail = detail,
            ReferenceCode = refCode,
            PlaytimeAmount = playtimeAmount,
            FandBAmount = fandBAmount,
            PointsToRedeem = pointsToRedeem,
            VoucherCode = voucherCode,
            AvailableVouchers = availableVouchers,
            Preview = preview,
            RecentReviews = recentReviews,
            CustomerNotifications = customerNotifications
        };

        return View(vm);
    }

    // GET /CashierSimulator/Preview (AJAX JSON cho receipt live preview)
    [HttpGet("/CashierSimulator/Preview")]
    [HttpGet("/LoyaltyTest/Preview")]
    public async Task<IActionResult> Preview(
        string customerId,
        decimal playtimeAmount,
        decimal fandBAmount,
        int pointsToRedeem = 0,
        string? voucherCode = null,
        string? referenceCode = null)
    {
        var res = await _loyaltyService.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId ?? string.Empty,
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

    // POST /CashierSimulator/CommitCheckout (Thanh toán trọn gói F3)
    [HttpPost("/CashierSimulator/CommitCheckout")]
    [HttpPost("/LoyaltyTest/CommitCheckout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CommitCheckout(
        string customerId,
        string referenceCode,
        decimal playtimeAmount,
        decimal fandBAmount,
        int pointsToRedeem = 0,
        string? voucherCode = null)
    {
        var result = await _loyaltyService.CommitCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId,
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
            TempData["Success"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            customerId,
            playtimeAmount,
            fandBAmount,
            pointsToRedeem = 0,
            referenceCode
        });
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
            ? $"Đã quét và reset điểm thưởng hết hạn cho {count} khách hàng (>180 ngày không thanh toán)."
            : "Đã quét xong: Không có khách hàng nào có điểm thưởng hết hạn.";

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
                reason: "Cấp điểm giả lập để kiểm thử hết hạn 180 ngày",
                actorId: CurrentActorId);
        }

        await _context.Entry(user).ReloadAsync();
        user.LastPaidDate = DateTime.UtcNow.AddDays(-185);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"[ĐÃ GIẢ LẬP] Đặt LastPaidDate của {user.FullName} về 185 ngày trước (Điểm hiện có: {user.RewardPoints}). Bạn có thể bấm 'Quét hết hạn điểm' ngay để kiểm tra kết quả!";
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
