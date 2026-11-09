using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record DailyJobRunSummary(
    int ExpiryWarningsSent,
    int PointsExpiredUsers,
    int VouchersExpired,
    TierEvaluationSummary TierSummary);

public interface ICampaignService
{
    Task<int> SendExpiryWarningNotificationsAsync(DateTime? nowUtc = null, CancellationToken ct = default);
    Task<DailyJobRunSummary> RunAllDailyTasksAsync(DateTime? nowUtc = null, CancellationToken ct = default);
}

public class CampaignService : ICampaignService
{
    private readonly ApplicationDbContext _db;
    private readonly IVoucherService _voucherService;
    private readonly ILoyaltyService _loyalty;
    private readonly ITierEvaluationService _tierEval;

    public CampaignService(
        ApplicationDbContext db,
        IVoucherService voucherService,
        ILoyaltyService loyalty,
        ITierEvaluationService tierEval)
    {
        _db = db;
        _voucherService = voucherService;
        _loyalty = loyalty;
        _tierEval = tierEval;
    }

    public async Task<int> SendExpiryWarningNotificationsAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        // Khách có LastPaidDate cách đây 86 ngày (còn 14 ngày nữa chạm mốc 100 ngày thu hồi điểm)
        var fromDate = now.AddDays(-87);
        var toDate = now.AddDays(-85);

        var warningUsers = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.RewardPoints > 0 && u.LastPaidDate >= fromDate && u.LastPaidDate <= toDate)
            .ToListAsync(ct);

        int sent = 0;
        foreach (var u in warningUsers)
        {
            bool recentWarned = await _db.Notifications
                .AnyAsync(n => n.UserId == u.Id && n.Type == "ExpireWarning" && n.CreatedAt >= now.AddDays(-15), ct);
            if (recentWarned) continue;

            var expireDate = u.LastPaidDate!.Value.AddDays(100).ToVn();
            _db.Notifications.Add(new Notification
            {
                UserId = u.Id,
                Title = "⚠️ Điểm thưởng sắp bị thu hồi sau 14 ngày",
                Content = $"Bạn đang có {u.RewardPoints} điểm (tương đương {VnFormat.Money(u.RewardPoints * 1000m)}) sẽ hết hạn vào ngày {expireDate:dd/MM/yyyy} (sau 100 ngày không giao dịch). Hãy ghé quán chơi hoặc đổi quà ngay!",
                Type = "ExpireWarning",
                IsRead = false,
                CreatedAt = now
            });
            sent++;
        }

        if (sent > 0)
            await _db.SaveChangesAsync(ct);

        return sent;
    }

    public async Task<DailyJobRunSummary> RunAllDailyTasksAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        int warn = await SendExpiryWarningNotificationsAsync(now, ct);
        int expPts = await _loyalty.ProcessPointExpirationsAsync(ct);
        int expVouchers = await _voucherService.ExpireOverdueVouchersAsync(now, ct);
        var tierSum = await _tierEval.ProcessDailyTierReviewsAsync(now, ct);

        return new DailyJobRunSummary(warn, expPts, expVouchers, tierSum);
    }
}
