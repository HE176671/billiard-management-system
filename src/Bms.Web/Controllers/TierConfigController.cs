using System.Globalization;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class TierConfigController : Controller
{
    private readonly ApplicationDbContext _context;

    public TierConfigController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET /TierConfig
    public async Task<IActionResult> Index()
    {
        var vm = new TierConfigViewModel { Tiers = await LoadItemsAsync() };
        return View(vm);
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
                ModelState.AddModelError(string.Empty, $"[{tier.TierName}] Ngưỡng chi tiêu không hợp lệ (phải là số >= 0).");
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
            foreach (var p in parsed)
            {
                p.Tier.SpendThreshold = p.Threshold;
                p.Tier.DiscountPercent = p.Discount;
                p.Tier.PointMultiplier = p.Multiplier;
            }
            await _context.SaveChangesAsync();

            // Ngưỡng thay đổi => xếp lại hạng cho toàn bộ khách theo TotalSpend hiện có
            var changed = await TierHelper.RecalculateUserTiersAsync(_context);
            await tx.CommitAsync();

            TempData["Success"] = changed > 0
                ? $"Đã lưu cấu hình hạng. Đã cập nhật lại hạng cho {changed} khách hàng."
                : "Đã lưu cấu hình hạng. Không có khách hàng nào thay đổi hạng.";
        }
        catch
        {
            await tx.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống khi lưu cấu hình. Vui lòng thử lại.");
            return View(nameof(Index), model);
        }

        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──

    private async Task<List<TierEditItem>> LoadItemsAsync()
    {
        var counts = await CountCustomersAsync();
        var tiers = await _context.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ToListAsync();

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
