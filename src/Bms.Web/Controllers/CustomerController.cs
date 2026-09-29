using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

/// <summary>
/// Placeholder cho thanh vien 3.
/// Thanh vien 3: them action dat ban, xem lich su dat ban cua minh.
/// </summary>
[Authorize(Roles = "Customer")]
public class CustomerController : Controller
{
    // GET /Customer
    public IActionResult Index()
    {
        ViewData["Title"] = "Customer Dashboard";
        return View();
    }
}
