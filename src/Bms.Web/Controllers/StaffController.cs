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
[Authorize(Roles = "Staff,Manager")]
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

    // GET /Staff (Đã gộp về trang Ca của tôi /Shift)
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Shift");
    }
}
