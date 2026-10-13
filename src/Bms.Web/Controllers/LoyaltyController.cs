using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// UC09/15 — Điểm thưởng & Hạng thành viên.
/// Điểm tính động từ PlaySessions Closed — không có bảng PointTransaction (FR-09.2).
/// </summary>
[Authorize(Roles = "Customer")]
public class LoyaltyController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LoyaltyController> _logger;
    private const int PageSize = 10;

    public LoyaltyController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<LoyaltyController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // GET /Loyalty  (FR-09.1)
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = _userManager.GetUserId(User)!;

        // Lấy toàn bộ phiên đã Closed của Customer, có PlaytimeAmount
        var closedQuery = _db.PlaySessions
            .Where(s => s.CustomerId == userId
                     && s.Status == "Closed"
                     && s.PlaytimeAmount.HasValue)
            .Include(s => s.Table);

        var totalSessions = await closedQuery.CountAsync();
        var totalPages    = (int)Math.Ceiling(totalSessions / (double)PageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        // Tổng điểm = SUM qua TẤT CẢ phiên, không chỉ trang hiện tại
        var allAmounts = await closedQuery
            .Select(s => s.PlaytimeAmount!.Value)
            .ToListAsync();
        var totalPoints = allAmounts.Sum(a => LoyaltyConstants.CalcSessionPoints(a));

        // Trang lịch sử — sắp xếp mới nhất trước (FR-09.1)
        var rawHistory = await closedQuery
            .OrderByDescending(s => s.StartAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(s => new
            {
                s.Id,
                TableCode      = s.Table.TableCode,
                s.StartAtUtc,
                s.EndAtUtc,
                PlaytimeAmount = s.PlaytimeAmount!.Value
            })
            .ToListAsync();

        var historyPage = rawHistory
            .Select(r => new LoyaltyHistoryRowViewModel
            {
                SessionId      = r.Id,
                TableCode      = r.TableCode,
                StartAtUtc     = r.StartAtUtc,
                EndAtUtc       = r.EndAtUtc ?? r.StartAtUtc,
                PlaytimeAmount = r.PlaytimeAmount,
                Points         = LoyaltyConstants.CalcSessionPoints(r.PlaytimeAmount)
            })
            .ToList();

        var vm = new LoyaltyViewModel
        {
            TotalPoints      = totalPoints,
            RankName         = LoyaltyConstants.GetRankName(totalPoints),
            PointsToNextRank = LoyaltyConstants.PointsToNextRank(totalPoints),
            History          = historyPage,
            Page             = page,
            TotalPages       = totalPages,
            TotalCount       = totalSessions
        };

        ViewData["Title"] = "Điểm thưởng & Hạng thành viên";
        return View(vm);
    }
}
