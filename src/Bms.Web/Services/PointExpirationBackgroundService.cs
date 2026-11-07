using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

/// <summary>
/// Background Service chạy định kỳ hằng ngày lúc 02:00 giờ Việt Nam (và 15 giây sau khi khởi chạy):
/// 1. Phát voucher mừng sinh nhật (1 lần/năm, hạn dùng 7 ngày) theo hạng (BirthdayVoucherValue > 0).
/// 2. Cảnh báo điểm sắp hết hạn trước 14 ngày (86 ngày không thanh toán).
/// 3. Thu hồi điểm thưởng đối với hồ sơ không giao dịch trên 100 ngày qua sổ cái.
/// 4. Chuyển trạng thái Voucher quá hạn sang Expired.
/// 5. Tự động xét giữ hạng 1 tháng, mở 30 ngày ân hạn khi thiếu chi tiêu, hạ tối đa 1 bậc liền kề sau ân hạn.
/// </summary>
public class LoyaltyDailyJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoyaltyDailyJob> _logger;

    public LoyaltyDailyJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LoyaltyDailyJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoyaltyDailyJob đã khởi động.");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSafeCycleAsync(stoppingToken);

            // Tính khoảng chờ tới 02:00 sáng giờ Việt Nam tiếp theo
            var nowVn = DateTime.UtcNow.ToVn();
            var nextRunVn = nowVn.Date.AddHours(2);
            if (nextRunVn <= nowVn)
                nextRunVn = nextRunVn.AddDays(1);

            var delay = nextRunVn - nowVn;
            if (delay < TimeSpan.FromMinutes(1))
                delay = TimeSpan.FromHours(24);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("LoyaltyDailyJob đang dừng.");
    }

    private async Task RunSafeCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var campaignSvc = scope.ServiceProvider.GetRequiredService<ICampaignService>();
        var loyaltySvc = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        var voucherSvc = scope.ServiceProvider.GetRequiredService<IVoucherService>();
        var tierSvc = scope.ServiceProvider.GetRequiredService<ITierEvaluationService>();

        try
        {
            int bday = await campaignSvc.IssueBirthdayVouchersAsync(null, ct);
            if (bday > 0) _logger.LogInformation("LoyaltyDailyJob: Đã phát {Count} voucher sinh nhật.", bday);
        }
        catch (Exception ex) { _logger.LogError(ex, "Lỗi tác vụ phát voucher sinh nhật."); }

        try
        {
            int warn = await campaignSvc.SendExpiryWarningNotificationsAsync(null, ct);
            if (warn > 0) _logger.LogInformation("LoyaltyDailyJob: Đã gửi {Count} cảnh báo điểm sắp hết hạn.", warn);
        }
        catch (Exception ex) { _logger.LogError(ex, "Lỗi tác vụ cảnh báo điểm sắp hết hạn."); }

        try
        {
            int exp = await loyaltySvc.ProcessPointExpirationsAsync(ct);
            if (exp > 0) _logger.LogInformation("LoyaltyDailyJob: Đã xử lý hết hạn điểm cho {Count} khách hàng.", exp);
        }
        catch (Exception ex) { _logger.LogError(ex, "Lỗi tác vụ hết hạn điểm."); }

        try
        {
            int vExp = await voucherSvc.ExpireOverdueVouchersAsync(null, ct);
            if (vExp > 0) _logger.LogInformation("LoyaltyDailyJob: Đã chuyển {Count} voucher quá hạn sang Expired.", vExp);
        }
        catch (Exception ex) { _logger.LogError(ex, "Lỗi tác vụ hết hạn voucher."); }

        try
        {
            var tierSum = await tierSvc.ProcessDailyTierReviewsAsync(null, ct);
            if (tierSum.RenewedCount + tierSum.EnteredGraceCount + tierSum.DowngradedCount > 0)
            {
                _logger.LogInformation("LoyaltyDailyJob TierReview (1M): Gia hạn={Renew}, Ân hạn 30 ngày={Grace}, Hạ 1 bậc liền kề={Down}.",
                    tierSum.RenewedCount, tierSum.EnteredGraceCount, tierSum.DowngradedCount);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Lỗi tác vụ xét giữ hạng 1 tháng / ân hạn 30 ngày / hạ 1 bậc."); }
    }
}
