using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

    // GET /Profile/ChangePassword  (UC03)
    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Đổi mật khẩu";
        return View(new ChangePasswordViewModel());
    }

    // POST /Profile/ChangePassword
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

        // Refresh cookie sau khi đổi mật khẩu thành công
        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Đổi mật khẩu thành công!";
        return RedirectToAction(nameof(ChangePassword));
    }

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "PasswordTooShort"   => "Mật khẩu tối thiểu 8 ký tự.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất 1 ký tự đặc biệt.",
        "PasswordRequiresDigit"           => "Mật khẩu phải có ít nhất 1 chữ số.",
        "PasswordRequiresUpper"           => "Mật khẩu phải có ít nhất 1 chữ hoa.",
        _ => err.Description
    };
}
