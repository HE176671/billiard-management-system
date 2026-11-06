namespace Bms.Web.Data;

/// <summary>
/// Hồ sơ vụ việc kiểm toán thất thoát doanh thu (bảng dbo.FinancialAuditCases).
/// Quản lý vòng đời End-to-End: Phát hiện (Open) -> Điều tra (Investigating) -> Xử lý thu hồi/khấu trừ (Resolved).
/// </summary>
public class FinancialAuditCase
{
    public const string StatusOpen          = "Open";
    public const string StatusInvestigating = "Investigating";
    public const string StatusResolved      = "Resolved";

    public const string ResRecoveredCash   = "RecoveredCash";   // Truy thu tiền mặt bổ sung (Cộng vào Doanh thu thu hồi)
    public const string ResDeductedPayroll = "DeductedPayroll"; // Khấu trừ lương nhân sự vi phạm
    public const string ResWaived          = "Waived";          // Miễn trừ có biên bản giải trình hợp lệ

    public int       Id                { get; set; }
    public string    CaseCode          { get; set; } = string.Empty;
    public string    LeakageCode       { get; set; } = string.Empty;
    public string    RuleName          { get; set; } = string.Empty;
    public string    Severity          { get; set; } = "Critical";
    public DateTime  OccurredAtUtc     { get; set; }
    public string    TableOrShiftLabel { get; set; } = string.Empty;
    public string    StaffName         { get; set; } = string.Empty;
    public decimal   ExpectedAmount    { get; set; }
    public decimal   ActualAmount      { get; set; }
    public decimal   LeakageAmount     { get; set; }
    public decimal   RecoveredAmount   { get; set; }
    public string    Status            { get; set; } = StatusOpen;
    public string?   ResolutionType    { get; set; }
    public string    EvidenceDetail    { get; set; } = string.Empty;
    public string    RecommendedAction { get; set; } = string.Empty;
    public string    ProblemSolved     { get; set; } = string.Empty;
    public string?   InvestigationNote { get; set; }
    public string?   ResolvedById      { get; set; }
    public DateTime? ResolvedAtUtc     { get; set; }
    public DateTime  CreatedAtUtc      { get; set; } = DateTime.UtcNow;

    public ApplicationUser? ResolvedBy { get; set; }

    public static string GetStatusDisplayName(string status) => status switch
    {
        StatusOpen          => "Mới phát hiện (Chưa xử lý)",
        StatusInvestigating => "Đang điều tra đối chiếu",
        StatusResolved      => "Đã xử lý & Đóng hồ sơ",
        _                   => status
    };

    public static string GetResolutionDisplayName(string? resType) => resType switch
    {
        ResRecoveredCash   => "Truy thu tiền mặt bổ sung vào quỹ",
        ResDeductedPayroll => "Khấu trừ vào bảng lương nhân viên vi phạm",
        ResWaived          => "Miễn trừ (Có giải trình hợp lệ)",
        _                  => "Chưa áp dụng chế tài"
    };
}
