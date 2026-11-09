using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

/// <summary>
/// TV5 — JOB-01 + JOB-02: chạy chu kỳ cuối ngày lúc 00:05 giờ Việt Nam mỗi ngày,
/// và một lần khoảng 15 giây sau khi web khởi động để bù ngày bị lỡ khi server tắt.
/// Nội dung chu kỳ nằm ở <see cref="IDailyCycleService"/>:
/// 1. Phát voucher sinh nhật (chỉ dùng trong ngày sinh nhật, 1 lần/năm, theo hạng).
/// 2. Chuyển voucher quá hạn sang Expired.
/// 3. Xét giữ hạng 12 tháng, mở 30 ngày ân hạn khi thiếu chi tiêu, hạ đúng 1 bậc khi hết ân hạn.
/// 4. Gửi email thông báo hạng và voucher cho hội viên có email.
/// Mỗi bước đều chạy lại an toàn (không phát trùng voucher, không xét trùng).
/// </summary>
public class LoyaltyDailyJob : BackgroundService
{
    private static readonly TimeSpan RunAtVn = new(0, 5, 0);

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

            // Chờ tới 00:05 giờ Việt Nam tiếp theo
            var nowVn = DateTime.UtcNow.ToVn();
            var nextRunVn = nowVn.Date.Add(RunAtVn);
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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var cycle = scope.ServiceProvider.GetRequiredService<IDailyCycleService>();
            var result = await cycle.RunAsync(actorId: null, manual: false, ct);
            var t = result.Summary.TierSummary;
            _logger.LogInformation(
                "LoyaltyDailyJob: voucher sinh nhật +{Bday}, voucher hết hạn {VExp}, giữ hạng {Renew}, ân hạn {Grace}, hạ hạng {Down}, email gửi {Sent}.",
                result.Summary.BirthdayVouchersIssued, result.Summary.VouchersExpired,
                t.RenewedCount, t.EnteredGraceCount, t.DowngradedCount, result.EmailsSent);
        }
        catch (OperationCanceledException)
        {
            // web đang dừng
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoyaltyDailyJob lỗi; sẽ chạy lại ở lần kế tiếp.");
        }
    }
}
