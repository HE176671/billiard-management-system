using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class DashboardViewModel
{
    public int TotalActiveStaff { get; set; }
    public int TotalCustomers { get; set; }
    public int TablesInUse { get; set; }
    public decimal TodayRevenue { get; set; }
}
