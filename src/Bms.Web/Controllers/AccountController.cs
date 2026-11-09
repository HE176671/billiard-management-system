using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
    }

    #region UC01 - Dang nhap noi bo (Admin & Staff)

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var loginInput = (model.Username ?? string.Empty).Trim();
        var user = await _userManager.FindByNameAsync(loginInput)
                   ?? await _userManager.FindByEmailAsync(loginInput)
                   ?? await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == loginInput);

        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");
            return View(model);
        }

        // Hệ thống quản lý nội bộ: cho phép Admin, Manager và Staff đăng nhập
        bool isInternalAccount = await _userManager.IsInRoleAsync(user, "Admin")
                              || await _userManager.IsInRoleAsync(user, "Manager")
                              || await _userManager.IsInRoleAsync(user, "Staff");
        if (!isInternalAccount)
        {
            ModelState.AddModelError(string.Empty, "Hệ thống quản lý nội bộ chỉ dành cho Nhân viên, Quản lý và Quản trị viên.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return await RedirectByRoleAsync(user);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản bị khóa tạm thời. Thử lại sau 5 phút.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");
        return View(model);
    }

    #endregion

    #region UC02 - Dang xuat

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }

    #endregion

    #region Thong bao tu choi truy cap

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    #endregion

    #region Dieu huong theo role

    private async Task<IActionResult> RedirectByRoleAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Index", "Employee");
        if (await _userManager.IsInRoleAsync(user, "Manager"))
            return RedirectToAction("Index", "Dashboard");
        if (await _userManager.IsInRoleAsync(user, "Staff"))
            return RedirectToAction("Index", "Table");
        return RedirectToAction("Login", "Account");
    }

    #endregion
}
