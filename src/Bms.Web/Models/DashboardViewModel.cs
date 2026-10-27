namespace Bms.Web.Models;

public class DashboardViewModel
{
    public int StaffCount { get; set; }
    public int CustomerCount { get; set; }
    public int AvailableTables { get; set; }
    public int InUseTables { get; set; }
    public int AwaitingPaymentTables { get; set; }
    /// <summary>Maintenance + Inactive.</summary>
    public int UnavailableTables { get; set; }
    public int TotalTables { get; set; }
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
    public int ActiveShiftCount { get; set; }
    /// <summary>Ca đã đóng hôm nay có tiền thực tế khác kỳ vọng.</summary>
    public int TodayShiftsWithDifference { get; set; }
}
