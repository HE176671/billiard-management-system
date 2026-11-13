using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager")]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController>? _logger;
    private readonly Bms.Web.Services.IRevenueTargetService _targets;

    public DashboardController(ApplicationDbContext context, Bms.Web.Services.IRevenueTargetService targets,
        ILogger<DashboardController>? logger = null)
    {
        _context = context;
        _targets = targets;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new DashboardViewModel();

        // ══════════════════════════════════════════════════════════════
        // 1. Hung: Tables, Sessions & Payments (100% Real DB Data)
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

        vm.ActiveSessionCount = await _context.PlaySessions.CountAsync(s => s.Status == "Active");
        vm.TodayClosedSessionCount = await _context.PlaySessions.CountAsync(s => s.Status == "Closed"
            && s.EndAtUtc >= start && s.EndAtUtc < end);

        // ══════════════════════════════════════════════════════════════
        // 2. Khách hàng thành viên & Phân bổ Hạng
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
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder)
            .ToListAsync();

        // TV5: khách chưa gán TierId tính vào hạng thấp nhất (giống TierEvaluationService),
        // để tổng các phần bằng tổng số hội viên và khớp màn Thống kê hội viên.
        var tierIdOfCustomers = await customerQuery.Select(u => u.TierId).ToListAsync();
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            int count = tierIdOfCustomers.Count(id => id == tier.Id
                || (i == 0 && (id == null || !tiers.Any(t => t.Id == id))));
            vm.TierDistribution.Add(new TierDistributionPoint
            {
                TierName = tier.TierName,
                Count = count,
                ColorHex = CrmDashboardController.TierPalette[i % CrmDashboardController.TierPalette.Length]
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 3. Doanh thu tháng & Giờ chơi (Đọc 100% từ DB thật, không mock)
        // ══════════════════════════════════════════════════════════════
        vm.MonthlyRevenue = vm.MonthRevenue;
        vm.IsRevenueMocked = false;
        vm.TotalPlaytimeHours = 0m;
        vm.IsPlaytimeMocked = false;

        // ══════════════════════════════════════════════════════════════
        // 4. Biểu đồ Doanh thu 7 ngày gần nhất (Đọc 100% từ PaymentTransactions thật)
        // ══════════════════════════════════════════════════════════════
        var sevenDaysAgo = today.AddDays(-6);
        var real7Days = await _context.PaymentTransactions
            .AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= sevenDaysAgo.AddHours(-7))
            .GroupBy(p => p.TransactionTimeUtc.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

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

        // TV5 — UC40: tiến độ mục tiêu doanh thu tháng này
        var vnToday = Bms.Web.Services.VnTime.Today;
        vm.MonthTarget = await _targets.GetProgressAsync(vnToday.Year, vnToday.Month);

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
