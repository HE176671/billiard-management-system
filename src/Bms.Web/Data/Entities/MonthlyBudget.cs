namespace Bms.Web.Data;

/// <summary>
/// Định mức ngân sách 4 luồng chi phí &amp; Kế hoạch tối ưu mùa vụ theo tháng (bảng dbo.MonthlyBudgets).
/// Chuyển hóa các phát hiện mùa vụ (T3-4-5 cao điểm giờ chơi, T4-5-6 cao điểm bia/nước, Ca Đêm 74% vs Ca Ngày 26%)
/// thành hạn mức kiểm soát chi tiêu và kế hoạch hành động thực tế cho CFO.
/// </summary>
public class MonthlyBudget
{
    public const string StatusDraft  = "Draft";
    public const string StatusActive = "Active";
    public const string StatusLocked = "Locked";

    public int      Id                { get; set; }
    public string   PeriodMonth       { get; set; } = string.Empty; // yyyy-MM
    public decimal  RevenueTarget     { get; set; }
    public decimal  MaintenanceCap    { get; set; }
    public decimal  InventoryCogsCap  { get; set; }
    public decimal  UtilitiesCap      { get; set; }
    public decimal  PayrollCap        { get; set; }
    public string   StrategicFocus    { get; set; } = string.Empty;
    public string   ActionPlanSummary { get; set; } = string.Empty;
    public string   Status            { get; set; } = StatusActive;
    public string?  UpdatedById       { get; set; }
    public DateTime UpdatedAtUtc      { get; set; } = DateTime.UtcNow;

    public ApplicationUser? UpdatedBy { get; set; }

    public decimal TotalExpenseCap => MaintenanceCap + InventoryCogsCap + UtilitiesCap + PayrollCap;
    public decimal TargetNetProfit => RevenueTarget - TotalExpenseCap;

    public static string GetStatusDisplayName(string status) => status switch
    {
        StatusDraft  => "Bản nháp (Đang lập kế hoạch)",
        StatusActive => "Đang áp dụng kiểm soát trần chi",
        StatusLocked => "Đã chốt sổ kỳ ngân sách",
        _            => status
    };
}
