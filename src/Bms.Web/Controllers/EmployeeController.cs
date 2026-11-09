using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")] // TV5: chỉ Admin quản lý tài khoản (ma trận phân quyền SRS I.4.4)
public class EmployeeController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private const int PageSize = 10;

    public EmployeeController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    #region UC43 - Danh sach, tim kiem, loc va phan trang

    // GET /Employee — TV5: một màn duy nhất quản lý mọi tài khoản nội bộ (Admin / Manager / Staff).
    public async Task<IActionResult> Index(string? search, string? status, string? role, int page = 1)
    {
        var roleOf = await LoadInternalRoleMapAsync();
        var roleFilter = role != null && InternalRoles.Contains(role) ? role : null;

        var targetIds = roleFilter == null
            ? roleOf.Keys.ToHashSet()
            : roleOf.Where(kv => kv.Value == roleFilter).Select(kv => kv.Key).ToHashSet();

        var query = _userManager.Users.Where(u => targetIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.FullName != null && u.FullName.ToLower().Contains(s)) ||
                (u.EmployeeCode != null && u.EmployeeCode.ToLower().Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)) ||
                (u.UserName  != null && u.UserName.ToLower().Contains(s)));
        }

        if (status == "active")   query = query.Where(u => u.IsActive);
        if (status == "inactive") query = query.Where(u => !u.IsActive);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var allIds = roleOf.Keys.ToList();
        var vm = new EmployeeListViewModel
        {
            Employees = users.Select(u => new EmployeeRowViewModel
            {
                Id           = u.Id,
                FullName     = u.FullName,
                EmployeeCode = u.EmployeeCode,
                UserName     = u.UserName,
                Email        = u.Email,
                PhoneNumber  = u.PhoneNumber,
                RoleName     = roleOf.GetValueOrDefault(u.Id, "Staff"),
                HireDate     = u.HireDate,
                IsActive     = u.IsActive,
                CreatedAtUtc = u.CreatedAtUtc
            }).ToList(),
            Search        = search,
            StatusFilter  = status,
            RoleFilter    = roleFilter,
            Page          = page,
            TotalPages    = totalPages,
            TotalCount    = total,
            ActiveCount   = await _userManager.Users.CountAsync(u => allIds.Contains(u.Id) && u.IsActive),
            AdminCount    = roleOf.Count(kv => kv.Value == "Admin"),
            ManagerCount  = roleOf.Count(kv => kv.Value == "Manager"),
            StaffCount    = roleOf.Count(kv => kv.Value == "Staff"),
            CurrentUserId = CurrentUserId
        };
        return View(vm);
    }

    // POST /Employee/ChangeRole — Admin đổi vai trò (thay màn "Phân quyền tài khoản" cũ).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string id, string newRole)
    {
        newRole = (newRole ?? "").Trim();
        if (!InternalRoles.Contains(newRole))
        {
            TempData["Error"] = "Vai trò không hợp lệ (chỉ Admin, Manager hoặc Staff).";
            return RedirectToAction(nameof(Index));
        }

        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();
        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "Không thể tự đổi vai trò của tài khoản đang đăng nhập.";
            return RedirectToAction(nameof(Index));
        }

        var currentRoles = (await _userManager.GetRolesAsync(user)).Where(r => InternalRoles.Contains(r)).ToList();
        var oldRole = currentRoles.FirstOrDefault() ?? "—";
        if (currentRoles.Count == 1 && oldRole == newRole)
        {
            TempData["Success"] = $"{user.FullName} đã là {newRole}.";
            return RedirectToAction(nameof(Index));
        }
        if (currentRoles.Contains("Admin") && newRole != "Admin" && await IsLastActiveAdminAsync(user))
        {
            TempData["Error"] = "Không thể hạ quyền Admin cuối cùng của hệ thống.";
            return RedirectToAction(nameof(Index));
        }

        if (currentRoles.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        var add = await _userManager.AddToRoleAsync(user, newRole);
        if (!add.Succeeded)
        {
            TempData["Error"] = "Không đổi được vai trò. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index));
        }
        // Đăng xuất phiên đang mở của tài khoản để quyền mới có hiệu lực ngay.
        await _userManager.UpdateSecurityStampAsync(user);
        await AuditAsync(user.Id, $"Đổi vai trò {user.FullName} ({user.UserName}) từ {oldRole} sang {newRole}.", oldRole, newRole);

        TempData["Success"] = $"Đã đổi vai trò {user.FullName} từ {oldRole} sang {newRole}.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC43 - Tao nhan vien va gan role Staff
 
    public async Task<IActionResult> Create()
    {
        var vm = new CreateEmployeeViewModel();
        vm.EmployeeCode = await GenerateNextEmployeeCodeAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmployeeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        string targetRole = InternalRoles.Contains(model.Role) ? model.Role : "Staff";
        string prefix = CodePrefix(targetRole);

        // Mã do hệ thống cấp (ô nhập là readonly), không tin giá trị gửi lên từ form.
        string empCode = await GenerateNextEmployeeCodeAsync(prefix);

        await ValidateUniqueAsync(model.PhoneNumber, empCode, excludeUserId: null,
            nameof(model.PhoneNumber), nameof(model.EmployeeCode));
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName     = model.UserName.Trim(),
            Email        = model.Email.Trim(),
            FullName     = ValidationRules.CollapseSpaces(model.FullName),
            PhoneNumber  = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim(),
            EmployeeCode = empCode,
            HireDate     = model.HireDate,
            IsActive     = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var createResult = await _userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                await tx.RollbackAsync();
                foreach (var err in createResult.Errors)
                    ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
                return View(model);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, targetRole);
            if (!roleResult.Succeeded)
            {
                await tx.RollbackAsync();
                ModelState.AddModelError(string.Empty, $"Gán quyền {targetRole} thất bại. Vui lòng thử lại.");
                return View(model);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống. Vui lòng thử lại.");
            return View(model);
        }

        await AuditAsync(user.Id, $"Tạo tài khoản {user.FullName} ({user.UserName}, {empCode}) với vai trò {targetRole}.", null, targetRole);
        TempData["Success"] = $"Đã tạo tài khoản {user.FullName} ({targetRole}, mã {empCode}) thành công.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC43 - Sua thong tin nhan vien

    public async Task<IActionResult> Edit(string id)
    {
        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();

        var vm = new EditEmployeeViewModel
        {
            Id           = user.Id,
            FullName     = user.FullName,
            PhoneNumber  = user.PhoneNumber,
            EmployeeCode = user.EmployeeCode,
            HireDate     = user.HireDate,
            UserName     = user.UserName,
            Email        = user.Email
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditEmployeeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await GetStaffOrNull(model.Id);
        if (user == null) return NotFound();

        // Mã người dùng không sửa được trên form; chỉ cấp mới nếu tài khoản cũ còn thiếu.
        string prefix = CodePrefix((await _userManager.GetRolesAsync(user)).FirstOrDefault(r => InternalRoles.Contains(r)) ?? "Staff");
        var newCode = string.IsNullOrWhiteSpace(user.EmployeeCode)
            ? await GenerateNextEmployeeCodeAsync(prefix)
            : user.EmployeeCode;
        await ValidateUniqueAsync(model.PhoneNumber, newCode, user.Id,
            nameof(model.PhoneNumber), nameof(model.EmployeeCode));
        if (!ModelState.IsValid)
        {
            model.UserName = user.UserName;
            model.Email    = user.Email;
            return View(model);
        }

        user.FullName     = ValidationRules.CollapseSpaces(model.FullName);
        user.PhoneNumber  = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        user.EmployeeCode = newCode;
        user.HireDate     = model.HireDate;

        IdentityResult result;
        try { result = await _userManager.UpdateAsync(user); }
        catch (DbUpdateException)
        {
            // Request đồng thời lấy cùng SĐT/mã: unique index SQL chặn, không để lỗi 500.
            result = IdentityResult.Failed(new IdentityError { Description = "Số điện thoại hoặc mã nhân viên vừa được người khác sử dụng." });
        }
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            model.UserName = user.UserName;
            model.Email    = user.Email;
            return View(model);
        }

        TempData["Success"] = $"Đã cập nhật thông tin {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC43 - Khoa va mo khoa nhan vien

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();
        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "Không thể tự khóa tài khoản đang đăng nhập.";
            return RedirectToAction(nameof(Index));
        }
        if (user.IsActive && await _userManager.IsInRoleAsync(user, "Admin") && await IsLastActiveAdminAsync(user))
        {
            TempData["Error"] = "Không thể khóa Admin cuối cùng đang hoạt động.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        // Đổi SecurityStamp để cookie phiên hiện tại mất hiệu lực ngay
        var result = await _userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Không lưu được trạng thái tài khoản. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index));
        }

        var action = user.IsActive ? "mở khóa" : "khóa";
        await AuditAsync(user.Id, $"Đã {action} tài khoản {user.FullName} ({user.UserName}).",
            user.IsActive ? "Khóa" : "Hoạt động", user.IsActive ? "Hoạt động" : "Khóa");
        TempData["Success"] = $"Đã {action} tài khoản {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC43 - Xoa nhan vien

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();
        if (user.Id == CurrentUserId || await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Error"] = "Không xóa tài khoản Admin. Hãy đổi vai trò hoặc khóa tài khoản.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                await AuditAsync(null, $"Xóa tài khoản chưa phát sinh dữ liệu: {user.FullName} ({user.UserName}, {user.EmployeeCode}).", user.UserName, null);
                TempData["Success"] = $"Đã xóa vĩnh viễn tài khoản {user.FullName}.";
            }
            else
            {
                TempData["Error"] = "Không thể xóa tài khoản. Lỗi hệ thống.";
            }
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"Không thể xóa {user.FullName} vì nhân viên này đã có dữ liệu giao dịch/ca làm việc. Khuyến nghị: Sử dụng tính năng Khóa tài khoản.";
        }
        catch (Exception)
        {
            TempData["Error"] = "Đã xảy ra lỗi khi xóa tài khoản.";
        }

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region UC44 - Reset mat khau nhan vien

    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();
        return View(new ResetStaffPasswordViewModel
        {
            UserId   = user.Id,
            FullName = user.FullName,
            UserName = user.UserName ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetStaffPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await GetStaffOrNull(model.UserId);
        if (user == null) return NotFound();

        // Validate and replace atomically: a rejected password must preserve the old one.
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            return View(model);
        }

        await _userManager.UpdateSecurityStampAsync(user);
        await AuditAsync(user.Id, $"Đặt lại mật khẩu cho {user.FullName} ({user.UserName}).", null, null);
        TempData["Success"] = $"Đã đặt lại mật khẩu cho {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Ham ho tro: kiem tra Staff, dich loi, sinh ma

    // ── Helpers ──

    /// <summary>Lấy user theo ID, chỉ trả về nếu là tài khoản nội bộ (Admin / Manager / Staff).</summary>
    private async Task<ApplicationUser?> GetStaffOrNull(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Any(r => InternalRoles.Contains(r)) ? user : null;
    }


    private static readonly string[] InternalRoles = { "Admin", "Manager", "Staff" };

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private static string CodePrefix(string role) => role switch { "Admin" => "AD", "Manager" => "MG", _ => "NV" };

    /// <summary>UserId → vai trò nội bộ (ưu tiên Admin > Manager > Staff nếu dữ liệu cũ có nhiều vai trò).</summary>
    private async Task<Dictionary<string, string>> LoadInternalRoleMapAsync()
    {
        var map = new Dictionary<string, string>();
        foreach (var r in new[] { "Staff", "Manager", "Admin" })
            foreach (var u in await _userManager.GetUsersInRoleAsync(r))
                map[u.Id] = r;
        return map;
    }

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user) =>
        !(await _userManager.GetUsersInRoleAsync("Admin")).Any(a => a.Id != user.Id && a.IsActive);

    /// <summary>Ghi Nhật ký hệ thống (bỏ qua khi chạy test không có DI).</summary>
    private async Task AuditAsync(string? targetUserId, string description, string? oldValue, string? newValue)
    {
        var audit = HttpContext?.RequestServices?.GetService<IAuditLogService>();
        if (audit == null) return;
        await audit.LogAsync(AuditActions.AccountChange, "User", targetUserId, description, oldValue, newValue);
    }

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "DuplicateUserName"  => "Tên đăng nhập đã tồn tại.",
        "DuplicateEmail"     => "Email đã được dùng bởi tài khoản khác.",
        "PasswordTooShort"   => "Mật khẩu tối thiểu 6 ký tự.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất 1 ký tự đặc biệt (vd: @, #, !).",
        "PasswordRequiresDigit"           => "Mật khẩu phải có ít nhất 1 chữ số.",
        "PasswordRequiresUpper"           => "Mật khẩu phải có ít nhất 1 chữ hoa.",
        "PasswordRequiresLower"           => "Mật khẩu phải có ít nhất 1 chữ thường.",
        "InvalidUserName"    => "Tên đăng nhập chứa ký tự không hợp lệ.",
        "InvalidEmail"       => "Email không hợp lệ.",
        _ => err.Description
    };

    /// <summary>Kiểm tra trước unique index UX_User_Phone / UX_User_EmployeeCode để báo lỗi rõ ràng.</summary>
    private async Task ValidateUniqueAsync(string? phone, string? code, string? excludeUserId,
        string phoneField, string codeField)
    {
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (phone != null && await _userManager.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != excludeUserId))
            ModelState.AddModelError(phoneField, "Số điện thoại đã được tài khoản khác sử dụng.");
        if (!string.IsNullOrEmpty(code) && await _userManager.Users.AnyAsync(u => u.EmployeeCode == code && u.Id != excludeUserId))
            ModelState.AddModelError(codeField, "Mã nhân viên đã tồn tại.");
    }

    /// <summary>Tự động sinh mã người dùng tiếp theo (NV001, MG001...)</summary>
    private async Task<string> GenerateNextEmployeeCodeAsync(string prefix = "NV")
    {
        var existingCodes = await _userManager.Users
            .Where(u => u.EmployeeCode != null && u.EmployeeCode.StartsWith(prefix))
            .Select(u => u.EmployeeCode)
            .ToListAsync();

        int maxNumber = 0;
        foreach (var code in existingCodes)
        {
            if (code != null && code.Length > prefix.Length && int.TryParse(code.AsSpan(prefix.Length), out int number))
            {
                if (number > maxNumber) maxNumber = number;
            }
        }
        return $"{prefix}{(maxNumber + 1).ToString("D3")}";
    }
    #endregion
}
