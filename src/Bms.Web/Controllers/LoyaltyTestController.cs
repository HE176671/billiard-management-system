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

    public LoyaltyTestController(ApplicationDbContext context, ILoyaltyService loyaltyService)
    {
        _context = context;
        _loyaltyService = loyaltyService;
    }

    // GET /LoyaltyTest?customerId=...
    public async Task<IActionResult> Index(string? customerId)
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
                TierName = u.Tier != null ? u.Tier.TierName : null,
                RewardPoints = u.RewardPoints
            })
            .ToListAsync();

        if (string.IsNullOrEmpty(customerId) && customers.Count > 0)
        {
            customerId = customers[0].Id;
        }

        SelectedCustomerDetailViewModel? detail = null;
        if (!string.IsNullOrEmpty(customerId))
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Tier)
                .FirstOrDefaultAsync(u => u.Id == customerId);

            if (user != null)
            {
                var transactions = await _context.PointTransactions
                    .AsNoTracking()
                    .Where(t => t.CustomerId == customerId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(10)
                    .Select(t => new PointTransactionRowViewModel
                    {
                        CreatedAt = t.CreatedAt,
                        TransactionType = t.TransactionType,
                        Amount = t.Amount
                    })
                    .ToListAsync();

                detail = new SelectedCustomerDetailViewModel
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    PhoneNumber = user.PhoneNumber,
                    TierName = user.Tier?.TierName ?? "Chưa có",
                    PointMultiplier = user.Tier?.PointMultiplier ?? 1,
                    DiscountPercent = user.Tier?.DiscountPercent ?? 0,
                    RewardPoints = user.RewardPoints,
                    TotalSpend = user.TotalSpend,
                    LastPaidDate = user.LastPaidDate,
                    RecentTransactions = transactions
                };
            }
        }

        // Tải danh sách đánh giá gần đây trong hệ thống
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

        // Tải danh sách thông báo gửi tới khách hàng đang chọn (nếu có)
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
            RecentReviews = recentReviews,
            CustomerNotifications = customerNotifications
        };

        return View(vm);
    }

    // POST /LoyaltyTest/SimulatePayment
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulatePayment(string customerId, decimal finalAmount)
    {
        var result = await _loyaltyService.ProcessPaymentLoyaltyAsync(customerId, finalAmount);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SimulateRedeem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateRedeem(string customerId, int pointsToRedeem, decimal billAmount)
    {
        var result = await _loyaltyService.RedeemPointsForBillAsync(customerId, pointsToRedeem, billAmount);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

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

    // POST /LoyaltyTest/SimulateAging (Hỗ trợ test: Đặt ngày thanh toán cuối về 185 ngày trước)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateAging(string customerId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == customerId);
        if (user == null) return NotFound();

        user.LastPaidDate = DateTime.UtcNow.AddDays(-185);
        if (user.RewardPoints == 0)
        {
            user.RewardPoints = 120; // Đảm bảo có điểm để test hết hạn
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"[ĐÃ GIẢ LẬP] Đặt LastPaidDate của {user.FullName} về 185 ngày trước (Điểm hiện có: {user.RewardPoints}). Bạn có thể bấm 'Quét hết hạn điểm' ngay để kiểm tra kết quả!";
        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SimulateReview (Hỗ trợ test: Giả lập khách hàng gửi đánh giá)
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

        // 1. Đảm bảo phiên chơi hợp lệ trong PlaySessions để thỏa mãn FK_Review_Session
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

        // 2. Thêm hoặc cập nhật đánh giá (đảm bảo ràng buộc UQ_Review_Session)
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.SessionId == sessionId);
        if (review != null)
        {
            review.CustomerId = customerId;
            review.Rating = rating;
            review.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            review.AdminReply = null; // Reset để tester có thể vào test nút Phản hồi
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

        TempData["Success"] = $"[ĐÃ GIẢ LẬP] Đã tạo đánh giá {rating}★ cho phiên #{sessionId} của khách {customer.FullName}! Bạn có thể sang màn hình 'Quản lý Đánh giá' để test chức năng Lọc theo số sao & Gửi phản hồi (AdminReply).";
        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/SeedSampleReviews (Tạo nhanh 5 đánh giá mẫu từ 1★ đến 5★)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedSampleReviews(string? customerId)
    {
        var customers = await _context.Users
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Customer")))
            .Take(5)
            .ToListAsync();

        if (customers.Count == 0)
        {
            customers = await _context.Users.Take(5).ToListAsync();
        }

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

        TempData["Success"] = "Đã khởi tạo thành công 5 đánh giá mẫu đa dạng từ 1★ đến 5★! Bạn có thể vào ngay màn hình [Quản lý Đánh giá] để test lọc theo sao và trả lời phản hồi.";
        return RedirectToAction(nameof(Index), new { customerId });
    }

    // POST /LoyaltyTest/ClearReviews (Xóa toàn bộ đánh giá để test lại từ đầu)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearReviews(string? customerId)
    {
        _context.Reviews.RemoveRange(_context.Reviews);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã xóa toàn bộ dữ liệu đánh giá trong hệ thống để làm sạch môi trường test.";
        return RedirectToAction(nameof(Index), new { customerId });
    }
}
