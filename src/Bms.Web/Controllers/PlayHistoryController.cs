using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// UC10 — Lịch sử phiên chơi của Customer.
/// CustomerId luôn lấy từ claims (FR-00.2). IDOR → 404 (FR-10.5).
/// </summary>
[Authorize(Roles = "Customer")]
public class PlayHistoryController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PlayHistoryController> _logger;
    private const int PageSize = 10;

    public PlayHistoryController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<PlayHistoryController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // GET /PlayHistory  (FR-10.1)
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = _userManager.GetUserId(User)!;

        // Server-side paging (FR, NFR phân trang)
        var query = _db.PlaySessions
            .Where(s => s.CustomerId == userId)
            .Include(s => s.Table).ThenInclude(t => t.TableType);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var rows = await query
            .OrderByDescending(s => s.StartAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(s => new PlayHistoryRowViewModel
            {
                Id             = s.Id,
                TableCode      = s.Table.TableCode,
                TableTypeName  = s.Table.TableType.Name,
                StartAtUtc     = s.StartAtUtc,
                EndAtUtc       = s.EndAtUtc,
                Status         = s.Status,
                PlaytimeAmount = s.PlaytimeAmount
            })
            .ToListAsync();

        var vm = new PlayHistoryListViewModel
        {
            Sessions   = rows,
            Page       = page,
            TotalPages = totalPages,
            TotalCount = total
        };

        ViewData["Title"] = "Lịch sử phiên chơi";
        return View(vm);
    }

    // GET /PlayHistory/Detail/5  (FR-10.2)
    [HttpGet]
    public async Task<IActionResult> Detail(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        // Lọc đúng CustomerId — IDOR trả 404 không tiết lộ tồn tại (FR-10.5, FR-00.2)
        var session = await _db.PlaySessions
            .Include(s => s.Table).ThenInclude(t => t.TableType)
            .FirstOrDefaultAsync(s => s.Id == id && s.CustomerId == userId);

        if (session == null) return NotFound();

        var vm = new PlayHistoryDetailViewModel
        {
            Id                = session.Id,
            TableCode         = session.Table.TableCode,
            TableTypeName     = session.Table.TableType.Name,
            FloorNumber       = session.Table.FloorNumber,
            StartAtUtc        = session.StartAtUtc,
            EndAtUtc          = session.EndAtUtc,
            HourlyRateSnapshot= session.HourlyRateSnapshot,
            PlaytimeAmount    = session.PlaytimeAmount,
            Status            = session.Status,
            SessionPoints     = session.Status == "Closed" && session.PlaytimeAmount.HasValue
                                    ? LoyaltyConstants.CalcSessionPoints(session.PlaytimeAmount.Value)
                                    : 0
        };

        ViewData["Title"] = $"Chi tiết phiên #{session.Id}";
        return View(vm);
    }
}
