using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin,Staff")]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new DashboardViewModel();
        
        // 1. Dem tong so nhan vien (Staff)
        var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
        vm.TotalActiveStaff = staffUsers.Count(u => u.IsActive);
        
        // 2. Dem tong so khach hang (Customer)
        var customers = await _userManager.GetUsersInRoleAsync("Customer");
        vm.TotalCustomers = customers.Count(u => u.IsActive);

        // 3. Query DB thuc te (ado.net vi chua co EF entity cho BilliardTables)
        var conn = _context.Database.GetDbConnection();
        var wasClosed = conn.State == ConnectionState.Closed;
        
        try 
        {
            if (wasClosed) await conn.OpenAsync();
            
            // So ban dang co khach (Status = 'InUse')
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM dbo.BilliardTables WHERE Status = 'InUse'";
                vm.TablesInUse = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // Tong doanh thu tien gio trong hom nay
            using (var cmd = conn.CreateCommand())
            {
                // Truy van dung gio UTC, ban co the sua thanh datetime phu hop
                cmd.CommandText = "SELECT ISNULL(SUM(PlaytimeAmount), 0) FROM dbo.PlaySessions WHERE Status = 'Closed' AND CAST(EndAtUtc AS DATE) = CAST(GETUTCDATE() AS DATE)";
                vm.TodayRevenue = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
            }
        }
        finally
        {
            if (wasClosed) await conn.CloseAsync();
        }

        return View(vm);
    }
}
