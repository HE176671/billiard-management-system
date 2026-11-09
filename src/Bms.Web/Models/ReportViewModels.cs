namespace Bms.Web.Models;

public class RevenueReportViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string GroupBy { get; set; } = "day";
    public decimal TotalRevenue { get; set; }
    public int PaymentCount { get; set; }
    public int InvoiceCount { get; set; }
    public List<RevenuePeriodRow> Periods { get; set; } = new();
    public List<RevenueMethodRow> Methods { get; set; } = new();
}
public record RevenuePeriodRow(string Period, decimal Revenue, int PaymentCount);
public record RevenueMethodRow(string Method, decimal Revenue, int PaymentCount);

// ══════════════════════════════════════════════════════════════════════════════
// Báo cáo vận hành (OperationsReport) — thay thế phần quản lý ca làm cho Manager
// ══════════════════════════════════════════════════════════════════════════════

public class OperationsReportViewModel
{
    // Bộ lọc (Input)
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int OpenHour { get; set; } = 8;
    public int CloseHour { get; set; } = 24;
    public int? TableTypeId { get; set; }
    public List<TableTypeFilterOption> TableTypeOptions { get; set; } = new();

    // Chỉ số tổng quan (KPI)
    public int TotalDays { get; set; }
    public int TotalTables { get; set; }
    public int ClosedSessionCount { get; set; }
    public int ActiveSessionCount { get; set; }
    public decimal TotalPlayHours { get; set; }
    public decimal AvgSessionMinutes { get; set; }
    public decimal AvgSessionsPerDay { get; set; }
    public decimal OverallOccupancyPercent { get; set; }
    public decimal TotalPlaytimeRevenue { get; set; }
    public decimal TotalFandBRevenue { get; set; }

    // Bảng phân tích chi tiết
    public List<TableTypeOccupancyRow> TableTypeRows { get; set; } = new();
    public List<TableUtilizationRow> TableRows { get; set; } = new();
    public List<TableUtilizationRow> MostUsedTables { get; set; } = new();
    public List<TableUtilizationRow> LeastUsedTables { get; set; } = new();
    public List<TopFandBRow> TopFandBItems { get; set; } = new();

    // Heatmap giờ cao điểm: 7 thứ (T2 -> CN) x các giờ trong [OpenHour, CloseHour)
    public List<int> HeatmapHours { get; set; } = new();
    public List<HeatmapDayRow> HeatmapRows { get; set; } = new();
    public string PeakSlotLabel { get; set; } = "—";
    public decimal PeakSlotPercent { get; set; }
}

public record TableTypeFilterOption(int Id, string Name);

public record TableTypeOccupancyRow(
    int TableTypeId,
    string TableTypeName,
    int TableCount,
    int ClosedSessionCount,
    decimal OccupiedHours,
    decimal AvailableHours,
    decimal OccupancyPercent,
    decimal PlaytimeRevenue
);

public record TableUtilizationRow(
    int TableId,
    string TableCode,
    string TableTypeName,
    int FloorNumber,
    string CurrentStatus,
    int SessionCount,
    decimal OccupiedHours,
    decimal OccupancyPercent,
    decimal PlaytimeRevenue
);

public record HeatmapDayRow(
    DayOfWeek DayOfWeek,
    string DayLabel,
    int DayOccurrencesInRange,
    List<HeatmapCell> Cells
);

public record HeatmapCell(
    int Hour,
    decimal OccupiedMinutes,
    decimal CapacityMinutes,
    decimal OccupancyPercent
);

public record TopFandBRow(
    int ProductId,
    string ProductName,
    int GrossQuantity,
    int CancelledQuantity,
    int NetQuantity,
    decimal UnitPrice,
    decimal Revenue
);

