using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Tháng của mục tiêu so với hôm nay (giờ Việt Nam).</summary>
public enum TargetMonthState { Future, Current, Ended }

/// <summary>Kết quả theo dõi một mục tiêu tháng. Mọi số đều tính lại mỗi lần xem, không lưu DB.</summary>
public record RevenueTargetProgress(
    int Id, int Year, int Month, decimal TargetAmount, string? Note,
    TargetMonthState State,
    decimal ActualRevenue,     // tổng PaymentTransactions.AmountPaid trong tháng (đến hiện tại)
    decimal PercentAchieved,   // ActualRevenue / TargetAmount × 100
    int DaysInMonth,
    int DaysElapsed,           // số ngày đã qua tính cả hôm nay (tháng tương lai = 0, tháng đã hết = DaysInMonth)
    decimal ForecastRevenue,   // dự báo cuối tháng = thực thu / số ngày đã qua × số ngày trong tháng
    decimal RequiredPerDay)    // cần thu thêm mỗi ngày còn lại (kể cả hôm nay) để đạt mục tiêu
{
    public string Label => $"{Month:00}/{Year}";
    public decimal Remaining => Math.Max(0, TargetAmount - ActualRevenue);
    public bool Achieved => ActualRevenue >= TargetAmount;

    /// <summary>Đánh giá cho Manager: Đạt / Không đạt / Đúng tiến độ / Chậm tiến độ / Chưa bắt đầu.</summary>
    public string StatusText => State switch
    {
        TargetMonthState.Future => "Chưa bắt đầu",
        TargetMonthState.Ended  => Achieved ? "Đạt" : "Không đạt",
        _ => Achieved || ForecastRevenue >= TargetAmount ? "Đúng tiến độ" : "Chậm tiến độ"
    };
}

public interface IRevenueTargetService
{
    /// <summary>Tổng thực thu của một tháng theo giờ Việt Nam, cùng nguồn với Báo cáo doanh thu.</summary>
    Task<decimal> GetActualRevenueAsync(int year, int month, CancellationToken ct = default);
    Task<RevenueTargetProgress?> GetProgressAsync(int year, int month, CancellationToken ct = default);
    Task<List<RevenueTargetProgress>> GetAllProgressAsync(CancellationToken ct = default);
}

public class RevenueTargetService : IRevenueTargetService
{
    private readonly ApplicationDbContext _db;
    public RevenueTargetService(ApplicationDbContext db) => _db = db;

    public static TargetMonthState GetState(int year, int month, DateOnly todayVn)
    {
        int key = year * 12 + month, now = todayVn.Year * 12 + todayVn.Month;
        return key > now ? TargetMonthState.Future : key == now ? TargetMonthState.Current : TargetMonthState.Ended;
    }

    /// <summary>
    /// Công thức thuần (không đọc DB) để dễ kiểm thử và giải thích:
    /// - % đạt = thực thu / mục tiêu × 100
    /// - dự báo = thực thu / số ngày đã qua × số ngày trong tháng (chỉ tháng đang chạy)
    /// - cần mỗi ngày = phần còn thiếu / số ngày còn lại (kể cả hôm nay)
    /// </summary>
    public static RevenueTargetProgress Calculate(RevenueTarget t, decimal actual, DateOnly todayVn)
    {
        var state = GetState(t.Year, t.Month, todayVn);
        int daysInMonth = DateTime.DaysInMonth(t.Year, t.Month);
        int elapsed = state switch
        {
            TargetMonthState.Future => 0,
            TargetMonthState.Ended  => daysInMonth,
            _ => todayVn.Day
        };

        decimal percent = t.TargetAmount > 0 ? Math.Round(actual / t.TargetAmount * 100, 1) : 0;
        decimal forecast = state switch
        {
            TargetMonthState.Current => Math.Round(actual / elapsed * daysInMonth, 0),
            TargetMonthState.Ended   => actual,
            _ => 0
        };
        int daysLeft = state == TargetMonthState.Current ? daysInMonth - todayVn.Day + 1
                     : state == TargetMonthState.Future ? daysInMonth : 0;
        decimal perDay = daysLeft > 0 ? Math.Ceiling(Math.Max(0, t.TargetAmount - actual) / daysLeft) : 0;

        return new RevenueTargetProgress(t.Id, t.Year, t.Month, t.TargetAmount, t.Note, state,
            actual, percent, daysInMonth, elapsed, forecast, perDay);
    }

    public async Task<decimal> GetActualRevenueAsync(int year, int month, CancellationToken ct = default)
    {
        // Nửa khoảng [00:00 ngày 1, 00:00 ngày 1 tháng sau) theo giờ VN, đổi về UTC.
        var first = new DateOnly(year, month, 1);
        var startUtc = VnTime.StartOfDayUtc(first);
        var endUtc = VnTime.StartOfDayUtc(first.AddMonths(1));
        return await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= startUtc && p.TransactionTimeUtc < endUtc)
            .SumAsync(p => (decimal?)p.AmountPaid, ct) ?? 0m;
    }

    public async Task<RevenueTargetProgress?> GetProgressAsync(int year, int month, CancellationToken ct = default)
    {
        var target = await _db.RevenueTargets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Year == year && t.Month == month, ct);
        if (target == null) return null;
        var actual = GetState(year, month, VnTime.Today) == TargetMonthState.Future
            ? 0 : await GetActualRevenueAsync(year, month, ct);
        return Calculate(target, actual, VnTime.Today);
    }

    public async Task<List<RevenueTargetProgress>> GetAllProgressAsync(CancellationToken ct = default)
    {
        var today = VnTime.Today;
        var targets = await _db.RevenueTargets.AsNoTracking()
            .OrderByDescending(t => t.Year).ThenByDescending(t => t.Month)
            .ToListAsync(ct);
        if (targets.Count == 0) return new();

        // Một truy vấn cho tất cả các tháng: lấy giao dịch từ tháng cũ nhất đến hết tháng mới nhất,
        // rồi cộng theo (năm, tháng) giờ VN trên bộ nhớ.
        var oldest = targets[^1];
        var newest = targets[0];
        var startUtc = VnTime.StartOfDayUtc(new DateOnly(oldest.Year, oldest.Month, 1));
        var endUtc = VnTime.StartOfDayUtc(new DateOnly(newest.Year, newest.Month, 1).AddMonths(1));
        var payments = await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= startUtc && p.TransactionTimeUtc < endUtc)
            .Select(p => new { p.TransactionTimeUtc, p.AmountPaid })
            .ToListAsync(ct);
        var byMonth = payments
            .GroupBy(p => { var vn = p.TransactionTimeUtc.ToVn(); return vn.Year * 12 + vn.Month; })
            .ToDictionary(g => g.Key, g => g.Sum(p => p.AmountPaid));

        return targets
            .Select(t => Calculate(t, byMonth.GetValueOrDefault(t.Year * 12 + t.Month), today))
            .ToList();
    }
}
