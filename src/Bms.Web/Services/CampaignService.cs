using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record CampaignPreviewResult(int TotalEligibleCount, IReadOnlyList<CustomerRfmProfile> SampleRecipients);
public record CampaignSendResult(bool Success, string Message, int SentCount, int SkippedDuplicateCount);
public record DailyJobRunSummary(
    int BirthdayVouchersIssued,
    int ExpiryWarningsSent,
    int PointsExpiredUsers,
    int VouchersExpired,
    TierEvaluationSummary TierSummary);

public interface ICampaignService
{
    Task<CampaignPreviewResult> PreviewRecipientsAsync(string? targetSegment, int? targetTierId, CancellationToken ct = default);
    Task<CampaignSendResult> SendCampaignAsync(int campaignId, string actorId, CancellationToken ct = default);
    Task<int> IssueBirthdayVouchersAsync(DateOnly? vnToday = null, CancellationToken ct = default);
    Task<int> SendExpiryWarningNotificationsAsync(DateTime? nowUtc = null, CancellationToken ct = default);
    Task<DailyJobRunSummary> RunAllDailyTasksAsync(DateTime? nowUtc = null, CancellationToken ct = default);
}

public class CampaignService : ICampaignService
{
    private readonly ApplicationDbContext _db;
    private readonly ICustomerSegmentService _segmentService;
    private readonly IPointLedgerService _ledger;
    private readonly IVoucherService _voucherService;
    private readonly ILoyaltyService _loyalty;
    private readonly ITierEvaluationService _tierEval;

    public CampaignService(
        ApplicationDbContext db,
        ICustomerSegmentService segmentService,
        IPointLedgerService ledger,
        IVoucherService voucherService,
        ILoyaltyService loyalty,
        ITierEvaluationService tierEval)
    {
        _db = db;
        _segmentService = segmentService;
        _ledger = ledger;
        _voucherService = voucherService;
        _loyalty = loyalty;
        _tierEval = tierEval;
    }

    public async Task<CampaignPreviewResult> PreviewRecipientsAsync(string? targetSegment, int? targetTierId, CancellationToken ct = default)
    {
        var eligibleIds = (await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.MarketingOptIn)
            .Select(u => u.Id)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var profiles = await _segmentService.GetAllProfilesAsync(forceRefresh: true, ct: ct);
        var filtered = profiles.Where(p => eligibleIds.Contains(p.CustomerId));

        if (!string.IsNullOrWhiteSpace(targetSegment) && !string.Equals(targetSegment, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => string.Equals(p.Segment, targetSegment, StringComparison.OrdinalIgnoreCase));
        }

        if (targetTierId.HasValue)
        {
            filtered = filtered.Where(p => p.TierId == targetTierId.Value);
        }

        var list = filtered.OrderByDescending(p => p.Monetary12M).ToList();
        return new CampaignPreviewResult(list.Count, list.Take(5).ToList());
    }

    public async Task<CampaignSendResult> SendCampaignAsync(int campaignId, string actorId, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns
            .Include(c => c.RewardItem)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign == null)
            return new CampaignSendResult(false, "Không tìm thấy chiến dịch.", 0, 0);

        if (campaign.Status == Campaign.StatusCancelled)
            return new CampaignSendResult(false, "Chiến dịch đã bị hủy.", 0, 0);

        var eligibleIds = (await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.MarketingOptIn)
            .Select(u => u.Id)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var profiles = await _segmentService.GetAllProfilesAsync(forceRefresh: true, ct: ct);
        var recipients = profiles.Where(p => eligibleIds.Contains(p.CustomerId));

        if (!string.IsNullOrWhiteSpace(campaign.TargetSegment) && !string.Equals(campaign.TargetSegment, "All", StringComparison.OrdinalIgnoreCase))
        {
            recipients = recipients.Where(p => string.Equals(p.Segment, campaign.TargetSegment, StringComparison.OrdinalIgnoreCase));
        }

        if (campaign.TargetTierId.HasValue)
        {
            recipients = recipients.Where(p => p.TierId == campaign.TargetTierId.Value);
        }

        var targetList = recipients.ToList();

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var now = DateTime.UtcNow;
            int sentCount = 0;
            int skippedCount = 0;

            foreach (var r in targetList)
            {
                string refCode = $"CMP-{campaign.Id}-{r.CustomerId}";
                if (refCode.Length > 50) refCode = refCode[..50];

                // Kiểm tra đã nhận chiến dịch này chưa (qua Voucher hoặc PointTransactions hoặc Notification)
                bool alreadyGotVoucher = await _db.CustomerVouchers
                    .AnyAsync(v => v.CampaignId == campaign.Id && v.CustomerId == r.CustomerId, ct);
                bool alreadyGotPoints = await _db.PointTransactions
                    .AnyAsync(t => t.ReferenceCode == refCode && t.TransactionType == PointTransaction.Bonus, ct);

                if (alreadyGotVoucher || alreadyGotPoints)
                {
                    skippedCount++;
                    continue;
                }

                string? issuedVoucherCode = null;
                if (campaign.RewardItemId.HasValue && campaign.RewardItem != null)
                {
                    issuedVoucherCode = await _voucherService.GenerateUniqueVoucherCodeAsync(ct);
                    _db.CustomerVouchers.Add(new CustomerVoucher
                    {
                        Code = issuedVoucherCode,
                        CustomerId = r.CustomerId,
                        RewardItemId = campaign.RewardItem.Id,
                        CampaignId = campaign.Id,
                        Title = $"{campaign.RewardItem.Name} ({campaign.Name})",
                        VoucherType = campaign.RewardItem.RewardType,
                        DiscountValue = campaign.RewardItem.DiscountValue,
                        Status = CustomerVoucher.StatusActive,
                        IssuedAtUtc = now,
                        ExpiresAtUtc = now.AddDays(Math.Max(1, campaign.RewardItem.ValidDays))
                    });
                }

                int newPointsBalance = r.RewardPoints;
                if (campaign.BonusPoints > 0)
                {
                    var ledgerRes = await _ledger.PostAsync(
                        customerId: r.CustomerId,
                        delta: campaign.BonusPoints,
                        type: PointTransaction.Bonus,
                        referenceCode: refCode,
                        invoiceId: null,
                        reason: $"Chiến dịch: {campaign.Name}",
                        actorId: actorId,
                        spendAmount: null,
                        ct: ct);

                    if (ledgerRes.Duplicate)
                    {
                        skippedCount++;
                        continue;
                    }
                    newPointsBalance = ledgerRes.BalanceAfter;
                }

                string resolvedTitle = ReplacePlaceholders(campaign.MessageTitle, r.FullName, r.TierName, newPointsBalance);
                string resolvedContent = ReplacePlaceholders(campaign.MessageContent, r.FullName, r.TierName, newPointsBalance);
                if (issuedVoucherCode != null)
                {
                    resolvedContent += $" Mã voucher ưu đãi của bạn: {issuedVoucherCode}.";
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = r.CustomerId,
                    Title = resolvedTitle,
                    Content = resolvedContent,
                    Type = "Campaign",
                    IsRead = false,
                    CreatedAt = now
                });

                sentCount++;
            }

            campaign.Status = Campaign.StatusSent;
            campaign.SentAtUtc = now;
            campaign.RecipientCount += sentCount;

            await _db.SaveChangesAsync(ct);
            if (ownTx != null) await ownTx.CommitAsync(ct);

            _segmentService.InvalidateCache();

            return new CampaignSendResult(
                true,
                $"Đã gửi chiến dịch \"{campaign.Name}\" tới {sentCount} khách hàng (bỏ qua {skippedCount} trùng lặp).",
                sentCount,
                skippedCount);
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    public async Task<int> IssueBirthdayVouchersAsync(DateOnly? vnToday = null, CancellationToken ct = default)
    {
        var today = vnToday ?? VnTime.Today;
        int year = today.Year;
        var now = DateTime.UtcNow;

        var customerRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);

        var birthdayUsers = await _db.Users.AsNoTracking()
            .Include(u => u.Tier)
            .Where(u => u.IsActive
                     && u.DateOfBirth != null
                     && u.DateOfBirth.Value.Month == today.Month
                     && u.DateOfBirth.Value.Day == today.Day
                     && u.Tier != null
                     && u.Tier.BirthdayVoucherValue > 0
                     && _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId))
            .ToListAsync(ct);

        int issued = 0;
        foreach (var u in birthdayUsers)
        {
            string bdayRef = $"BDAY-{year}-{u.Id}";
            if (bdayRef.Length > 50) bdayRef = bdayRef[..50];

            // Kiểm tra đã phát voucher sinh nhật trong năm nay chưa
            bool alreadyIssued = await _db.CustomerVouchers
                .AnyAsync(v => v.CustomerId == u.Id && v.UsedReferenceCode == bdayRef, ct);

            if (alreadyIssued) continue;

            var code = await _voucherService.GenerateUniqueVoucherCodeAsync(ct);
            decimal val = u.Tier!.BirthdayVoucherValue;

            _db.CustomerVouchers.Add(new CustomerVoucher
            {
                Code = code,
                CustomerId = u.Id,
                Title = $"Voucher mừng sinh nhật hạng {u.Tier.TierName} ({VnFormat.Money(val)})",
                VoucherType = RewardItemEntity.TypeBillDiscount,
                DiscountValue = val,
                Status = CustomerVoucher.StatusActive,
                IssuedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30),
                UsedReferenceCode = bdayRef
            });

            _db.Notifications.Add(new Notification
            {
                UserId = u.Id,
                Title = $"🎂 Chúc mừng sinh nhật {u.FullName}!",
                Content = $"Quán gửi tặng bạn voucher giảm {VnFormat.Money(val)} nhân dịp sinh nhật (Hạng {u.Tier.TierName}). Mã voucher: {code} (hạn dùng 30 ngày).",
                Type = "Birthday",
                IsRead = false,
                CreatedAt = now
            });

            issued++;
        }

        if (issued > 0)
            await _db.SaveChangesAsync(ct);

        return issued;
    }

    public async Task<int> SendExpiryWarningNotificationsAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        // Khách có LastPaidDate cách đây 166 ngày (còn 14 ngày nữa chạm mốc 180 ngày)
        var fromDate = now.AddDays(-167);
        var toDate = now.AddDays(-165);

        var warningUsers = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.RewardPoints > 0 && u.LastPaidDate >= fromDate && u.LastPaidDate <= toDate)
            .ToListAsync(ct);

        int sent = 0;
        foreach (var u in warningUsers)
        {
            bool recentWarned = await _db.Notifications
                .AnyAsync(n => n.UserId == u.Id && n.Type == "ExpireWarning" && n.CreatedAt >= now.AddDays(-15), ct);
            if (recentWarned) continue;

            var expireDate = u.LastPaidDate!.Value.AddDays(180).ToVn();
            _db.Notifications.Add(new Notification
            {
                UserId = u.Id,
                Title = "⚠️ Điểm thưởng sắp hết hạn sau 14 ngày",
                Content = $"Bạn đang có {u.RewardPoints} điểm (tương đương {VnFormat.Money(u.RewardPoints * 1000m)}) sẽ hết hạn vào ngày {expireDate:dd/MM/yyyy}. Hãy ghé quán chơi hoặc đổi quà ngay!",
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
        var vnToday = DateOnly.FromDateTime(now.ToVn());

        int bday = await IssueBirthdayVouchersAsync(vnToday, ct);
        int warn = await SendExpiryWarningNotificationsAsync(now, ct);
        int expPts = await _loyalty.ProcessPointExpirationsAsync(ct);
        int expVouchers = await _voucherService.ExpireOverdueVouchersAsync(now, ct);
        var tierSum = await _tierEval.ProcessDailyTierReviewsAsync(now, ct);

        return new DailyJobRunSummary(bday, warn, expPts, expVouchers, tierSum);
    }

    private static string ReplacePlaceholders(string template, string fullName, string tierName, int points) =>
        (template ?? string.Empty)
            .Replace("{FullName}", fullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{TierName}", tierName, StringComparison.OrdinalIgnoreCase)
            .Replace("{Points}", points.ToString("N0"), StringComparison.OrdinalIgnoreCase);
}
