using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize]
public class CustomerController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext         _context;
    private const int PageSize = 10;

    public CustomerController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _userManager = userManager;
        _context     = context;
    }

    #region UC35 - Danh sach, tim kiem, loc va phan trang

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index(string? search, string? status, int? tierId, int page = 1)
    {
        var customerIds = (await _userManager.GetUsersInRoleAsync("Customer"))
                          .Select(u => u.Id).ToHashSet();

        var tiers = await _context.MembershipTiers
                          .OrderBy(t => t.MinPoints)
                          .Select(t => new TierOptionViewModel { Id = t.Id, TierName = t.TierName })
                          .ToListAsync();

        var query = _userManager.Users.Where(u => customerIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.FullName    != null && u.FullName.ToLower().Contains(s)) ||
                (u.UserName    != null && u.UserName.ToLower().Contains(s)) ||
                (u.Email       != null && u.Email.ToLower().Contains(s))    ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }

        if (status == "active")   query = query.Where(u => u.IsActive);
        if (status == "inactive") query = query.Where(u => !u.IsActive);
        if (tierId.HasValue)      query = query.Where(u => u.TierId == tierId.Value);

        var total      = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var rows = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new CustomerRowViewModel
            {
                Id           = u.Id,
                FullName     = u.FullName,
                UserName     = u.UserName,
                Email        = u.Email,
                PhoneNumber  = u.PhoneNumber,
                IsActive     = u.IsActive,
                RewardPoints = u.RewardPoints,
                TierName     = u.Tier == null ? null : u.Tier.TierName,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync();


        var vm = new CustomerListViewModel
        {
            Customers   = rows,
            Search      = search,
            StatusFilter= status,
            TierFilter  = tierId,
            Page        = page,
            TotalPages  = totalPages,
            TotalCount  = total,
            TierOptions = tiers
        };
        return View(vm);
    }

    #endregion

    #region UC35 - Khoa va mo khoa khach hang

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleLock(string id, string? returnTo = null)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Customer"))
            return NotFound();

        user.IsActive = !user.IsActive;

    
        var result = await _userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Không lưu được trạng thái tài khoản. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = user.IsActive
            ? $"Đã mở khóa tài khoản {user.FullName}."
            : $"Đã khóa tài khoản {user.FullName}.";
        return returnTo == "details" ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC35 - Xem chi tiet va sua thong tin khach hang

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Details(string id)
    {
        var user = await GetCustomerOrNull(id);
        return user == null ? NotFound() : View(await ToEditModelAsync(user));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await GetCustomerOrNull(id);
        return user == null ? NotFound() : View(await ToEditModelAsync(user));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(EditCustomerViewModel model)
    {
        var user = await GetCustomerOrNull(model.Id);
        if (user == null) return NotFound();

        var email = model.Email?.Trim() ?? "";
        var phone = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        if (phone != null && await _userManager.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != user.Id))
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại đã được tài khoản khác sử dụng.");
        if (ModelState.IsValid)
        {
            var owner = await _userManager.FindByEmailAsync(email);
            if (owner != null && owner.Id != user.Id)
                ModelState.AddModelError(nameof(model.Email), "Email đã được tài khoản khác sử dụng.");
        }
        if (!ModelState.IsValid) return View(await RefillAsync(user, model));

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = phone;
        IdentityResult result;
        try
        {
            // SetEmailAsync cũng lưu FullName/PhoneNumber vì cùng entity đang theo dõi.
            result = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
                ? await _userManager.SetEmailAsync(user, email)
                : await _userManager.UpdateAsync(user);
        }
        catch (DbUpdateException)
        {
            result = IdentityResult.Failed(new IdentityError { Description = "Email hoặc số điện thoại vừa được tài khoản khác sử dụng." });
        }
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err.Code == "DuplicateEmail" ? "Email đã được tài khoản khác sử dụng." : err.Description);
            return View(await RefillAsync(user, model));
        }

        TempData["Success"] = $"Đã cập nhật thông tin khách hàng {user.FullName}.";
        return RedirectToAction(nameof(Details), new { id = user.Id });
    }

    private async Task<ApplicationUser?> GetCustomerOrNull(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var user = await _userManager.FindByIdAsync(id);
        return user != null && await _userManager.IsInRoleAsync(user, "Customer") ? user : null;
    }

    private async Task<EditCustomerViewModel> ToEditModelAsync(ApplicationUser user) => new()
    {
        Id = user.Id, FullName = user.FullName, Email = user.Email ?? "", PhoneNumber = user.PhoneNumber,
        UserName = user.UserName, RewardPoints = user.RewardPoints, IsActive = user.IsActive,
        CreatedAtUtc = user.CreatedAtUtc,
        TierName = user.TierId == null ? null
            : await _context.MembershipTiers.Where(t => t.Id == user.TierId).Select(t => t.TierName).FirstOrDefaultAsync()
    };

    private async Task<EditCustomerViewModel> RefillAsync(ApplicationUser user, EditCustomerViewModel posted)
    {
        var vm = await ToEditModelAsync(user);
        vm.FullName = posted.FullName; vm.Email = posted.Email; vm.PhoneNumber = posted.PhoneNumber;
        return vm;
    }

    #endregion

    #region Trang tai khoan rieng cua Customer

    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Portal()
    {
        var id = _userManager.GetUserId(User);
        var customer = await _context.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new CustomerRowViewModel {
                FullName = u.FullName, UserName = u.UserName, Email = u.Email,
                PhoneNumber = u.PhoneNumber, RewardPoints = u.RewardPoints,
                TierName = u.Tier == null ? null : u.Tier.TierName
            }).SingleOrDefaultAsync();
        return customer == null ? Challenge() : View(customer);
    }
    #endregion
}
