using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin,Staff")]
public class ReviewController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
