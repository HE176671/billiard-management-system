using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class EmployeeController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private const int PageSize = 10;

    public EmployeeController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    // GET /Employee
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        // Lấy tất cả user có role Staff
        var staffIds = (await _userManager.GetUsersInRoleAsync("Staff"))
                        .Select(u => u.Id).ToHashSet();

        var query = _userManager.Users
            .Where(u => staffIds.Contains(u.Id));

        // Tìm kiếm
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.FullName != null && u.FullName.ToLower().Contains(s)) ||
                (u.EmployeeCode != null && u.EmployeeCode.ToLower().Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)) ||
                (u.UserName  != null && u.UserName.ToLower().Contains(s)));
        }

        // Lọc trạng thái
        if (status == "active")   query = query.Where(u => u.IsActive);
        if (status == "inactive") query = query.Where(u => !u.IsActive);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var rows = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new EmployeeRowViewModel
            {
                Id           = u.Id,
                FullName     = u.FullName,
                EmployeeCode = u.EmployeeCode,
                UserName     = u.UserName,
                Email        = u.Email,
                PhoneNumber  = u.PhoneNumber,
                HireDate     = u.HireDate,
                IsActive     = u.IsActive,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync();

        var vm = new EmployeeListViewModel
        {
            Employees    = rows,
            Search       = search,
            StatusFilter = status,
            Page         = page,
            TotalPages   = totalPages,
            TotalCount   = total
        };
        return View(vm);
    }

    // GET /Employee/Create
    public async Task<IActionResult> Create()
    {
        var vm = new CreateEmployeeViewModel();
        vm.EmployeeCode = await GenerateNextEmployeeCodeAsync();
        return View(vm);
    }

    // POST /Employee/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmployeeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        string empCode = string.IsNullOrWhiteSpace(model.EmployeeCode) 
            ? await GenerateNextEmployeeCodeAsync() 
            : model.EmployeeCode.Trim().ToUpper();

        var user = new ApplicationUser
        {
            UserName     = model.UserName.Trim(),
            Email        = model.Email.Trim(),
            FullName     = model.FullName.Trim(),
            PhoneNumber  = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim(),
            EmployeeCode = empCode,
            HireDate     = model.HireDate,
            IsActive     = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Tạo user + gán role Staff trong cùng transaction
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var err in createResult.Errors)
                ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, "Staff");
        if (!roleResult.Succeeded)
        {
            // Gán role thất bại → xóa user vừa tạo (rollback thủ công)
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Gán quyền Staff thất bại. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = $"Đã tạo tài khoản nhân viên {user.FullName} thành công.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Employee/Edit/id
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

    // POST /Employee/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditEmployeeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await GetStaffOrNull(model.Id);
        if (user == null) return NotFound();

        user.FullName     = model.FullName.Trim();
        user.PhoneNumber  = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        user.EmployeeCode = string.IsNullOrWhiteSpace(model.EmployeeCode) 
            ? await GenerateNextEmployeeCodeAsync() 
            : model.EmployeeCode.Trim().ToUpper();
        user.HireDate     = model.HireDate;

        var result = await _userManager.UpdateAsync(user);
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

    // POST /Employee/ToggleLock
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await GetStaffOrNull(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        // Đổi SecurityStamp để cookie phiên hiện tại mất hiệu lực ngay
        await _userManager.UpdateSecurityStampAsync(user);
        await _userManager.UpdateAsync(user);

        var action = user.IsActive ? "mở khóa" : "khóa";
        TempData["Success"] = $"Đã {action} tài khoản {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Employee/ResetPassword/id
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

    // POST /Employee/ResetPassword
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetStaffPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await GetStaffOrNull(model.UserId);
        if (user == null) return NotFound();

        // Xóa mật khẩu cũ (nếu có) rồi đặt mới
        if (await _userManager.HasPasswordAsync(user))
            await _userManager.RemovePasswordAsync(user);

        var result = await _userManager.AddPasswordAsync(user, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            return View(model);
        }

        TempData["Success"] = $"Đã đặt lại mật khẩu cho {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──

    /// <summary>Lấy user theo ID, chỉ trả về nếu đúng là Staff.</summary>
    private async Task<ApplicationUser?> GetStaffOrNull(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;
        return await _userManager.IsInRoleAsync(user, "Staff") ? user : null;
    }

    /// <summary>Dịch lỗi Identity sang tiếng Việt dễ hiểu.</summary>
    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "DuplicateUserName"  => "Tên đăng nhập đã tồn tại.",
        "DuplicateEmail"     => "Email đã được dùng bởi tài khoản khác.",
        "PasswordTooShort"   => "Mật khẩu tối thiểu 8 ký tự.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất 1 ký tự đặc biệt (vd: @, #, !).",
        "PasswordRequiresDigit"           => "Mật khẩu phải có ít nhất 1 chữ số.",
        "PasswordRequiresUpper"           => "Mật khẩu phải có ít nhất 1 chữ hoa.",
        _ => err.Description
    };

    /// <summary>Tự động sinh mã nhân viên tiếp theo (NV001, NV002...)</summary>
    private async Task<string> GenerateNextEmployeeCodeAsync()
    {
        var existingCodes = await _userManager.Users
            .Where(u => u.EmployeeCode != null && u.EmployeeCode.StartsWith("NV"))
            .Select(u => u.EmployeeCode)
            .ToListAsync();

        int maxNumber = 0;
        foreach (var code in existingCodes)
        {
            if (code != null && int.TryParse(code.AsSpan(2), out int number))
            {
                if (number > maxNumber) maxNumber = number;
            }
        }
        return $"NV{(maxNumber + 1).ToString("D3")}";
    }
}
