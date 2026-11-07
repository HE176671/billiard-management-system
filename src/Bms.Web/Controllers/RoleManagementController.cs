using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// Admin chỉ chịu trách nhiệm quản trị tài khoản nội bộ & phân quyền (Admin / Manager / Staff).
/// </summary>
[Authorize(Roles = "Admin")]
public class RoleManagementController : Controller
{
    private static readonly string[] InternalRoles = { "Admin", "Manager", "Staff" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _db;

    public RoleManagementController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // GET /RoleManagement
    [HttpGet]
    public async Task<IActionResult> Index(string? search = null, string? roleFilter = null)
    {
        await EnsureInternalRolesExistAsync();
        return View(await BuildViewModelAsync(search, roleFilter));
    }

    // POST /RoleManagement/ChangeRole
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string userId, string newRole, string? search = null, string? roleFilter = null)
    {
        await EnsureInternalRolesExistAsync();
        newRole = (newRole ?? string.Empty).Trim();

        if (!InternalRoles.Contains(newRole))
        {
            TempData["Error"] = "Vai trò phân quyền không hợp lệ (chỉ chấp nhận Admin, Manager hoặc Staff).";
            return RedirectToAction(nameof(Index), new { search, roleFilter });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            TempData["Error"] = "Không tìm thấy tài khoản cần phân quyền.";
            return RedirectToAction(nameof(Index), new { search, roleFilter });
        }

        if (user.Id == CurrentUserId && newRole != "Admin")
        {
            TempData["Error"] = "Không thể tự hạ quyền Admin của chính tài khoản đang đăng nhập.";
            return RedirectToAction(nameof(Index), new { search, roleFilter });
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var internalToRemove = currentRoles.Where(r => InternalRoles.Contains(r)).ToList();
        if (internalToRemove.Any())
        {
            await _userManager.RemoveFromRolesAsync(user, internalToRemove);
        }

        var addRes = await _userManager.AddToRoleAsync(user, newRole);
        if (!addRes.Succeeded)
        {
            TempData["Error"] = "Không thể cập nhật quyền cho tài khoản. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index), new { search, roleFilter });
        }

        await _userManager.UpdateSecurityStampAsync(user);
        TempData["Success"] = $"Đã phân quyền tài khoản {user.FullName} ({user.UserName}) sang vai trò [{newRole}].";
        return RedirectToAction(nameof(Index), new { search, roleFilter });
    }

    // POST /RoleManagement/ToggleActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string userId, string? search = null, string? roleFilter = null)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "Không thể tự khóa tài khoản Admin đang đăng nhập.";
            return RedirectToAction(nameof(Index), new { search, roleFilter });
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Success"] = user.IsActive
            ? $"Đã MỞ KHÓA tài khoản {user.FullName} ({user.UserName})."
            : $"Đã KHÓA quyền truy cập của tài khoản {user.FullName} ({user.UserName}).";

        return RedirectToAction(nameof(Index), new { search, roleFilter });
    }

    // POST /RoleManagement/CreateAccount
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(CreateInternalUserInputModel input)
    {
        await EnsureInternalRolesExistAsync();

        input.FullName = (input.FullName ?? string.Empty).Trim();
        input.UserName = (input.UserName ?? string.Empty).Trim();
        input.Email = (input.Email ?? string.Empty).Trim();
        input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        input.Role = InternalRoles.Contains(input.Role) ? input.Role : "Manager";

        if (string.IsNullOrEmpty(input.FullName) || string.IsNullOrEmpty(input.UserName) || string.IsNullOrEmpty(input.Email))
        {
            TempData["Error"] = "Vui lòng nhập đầy đủ Họ tên, Tên đăng nhập và Email.";
            return RedirectToAction(nameof(Index));
        }

        if (await _userManager.FindByNameAsync(input.UserName) != null)
        {
            TempData["Error"] = $"Tên đăng nhập '{input.UserName}' đã tồn tại.";
            return RedirectToAction(nameof(Index));
        }

        if (await _userManager.FindByEmailAsync(input.Email) != null)
        {
            TempData["Error"] = $"Email '{input.Email}' đã được sử dụng.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(input.PhoneNumber) && await _db.Users.AnyAsync(u => u.PhoneNumber == input.PhoneNumber))
        {
            TempData["Error"] = $"Số điện thoại '{input.PhoneNumber}' đã được sử dụng.";
            return RedirectToAction(nameof(Index));
        }

        string prefix = input.Role == "Admin" ? "AD" : input.Role == "Manager" ? "MG" : "NV";
        string code = await GenerateNextCodeAsync(prefix);

        var user = new ApplicationUser
        {
            UserName = input.UserName,
            Email = input.Email,
            FullName = input.FullName,
            PhoneNumber = input.PhoneNumber,
            EmployeeCode = code,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            IsActive = true,
            EmailConfirmed = true,
            PhoneNumberConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createRes = await _userManager.CreateAsync(user, input.Password);
        if (!createRes.Succeeded)
        {
            TempData["Error"] = "Không thể tạo tài khoản: " + string.Join("; ", createRes.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        await _userManager.AddToRoleAsync(user, input.Role);
        TempData["Success"] = $"Đã tạo tài khoản {user.FullName} ({user.UserName} — Mã {code}) và phân quyền [{input.Role}].";
        return RedirectToAction(nameof(Index));
    }

    private async Task EnsureInternalRolesExistAsync()
    {
        foreach (var r in InternalRoles)
        {
            if (!await _roleManager.RoleExistsAsync(r))
            {
                await _roleManager.CreateAsync(new IdentityRole(r));
            }
        }
    }

    private async Task<string> GenerateNextCodeAsync(string prefix)
    {
        for (int i = 1; i <= 999; i++)
        {
            string candidate = $"{prefix}{i:D3}";
            if (!await _db.Users.AnyAsync(u => u.EmployeeCode == candidate))
                return candidate;
        }
        return $"{prefix}{Random.Shared.Next(1000, 9999)}";
    }

    private async Task<RoleManagementViewModel> BuildViewModelAsync(string? search, string? roleFilter)
    {
        var roleMap = await (from ur in _db.UserRoles
                             join r in _db.Roles on ur.RoleId equals r.Id
                             where InternalRoles.Contains(r.Name!)
                             select new { ur.UserId, RoleName = r.Name! })
                             .ToListAsync();

        var userRoleDict = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName)
                      .OrderBy(r => r == "Admin" ? 0 : r == "Manager" ? 1 : 2)
                      .First());

        var internalUserIds = userRoleDict.Keys.ToHashSet();
        var users = await _db.Users.AsNoTracking()
            .Where(u => internalUserIds.Contains(u.Id))
            .OrderBy(u => u.EmployeeCode)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        var allRows = users.Select(u => new UserRoleRowViewModel
        {
            Id = u.Id,
            FullName = u.FullName,
            UserName = u.UserName ?? string.Empty,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            EmployeeCode = u.EmployeeCode ?? "—",
            CurrentRole = userRoleDict.GetValueOrDefault(u.Id, "Staff"),
            IsActive = u.IsActive,
            CreatedAtUtc = u.CreatedAtUtc
        }).ToList();

        var filtered = allRows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(u =>
                u.FullName.ToLowerInvariant().Contains(s) ||
                u.UserName.ToLowerInvariant().Contains(s) ||
                (u.EmployeeCode != null && u.EmployeeCode.ToLowerInvariant().Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(roleFilter) && InternalRoles.Contains(roleFilter))
        {
            filtered = filtered.Where(u => u.CurrentRole == roleFilter);
        }

        return new RoleManagementViewModel
        {
            Users = filtered.ToList(),
            Search = search,
            RoleFilter = roleFilter,
            AdminCount = allRows.Count(x => x.CurrentRole == "Admin"),
            ManagerCount = allRows.Count(x => x.CurrentRole == "Manager"),
            StaffCount = allRows.Count(x => x.CurrentRole == "Staff")
        };
    }
}
