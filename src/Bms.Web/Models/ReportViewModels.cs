using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

/// <summary>
/// Bảng mã thông báo chuẩn tiếng Việt theo đặc tả SRS.
/// </summary>
public static class FinancialMessages
{
    public const string MsgFin01InvalidOrder =
        "Ngày bắt đầu không được lớn hơn ngày kết thúc. Vui lòng chọn lại khoảng thời gian.";
    public const string MsgFin02RangeTooLong =
        "Khoảng thời gian phân tích tối đa là 367 ngày (1 năm tài chính). Vui lòng thu hẹp khoảng ngày.";
    public const string MsgFin03InvalidTitleOrCategory =
        "Vui lòng nhập tên khoản chi phí vận hành (tối đa 150 ký tự) và chọn đúng danh mục chi phí.";
    public const string MsgFin04InvalidAmount =
        "Số tiền chi phí phải lớn hơn 0 ₫ và không vượt quá 1.000.000.000 ₫.";
    public const string MsgFin06Deleted =
        "Đã xóa phiếu chi và tính toán lại báo cáo Lãi/Lỗ.";
    public const string MsgFin07SeedSuccess =
        "Đã đồng bộ thành công bộ dữ liệu tài chính 12 tháng.";
    public const string MsgFin08AuditClean =
        "Đối soát hoàn tất: Không phát hiện chênh lệch hay thất thoát doanh thu nào trong kỳ đã chọn.";
    public const string MsgFin10EmptyData =
        "Chưa có dữ liệu giao dịch trong khoảng thời gian này. Bạn có thể bấm 'Đồng bộ dữ liệu' để nạp dữ liệu phân tích.";
    public const string MsgFin11NotFound =
        "Không tìm thấy phiếu chi hoặc hồ sơ cần thao tác.";
    public const string MsgFin12TimezoneInfo =
        "Số liệu doanh thu, chi phí vận hành và đối soát được tính theo múi giờ Việt Nam (UTC+7).";
    public const string MsgFin13ExpenseUpdated =
        "Đã cập nhật thông tin phiếu chi vận hành thành công.";
    public const string MsgFin14ExpenseReviewed =
        "Đã cập nhật trạng thái phê duyệt phiếu chi và đồng bộ lại báo cáo Lãi/Lỗ.";
    public const string MsgFin15BudgetCapWarning =
        "Lưu ý: Khoản chi này làm tổng chi của danh mục vượt định mức ngân sách tháng đã thiết lập.";
    public const string MsgFin16AuditSynced =
        "Đã quét 5 quy tắc đối soát và cập nhật danh sách hồ sơ kiểm toán thất thoát.";
    public const string MsgFin17AuditResolved =
        "Đã lưu quyết định xử lý hồ sơ thất thoát và cập nhật khoản tiền thu hồi vào báo cáo Lãi/Lỗ.";
    public const string MsgFin18AuditInvalidResolution =
        "Số tiền thu hồi/khấu trừ phải lớn hơn 0 và không vượt quá số tiền thất thoát (hoặc phải nhập lý do giải trình khi miễn trừ).";
    public const string MsgFin19BudgetSaved =
        "Đã lưu định mức ngân sách tháng và kế hoạch vận hành thành công.";

    public static string FormatMsgFin05Created(string code, decimal amount) =>
        $"Đã ghi nhận phiếu chi [{code}] ({amount:N0} ₫) vào sổ cái vận hành.";

    public static string FormatMsgFin09AuditAlert(int count, decimal totalLoss) =>
        $"Phát hiện {count} hồ sơ chênh lệch dữ liệu với tổng mức thất thoát {totalLoss:N0} ₫ cần kiểm tra.";
}

public class RevenueReportViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string GroupBy { get; set; } = "month";

    // --- Chỉ số gốc (Giữ tương thích ngược 100% với UC46 & Smoke Tests) ---
    public decimal TotalRevenue { get; set; }
    public int PaymentCount { get; set; }
    public int InvoiceCount { get; set; }
    public List<RevenuePeriodRow> Periods { get; set; } = new();
    public List<RevenueMethodRow> Methods { get; set; } = new();

    // --- Bài toán 1 & 2A: Báo cáo Lãi/Lỗ (P&L) & Phân tích Mùa vụ 12 tháng ---
    public decimal TotalPlaytimeRevenue { get; set; }
    public decimal TotalFnbRevenue { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public decimal FnbSharePercent { get; set; }
    public decimal TotalOperatingCost { get; set; }
    public decimal PendingOperatingCost { get; set; }
    public int PendingExpenseCount { get; set; }
    public decimal TotalBudgetCap { get; set; }
    public decimal TotalRecoveredAmount { get; set; }
    public decimal NetProfit => TotalRevenue + TotalRecoveredAmount - TotalOperatingCost;
    public decimal NetMarginPercent => (TotalRevenue + TotalRecoveredAmount) > 0
        ? Math.Round(NetProfit / (TotalRevenue + TotalRecoveredAmount) * 100m, 1)
        : 0m;
    public int ProfitablePeriodsCount { get; set; }
    public int LossPeriodsCount { get; set; }

    public string PeakRevenueMonthsSummary { get; set; } = string.Empty;
    public string PeakBeverageMonthsSummary { get; set; } = string.Empty;
    public DayNightComparisonSummary DayNight { get; set; } = new();

    public List<FinancialPeriodPnlRow> PnlPeriods { get; set; } = new();
    public List<BeveragePerformanceRow> TopBeverages { get; set; } = new();

    // --- Bài toán 2B: Sổ cái 4 Luồng Chi phí Mất tiền (OperatingExpenses) ---
    public List<ExpenseCategorySummaryRow> ExpenseCategories { get; set; } = new();
    public List<OperatingExpenseItemRow> ExpenseItems { get; set; } = new();
    public List<TableSelectOption> TableOptions { get; set; } = new();
    public CreateExpenseInputModel NewExpense { get; set; } = new();

    // --- Bài toán 3: Kiểm toán & Truy vết Thất thoát Doanh thu (Leakage Audit) ---
    public List<LeakageAuditRow> LeakageItems { get; set; } = new();
    public decimal TotalLeakageAmount { get; set; }
    public int TotalLeakageCases => LeakageItems.Count;
    public int OpenAuditCaseCount { get; set; }
    public int ResolvedAuditCaseCount { get; set; }
    public decimal NetProfitAfterLeakage => NetProfit - (TotalLeakageAmount - TotalRecoveredAmount);
}

public record RevenuePeriodRow(string Period, decimal Revenue, int PaymentCount);
public record RevenueMethodRow(string Method, decimal Revenue, int PaymentCount);

/// <summary>
/// Dòng báo cáo Lãi/Lỗ (P&amp;L) và phân tích mùa vụ theo từng kỳ (Ngày hoặc Tháng).
/// </summary>
public class FinancialPeriodPnlRow
{
    public string Period { get; set; } = string.Empty;
    public decimal PlaytimeRevenue { get; set; }
    public decimal DayPlaytimeRevenue { get; set; }
    public decimal NightPlaytimeRevenue { get; set; }
    public decimal FnbRevenue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal CollectedRevenue { get; set; }
    public decimal RecoveredAmount { get; set; }
    public int PaymentCount { get; set; }

    // 4 luồng chi phí mất tiền (đã duyệt Approved)
    public decimal MaintenanceCost { get; set; }
    public decimal InventoryCogsCost { get; set; }
    public decimal UtilitiesCost { get; set; }
    public decimal PayrollCost { get; set; }
    public decimal TotalExpense => MaintenanceCost + InventoryCogsCost + UtilitiesCost + PayrollCost;
    public decimal BudgetExpenseCap { get; set; }
    public bool IsOverBudget => BudgetExpenseCap > 0 && TotalExpense > BudgetExpenseCap;

    // Lãi / Lỗ & Thất thoát
    public decimal NetProfit => CollectedRevenue + RecoveredAmount - TotalExpense;
    public decimal LeakageAmount { get; set; }
    public decimal FnbSharePercent => (PlaytimeRevenue + FnbRevenue) > 0
        ? Math.Round(FnbRevenue / (PlaytimeRevenue + FnbRevenue) * 100m, 1)
        : 0m;
    public decimal NightSharePercent => PlaytimeRevenue > 0
        ? Math.Round(NightPlaytimeRevenue / PlaytimeRevenue * 100m, 1)
        : 0m;

    public bool IsPeakRevenue { get; set; }
    public bool IsPeakBeverage { get; set; }
    public string DiagnosticReason { get; set; } = string.Empty;
}

/// <summary>
/// So sánh hiệu suất kinh doanh giữa Ca Ban Ngày (08:00–18:00) và Ca Ban Đêm (18:00–02:00).
/// </summary>
public class DayNightComparisonSummary
{
    public decimal DayRevenue { get; set; }
    public decimal NightRevenue { get; set; }
    public int DaySessionCount { get; set; }
    public int NightSessionCount { get; set; }
    public decimal DayPayrollCost { get; set; }
    public decimal NightPayrollCost { get; set; }

    public decimal TotalSessionRevenue => DayRevenue + NightRevenue;
    public decimal DayRevenueSharePercent => TotalSessionRevenue > 0
        ? Math.Round(DayRevenue / TotalSessionRevenue * 100m, 1)
        : 0m;
    public decimal NightRevenueSharePercent => TotalSessionRevenue > 0
        ? Math.Round(NightRevenue / TotalSessionRevenue * 100m, 1)
        : 0m;

    public decimal DayPayrollToRevenuePercent => DayRevenue > 0
        ? Math.Round(DayPayrollCost / DayRevenue * 100m, 1)
        : 0m;
    public decimal NightPayrollToRevenuePercent => NightRevenue > 0
        ? Math.Round(NightPayrollCost / NightRevenue * 100m, 1)
        : 0m;

    public string ProblemSolvedExplanation { get; set; } = string.Empty;
}

/// <summary>
/// Thống kê hiệu quả bán hàng của từng mặt hàng đồ uống / bia / F&amp;B.
/// </summary>
public class BeveragePerformanceRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int TotalQuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal ShareOfFnbPercent { get; set; }
    public decimal PeakSummerRevenue { get; set; }
    public string ProblemSolvedNote { get; set; } = string.Empty;
}

/// <summary>
/// Tổng hợp theo 4 nhóm chi phí vận hành mất tiền.
/// </summary>
public class ExpenseCategorySummaryRow
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal BudgetCap { get; set; }
    public decimal ShareOfTotalExpensePercent { get; set; }
    public decimal RatioToRevenuePercent { get; set; }
    public int EntryCount { get; set; }
    public string ProblemSolvedPurpose { get; set; } = string.Empty;
}

/// <summary>
/// Dòng chi tiết trong Sổ cái Chi phí vận hành (dbo.OperatingExpenses).
/// </summary>
public class OperatingExpenseItemRow
{
    public int Id { get; set; }
    public string ExpenseCode { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int? TableId { get; set; }
    public string? TableCode { get; set; }
    public string ShiftPeriod { get; set; } = "All";
    public string ShiftDisplayName { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string Status { get; set; } = "Approved";
    public string StatusDisplayName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}

public record TableSelectOption(int Id, string DisplayName);

/// <summary>
/// Form nhập bút toán chi phí vận hành mới.
/// </summary>
public class CreateExpenseInputModel
{
    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    [StringLength(30)]
    public string Category { get; set; } = "Maintenance";

    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    [StringLength(150, MinimumLength = 3, ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = FinancialMessages.MsgFin04InvalidAmount)]
    [Range(typeof(decimal), "1", "1000000000", ErrorMessage = FinancialMessages.MsgFin04InvalidAmount)]
    public decimal Amount { get; set; }

    public int? TableId { get; set; }

    [Required]
    [StringLength(20)]
    public string ShiftPeriod { get; set; } = "All";

    [StringLength(150)]
    public string? VendorName { get; set; }

    [StringLength(20)]
    public string InitialStatus { get; set; } = "Approved";

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Form chỉnh sửa bút toán chi phí vận hành.
/// </summary>
public class UpdateExpenseInputModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    public DateOnly ExpenseDate { get; set; }

    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    [StringLength(30)]
    public string Category { get; set; } = "Maintenance";

    [Required(ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    [StringLength(150, MinimumLength = 3, ErrorMessage = FinancialMessages.MsgFin03InvalidTitleOrCategory)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = FinancialMessages.MsgFin04InvalidAmount)]
    [Range(typeof(decimal), "1", "1000000000", ErrorMessage = FinancialMessages.MsgFin04InvalidAmount)]
    public decimal Amount { get; set; }

    public int? TableId { get; set; }

    [Required]
    [StringLength(20)]
    public string ShiftPeriod { get; set; } = "All";

    [StringLength(150)]
    public string? VendorName { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Bản ghi phát hiện sai lệch dữ liệu / thất thoát doanh thu.
/// </summary>
public class LeakageAuditRow
{
    public int? AuditCaseId { get; set; }
    public string CaseCode { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "Critical";
    public DateTime OccurredAtVn { get; set; }
    public string PeriodMonth => OccurredAtVn.ToString("yyyy-MM");
    public string LeakageType { get; set; } = string.Empty;
    public string LocationOrRef { get; set; } = string.Empty;
    public string ResponsibleStaff { get; set; } = string.Empty;
    public decimal ExpectedAmount { get; set; }
    public decimal RecordedAmount { get; set; }
    public decimal LeakageAmount { get; set; }
    public decimal RecoveredAmount { get; set; }
    public string Status { get; set; } = "Open";
    public string StatusDisplayName { get; set; } = "Mới phát hiện";
    public string? ResolutionType { get; set; }
    public string ResolutionDisplayName { get; set; } = string.Empty;
    public string EvidenceDetail { get; set; } = string.Empty;
    public string ProblemSolvedAction { get; set; } = string.Empty;
    public string? InvestigationNote { get; set; }
    public string? ResolvedByName { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public int? RelatedShiftId { get; set; }
}

/// <summary>
/// Sổ cái Chi phí Vận hành &amp; Lọc Đa Chiều.
/// </summary>
public class ExpenseListViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? Category { get; set; }
    public string? ShiftPeriod { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }

    public decimal TotalApprovedAmount { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public decimal TotalRejectedAmount { get; set; }
    public int ApprovedCount { get; set; }
    public int PendingCount { get; set; }
    public int RejectedCount { get; set; }
    public decimal PeriodBudgetCap { get; set; }
    public decimal BudgetUtilizationPercent => PeriodBudgetCap > 0
        ? Math.Round(TotalApprovedAmount / PeriodBudgetCap * 100m, 1)
        : 0m;

    public List<ExpenseCategorySummaryRow> CategorySummaries { get; set; } = new();
    public List<OperatingExpenseItemRow> Items { get; set; } = new();
}

/// <summary>
/// Lập Phiếu Chi Vận Hành Mới &amp; Kiểm Tra Trần Ngân Sách.
/// </summary>
public class CreateExpenseScreenViewModel
{
    public CreateExpenseInputModel Input { get; set; } = new();
    public List<TableSelectOption> TableOptions { get; set; } = new();
    public string CurrentPeriodMonth { get; set; } = string.Empty;
    public List<BudgetCategoryUsageRow> CategoryBudgets { get; set; } = new();
}

public class BudgetCategoryUsageRow
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal BudgetCap { get; set; }
    public decimal ActualApproved { get; set; }
    public decimal RemainingCap => BudgetCap - ActualApproved;
    public decimal UsagePercent => BudgetCap > 0 ? Math.Round(ActualApproved / BudgetCap * 100m, 1) : 0m;
}

/// <summary>
/// Chi Tiết, Chỉnh Sửa &amp; Phê Duyệt Phiếu Chi.
/// </summary>
public class ExpenseDetailViewModel
{
    public OperatingExpenseItemRow Expense { get; set; } = new();
    public UpdateExpenseInputModel EditInput { get; set; } = new();
    public List<TableSelectOption> TableOptions { get; set; } = new();
    public string PeriodMonth { get; set; } = string.Empty;
    public decimal CategoryBudgetCap { get; set; }
    public decimal CategoryApprovedActual { get; set; }
    public bool WouldExceedBudget => CategoryBudgetCap > 0
        && (CategoryApprovedActual + (Expense.Status == "Approved" ? 0m : Expense.Amount)) > CategoryBudgetCap;
}

/// <summary>
/// Danh Sách Hồ Sơ Kiểm Toán Thất Thoát &amp; Quét Đối Soát.
/// </summary>
public class AuditCaseListViewModel
{
    public string? RuleCode { get; set; }
    public string? Severity { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }

    public decimal TotalDetectedLeakage { get; set; }
    public decimal TotalRecoveredAmount { get; set; }
    public decimal RemainingUnresolvedLeakage => Math.Max(0m, TotalDetectedLeakage - TotalRecoveredAmount);
    public int OpenCount { get; set; }
    public int InvestigatingCount { get; set; }
    public int ResolvedCount { get; set; }

    public List<LeakageAuditRow> Cases { get; set; } = new();
}

/// <summary>
/// Điều Tra &amp; Ra Quyết Định Xử Lý Thất Thoát.
/// </summary>
public class AuditCaseDetailViewModel
{
    public LeakageAuditRow Case { get; set; } = new();
    public ResolveAuditCaseInputModel ResolutionInput { get; set; } = new();
}

public class ResolveAuditCaseInputModel
{
    public int CaseId { get; set; }

    [Required]
    [StringLength(24)]
    public string TargetStatus { get; set; } = "Resolved";

    [StringLength(32)]
    public string? ResolutionType { get; set; } = "RecoveredCash";

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal RecoveredAmount { get; set; }

    [Required(ErrorMessage = FinancialMessages.MsgFin18AuditInvalidResolution)]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = FinancialMessages.MsgFin18AuditInvalidResolution)]
    public string InvestigationNote { get; set; } = string.Empty;
}

/// <summary>
/// Quản Lý Định Mức Ngân Sách 4 Luồng &amp; Kế Hoạch Mùa Vụ 12 Tháng.
/// </summary>
public class BudgetManagementViewModel
{
    public int Year { get; set; } = 2026;
    public string SelectedPeriod { get; set; } = "2026-04";
    public decimal TotalAnnualTargetRevenue { get; set; }
    public decimal TotalAnnualActualRevenue { get; set; }
    public decimal TotalAnnualExpenseCap { get; set; }
    public decimal TotalAnnualActualExpense { get; set; }
    public int OverBudgetMonthsCount { get; set; }

    public List<MonthlyBudgetRow> Budgets { get; set; } = new();
    public SaveBudgetInputModel EditInput { get; set; } = new();
}

public class MonthlyBudgetRow
{
    public int Id { get; set; }
    public string PeriodMonth { get; set; } = string.Empty;
    public decimal RevenueTarget { get; set; }
    public decimal ActualRevenue { get; set; }
    public decimal MaintenanceCap { get; set; }
    public decimal ActualMaintenance { get; set; }
    public decimal InventoryCogsCap { get; set; }
    public decimal ActualInventoryCogs { get; set; }
    public decimal UtilitiesCap { get; set; }
    public decimal ActualUtilities { get; set; }
    public decimal PayrollCap { get; set; }
    public decimal ActualPayroll { get; set; }

    public decimal TotalExpenseCap => MaintenanceCap + InventoryCogsCap + UtilitiesCap + PayrollCap;
    public decimal ActualTotalExpense => ActualMaintenance + ActualInventoryCogs + ActualUtilities + ActualPayroll;
    public decimal TargetNetProfit => RevenueTarget - TotalExpenseCap;
    public decimal ActualNetProfit => ActualRevenue - ActualTotalExpense;
    public bool IsOverBudget => TotalExpenseCap > 0 && ActualTotalExpense > TotalExpenseCap;

    public string StrategicFocus { get; set; } = string.Empty;
    public string ActionPlanSummary { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string StatusDisplayName { get; set; } = string.Empty;
}

public class SaveBudgetInputModel
{
    [Required]
    [RegularExpression(@"^\d{4}-\d{2}$")]
    public string PeriodMonth { get; set; } = "2026-04";

    [Range(typeof(decimal), "1000000", "2000000000")]
    public decimal RevenueTarget { get; set; }

    [Range(typeof(decimal), "0", "500000000")]
    public decimal MaintenanceCap { get; set; }

    [Range(typeof(decimal), "0", "500000000")]
    public decimal InventoryCogsCap { get; set; }

    [Range(typeof(decimal), "0", "500000000")]
    public decimal UtilitiesCap { get; set; }

    [Range(typeof(decimal), "0", "500000000")]
    public decimal PayrollCap { get; set; }

    [Required]
    [StringLength(150, MinimumLength = 3)]
    public string StrategicFocus { get; set; } = string.Empty;

    [Required]
    [StringLength(1000, MinimumLength = 5)]
    public string ActionPlanSummary { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";
}
