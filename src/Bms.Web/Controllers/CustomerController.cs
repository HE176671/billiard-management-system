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
    public async Task<IActionResult> ToggleLock(string id)
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
        return RedirectToAction(nameof(Index));
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
