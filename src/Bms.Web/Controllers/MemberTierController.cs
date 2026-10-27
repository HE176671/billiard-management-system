using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;


[Authorize(Roles = "Admin")]
public class MemberTierController : Controller
{
    private readonly ApplicationDbContext _context;

    public MemberTierController(ApplicationDbContext context) => _context = context;

    #region Danh sach hang va so nguoi su dung

    public async Task<IActionResult> Index()
    {
        var tiers = await _context.MembershipTiers.OrderBy(t => t.MinPoints).ToListAsync();

        // Đếm số khách hàng ở mỗi hạng
        var userCounts = await _context.Users
            .Where(u => u.TierId != null)
            .GroupBy(u => u.TierId!.Value)
            .Select(g => new { TierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TierId, x => x.Count);

        var vm = new MemberTierListViewModel
        {
            Tiers = tiers.Select(t => new MemberTierRowViewModel
            {
                Id              = t.Id,
                TierName        = t.TierName,
                MinPoints       = t.MinPoints,
                DiscountPercent = t.DiscountPercent,
                UserCount       = userCounts.GetValueOrDefault(t.Id, 0)
            }).ToList()
        };
        return View(vm);
    }

    #endregion

    #region Them hang va kiem tra nguong diem

    public IActionResult Create() => View("CreateEdit", new CreateEditTierViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEditTierViewModel model)
    {
        model.Id = 0;
        if (!ModelState.IsValid) return View("CreateEdit", model);

        if (await _context.MembershipTiers.AnyAsync(t => t.MinPoints == model.MinPoints))
        {
            ModelState.AddModelError(nameof(model.MinPoints), "Ngưỡng điểm này đã được dùng cho một hạng khác.");
            return View("CreateEdit", model);
        }

        if (await _context.MembershipTiers.AnyAsync(t => t.TierName == model.TierName.Trim()))
        {
            ModelState.AddModelError(nameof(model.TierName), "Tên hạng này đã tồn tại.");
            return View("CreateEdit", model);
        }

        _context.MembershipTiers.Add(new MembershipTier
        {
            TierName        = model.TierName.Trim(),
            MinPoints       = model.MinPoints,
            DiscountPercent = model.DiscountPercent
        });
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã thêm hạng '{model.TierName}'.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Sua hang va nguong diem

    public async Task<IActionResult> Edit(int id)
    {
        var tier = await _context.MembershipTiers.FindAsync(id);
        if (tier == null) return NotFound();

        return View("CreateEdit", new CreateEditTierViewModel
        {
            Id              = tier.Id,
            TierName        = tier.TierName,
            MinPoints       = tier.MinPoints,
            DiscountPercent = tier.DiscountPercent
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CreateEditTierViewModel model)
    {
        if (!ModelState.IsValid) return View("CreateEdit", model);

        var tier = await _context.MembershipTiers.FindAsync(model.Id);
        if (tier == null) return NotFound();

        if (await _context.MembershipTiers.AnyAsync(t => t.MinPoints == model.MinPoints && t.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.MinPoints), "Ngưỡng điểm này đã được dùng cho một hạng khác.");
            return View("CreateEdit", model);
        }

        bool nameConflict = await _context.MembershipTiers
            .AnyAsync(t => t.TierName == model.TierName.Trim() && t.Id != model.Id);
        if (nameConflict)
        {
            ModelState.AddModelError(nameof(model.TierName), "Tên hạng này đã tồn tại.");
            return View("CreateEdit", model);
        }

        tier.TierName        = model.TierName.Trim();
        tier.MinPoints       = model.MinPoints;
        tier.DiscountPercent = model.DiscountPercent;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã cập nhật hạng '{tier.TierName}'.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Xoa hang va bao ve khoa ngoai

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var tier = await _context.MembershipTiers.FindAsync(id);
        if (tier == null) return NotFound();

        bool hasUsers = await _context.Users.AnyAsync(u => u.TierId == id);
        if (hasUsers)
        {
            TempData["Error"] = $"Không thể xóa hạng '{tier.TierName}' vì đang có khách hàng ở hạng này.";
            return RedirectToAction(nameof(Index));
        }

        _context.MembershipTiers.Remove(tier);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Hạng đang được sử dụng. Không thể xóa.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Đã xóa hạng '{tier.TierName}'.";
        return RedirectToAction(nameof(Index));
    }
    #endregion
}
