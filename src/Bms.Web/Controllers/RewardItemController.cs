using Bms.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Admin")]
public class RewardItemController : Controller
{
    private readonly ApplicationDbContext _db;

    public RewardItemController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /RewardItem?editId=...
    [HttpGet]
    public async Task<IActionResult> Index(int? editId = null)
    {
        ViewBag.Tiers = await _db.MembershipTiers.AsNoTracking().OrderBy(t => t.SpendThreshold).ToListAsync();
        var items = await _db.RewardItems.AsNoTracking()
            .Include(r => r.MinTier)
            .OrderBy(r => r.PointCost)
            .ThenBy(r => r.Id)
            .ToListAsync();

        if (editId.HasValue)
        {
            ViewBag.EditItem = items.FirstOrDefault(r => r.Id == editId.Value);
        }

        return View(items);
    }

    // POST /RewardItem/ToggleStatus (Đóng / Mở trạng thái mã quà tặng bất cứ lúc nào)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var item = await _db.RewardItems.FindAsync(id);
        if (item == null)
        {
            TempData["Error"] = "Không tìm thấy mã quà tặng.";
            return RedirectToAction(nameof(Index));
        }

        item.IsActive = !item.IsActive;
        await _db.SaveChangesAsync();

        TempData["Success"] = item.IsActive
            ? $"Đã MỞ hoạt động mã quà tặng [{item.Code}] — {item.Name}."
            : $"Đã ĐÓNG (tạm ngưng) mã quà tặng [{item.Code}] — {item.Name}.";

        return RedirectToAction(nameof(Index));
    }

    // POST /RewardItem/Save
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string code, string name, string? description,
        int pointCost, string rewardType, decimal? discountValue, int validDays = 7, int? minTierId = null, bool isActive = true)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        name = (name ?? string.Empty).Trim();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ: Điểm đổi và Giá trị quy đổi (VND) chỉ được nhập số từ 0 trở lên (không được nhập ký tự đặc biệt hoặc chữ cái).";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? (int?)id : null });
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
        {
            TempData["Error"] = "Mã quà và tên phần quà không được để trống.";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? (int?)id : null });
        }

        if (pointCost < 0 || (discountValue.HasValue && discountValue.Value < 0))
        {
            TempData["Error"] = "Điểm đổi và Giá trị quy đổi (VND) phải từ 0 trở lên (không được nhập số âm).";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? (int?)id : null });
        }

        if (await _db.RewardItems.AnyAsync(r => r.Code == code && r.Id != id))
        {
            TempData["Error"] = $"Mã quà {code} đã tồn tại.";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? (int?)id : null });
        }

        decimal normalizedDiscount = discountValue ?? 0m;

        if (id == 0)
        {
            _db.RewardItems.Add(new RewardItemEntity
            {
                Code = code,
                Name = name,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                PointCost = pointCost,
                RewardType = rewardType,
                DiscountValue = normalizedDiscount,
                ValidDays = Math.Max(1, validDays),
                MinTierId = minTierId,
                IsActive = isActive
            });
            TempData["Success"] = $"Đã thêm phần quà \"{name}\" ({pointCost} điểm · {normalizedDiscount:N0} VND).";
        }
        else
        {
            var existing = await _db.RewardItems.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Code = code;
            existing.Name = name;
            existing.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            existing.PointCost = pointCost;
            existing.RewardType = rewardType;
            existing.DiscountValue = normalizedDiscount;
            existing.ValidDays = Math.Max(1, validDays);
            existing.MinTierId = minTierId;
            existing.IsActive = isActive;

            // Đồng bộ ngay các voucher đang Active chưa sử dụng thuộc phần quà này
            var activeVouchers = await _db.CustomerVouchers
                .Where(v => v.RewardItemId == existing.Id && v.Status == CustomerVoucher.StatusActive)
                .ToListAsync();
            foreach (var v in activeVouchers)
            {
                v.Title = existing.Name;
                v.VoucherType = existing.RewardType;
                v.DiscountValue = existing.DiscountValue;
            }

            TempData["Success"] = $"Đã cập nhật phần quà \"{name}\" và đồng bộ {activeVouchers.Count} mã voucher đang hoạt động.";
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
