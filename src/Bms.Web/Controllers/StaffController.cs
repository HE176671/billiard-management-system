using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

/// <summary>
/// Placeholder cho thanh vien 1 va 2.
/// Thanh vien 1: them action quan ly ban, mo phien, dong phien.
/// Thanh vien 2: them action quan ly dat ban, check-in, huy dat.
/// </summary>
[Authorize(Roles = "Staff,Admin")]
public class StaffController : Controller
{
    // GET /Staff
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Table");
    }
}
