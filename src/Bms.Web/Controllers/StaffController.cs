using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Admin")]
public class StaffController : Controller
{
    // GET /Staff
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Table");
    }

    // GET /Staff/Bookings
    public IActionResult Bookings()
    {
        return RedirectToAction("Index", "Booking");
    }
}
