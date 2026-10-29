using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // GET /Dashboard
    public async Task<IActionResult> Index()
    {
        var vm = new DashboardViewModel();

        // ══════════════════════════════════════════════════════════════
        // 1. DỮ LIỆU THẬT: Khách hàng thành viên & Phân bổ Hạng
        // ══════════════════════════════════════════════════════════════
        var customerRoleId = await _context.Roles
            .Where(r => r.Name == "Customer")
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

        var customerQuery = _context.Users
            .AsNoTracking()
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId));

        vm.TotalCustomerMembers = await customerQuery.CountAsync();

        var tiers = await _context.MembershipTiers
            .AsNoTracking()
            .OrderBy(t => t.SpendThreshold)
            .ToListAsync();

        var tierCounts = await customerQuery
            .Where(u => u.TierId != null)
            .GroupBy(u => u.TierId!.Value)
            .Select(g => new { TierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TierId, x => x.Count);

        var tierColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Member"] = "#3b82f6", // Xanh dương
            ["Silver"] = "#94a3b8", // Bạc / Xám
            ["Gold"] = "#f59e0b"    // Vàng cam
        };

        foreach (var tier in tiers)
        {
            int count = tierCounts.GetValueOrDefault(tier.Id, 0);
            vm.TierDistribution.Add(new TierDistributionPoint
            {
                TierName = tier.TierName,
                Count = count,
                ColorHex = tierColors.GetValueOrDefault(tier.TierName, "#64748b")
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 2. DỮ LIỆU THẬT: Đánh giá & Điểm sao trung bình
        // ══════════════════════════════════════════════════════════════
        var reviews = await _context.Reviews.AsNoTracking().ToListAsync();
        vm.TotalReviews = reviews.Count;
        vm.AverageRating = reviews.Count > 0
            ? Math.Round(reviews.Average(r => (double)r.Rating), 1)
            : 5.0;

        // ══════════════════════════════════════════════════════════════
        // 3. DOANH THU THÁNG & 7 NGÀY GẦN NHẤT (Invoices)
        // ══════════════════════════════════════════════════════════════
        // TODO: Connect real data from Member X (Invoices)
        bool hasRealRevenue = false;
        try
        {
            var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var monthlySum = (await _context.Database.SqlQuery<decimal>($@"
                SELECT ISNULL(SUM(FinalAmount), 0) AS [Value]
                FROM dbo.Invoices
                WHERE Status = 'Paid' AND CreatedAtUtc >= {startOfMonth}").ToListAsync()).FirstOrDefault();

            if (monthlySum > 0)
            {
                vm.MonthlyRevenue = monthlySum;
                hasRealRevenue = true;
            }

            // Truy vấn doanh thu 7 ngày gần nhất
            var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
            var dailyData = await _context.Database.SqlQuery<DailyRevenueRaw>($@"
                SELECT CAST(CreatedAtUtc AS DATE) AS [Date], ISNULL(SUM(FinalAmount), 0) AS [Total]
                FROM dbo.Invoices
                WHERE Status = 'Paid' AND CreatedAtUtc >= {sevenDaysAgo}
                GROUP BY CAST(CreatedAtUtc AS DATE)
                ORDER BY [Date]").ToListAsync();

            if (dailyData.Count > 0 && dailyData.Sum(d => d.Total) > 0)
            {
                var dailyDict = dailyData.ToDictionary(d => d.Date.ToString("yyyy-MM-dd"), d => d.Total);
                for (int i = 6; i >= 0; i--)
                {
                    var day = DateTime.UtcNow.Date.AddDays(-i);
                    var key = day.ToString("yyyy-MM-dd");
                    vm.Last7DaysRevenue.Add(new DailyRevenuePoint
                    {
                        DateLabel = $"{GetDayOfWeekVn(day.DayOfWeek)} {day:dd/MM}",
                        Amount = dailyDict.GetValueOrDefault(key, 0)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chưa thể truy vấn dữ liệu từ Invoices. Sẽ sử dụng Mock Data.");
        }

        // Áp dụng Mock Data nếu chưa có dữ liệu doanh thu thực tế từ thành viên khác
        if (!hasRealRevenue || vm.Last7DaysRevenue.Count == 0)
        {
            vm.IsRevenueMocked = true;
            vm.MonthlyRevenue = 38_650_000m; // 38.65 triệu VNĐ

            vm.Last7DaysRevenue.Clear();
            var mockAmounts = new decimal[] { 4_200_000m, 5_100_000m, 3_850_000m, 6_400_000m, 7_250_000m, 8_650_000m, 5_450_000m };
            for (int i = 6; i >= 0; i--)
            {
                var day = DateTime.UtcNow.Date.AddDays(-i);
                vm.Last7DaysRevenue.Add(new DailyRevenuePoint
                {
                    DateLabel = $"{GetDayOfWeekVn(day.DayOfWeek)} {day:dd/MM}",
                    Amount = mockAmounts[6 - i]
                });
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 4. TỔNG SỐ GIỜ CHƠI (PlaySessions)
        // ══════════════════════════════════════════════════════════════
        // TODO: Connect real data from Member X (PlaySessions)
        bool hasRealPlaytime = false;
        try
        {
            var totalMinutes = (await _context.Database.SqlQuery<int>($@"
                SELECT ISNULL(SUM(DATEDIFF(MINUTE, StartAtUtc, EndAtUtc)), 0) AS [Value]
                FROM dbo.PlaySessions
                WHERE Status = 'Closed' AND EndAtUtc IS NOT NULL").ToListAsync()).FirstOrDefault();

            if (totalMinutes > 0)
            {
                vm.TotalPlaytimeHours = Math.Round(totalMinutes / 60.0m, 1);
                hasRealPlaytime = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chưa thể truy vấn dữ liệu từ PlaySessions. Sẽ sử dụng Mock Data.");
        }

        if (!hasRealPlaytime)
        {
            vm.IsPlaytimeMocked = true;
            vm.TotalPlaytimeHours = 485.5m; // Mock 485.5 giờ
        }

        return View(vm);
    }

    private static string GetDayOfWeekVn(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "T2",
        DayOfWeek.Tuesday => "T3",
        DayOfWeek.Wednesday => "T4",
        DayOfWeek.Thursday => "T5",
        DayOfWeek.Friday => "T6",
        DayOfWeek.Saturday => "T7",
        DayOfWeek.Sunday => "CN",
        _ => ""
    };

    public class DailyRevenueRaw
    {
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
    }
}
