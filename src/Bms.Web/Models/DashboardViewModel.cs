namespace Bms.Web.Models;

public class DashboardViewModel
{
    // 4 Stat Cards
    public decimal MonthlyRevenue { get; set; }
    public decimal TotalPlaytimeHours { get; set; }
    public int TotalCustomerMembers { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }

    // Mock indicators
    public bool IsRevenueMocked { get; set; }
    public bool IsPlaytimeMocked { get; set; }

    // Biểu đồ 1: Doanh thu 7 ngày gần nhất
    public List<DailyRevenuePoint> Last7DaysRevenue { get; set; } = new();

    // Biểu đồ 2: Tỷ lệ phân bổ hạng thành viên
    public List<TierDistributionPoint> TierDistribution { get; set; } = new();
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
