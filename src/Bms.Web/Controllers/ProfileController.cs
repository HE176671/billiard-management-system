using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ProfileController(UserManager<ApplicationUser> userManager,
                             SignInManager<ApplicationUser> signInManager)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
    }

    #region Ho so ca nhan: xem va sua ho ten, so dien thoai

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        return View(await ToProfileAsync(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(EditProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var phone = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        if (phone != null && await _userManager.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != user.Id))
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại đã được tài khoản khác sử dụng.");
        if (!ModelState.IsValid)
        {
            var display = await ToProfileAsync(user);
            display.FullName = model.FullName;
            display.PhoneNumber = model.PhoneNumber;
            return View(display);
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = phone;
        IdentityResult result;
        try { result = await _userManager.UpdateAsync(user); }
        catch (DbUpdateException)
        {
            // Hai người cùng lưu một số điện thoại: UX_User_Phone chặn ở SQL.
            result = IdentityResult.Failed(new IdentityError { Description = "Số điện thoại đã được tài khoản khác sử dụng." });
        }
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors) ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            return View(await ToProfileAsync(user));
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Đã cập nhật hồ sơ.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<EditProfileViewModel> ToProfileAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new EditProfileViewModel
        {
            FullName = user.FullName, PhoneNumber = user.PhoneNumber, UserName = user.UserName,
            Email = user.Email, EmployeeCode = user.EmployeeCode,
            RoleLabel = roles.Contains("Admin") ? "Quản trị viên" : roles.Contains("Staff") ? "Nhân viên" : "Khách hàng"
        };
    }

    #endregion

    #region UC03 - Hien thi form doi mat khau

    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Đổi mật khẩu";
        return View(new ChangePasswordViewModel());
    }

    #endregion

    #region UC03 - Kiem tra mat khau cu va luu mat khau moi

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        ViewData["Title"] = "Đổi mật khẩu";
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
            {
                var msg = err.Code == "PasswordMismatch"
                    ? "Mật khẩu hiện tại không đúng."
                    : TranslateIdentityError(err);
                ModelState.AddModelError(string.Empty, msg);
            }
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Đổi mật khẩu thành công!";
        return RedirectToAction(nameof(ChangePassword));
    }

    #endregion

    #region Dich loi Identity

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "PasswordTooShort"   => "Mật khẩu tối thiểu 6 ký tự.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất 1 ký tự đặc biệt.",
        "PasswordRequiresDigit"           => "Mật khẩu phải có ít nhất 1 chữ số.",
        "PasswordRequiresUpper"           => "Mật khẩu phải có ít nhất 1 chữ hoa.",
        _ => err.Description
    };
    #endregion
}
