using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// Trang chủ nhân viên (TV5 duy trì khung). Mỗi workflow dùng controller riêng
/// (TV1 bàn/booking/phiên, TV3 thanh toán, TV4 món) và thêm lối tắt vào view này.
/// </summary>
[Authorize(Roles = "Staff,Admin")]
public class StaffController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IShiftService _shifts;
    private readonly UserManager<ApplicationUser> _users;

    public StaffController(ApplicationDbContext db, IShiftService shifts, UserManager<ApplicationUser> users)
    {
        _db = db;
        _shifts = shifts;
        _users = users;
    }

    // GET /Staff
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Trang nhân viên";
        var active = await _shifts.GetActiveShiftAsync(_users.GetUserId(User)!);
        var vm = new StaffHomeViewModel
        {
            FullName = (await _users.GetUserAsync(User))?.FullName ?? User.Identity?.Name ?? "",
            ActiveShift = active == null ? null : await _shifts.GetSummaryAsync(active.Id),
            TableCounts = await _db.BilliardTables.AsNoTracking()
                .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count)
        };
        return View(vm);
    }
}
