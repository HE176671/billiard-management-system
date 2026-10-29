using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

public class ReviewController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReviewController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ══════════════════════════════════════════════════════════════
    // ADMIN / STAFF: Xem danh sách đánh giá & Phản hồi
    // ══════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Index(int? rating)
    {
        var allReviews = await _context.Reviews
            .AsNoTracking()
            .Include(r => r.Customer)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var starCounts = new Dictionary<int, int>
        {
            [5] = allReviews.Count(r => r.Rating == 5),
            [4] = allReviews.Count(r => r.Rating == 4),
            [3] = allReviews.Count(r => r.Rating == 3),
            [2] = allReviews.Count(r => r.Rating == 2),
            [1] = allReviews.Count(r => r.Rating == 1),
        };

        var filtered = rating.HasValue
            ? allReviews.Where(r => r.Rating == rating.Value).ToList()
            : allReviews;

        var vm = new ReviewListAdminViewModel
        {
            RatingFilter = rating,
            TotalCount = allReviews.Count,
            AverageRating = allReviews.Count > 0 ? Math.Round(allReviews.Average(r => (double)r.Rating), 1) : 5.0,
            StarCounts = starCounts,
            Reviews = filtered.Select(r => new ReviewItemViewModel
            {
                Id = r.Id,
                SessionId = r.SessionId,
                CustomerId = r.CustomerId,
                CustomerName = r.Customer?.FullName ?? "Khách vãng lai",
                CustomerPhone = r.Customer?.PhoneNumber,
                Rating = r.Rating,
                Comment = r.Comment,
                AdminReply = r.AdminReply,
                CreatedAtUtc = r.CreatedAtUtc
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Reply(AdminReplyInputModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Nội dung phản hồi không hợp lệ.";
            return RedirectToAction(nameof(Index), new { rating = model.RatingFilter });
        }

        var review = await _context.Reviews
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == model.ReviewId);

        if (review == null)
        {
            TempData["Error"] = "Không tìm thấy đánh giá cần phản hồi.";
            return RedirectToAction(nameof(Index), new { rating = model.RatingFilter });
        }

        review.AdminReply = model.Reply.Trim();

        // Tự động tạo thông báo gửi tới Customer
        var notification = new Notification
        {
            UserId = review.CustomerId,
            Title = "Phản hồi đánh giá phiên chơi",
            Content = $"Quản lý đã phản hồi đánh giá của bạn (Phiên #{review.SessionId}): \"{review.AdminReply}\"",
            Type = "ReviewReply",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Notifications.Add(notification);

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã gửi phản hồi tới khách hàng {review.Customer?.FullName ?? review.CustomerId}.";
        return RedirectToAction(nameof(Index), new { rating = model.RatingFilter });
    }

    // ══════════════════════════════════════════════════════════════
    // CUSTOMER: Gửi đánh giá cho phiên chơi đã hoàn thành
    // ══════════════════════════════════════════════════════════════

    [Authorize(Roles = "Customer")]
    [HttpGet]
    public async Task<IActionResult> Create(int? sessionId)
    {
        var uid = CurrentUserId;
        var vm = new CreateReviewViewModel
        {
            SessionId = sessionId ?? 0,
            Rating = 5
        };

        // Tìm thông tin bàn nếu có
        if (sessionId.HasValue && sessionId.Value > 0)
        {
            var tableCode = (await _context.Database.SqlQuery<string>($@"
                SELECT t.TableCode AS [Value]
                FROM dbo.PlaySessions s
                JOIN dbo.BilliardTables t ON t.Id = s.TableId
                WHERE s.Id = {sessionId.Value}").ToListAsync()).FirstOrDefault();

            vm.TableCode = tableCode;
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Create(CreateReviewViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var customerId = CurrentUserId;

        // Đảm bảo SessionId trỏ vào 1 bản ghi hợp lệ trong dbo.PlaySessions để thỏa mãn FK_Review_Session
        var sessionExists = model.SessionId > 0 && (await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.PlaySessions WHERE Id = {model.SessionId}")
            .ToListAsync()).FirstOrDefault() > 0;

        if (!sessionExists)
        {
            // Tìm phiên chơi gần nhất của user hoặc phiên có sẵn trong DB
            var existingSession = (await _context.Database
                .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.PlaySessions ORDER BY Id DESC")
                .ToListAsync()).FirstOrDefault();

            if (existingSession == 0)
            {
                // DB hoàn toàn chưa có phiên nào: Tạo 1 phiên mẫu hoàn thành để liên kết FK
                await _context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO dbo.PlaySessions 
                    (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
                    VALUES (1, {0}, {0}, {0}, SYSUTCDATETIME(), SYSUTCDATETIME(), 60000, 60000, 'Closed')", customerId);

                existingSession = (await _context.Database
                    .SqlQuery<int>($"SELECT TOP 1 Id AS [Value] FROM dbo.PlaySessions ORDER BY Id DESC")
                    .ToListAsync()).FirstOrDefault();
            }

            model.SessionId = existingSession;
        }

        var review = new Review
        {
            CustomerId = customerId,
            SessionId = model.SessionId,
            Rating = model.Rating,
            Comment = string.IsNullOrWhiteSpace(model.Comment) ? null : model.Comment.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Cảm ơn bạn đã gửi đánh giá! Ý kiến của bạn giúp chúng tôi nâng cao chất lượng phục vụ.";
        return RedirectToAction("History", "CustomerPortal");
    }
}
