using System.Text.Json;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record TierEvaluationSummary(
    int UpgradedCount,
    int RenewedCount,
    int EnteredGraceCount,
    int DowngradedCount);

public record TierImpactItem(
    string CustomerId,
    string FullName,
    string CurrentTierName,
    string ProposedTierName,
    decimal Spend12Months,
    string ChangeType); // "Upgrade" | "Grace"

public interface ITierEvaluationService
{
    /// <summary>Tổng chi tiêu xét hạng = SUM(SpendAmount) của các bút toán Earn trong 365 ngày gần nhất.</summary>
    Task<decimal> GetSpendLast12MonthsAsync(string customerId, DateTime? asOfUtc = null, CancellationToken ct = default);

    /// <summary>Xét nâng hạng ngay khi thanh toán (hoặc xóa ân hạn nếu đã đạt lại ngưỡng giữ hạng).</summary>
    Task<(bool Upgraded, string? OldTierName, MembershipTier? CurrentTier)> EvaluateUpgradeOnCheckoutAsync(
        ApplicationUser user, decimal newSpend12Months, string? actorId = null, CancellationToken ct = default);

    /// <summary>Job hằng ngày xét giữ hạng, đưa vào diện ân hạn 30 ngày hoặc hạ 1 bậc khi hết ân hạn.</summary>
    Task<TierEvaluationSummary> ProcessDailyTierReviewsAsync(DateTime? nowUtc = null, CancellationToken ct = default);

    /// <summary>Xem trước ảnh hưởng nếu áp dụng bộ ngưỡng chi tiêu 12 tháng mới.</summary>
    Task<List<TierImpactItem>> PreviewThresholdImpactAsync(
        IReadOnlyDictionary<int, decimal> proposedThresholds, CancellationToken ct = default);
}

public class TierEvaluationService : ITierEvaluationService
{
    private readonly ApplicationDbContext _db;

    public TierEvaluationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetSpendLast12MonthsAsync(string customerId, DateTime? asOfUtc = null, CancellationToken ct = default)
    {
        var now = asOfUtc ?? DateTime.UtcNow;
        var since = now.AddDays(-365);
        var upper = asOfUtc.HasValue ? asOfUtc.Value.AddSeconds(5) : DateTime.UtcNow.AddMinutes(5);

        var ledgerSpend = await _db.PointTransactions
            .AsNoTracking()
            .Where(t => t.CustomerId == customerId
                     && t.TransactionType == PointTransaction.Earn
                     && t.CreatedAt >= since
                     && t.CreatedAt <= upper
                     && t.SpendAmount != null)
            .SumAsync(t => t.SpendAmount, ct) ?? 0m;

        return ledgerSpend;
    }

    public async Task<(bool Upgraded, string? OldTierName, MembershipTier? CurrentTier)> EvaluateUpgradeOnCheckoutAsync(
        ApplicationUser user, decimal newSpend12Months, string? actorId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

        if (tiers.Count == 0)
            return (false, null, null);

        var currentTier = tiers.FirstOrDefault(t => t.Id == user.TierId) ?? tiers[0];
        var oldTierName = currentTier.TierName;

        // Hạng cao nhất đạt được theo chi tiêu 12 tháng
        var resolvedTier = TierHelper.Resolve(tiers, newSpend12Months) ?? tiers[0];

        // 1. Nếu khách đang trong ân hạn mà đã chi tiêu đủ giữ hạng hiện tại -> xóa ân hạn, gia hạn review +12 tháng
        if (user.TierGraceUntilUtc != null && newSpend12Months >= currentTier.SpendThreshold)
        {
            user.TierGraceUntilUtc = null;
            user.TierReviewAtUtc = now.AddMonths(12);
        }

        // 2. Nếu đạt ngưỡng hạng cao hơn hiện tại -> Lên hạng ngay lập tức (BR-F5-1)
        bool upgraded = false;
        if (resolvedTier.SpendThreshold > currentTier.SpendThreshold || user.TierId == null)
        {
            if (user.TierId != resolvedTier.Id)
            {
                upgraded = user.TierId != null && resolvedTier.SpendThreshold > currentTier.SpendThreshold;
                user.TierId = resolvedTier.Id;
                user.TierAchievedAtUtc = now;
                user.TierReviewAtUtc = now.AddMonths(12);
                user.TierGraceUntilUtc = null;

                if (upgraded)
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId = user.Id,
                        Title = $"Chúc mừng thăng hạng {resolvedTier.TierName}!",
                        Content = $"Chi tiêu 12 tháng của bạn đạt {VnFormat.Money(newSpend12Months)}, chính thức lên hạng {resolvedTier.TierName} (giảm {resolvedTier.DiscountPercent:0.##}% tiền giờ, tích điểm ×{resolvedTier.PointMultiplier}). Hạng có hiệu lực đến {now.AddMonths(12):dd/MM/yyyy}.",
                        Type = "TierUpgrade",
                        IsRead = false,
                        CreatedAt = now
                    });

                    _db.CustomerAuditLogs.Add(new CustomerAuditLog
                    {
                        CustomerId = user.Id,
                        Action = "TierChange",
                        Detail = JsonSerializer.Serialize(new { from = oldTierName, to = resolvedTier.TierName, reason = "Upgrade12M", spend12M = newSpend12Months }),
                        ActorId = actorId,
                        CreatedAtUtc = now
                    });
                }

                currentTier = resolvedTier;
            }
        }
        else if (user.TierReviewAtUtc == null)
        {
            user.TierAchievedAtUtc ??= now;
            user.TierReviewAtUtc = now.AddMonths(12);
        }

        return (upgraded, oldTierName, currentTier);
    }

    public async Task<TierEvaluationSummary> ProcessDailyTierReviewsAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

        if (tiers.Count == 0)
            return new TierEvaluationSummary(0, 0, 0, 0);

        var customerRoleId = await _db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);
        var users = await _db.Users
            .Where(u => u.IsActive && _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId))
            .ToListAsync(ct);

        int upgraded = 0, renewed = 0, grace = 0, downgraded = 0;

        foreach (var user in users)
        {
            var currentIdx = tiers.FindIndex(t => t.Id == user.TierId);
            if (currentIdx < 0) currentIdx = 0;
            var currentTier = tiers[currentIdx];

            var spend12M = await GetSpendLast12MonthsAsync(user.Id, now, ct);

            // A. Khách đang trong thời gian ân hạn
            if (user.TierGraceUntilUtc.HasValue)
            {
                if (spend12M >= currentTier.SpendThreshold)
                {
                    // Đã chi tiêu đủ trong thời gian ân hạn -> Giữ hạng
                    user.TierGraceUntilUtc = null;
                    user.TierReviewAtUtc = now.AddMonths(12);
                    renewed++;
                }
                else if (user.TierGraceUntilUtc.Value <= now)
                {
                    // BR-F5-3: Hết ân hạn mà vẫn chưa đủ -> Hạ đúng 1 bậc (không rơi thẳng về Member)
                    int newIdx = Math.Max(0, currentIdx - 1);
                    var lowerTier = tiers[newIdx];
                    var oldName = currentTier.TierName;

                    user.TierId = lowerTier.Id;
                    user.TierAchievedAtUtc = now;
                    user.TierReviewAtUtc = now.AddMonths(12);
                    user.TierGraceUntilUtc = null;
                    downgraded++;

                    _db.Notifications.Add(new Notification
                    {
                        UserId = user.Id,
                        Title = $"Thông báo điều chỉnh hạng thành viên ({oldName} → {lowerTier.TierName})",
                        Content = $"Thời gian ân hạn giữ hạng {oldName} đã kết thúc. Hạng hiện tại của bạn được điều chỉnh về {lowerTier.TierName}. Hãy tiếp tục tích lũy chi tiêu để thăng hạng trở lại!",
                        Type = "TierDowngrade",
                        IsRead = false,
                        CreatedAt = now
                    });

                    _db.CustomerAuditLogs.Add(new CustomerAuditLog
                    {
                        CustomerId = user.Id,
                        Action = "TierChange",
                        Detail = JsonSerializer.Serialize(new { from = oldName, to = lowerTier.TierName, reason = "GraceExpired", spend12M }),
                        CreatedAtUtc = now
                    });
                }
                continue;
            }

            // B. Đến kỳ xét giữ hạng (TierReviewAtUtc <= now) cho các hạng > hạng thấp nhất
            if (currentIdx > 0 && user.TierReviewAtUtc.HasValue && user.TierReviewAtUtc.Value <= now)
            {
                if (spend12M >= currentTier.SpendThreshold)
                {
                    // BR-F5-2: Đủ chi tiêu 12 tháng -> gia hạn review thêm 12 tháng
                    user.TierReviewAtUtc = now.AddMonths(12);
                    renewed++;
                }
                else
                {
                    // BR-F5-2: Chưa đủ -> vào ân hạn 30 ngày
                    var graceUntil = now.AddDays(30);
                    user.TierGraceUntilUtc = graceUntil;
                    var needed = currentTier.SpendThreshold - spend12M;
                    grace++;

                    _db.Notifications.Add(new Notification
                    {
                        UserId = user.Id,
                        Title = $"Cảnh báo duy trì hạng {currentTier.TierName}",
                        Content = $"Kỳ xét hạng 12 tháng đã đến. Bạn cần chi tiêu thêm {VnFormat.Money(needed)} trước ngày {graceUntil.ToVn():dd/MM/yyyy} để giữ hạng {currentTier.TierName}.",
                        Type = "TierGrace",
                        IsRead = false,
                        CreatedAt = now
                    });
                }
            }
        }

        if (upgraded + renewed + grace + downgraded > 0)
            await _db.SaveChangesAsync(ct);

        return new TierEvaluationSummary(upgraded, renewed, grace, downgraded);
    }

    public async Task<List<TierImpactItem>> PreviewThresholdImpactAsync(
        IReadOnlyDictionary<int, decimal> proposedThresholds, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

        var simulatedTiers = tiers.Select(t => new MembershipTier
        {
            Id = t.Id,
            TierName = t.TierName,
            SpendThreshold = proposedThresholds.TryGetValue(t.Id, out var th) ? th : t.SpendThreshold,
            DiscountPercent = t.DiscountPercent,
            PointMultiplier = t.PointMultiplier
        }).OrderBy(t => t.SpendThreshold).ToList();

        var customerRoleId = await _db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);
        var customers = await _db.Users.AsNoTracking()
            .Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId))
            .ToListAsync(ct);

        var result = new List<TierImpactItem>();
        foreach (var u in customers)
        {
            var spend12M = await GetSpendLast12MonthsAsync(u.Id, now, ct);
            var curTier = simulatedTiers.FirstOrDefault(t => t.Id == u.TierId) ?? simulatedTiers[0];
            var resolved = TierHelper.Resolve(simulatedTiers, spend12M) ?? simulatedTiers[0];

            if (resolved.SpendThreshold > curTier.SpendThreshold)
            {
                result.Add(new TierImpactItem(u.Id, u.FullName, curTier.TierName, resolved.TierName, spend12M, "Upgrade"));
            }
            else if (spend12M < curTier.SpendThreshold)
            {
                result.Add(new TierImpactItem(u.Id, u.FullName, curTier.TierName, resolved.TierName, spend12M, "Grace"));
            }
        }

        return result;
    }
}
