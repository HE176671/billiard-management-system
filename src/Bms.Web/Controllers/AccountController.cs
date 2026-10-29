using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bms.Web.Services;

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

    #region UC01 - Dang nhap

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

        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Ten dang nhap hoac mat khau khong dung.");
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
            ModelState.AddModelError(string.Empty, "Tai khoan bi khoa tam thoi. Thu lai sau 5 phut.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Ten dang nhap hoac mat khau khong dung.");
        return View(model);
    }

    #endregion

    #region UC19 - Dang ky khach hang

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var phone = model.PhoneNumber?.Trim();
        var email = model.Email.Trim();

        // Validate trùng SĐT / Email
        if (!string.IsNullOrWhiteSpace(phone) && await _context.Users.AnyAsync(u => u.PhoneNumber == phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại đã được đăng ký.");
            return View(model);
        }
        if (await _userManager.FindByEmailAsync(email) != null)
        {
            ModelState.AddModelError(nameof(model.Email), "Email đã được dùng bởi tài khoản khác.");
            return View(model);
        }

        // Hạng mặc định: hạng thấp nhất (Member)
        var tiers = await _context.MembershipTiers.AsNoTracking().ToListAsync();
        var defaultTier = TierHelper.Resolve(tiers, 0)
            ?? await _context.MembershipTiers.Where(t => t.MinPoints == 0).OrderBy(t => t.Id).FirstOrDefaultAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            UserName     = model.UserName.Trim(),
            Email        = email,
            FullName     = model.FullName.Trim(),
            PhoneNumber  = phone,
            IsActive     = true,
            CreatedAtUtc = DateTime.UtcNow,
            TierId       = defaultTier?.Id,
            RewardPoints = 0,
            TotalSpend   = 0
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync();
            foreach (var err in createResult.Errors)
                ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, "Customer");
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Đăng ký thất bại. Vui lòng thử lại.");
            return View(model);
        }

        await transaction.CommitAsync();
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Profile", "CustomerPortal");
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

    #region Dieu huong theo role va dich loi Identity

    private async Task<IActionResult> RedirectByRoleAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Index", "Dashboard");
        if (await _userManager.IsInRoleAsync(user, "Staff"))
            return RedirectToAction("Index", "Staff");
        if (await _userManager.IsInRoleAsync(user, "Customer"))
            return RedirectToAction("Profile", "CustomerPortal");
        return RedirectToAction("Index", "Home");
    }

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "DuplicateUserName"  => "Ten dang nhap da ton tai.",
        "DuplicateEmail"     => "Email da duoc dung boi tai khoan khac.",
        "PasswordTooShort"   => "Mat khau toi thieu 8 ky tu.",
        "PasswordRequiresNonAlphanumeric" => "Mat khau phai co it nhat 1 ky tu dac biet (vd: @, #, !).",
        "PasswordRequiresDigit"           => "Mat khau phai co it nhat 1 chu so.",
        "PasswordRequiresUpper"           => "Mat khau phai co it nhat 1 chu hoa.",
        _ => err.Description
    };
    #endregion
}
