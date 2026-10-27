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
    public DashboardController(ApplicationDbContext context) => _context = context;
    #region UC04 - So lieu tong quan va doanh thu ngay/thang

    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.AddHours(7).Date;
        var start = today.AddHours(-7);
        var end = start.AddDays(1);
        var month = new DateTime(today.Year, today.Month, 1).AddHours(-7);
        var model = new DashboardViewModel {
            StaffCount = await CountRoleAsync("Staff"), CustomerCount = await CountRoleAsync("Customer"),
            TotalTables = await _context.BilliardTables.CountAsync(),
            AvailableTables = await _context.BilliardTables.CountAsync(t => t.Status == "Available"),
            InUseTables = await _context.BilliardTables.CountAsync(t => t.Status == "InUse"),
            TodayRevenue = await _context.PaymentTransactions.Where(p => p.TransactionTimeUtc >= start && p.TransactionTimeUtc < end)
                .SumAsync(p => (decimal?)p.AmountPaid) ?? 0,
            MonthRevenue = await _context.PaymentTransactions.Where(p => p.TransactionTimeUtc >= month && p.TransactionTimeUtc < end)
                .SumAsync(p => (decimal?)p.AmountPaid) ?? 0
        };
        return View(model);
    }
    #endregion

    #region Dem tai khoan theo role

    private Task<int> CountRoleAsync(string role) => (
        from ur in _context.UserRoles join r in _context.Roles on ur.RoleId equals r.Id
        where r.Name == role select ur.UserId).Distinct().CountAsync();
    #endregion
}
