using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin,Staff")]
public class CustomerManagementController : Controller
{
    private const int PageSize = 10;
    private const string PlaceholderEmailDomain = "@noemail.bms.local";
    private const string CardPrefix = "MB";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public CustomerManagementController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    // GET /CustomerManagement?search=&tierId=&status=&page=&id=&add=true
    public async Task<IActionResult> Index(string? search, int? tierId, string? status, int page = 1,
        string? id = null, bool add = false)
    {
        CustomerFormViewModel? form = null;
        if (add)
        {
            form = new CustomerFormViewModel { IsActive = true };
            form.TierName = (await DefaultTierAsync())?.TierName;
        }
        else if (!string.IsNullOrEmpty(id))
        {
            var customer = await GetCustomerOrNullAsync(id);
            if (customer == null) return NotFound();
            form = ToForm(customer);
        }

        return View(await BuildViewModelAsync(search, tierId, status, page, form));
    }

    // POST /CustomerManagement/Save  (Id rỗng = thêm mới)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CustomerFormViewModel form, string? search, int? tierId,
        string? status, int page = 1)
    {
        form.FullName = (form.FullName ?? string.Empty).Trim();
        form.PhoneNumber = (form.PhoneNumber ?? string.Empty).Trim();
        form.Email = string.IsNullOrWhiteSpace(form.Email) ? null : form.Email.Trim();

        ApplicationUser? existing = null;
        if (!form.IsNew)
        {
            existing = await GetCustomerOrNullAsync(form.Id!);
            if (existing == null) return NotFound();
        }

        // Unique: số điện thoại & email
        if (ModelState.IsValid)
        {
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == form.PhoneNumber && u.Id != form.Id))
                ModelState.AddModelError(nameof(form.PhoneNumber), "Số điện thoại đã được dùng bởi tài khoản khác.");

            if (form.Email != null)
            {
                var byEmail = await _userManager.FindByEmailAsync(form.Email);
                if (byEmail != null && byEmail.Id != form.Id)
                    ModelState.AddModelError(nameof(form.Email), "Email đã được dùng bởi tài khoản khác.");
            }
        }

        if (!ModelState.IsValid)
            return await RedisplayAsync(form, existing, search, tierId, status, page);

        if (existing == null)
        {
            var defaultTier = await DefaultTierAsync();
            var user = new ApplicationUser
            {
                UserName = form.PhoneNumber,
                // Identity đang bật RequireUniqueEmail nên email trống phải được thay bằng email giữ chỗ
                Email = form.Email ?? PlaceholderEmail(form.PhoneNumber),
                FullName = form.FullName,
                PhoneNumber = form.PhoneNumber,
                IsActive = form.IsActive,
                CreatedAtUtc = DateTime.UtcNow,
                TierId = defaultTier?.Id,
                RewardPoints = 0,
                TotalSpend = 0
            };

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var create = await _userManager.CreateAsync(user);
                if (!create.Succeeded)
                {
                    await tx.RollbackAsync();
                    foreach (var err in create.Errors) ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
                    return await RedisplayAsync(form, null, search, tierId, status, page);
                }
                var role = await _userManager.AddToRoleAsync(user, "Customer");
                if (!role.Succeeded)
                {
                    await tx.RollbackAsync();
                    ModelState.AddModelError(string.Empty, "Gán quyền Customer thất bại. Vui lòng thử lại.");
                    return await RedisplayAsync(form, null, search, tierId, status, page);
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống. Vui lòng thử lại.");
                return await RedisplayAsync(form, null, search, tierId, status, page);
            }

            TempData["Success"] = $"Đã thêm khách hàng {user.FullName}.";
            return RedirectToIndex(user.Id, search, tierId, status, page);
        }

        // ── Cập nhật ──
        var oldPhone = existing.PhoneNumber;
        var wasActive = existing.IsActive;

        existing.FullName = form.FullName;
        existing.PhoneNumber = form.PhoneNumber;
        if (existing.UserName == oldPhone) existing.UserName = form.PhoneNumber;   // username = SĐT thì đồng bộ theo
        // Email trống => dùng email giữ chỗ (Identity đang bắt buộc email duy nhất)
        existing.Email = form.Email ?? PlaceholderEmail(form.PhoneNumber);
        existing.IsActive = form.IsActive;

        var update = await _userManager.UpdateAsync(existing);
        if (!update.Succeeded)
        {
            foreach (var err in update.Errors) ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            _context.Entry(existing).State = EntityState.Detached;
            var fresh = await GetCustomerOrNullAsync(form.Id!);
            return await RedisplayAsync(form, fresh, search, tierId, status, page);
        }
        if (wasActive && !existing.IsActive)
            await _userManager.UpdateSecurityStampAsync(existing);   // đá phiên đăng nhập hiện tại

        TempData["Success"] = $"Đã cập nhật thông tin {existing.FullName}.";
        return RedirectToIndex(existing.Id, search, tierId, status, page);
    }

    // POST /CustomerManagement/ToggleLock
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id, string? search, int? tierId, string? status, int page = 1)
    {
        var user = await GetCustomerOrNullAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        if (!user.IsActive) await _userManager.UpdateSecurityStampAsync(user);

        TempData["Success"] = $"Đã {(user.IsActive ? "mở khóa" : "khóa")} tài khoản {user.FullName}.";
        return RedirectToIndex(id, search, tierId, status, page);
    }

    // POST /CustomerManagement/AssignCard  — "Gắn thẻ thành viên"
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCard(string id, string? cardCode, string? search, int? tierId,
        string? status, int page = 1)
    {
        var user = await GetCustomerOrNullAsync(id);
        if (user == null) return NotFound();

        var code = string.IsNullOrWhiteSpace(cardCode) ? await GenerateNextCardCodeAsync() : cardCode.Trim().ToUpperInvariant();

        if (code.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9\-]+$"))
        {
            TempData["Error"] = "Mã thẻ chỉ gồm chữ, số, dấu gạch ngang và tối đa 20 ký tự.";
            return RedirectToIndex(id, search, tierId, status, page);
        }
        if (await _context.Users.AnyAsync(u => u.EmployeeCode == code && u.Id != id))
        {
            TempData["Error"] = $"Mã thẻ {code} đã được gắn cho người khác.";
            return RedirectToIndex(id, search, tierId, status, page);
        }

        user.EmployeeCode = code;   // mã thẻ lưu vào cột EmployeeCode theo spec
        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded) TempData["Success"] = $"Đã gắn thẻ {code} cho {user.FullName}.";
        else TempData["Error"] = "Không thể gắn thẻ: " + string.Join(" ", result.Errors.Select(TranslateIdentityError));

        return RedirectToIndex(id, search, tierId, status, page);
    }

    // ── Helpers ──

    private IActionResult RedirectToIndex(string? id, string? search, int? tierId, string? status, int page) =>
        RedirectToAction(nameof(Index), new { search, tierId, status, page, id });

    private async Task<IActionResult> RedisplayAsync(CustomerFormViewModel form, ApplicationUser? existing,
        string? search, int? tierId, string? status, int page)
    {
        // Các trường read-only không được post lên nên nạp lại từ DB
        if (existing != null)
        {
            form.TierName = existing.Tier?.TierName;
            form.RewardPoints = existing.RewardPoints;
            form.TotalSpend = existing.TotalSpend;
            form.CardCode = existing.EmployeeCode;
        }
        else
        {
            form.TierName = (await DefaultTierAsync())?.TierName;
        }
        return View(nameof(Index), await BuildViewModelAsync(search, tierId, status, page, form));
    }

    private async Task<CustomerManagementViewModel> BuildViewModelAsync(string? search, int? tierId,
        string? status, int page, CustomerFormViewModel? form)
    {
        var query = CustomersQuery();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(s) ||
                                     (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }
        if (tierId.HasValue) query = query.Where(u => u.TierId == tierId);
        if (status == "active") query = query.Where(u => u.IsActive);
        if (status == "locked") query = query.Where(u => !u.IsActive);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var rows = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new CustomerRowViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                TierName = u.Tier != null ? u.Tier.TierName : null,
                RewardPoints = u.RewardPoints,
                IsActive = u.IsActive
            })
            .ToListAsync();

        var tiers = await _context.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold)
            .Select(t => new TierOptionViewModel { Id = t.Id, Name = t.TierName })
            .ToListAsync();

        return new CustomerManagementViewModel
        {
            Customers = rows,
            Tiers = tiers,
            Search = search,
            TierId = tierId,
            Status = status,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            Form = form
        };
    }

    /// <summary>Tất cả user có role Customer.</summary>
    private IQueryable<ApplicationUser> CustomersQuery()
    {
        var roleId = _context.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefault();
        return _context.Users.AsNoTracking()
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
    }

    private async Task<ApplicationUser?> GetCustomerOrNullAsync(string id)
    {
        var user = await _context.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;
        return await _userManager.IsInRoleAsync(user, "Customer") ? user : null;
    }

    private async Task<MembershipTier?> DefaultTierAsync()
    {
        var tiers = await _context.MembershipTiers.AsNoTracking().ToListAsync();
        return TierHelper.Resolve(tiers, 0);
    }

    private static CustomerFormViewModel ToForm(ApplicationUser u) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        PhoneNumber = u.PhoneNumber ?? string.Empty,
        Email = IsPlaceholderEmail(u.Email) ? null : u.Email,
        IsActive = u.IsActive,
        TierName = u.Tier?.TierName,
        RewardPoints = u.RewardPoints,
        TotalSpend = u.TotalSpend,
        CardCode = u.EmployeeCode
    };

    private static string PlaceholderEmail(string phone) => phone + PlaceholderEmailDomain;

    private static bool IsPlaceholderEmail(string? email) =>
        email != null && email.EndsWith(PlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sinh mã thẻ tiếp theo: MB0001, MB0002...</summary>
    private async Task<string> GenerateNextCardCodeAsync()
    {
        var codes = await _context.Users
            .Where(u => u.EmployeeCode != null && u.EmployeeCode.StartsWith(CardPrefix))
            .Select(u => u.EmployeeCode!)
            .ToListAsync();

        int max = 0;
        foreach (var c in codes)
            if (int.TryParse(c.AsSpan(CardPrefix.Length), out var n) && n > max) max = n;
        return $"{CardPrefix}{(max + 1):D4}";
    }

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "DuplicateUserName" => "Số điện thoại đã được dùng làm tên đăng nhập của tài khoản khác.",
        "DuplicateEmail" => "Email đã được dùng bởi tài khoản khác.",
        "InvalidEmail" => "Email không hợp lệ.",
        _ => err.Description
    };
}
