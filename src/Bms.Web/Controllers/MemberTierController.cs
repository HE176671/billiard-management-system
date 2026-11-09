using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

/// <summary>Địa chỉ cũ chuyển về cấu hình duy nhất theo tích lũy 365 ngày.</summary>
[Authorize(Roles = "Admin,Manager")]
public class MemberTierController : Controller
{
    public MemberTierController(ApplicationDbContext context) { }
    public Task<IActionResult> Index() => Task.FromResult<IActionResult>(RedirectToAction("Index", "TierConfig"));
    public IActionResult Create() => RedirectToAction("Index", "TierConfig");
    public Task<IActionResult> Edit(int id) => Index();
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Create(CreateEditTierViewModel model) => LegacyPost();
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(CreateEditTierViewModel model) => LegacyPost();
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(int id) => LegacyPost();
    private Task<IActionResult> LegacyPost()
    {
        TempData["Error"] = "Màn hình cấu hình hạng đã được hợp nhất. Vui lòng cập nhật tại Hạng thành viên theo tích lũy 365 ngày.";
        return Index();
    }
}
