namespace Bms.Web.Models;

public class DashboardViewModel
{
    public int StaffCount { get; set; }
    public int CustomerCount { get; set; }
    public int AvailableTables { get; set; }
    public int InUseTables { get; set; }
    public int TotalTables { get; set; }
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
}
