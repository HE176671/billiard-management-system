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
        var parsed = new List<(MembershipTier Tier, decimal Threshold, decimal Discount, int Multiplier, decimal BirthdayVoucher)>();

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
            if (!TryParseDecimal(item.BirthdayVoucherValue, out var bdayVoucher) || bdayVoucher < 0 || bdayVoucher > 100_000_000m)
            {
                ModelState.AddModelError(string.Empty, $"[{tier.TierName}] Giá trị voucher sinh nhật không hợp lệ.");
                continue;
            }

            parsed.Add((tier, threshold, Math.Round(discount, 2), item.PointMultiplier, Math.Round(bdayVoucher, 0)));
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
            for (int i = 0; i < parsed.Count; i++)
            {
                var p = parsed[i];
                p.Tier.SpendThreshold = p.Threshold;
                p.Tier.DiscountPercent = p.Discount;
                p.Tier.PointMultiplier = p.Multiplier;
                p.Tier.BirthdayVoucherValue = p.BirthdayVoucher;
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
                var curTier = allTiers.FirstOrDefault(t => t.Id == u.TierId) ?? allTiers[0];
                var resolved = TierHelper.Resolve(allTiers, spend12M, u.RewardPoints) ?? allTiers[0];

                if (u.TierId == null || resolved.SpendThreshold > curTier.SpendThreshold)
                {
                    u.TierId = resolved.Id;
                    u.TierAchievedAtUtc = now;
                    u.TierReviewAtUtc = now.AddMonths(1);
                    u.TierGraceUntilUtc = null;
                    upgradedCount++;
                }
            }

            if (upgradedCount > 0) await _context.SaveChangesAsync();
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
            BirthdayVoucherValue = t.BirthdayVoucherValue.ToString("0.##", CultureInfo.InvariantCulture),
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
