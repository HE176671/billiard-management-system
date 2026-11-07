using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Admin")]
public class ReviewController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReviewController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET /Review?rating=...
    [HttpGet]
    public async Task<IActionResult> Index(int? rating = null)
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

    // POST /Review/Reply
    [HttpPost]
    [ValidateAntiForgeryToken]
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

        _context.Notifications.Add(new Notification
        {
            UserId = review.CustomerId,
            Title = "Phản hồi đánh giá dịch vụ từ Quản lý CLB",
            Content = $"Quản lý CLB đã phản hồi đánh giá của bạn (Phiên #{review.SessionId}): \"{review.AdminReply}\"",
            Type = "ReviewReply",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã gửi phản hồi tới hội viên {review.Customer?.FullName ?? review.CustomerId}.";
        return RedirectToAction(nameof(Index), new { rating = model.RatingFilter });
    }
}
