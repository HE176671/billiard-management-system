using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

/// <summary>
/// Bảng mã thông báo chuẩn tiếng Việt (MSG-FIN-01 -> MSG-FIN-12) theo đặc tả SRS.
/// </summary>
public static class FinancialMessages
{
    public const string MsgFin01InvalidOrder =
        "MSG-FIN-01: Ngày bắt đầu không được lớn hơn ngày kết thúc. Vui lòng chọn lại khoảng thời gian.";
    public const string MsgFin02RangeTooLong =
        "MSG-FIN-02: Khoảng thời gian phân tích tối đa là 367 ngày (1 năm tài chính). Vui lòng thu hẹp khoảng ngày.";
    public const string MsgFin03InvalidTitleOrCategory =
        "MSG-FIN-03: Vui lòng nhập tên khoản chi phí vận hành (tối đa 150 ký tự) và chọn đúng danh mục chi phí.";
    public const string MsgFin04InvalidAmount =
        "MSG-FIN-04: Số tiền chi phí phải lớn hơn 0 ₫ và không vượt quá 1.000.000.000 ₫.";
    public const string MsgFin06Deleted =
        "MSG-FIN-06: Đã xóa bút toán chi phí và tính toán lại báo cáo Lãi/Lỗ.";
    public const string MsgFin07SeedSuccess =
        "MSG-FIN-07: Đã đồng bộ thành công bộ dữ liệu tài chính 12 tháng (gồm doanh thu, 4 nhóm chi phí vận hành và 5 kịch bản kiểm toán thất thoát).";
    public const string MsgFin08AuditClean =
        "MSG-FIN-08: Đối soát hoàn tất: Không phát hiện chênh lệch hay thất thoát doanh thu nào trong khoảng thời gian đã chọn.";
    public const string MsgFin10EmptyData =
        "MSG-FIN-10: Chưa có dữ liệu giao dịch trong khoảng thời gian này. Bạn có thể bấm 'Đồng bộ dữ liệu mẫu 12 tháng' để nạp dữ liệu phân tích.";
    public const string MsgFin11NotFound =
        "MSG-FIN-11: Không tìm thấy bút toán chi phí cần xóa hoặc bút toán đã bị xóa trước đó.";
    public const string MsgFin12TimezoneInfo =
        "MSG-FIN-12: Toàn bộ số liệu Doanh thu, Chi phí vận hành (P&L) và Kiểm toán thất thoát được tính theo múi giờ Việt Nam (UTC+7).";

    public static string FormatMsgFin05Created(string code, decimal amount) =>
        $"MSG-FIN-05: Đã ghi nhận bút toán chi phí [{code}] ({amount:N0} ₫) vào sổ cái vận hành và cập nhật lại báo cáo Lãi/Lỗ.";

    public static string FormatMsgFin09AuditAlert(int count, decimal totalLoss) =>
        $"MSG-FIN-09: Cảnh báo Kiểm toán — Phát hiện {count} vụ sai lệch dữ liệu với tổng thất thoát {totalLoss:N0} ₫. Xem chi tiết tại bảng Truy vết bên dưới!";
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
    public decimal NetProfit => TotalRevenue - TotalOperatingCost;
    public decimal NetMarginPercent => TotalRevenue > 0
        ? Math.Round(NetProfit / TotalRevenue * 100m, 1)
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
    public decimal NetProfitAfterLeakage => NetProfit - TotalLeakageAmount;
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
    public int PaymentCount { get; set; }

    // 4 luồng chi phí mất tiền
    public decimal MaintenanceCost { get; set; }
    public decimal InventoryCogsCost { get; set; }
    public decimal UtilitiesCost { get; set; }
    public decimal PayrollCost { get; set; }
    public decimal TotalExpense => MaintenanceCost + InventoryCogsCost + UtilitiesCost + PayrollCost;

    // Lãi / Lỗ & Thất thoát
    public decimal NetProfit => CollectedRevenue - TotalExpense;
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
    public decimal PeakSummerRevenue { get; set; } // Doanh thu riêng trong T4-T5-T6
    public string ProblemSolvedNote { get; set; } = string.Empty;
}

/// <summary>
/// Tổng hợp theo 4 nhóm chi phí vận hành mất tiền (BR-FIN-03).
/// </summary>
public class ExpenseCategorySummaryRow
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
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
    public string? TableCode { get; set; }
    public string ShiftPeriod { get; set; } = "All";
    public string ShiftDisplayName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CreatedByName { get; set; }
}

public record TableSelectOption(int Id, string DisplayName);

/// <summary>
/// Form nhập bút toán chi phí vận hành mới (UC-FIN-02).
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

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Bản ghi phát hiện sai lệch dữ liệu / thất thoát doanh thu (UC-FIN-03, BR-FIN-08 -> BR-FIN-12).
/// </summary>
public class LeakageAuditRow
{
    public string RuleCode { get; set; } = string.Empty;       // LEAK-01 .. LEAK-05
    public DateTime OccurredAtVn { get; set; }                 // Thời điểm phát sinh (UTC+7)
    public string PeriodMonth => OccurredAtVn.ToString("yyyy-MM");
    public string LeakageType { get; set; } = string.Empty;    // Tên loại thất thoát
    public string LocationOrRef { get; set; } = string.Empty;  // Bàn / Phiên / Hóa đơn / Ca
    public string ResponsibleStaff { get; set; } = string.Empty; // Nhân viên liên quan
    public decimal ExpectedAmount { get; set; }                // Số tiền đúng theo hệ thống
    public decimal RecordedAmount { get; set; }                // Số tiền thực tế đã ghi nhận
    public decimal LeakageAmount { get; set; }                 // Số tiền thất thoát (Expected - Recorded)
    public string EvidenceDetail { get; set; } = string.Empty; // Bằng chứng đối chiếu dữ liệu
    public string ProblemSolvedAction { get; set; } = string.Empty; // Nhằm giải quyết vấn đề gì & cần làm gì
    public int? RelatedShiftId { get; set; }
}
