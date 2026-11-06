using Bms.Web.Data;
using Bms.Web.Models;

namespace Bms.Web.Services;

/// <summary>
/// Hợp đồng dịch vụ Quản trị Tài chính (P&amp;L), Quản lý Chi phí Vận hành (OPEX/COGS)
/// và Kiểm toán Truy vết Thất thoát Doanh thu (Revenue Leakage Audit).
/// </summary>
public interface IFinancialAuditService
{
    /// <summary>
    /// Tổng hợp toàn bộ Báo cáo Tài chính (P&amp;L), Phân tích mùa vụ (T3–T5, T4–T6, Ca Ngày/Đêm),
    /// Sổ cái 4 luồng chi phí mất tiền và Bảng Kiểm toán 5 kịch bản thất thoát doanh thu.
    /// </summary>
    Task PopulateReportAsync(RevenueReportViewModel model, CancellationToken ct = default);

    /// <summary>
    /// Ghi nhận một bút toán chi phí vận hành mới (UC-FIN-02, BR-FIN-05).
    /// </summary>
    Task<(bool Success, string Message, OperatingExpense? Expense)> AddExpenseAsync(
        CreateExpenseInputModel input, string? actorId, CancellationToken ct = default);

    /// <summary>
    /// Xóa một bút toán chi phí vận hành nhập nhầm (UC-FIN-02).
    /// </summary>
    Task<(bool Success, string Message)> DeleteExpenseAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ bộ dữ liệu tài chính thực tế 12 tháng năm 2026 (cả Lãi, Lỗ, Chi phí, Sai lệch dữ liệu)
    /// theo nguyên tắc Idempotent (chạy nhiều lần không nhân đôi dữ liệu — UC-FIN-05, BR-FIN-13).
    /// </summary>
    Task<int> EnsureYearFinancialDataSeededAsync(string? actorId = null, CancellationToken ct = default);
}
