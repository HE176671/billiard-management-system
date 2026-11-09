using System.Net;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Kết quả một lần chạy chu kỳ cuối ngày (JOB-01 + JOB-02).</summary>
public record DailyCycleResult(DailyJobRunSummary Summary, int EmailsSent, int EmailsSkipped, int EmailsFailed);

/// <summary>
/// TV5 — chu kỳ cuối ngày: phát voucher sinh nhật, hết hạn voucher quá hạn, xét giữ hạng 12 tháng / ân hạn 30 ngày / hạ 1 bậc,
/// rồi gửi email thông báo hạng và voucher cho hội viên có email (API-02), cuối cùng ghi nhật ký.
/// Dùng chung cho job tự động (LoyaltyDailyJob) và nút "Chạy job ngay" của Manager (BR-17h).
/// </summary>
public interface IDailyCycleService
{
    Task<DailyCycleResult> RunAsync(string? actorId, bool manual, CancellationToken ct = default);
}

public class DailyCycleService : IDailyCycleService
{
    /// <summary>Loại thông báo được gửi kèm email.</summary>
    private static readonly string[] EmailTypes = { "Birthday", "TierUpgrade", "TierGrace", "TierDowngrade" };

    private readonly ApplicationDbContext _db;
    private readonly ICampaignService _campaign;
    private readonly IEmailService _email;
    private readonly IAuditLogService _audit;

    public DailyCycleService(ApplicationDbContext db, ICampaignService campaign, IEmailService email, IAuditLogService audit)
    {
        _db = db;
        _campaign = campaign;
        _email = email;
        _audit = audit;
    }

    public async Task<DailyCycleResult> RunAsync(string? actorId, bool manual, CancellationToken ct = default)
    {
        // Email cho mọi thông báo phát sinh kể từ lần chạy trước (kể cả nâng hạng lúc thanh toán trong ngày).
        var lastRunUtc = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.ActionType == AuditActions.TierReviewRun)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => (DateTime?)a.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        var emailSinceUtc = lastRunUtc ?? DateTime.UtcNow.AddDays(-1);

        var summary = await _campaign.RunAllDailyTasksAsync(null, ct);

        var pending = await _db.Notifications.AsNoTracking()
            .Where(n => n.CreatedAt > emailSinceUtc && n.Type != null && EmailTypes.Contains(n.Type))
            .Join(_db.Users.AsNoTracking(), n => n.UserId, u => u.Id,
                (n, u) => new { n.Title, n.Content, u.Email, u.FullName, u.IsActive })
            .Where(x => x.IsActive && x.Email != null && x.Email != "")
            .ToListAsync(ct);

        int sent = 0, skipped = 0, failed = 0;
        foreach (var item in pending)
        {
            var body = $"""
                <p>Xin chào {WebUtility.HtmlEncode(item.FullName)},</p>
                <p>{WebUtility.HtmlEncode(item.Content)}</p>
                <p style="color:#64748b;font-size:12px">Email tự động từ Billiard Management System. Vui lòng không trả lời email này.</p>
                """;
            switch (await _email.SendAsync(item.Email!, item.Title, body, ct))
            {
                case EmailSendStatus.Sent: sent++; break;
                case EmailSendStatus.Skipped: skipped++; break;
                default: failed++; break;
            }
        }

        var t = summary.TierSummary;
        await _audit.LogAsync(AuditActions.TierReviewRun, "DailyCycle", VnTime.Today.ToString("yyyy-MM-dd"),
            $"{(manual ? "Manager chạy tay" : "Job tự động")}: voucher sinh nhật +{summary.BirthdayVouchersIssued}, " +
            $"voucher hết hạn {summary.VouchersExpired}, giữ hạng {t.RenewedCount}, ân hạn {t.EnteredGraceCount}, " +
            $"hạ hạng {t.DowngradedCount}; email gửi {sent}, bỏ qua {skipped}, lỗi {failed}.",
            actorId: actorId, ct: ct);

        return new DailyCycleResult(summary, sent, skipped, failed);
    }
}
