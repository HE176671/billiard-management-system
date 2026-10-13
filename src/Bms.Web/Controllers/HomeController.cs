using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Bms.Web.Models;

namespace Bms.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin"))
                return RedirectToAction("Index", "Employee");
            if (User.IsInRole("Staff"))
                return RedirectToAction("Index", "Staff");
            if (User.IsInRole("Customer"))
                return RedirectToAction("Index", "CustomerProfile");
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
