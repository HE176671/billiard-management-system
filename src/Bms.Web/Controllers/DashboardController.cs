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

    public async Task<IActionResult> Index()
    {
        var vm = new DashboardViewModel();

        // ══════════════════════════════════════════════════════════════
        // 1. Hung: Tables, Shifts & Payments
        // ══════════════════════════════════════════════════════════════
        var today = DateTime.UtcNow.AddHours(7).Date;
        var start = today.AddHours(-7);
        var end = start.AddDays(1);
        var month = new DateTime(today.Year, today.Month, 1).AddHours(-7);

        var tables = await _context.BilliardTables.AsNoTracking()
            .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        vm.StaffCount = await CountRoleAsync("Staff");
        vm.CustomerCount = await CountRoleAsync("Customer");
        vm.TotalTables = tables.Values.Sum();
        vm.AvailableTables = tables.GetValueOrDefault("Available");
        vm.InUseTables = tables.GetValueOrDefault("InUse");
        vm.AwaitingPaymentTables = tables.GetValueOrDefault("AwaitingPayment");
        vm.UnavailableTables = tables.GetValueOrDefault("Maintenance") + tables.GetValueOrDefault("Inactive");

        vm.TodayRevenue = await _context.PaymentTransactions
            .Where(p => p.TransactionTimeUtc >= start && p.TransactionTimeUtc < end)
            .SumAsync(p => (decimal?)p.AmountPaid) ?? 0;

        vm.MonthRevenue = await _context.PaymentTransactions
            .Where(p => p.TransactionTimeUtc >= month && p.TransactionTimeUtc < end)
            .SumAsync(p => (decimal?)p.AmountPaid) ?? 0;

        vm.ActiveShiftCount = await _context.WorkShifts.CountAsync(s => s.Status == WorkShift.StatusActive);
        vm.TodayShiftsWithDifference = await _context.WorkShifts.CountAsync(s => s.Status == WorkShift.StatusClosed
            && s.EndTimeUtc >= start && s.EndTimeUtc < end && s.EndingCash != s.ExpectedCash);

        // ══════════════════════════════════════════════════════════════
        // 2. TV2 (Dung): Khách hàng thành viên & Phân bổ Hạng
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
            tierCounts.TryGetValue(tier.Id, out int count);
            string color = tierColors.TryGetValue(tier.TierName, out var c) ? c : "#6b7280";
            vm.TierDistribution.Add(new TierDistributionPoint
            {
                TierName = tier.TierName,
                Count = count,
                ColorHex = color
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 3. TV2 (Dung): Đánh giá chất lượng dịch vụ (Reviews)
        // ══════════════════════════════════════════════════════════════
        var reviewStats = await _context.Reviews
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                AvgRating = g.Average(r => (double)r.Rating),
                Total = g.Count()
            })
            .FirstOrDefaultAsync();

        if (reviewStats != null && reviewStats.Total > 0)
        {
            vm.AverageRating = Math.Round(reviewStats.AvgRating, 1);
            vm.TotalReviews = reviewStats.Total;
        }
        else
        {
            vm.AverageRating = 4.8;
            vm.TotalReviews = 0;
        }

        // ══════════════════════════════════════════════════════════════
        // 4. TV2 (Dung): Doanh thu tháng & Giờ chơi
        // ══════════════════════════════════════════════════════════════
        if (vm.MonthRevenue > 0)
        {
            vm.MonthlyRevenue = vm.MonthRevenue;
            vm.IsRevenueMocked = false;
        }
        else
        {
            vm.MonthlyRevenue = 28500000m;
            vm.IsRevenueMocked = true;
        }

        vm.TotalPlaytimeHours = 468.5m;
        vm.IsPlaytimeMocked = true;

        // ══════════════════════════════════════════════════════════════
        // 5. TV2 (Dung): Biểu đồ Doanh thu 7 ngày gần nhất
        // ══════════════════════════════════════════════════════════════
        var sevenDaysAgo = today.AddDays(-6);
        var real7Days = await _context.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= sevenDaysAgo.AddHours(-7))
            .GroupBy(p => p.TransactionTimeUtc.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        if (real7Days.Any(x => x.Total > 0))
        {
            for (int i = -6; i <= 0; i++)
            {
                var d = today.AddDays(i);
                var match = real7Days.FirstOrDefault(x => x.Date == d);
                vm.Last7DaysRevenue.Add(new DailyRevenuePoint
                {
                    DateLabel = $"{FormatDayOfWeek(d.DayOfWeek)} ({d:dd/MM})",
                    Amount = match?.Total ?? 0
                });
            }
        }
        else
        {
            var mockAmounts = new[] { 3200000m, 4100000m, 2800000m, 3900000m, 5200000m, 6800000m, 4500000m };
            for (int i = -6; i <= 0; i++)
            {
                var d = today.AddDays(i);
                vm.Last7DaysRevenue.Add(new DailyRevenuePoint
                {
                    DateLabel = $"{FormatDayOfWeek(d.DayOfWeek)} ({d:dd/MM})",
                    Amount = mockAmounts[i + 6]
                });
            }
        }

        return View(vm);
    }

    private Task<int> CountRoleAsync(string role) => (
        from ur in _context.UserRoles
        join r in _context.Roles on ur.RoleId equals r.Id
        where r.Name == role
        select ur.UserId).Distinct().CountAsync();

    private static string FormatDayOfWeek(DayOfWeek dow) => dow switch
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
}
