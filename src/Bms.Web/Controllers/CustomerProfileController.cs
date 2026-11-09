using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// UC08 — Hồ sơ cá nhân Customer.
/// CustomerId luôn lấy từ claims, KHÔNG từ route/query/form (FR-00.2).
/// </summary>
[Authorize(Roles = "Customer")]
public class CustomerProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CustomerProfileController> _logger;

    public CustomerProfileController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ILogger<CustomerProfileController> logger)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
    }

    // GET /CustomerProfile  (route = /Profile — xem spec FR-08.1)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _db.Entry(user).Reference(u => u.Tier).LoadAsync();
        var totalPoints = user.RewardPoints;

        var vm = new ProfileViewModel
        {
            FullName         = user.FullName,
            UserName         = user.UserName,
            Email            = user.Email,
            PhoneNumber      = user.PhoneNumber,
            RankName         = user.Tier?.TierName ?? "Member",
            TotalPoints      = totalPoints,
            CreatedAtUtc     = user.CreatedAtUtc,
            WalletBalance = user.WalletBalance
        };

        vm.Spend12Months = await new TierEvaluationService(_db).GetSpendLast12MonthsAsync(user.Id);
        var next = await _db.MembershipTiers.AsNoTracking()
            .Where(t => t.SpendThreshold > (user.Tier != null ? user.Tier.SpendThreshold : 0m))
            .OrderBy(t => t.SpendThreshold).FirstOrDefaultAsync();
        vm.NextTierName = next?.TierName;
        vm.AmountToNextTier = next == null ? 0m : Math.Max(0m, next.SpendThreshold - vm.Spend12Months);
        ViewData["Title"] = "Hồ sơ của tôi";
        return View(vm);
    }

    // GET /CustomerProfile/Edit
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await _db.Entry(user).Reference(u => u.Tier).LoadAsync();
        var totalPoints = user.RewardPoints;

        var vm = new CustomerEditProfileViewModel
        {
            FullName     = user.FullName,
            Email        = user.Email ?? string.Empty,
            PhoneNumber  = user.PhoneNumber ?? string.Empty,
            UserName     = user.UserName,
            RankName     = user.Tier?.TierName ?? "Member",
            TotalPoints  = totalPoints
        };

        ViewData["Title"] = "Chỉnh sửa hồ sơ";
        return View(vm);
    }

    // POST /CustomerProfile/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerEditProfileViewModel model)
    {
        // Validate server-side (FR-00.4)
        if (!ModelState.IsValid)
        {
            await RestoreReadOnlyFields(model);
            ViewData["Title"] = "Chỉnh sửa hồ sơ";
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // Kiểm tra email trùng với tài khoản KHÁC (FR-08.5)
        var emailOwner = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (emailOwner != null && emailOwner.Id != user.Id)
        {
            ModelState.AddModelError(nameof(model.Email), "Email đã được sử dụng.");
            await RestoreReadOnlyFields(model);
            ViewData["Title"] = "Chỉnh sửa hồ sơ";
            return View(model);
        }

        // Kiểm tra số điện thoại trùng với tài khoản KHÁC (FR-08.5)
        var phone = model.PhoneNumber.Trim();
        var phoneOwner = await _db.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone && u.Id != user.Id);
        if (phoneOwner != null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại đã được sử dụng.");
            await RestoreReadOnlyFields(model);
            ViewData["Title"] = "Chỉnh sửa hồ sơ";
            return View(model);
        }

        // Cập nhật — KHÔNG đổi UserName, Role, điểm, hạng (FR-08.2)
        user.FullName    = model.FullName.Trim();
        user.PhoneNumber = phone;

        // Đổi email qua UserManager để chuẩn hoá NormalizedEmail
        var setEmailResult = await _userManager.SetEmailAsync(user, model.Email.Trim());
        if (!setEmailResult.Succeeded)
        {
            foreach (var err in setEmailResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            await RestoreReadOnlyFields(model);
            ViewData["Title"] = "Chỉnh sửa hồ sơ";
            return View(model);
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var err in updateResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            await RestoreReadOnlyFields(model);
            ViewData["Title"] = "Chỉnh sửa hồ sơ";
            return View(model);
        }

        TempData["Success"] = "Cập nhật hồ sơ thành công.";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──

    private async Task RestoreReadOnlyFields(CustomerEditProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return;
        model.UserName  = user.UserName;
        await _db.Entry(user).Reference(u => u.Tier).LoadAsync();
        var pts = user.RewardPoints;
        model.TotalPoints = pts;
        model.RankName    = user.Tier?.TierName ?? "Member";
    }
}
