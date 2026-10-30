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
    private readonly ApplicationDbContext _context;
    private const int PageSize = 10;

    public EmployeeController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    #region UC43 - Danh sach, tim kiem, loc va phan trang

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

        // Mã do hệ thống cấp (ô nhập là readonly), không tin giá trị gửi lên từ form.
        string empCode = await GenerateNextEmployeeCodeAsync();

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

            var roleResult = await _userManager.AddToRoleAsync(user, "Staff");
            if (!roleResult.Succeeded)
            {
                await tx.RollbackAsync();
                ModelState.AddModelError(string.Empty, "Gán quyền Staff thất bại. Vui lòng thử lại.");
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

        TempData["Success"] = $"Đã tạo tài khoản nhân viên {user.FullName} thành công.";
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

        // Mã nhân viên không sửa được trên form; chỉ cấp mới nếu tài khoản cũ còn thiếu.
        var newCode = string.IsNullOrWhiteSpace(user.EmployeeCode)
            ? await GenerateNextEmployeeCodeAsync()
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

        user.IsActive = !user.IsActive;
        // Đổi SecurityStamp để cookie phiên hiện tại mất hiệu lực ngay
        var result = await _userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Không lưu được trạng thái tài khoản. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index));
        }

        var action = user.IsActive ? "mở khóa" : "khóa";
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

        try
        {
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
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

        TempData["Success"] = $"Đã đặt lại mật khẩu cho {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Ham ho tro: kiem tra Staff, dich loi, sinh ma

    // ── Helpers ──

    /// <summary>Lấy user theo ID, chỉ trả về nếu đúng là Staff.</summary>
    private async Task<ApplicationUser?> GetStaffOrNull(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return null;
        return await _userManager.IsInRoleAsync(user, "Staff") ? user : null;
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
    #endregion
}
