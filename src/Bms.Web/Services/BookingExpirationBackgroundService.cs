using Bms.Web.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

/// <summary>
/// Tự động kiểm tra mỗi 30 giây và chuyển các Booking quá 15 phút kể từ giờ đặt mà chưa Check-in sang trạng thái NoShow.
/// </summary>
public class BookingExpirationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingExpirationBackgroundService> _logger;

    public BookingExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var expiredCount = await bookingService.AutoExpireOverdueBookingsAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Đã tự động chuyển {ExpiredCount} booking quá hạn 15 phút sang trạng thái NoShow.", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong tiến trình nền BookingExpirationBackgroundService.");
            }

            await Task.Delay(TimeSpan.FromSeconds(BookingPolicy.AutoExpireCheckSeconds), stoppingToken);
        }
    }
}
