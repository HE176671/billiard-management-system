namespace Bms.Web.Models;

public class DashboardViewModel
{
    // --- TV2 (Dung) Loyalty & Charts ---
    public decimal MonthlyRevenue { get; set; }
    /// <summary>TV5 — UC40: tiến độ mục tiêu doanh thu tháng này (null nếu chưa đặt).</summary>
    public Bms.Web.Services.RevenueTargetProgress? MonthTarget { get; set; }
    public decimal TotalPlaytimeHours { get; set; }
    public int TotalCustomerMembers { get; set; }

    // Mock indicators
    public bool IsRevenueMocked { get; set; }
    public bool IsPlaytimeMocked { get; set; }

    // Biểu đồ 1: Doanh thu 7 ngày gần nhất
    public List<DailyRevenuePoint> Last7DaysRevenue { get; set; } = new();

    // Biểu đồ 2: Tỷ lệ phân bổ hạng thành viên
    public List<TierDistributionPoint> TierDistribution { get; set; } = new();

    // --- Hung: Tables & Staff & Shifts ---
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
    /// <summary>Số phiên chơi đang mở (PlaySessions.Status = "Active").</summary>
    public int ActiveSessionCount { get; set; }
    /// <summary>Số phiên đã kết thúc trong hôm nay (giờ VN).</summary>
    public int TodayClosedSessionCount { get; set; }
}

public class DailyRevenuePoint
{
    public string DateLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class TierDistributionPoint
{
    public string TierName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string ColorHex { get; set; } = string.Empty;
}
