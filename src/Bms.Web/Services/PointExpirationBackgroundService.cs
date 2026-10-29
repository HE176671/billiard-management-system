using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.Web.Services;

/// <summary>
/// Background Service chạy định kỳ quét và reset điểm thưởng hết hạn sau 180 ngày không phát sinh thanh toán mới.
/// </summary>
public class PointExpirationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PointExpirationBackgroundService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public PointExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PointExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PointExpirationBackgroundService đã khởi động.");

        // Chờ 15 giây sau khi ứng dụng khởi chạy trước lần quét đầu tiên
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
            try
            {
                _logger.LogInformation("Bắt đầu tiến trình kiểm tra điểm thưởng hết hạn (>180 ngày)...");

                using var scope = _scopeFactory.CreateScope();
                var loyaltyService = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();

                int expiredCount = await loyaltyService.ProcessPointExpirationsAsync(stoppingToken);

                if (expiredCount > 0)
                {
                    _logger.LogInformation("Đã xử lý hết hạn điểm thưởng cho {Count} khách hàng.", expiredCount);
                }
                else
                {
                    _logger.LogInformation("Không có tài khoản nào có điểm thưởng hết hạn trong lần quét này.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình quét điểm thưởng hết hạn.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PointExpirationBackgroundService đang dừng.");
    }
}
