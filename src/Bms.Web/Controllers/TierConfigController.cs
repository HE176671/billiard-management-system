using System.Globalization;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Admin")]
public class TierConfigController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ITierEvaluationService _tierEval;

    public TierConfigController(ApplicationDbContext context, ITierEvaluationService tierEval)
    {
        _context = context;
        _tierEval = tierEval;
    }

    // GET /TierConfig
    public async Task<IActionResult> Index()
    {
        var vm = new TierConfigViewModel { Tiers = await LoadItemsAsync() };
        return View(vm);
    }

    // POST /TierConfig/PreviewImpact (Xem trước ảnh hưởng nếu đổi ngưỡng chi tiêu 12 tháng)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewImpact(TierConfigViewModel model)
    {
        var counts = await CountCustomersAsync();
        var map = new Dictionary<int, decimal>();

        foreach (var item in model.Tiers)
        {
            item.CustomerCount = counts.GetValueOrDefault(item.Id);
            if (TryParseDecimal(item.SpendThreshold, out var th) && th >= 0)
            {
                map[item.Id] = th;
            }
        }

        model.ImpactPreview = await _tierEval.PreviewThresholdImpactAsync(map);
        return View("Index", model);
    }

    // POST /TierConfig/Save
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TierConfigViewModel model)
    {
        var tiers = await _context.MembershipTiers.ToListAsync();
        var counts = await CountCustomersAsync();
        var parsed = new List<(MembershipTier Tier, decimal Threshold, decimal Discount, int Multiplier)>();

        foreach (var item in model.Tiers)
        {
            item.CustomerCount = counts.GetValueOrDefault(item.Id);
            var tier = tiers.FirstOrDefault(t => t.Id == item.Id);
            if (tier == null)
            {
                ModelState.AddModelError(string.Empty, "Không tìm thấy hạng thành viên cần cập nhật.");
                continue;
            }
            item.TierName = tier.TierName;

            if (!TryParseDecimal(item.SpendThreshold, out var threshold) || threshold < 0 || threshold > 999_999_999_999m)
            {
                ModelState.AddModelError(string.Empty, $"[{tier.TierName}] Ngưỡng chi tiêu 12 tháng không hợp lệ (phải là số >= 0).");
                continue;
            }
            if (!TryParseDecimal(item.DiscountPercent, out var discount) || discount < 0 || discount > 100)
            {
                ModelState.AddModelError(string.Empty, $"[{tier.TierName}] % giảm giá phải nằm trong khoảng 0 – 100.");
                continue;
            }
            if (item.PointMultiplier < 1 || item.PointMultiplier > 100)
            {
                ModelState.AddModelError(string.Empty, $"[{tier.TierName}] Hệ số nhân điểm phải từ 1 đến 100.");
                continue;
            }

            parsed.Add((tier, threshold, Math.Round(discount, 2), item.PointMultiplier));
        }

        if (ModelState.IsValid)
        {
            if (parsed.Count != tiers.Count)
                ModelState.AddModelError(string.Empty, "Dữ liệu gửi lên không đầy đủ các hạng thành viên.");
            else if (parsed.Select(p => p.Tier.Id).Distinct().Count() != tiers.Count)
                ModelState.AddModelError(string.Empty, "Danh sách hạng bị trùng lặp.");
            else if (parsed[0].Threshold != 0)
                ModelState.AddModelError(string.Empty, $"Hạng thấp nhất ({parsed[0].Tier.TierName}) phải có ngưỡng chi tiêu bằng 0 để mọi khách đều có hạng.");
            else
            {
                for (int i = 1; i < parsed.Count; i++)
                {
                    if (parsed[i].Threshold <= parsed[i - 1].Threshold)
                    {
                        ModelState.AddModelError(string.Empty,
                            $"Ngưỡng của {parsed[i].Tier.TierName} phải lớn hơn ngưỡng của {parsed[i - 1].Tier.TierName}.");
                        break;
                    }
                }
            }
        }

        if (!ModelState.IsValid) return View(nameof(Index), model);

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                var p = parsed[i];
                p.Tier.SpendThreshold = p.Threshold;
                p.Tier.DiscountPercent = p.Discount;
                p.Tier.PointMultiplier = p.Multiplier;
                p.Tier.SortOrder = i + 1;
            }
            await _context.SaveChangesAsync();

            // F5: Không tự hạ hạng hàng loạt khi đổi ngưỡng (chỉ nâng ngay cho khách đủ điều kiện chi tiêu 12 tháng; hạ thì theo chu kỳ review)
            int upgradedCount = 0;
            var allTiers = parsed.Select(x => x.Tier).OrderBy(t => t.SpendThreshold).ToList();
            var roleId = await _context.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync();
            var customers = await _context.Users
                .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var u in customers)
            {
                var spend12M = await _tierEval.GetSpendLast12MonthsAsync(u.Id, now);
                var evaluation = await _tierEval.EvaluateUpgradeOnCheckoutAsync(u, spend12M);
                if (evaluation.Upgraded) upgradedCount++;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = upgradedCount > 0
                ? $"Đã lưu cấu hình hạng & ưu đãi. Đã nâng hạng ngay cho {upgradedCount} khách hàng đủ điều kiện (các khách hàng chưa đủ ngưỡng sẽ được xét giữ/ân hạn theo chu kỳ 1 tháng)."
                : "Đã lưu cấu hình hạng & ưu đãi thành công.";
        }
        catch
        {
            await tx.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống khi lưu cấu hình. Vui lòng thử lại.");
            return View(nameof(Index), model);
        }

        return RedirectToAction(nameof(Index));
    }

    // POST /TierConfig/AddTier (Thêm mới hạng thành viên)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTier(string tierName, decimal spendThreshold, decimal discountPercent = 0m, int pointMultiplier = 1)
    {
        tierName = (tierName ?? string.Empty).Trim();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ: Chỉ được nhập số từ 0 trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrEmpty(tierName))
        {
            TempData["Error"] = "Vui lòng nhập tên hạng thành viên mới.";
            return RedirectToAction(nameof(Index));
        }

        if (spendThreshold < 0 || discountPercent < 0 || discountPercent > 100 || pointMultiplier < 1 || pointMultiplier > 100)
        {
            TempData["Error"] = "Thông số hạng không hợp lệ: Mức chi tiêu >= 0, % giảm giá từ 0-100%, hệ số nhân điểm >= 1.";
            return RedirectToAction(nameof(Index));
        }

        var existingTiers = await _context.MembershipTiers.OrderBy(t => t.SpendThreshold).ToListAsync();
        if (existingTiers.Any(t => string.Equals(t.TierName, tierName, StringComparison.OrdinalIgnoreCase)))
        {
            TempData["Error"] = $"Hạng thành viên \"{tierName}\" đã tồn tại.";
            return RedirectToAction(nameof(Index));
        }

        if (existingTiers.Any(t => t.SpendThreshold == spendThreshold))
        {
            TempData["Error"] = $"Mức chi tiêu {VnFormat.Money(spendThreshold)} đã được sử dụng cho một hạng khác. Vui lòng nhập mức chi tiêu khác.";
            return RedirectToAction(nameof(Index));
        }

        int minPoints = (int)Math.Min(1_000_000_000m, Math.Max(0m, Math.Round(spendThreshold / 10000m)));
        while (existingTiers.Any(t => t.MinPoints == minPoints))
        {
            minPoints++;
        }

        var newTier = new MembershipTier
        {
            TierName = tierName,
            SpendThreshold = spendThreshold,
            DiscountPercent = Math.Round(discountPercent, 2),
            PointMultiplier = pointMultiplier,
            MinPoints = minPoints,
            SortOrder = existingTiers.Count + 1
        };

        _context.MembershipTiers.Add(newTier);
        await _context.SaveChangesAsync();

        var ordered = await _context.MembershipTiers.OrderBy(t => t.SpendThreshold).ThenBy(t => t.Id).ToListAsync();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i + 1;
        }
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã thêm hạng thành viên \"{tierName}\" (Ngưỡng chi tiêu: {VnFormat.Money(spendThreshold)}, Giảm giờ: {discountPercent:0.##}%, Nhân điểm: ×{pointMultiplier}).";
        return RedirectToAction(nameof(Index));
    }

    // POST /TierConfig/DeleteTier (Xóa hạng thành viên chưa có dữ liệu gắn kèm)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTier(int id)
    {
        var tier = await _context.MembershipTiers.FindAsync(id);
        if (tier == null) return NotFound();

        if (tier.SpendThreshold == 0)
        {
            TempData["Error"] = $"Không thể xóa hạng mặc định \"{tier.TierName}\" (mức chi tiêu 0 VND).";
            return RedirectToAction(nameof(Index));
        }

        bool hasUsers = await _context.Users.AnyAsync(u => u.TierId == id);
        bool hasRewards = await _context.RewardItems.AnyAsync(r => r.MinTierId == id);
        if (hasUsers || hasRewards)
        {
            TempData["Error"] = $"Không thể xóa hạng \"{tier.TierName}\" vì đang có hội viên hoặc phần quà đổi điểm sử dụng hạng này.";
            return RedirectToAction(nameof(Index));
        }

        _context.MembershipTiers.Remove(tier);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã xóa hạng thành viên \"{tier.TierName}\".";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<TierEditItem>> LoadItemsAsync()
    {
        var counts = await CountCustomersAsync();
        var tiers = await _context.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ToListAsync();

        return tiers.Select(t => new TierEditItem
        {
            Id = t.Id,
            TierName = t.TierName,
            SpendThreshold = t.SpendThreshold.ToString("0.##", CultureInfo.InvariantCulture),
            DiscountPercent = t.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture),
            PointMultiplier = t.PointMultiplier,
            CustomerCount = counts.GetValueOrDefault(t.Id)
        }).ToList();
    }

    private async Task<Dictionary<int, int>> CountCustomersAsync()
    {
        return await _context.Users
            .Where(u => u.TierId != null)
            .GroupBy(u => u.TierId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
