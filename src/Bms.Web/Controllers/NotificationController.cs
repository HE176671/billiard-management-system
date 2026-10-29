using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly ApplicationDbContext _context;

    public NotificationController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // GET /Notification
    public async Task<IActionResult> Index()
    {
        var uid = CurrentUserId;
        var list = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Content = n.Content,
                IsRead = n.IsRead,
                Type = n.Type,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        var vm = new NotificationListViewModel
        {
            Notifications = list,
            UnreadCount = list.Count(n => !n.IsRead)
        };

        return View(vm);
    }

    // POST /Notification/MarkAllAsRead
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var uid = CurrentUserId;
        await _context.Notifications
            .Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        TempData["Success"] = "Đã đánh dấu tất cả thông báo là đã đọc.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Notification/MarkAsRead
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var uid = CurrentUserId;
        await _context.Notifications
            .Where(n => n.Id == id && n.UserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return RedirectToAction(nameof(Index));
    }

    // GET /Notification/GetUnreadCount (Dành cho badge Topbar AJAX nếu cần)
    [HttpGet]
    public async Task<IActionResult> GetUnreadCount()
    {
        var uid = CurrentUserId;
        var count = await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == uid && !n.IsRead);

        return Json(new { unreadCount = count });
    }
}
