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
    // GET /Staff → sơ đồ bàn là màn hình làm việc chính của nhân viên quầy
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Table");
    }
}
