using Bms.Web.Data;
using Bms.Web.Models;

namespace Bms.Web.Services;

/// <summary>
/// Hợp đồng dịch vụ Quản trị Tài chính (P&amp;L), Quản lý Chi phí Vận hành (OPEX/COGS),
/// Quản lý Định mức Ngân sách Mùa vụ và Kiểm toán Truy vết &amp; Xử lý Thất thoát Doanh thu End-to-End (7 Screens).
/// </summary>
public interface IFinancialAuditService
{
    /// <summary>
    /// Màn hình 1 (/Revenue/Index): Tổng hợp Báo cáo Tài chính (P&amp;L), Phân tích mùa vụ (T3–T5, T4–T6, Ca Ngày/Đêm),
    /// Sổ cái 4 luồng chi phí mất tiền, Định mức ngân sách và Bảng Kiểm toán 5 kịch bản thất thoát doanh thu.
    /// </summary>
    Task PopulateReportAsync(RevenueReportViewModel model, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 2 (/Revenue/Expenses): Tra cứu và lọc đa chiều Sổ cái 4 luồng chi phí vận hành (UC-FIN-02).
    /// </summary>
    Task<ExpenseListViewModel> BuildExpenseListAsync(
        DateOnly? from, DateOnly? to, string? category, string? shiftPeriod, string? status, string? keyword,
        CancellationToken ct = default);

    /// <summary>
    /// Màn hình 3 (/Revenue/CreateExpense): Chuẩn bị dữ liệu form lập phiếu chi mới kèm kiểm tra trần ngân sách tháng (UC-FIN-03).
    /// </summary>
    Task<CreateExpenseScreenViewModel> BuildCreateExpenseScreenAsync(
        DateOnly? expenseDate = null, CancellationToken ct = default);

    /// <summary>
    /// Ghi nhận một bút toán chi phí vận hành mới (UC-FIN-02 / UC-FIN-03, BR-FIN-05, BR-FIN-14, BR-FIN-15).
    /// </summary>
    Task<(bool Success, string Message, OperatingExpense? Expense)> AddExpenseAsync(
        CreateExpenseInputModel input, string? actorId, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 4 (/Revenue/ExpenseDetail/{id}): Xem chi tiết một phiếu chi vận hành và đối chiếu trần ngân sách (UC-FIN-04).
    /// </summary>
    Task<ExpenseDetailViewModel?> GetExpenseDetailAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 4 (/Revenue/UpdateExpense): Cập nhật thông tin phiếu chi vận hành (UC-FIN-04 - Update).
    /// </summary>
    Task<(bool Success, string Message)> UpdateExpenseAsync(
        UpdateExpenseInputModel input, string? actorId, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 4 (/Revenue/ReviewExpense): Phê duyệt (Approved) hoặc Từ chối (Rejected) phiếu chi vận hành (UC-FIN-04, BR-FIN-14).
    /// </summary>
    Task<(bool Success, string Message)> ReviewExpenseAsync(
        int id, string targetStatus, string? reviewNote, string? reviewerId, CancellationToken ct = default);

    /// <summary>
    /// Xóa một bút toán chi phí vận hành nhập nhầm (UC-FIN-02 / UC-FIN-04).
    /// </summary>
    Task<(bool Success, string Message)> DeleteExpenseAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 5 (/Revenue/AuditCases): Quét đối soát 5 quy tắc thất thoát và đồng bộ vào bảng dbo.FinancialAuditCases (UC-FIN-05, BR-FIN-16).
    /// </summary>
    Task<int> SyncAuditCasesAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 5 (/Revenue/AuditCases): Lấy danh sách hồ sơ vụ việc kiểm toán thất thoát có bộ lọc (UC-FIN-05).
    /// </summary>
    Task<AuditCaseListViewModel> BuildAuditCaseListAsync(
        string? ruleCode, string? severity, string? status, string? keyword, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 6 (/Revenue/AuditDetail/{id}): Xem chi tiết bằng chứng hồ sơ vụ việc thất thoát (UC-FIN-06).
    /// </summary>
    Task<AuditCaseDetailViewModel?> GetAuditCaseDetailAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 6 (/Revenue/ResolveAuditCase): Điều tra, đổi trạng thái và thực thi chế tài thu hồi tiền mặt / khấu trừ lương / miễn trừ (UC-FIN-06, BR-FIN-17).
    /// </summary>
    Task<(bool Success, string Message)> ResolveAuditCaseAsync(
        ResolveAuditCaseInputModel input, string? actorId, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 7 (/Revenue/Budgets): Quản lý Định mức Ngân sách 4 luồng chi phí &amp; Kế hoạch mùa vụ 12 tháng (UC-FIN-07, BR-FIN-18).
    /// </summary>
    Task<BudgetManagementViewModel> BuildBudgetManagementAsync(
        int year = 2026, string? selectedPeriod = null, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 7 (/Revenue/SaveBudget): Tạo mới hoặc cập nhật định mức ngân sách &amp; kế hoạch mùa vụ tháng (UC-FIN-07).
    /// </summary>
    Task<(bool Success, string Message)> SaveMonthlyBudgetAsync(
        SaveBudgetInputModel input, string? actorId, CancellationToken ct = default);

    /// <summary>
    /// Màn hình 7 (/Revenue/DeleteBudget): Xóa định mức ngân sách tháng chưa khóa (UC-FIN-07).
    /// </summary>
    Task<(bool Success, string Message)> DeleteMonthlyBudgetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ bộ dữ liệu tài chính thực tế 12 tháng năm 2026 (cả Lãi, Lỗ, Chi phí, Ngân sách, Sai lệch dữ liệu)
    /// theo nguyên tắc Idempotent (UC-FIN-05, BR-FIN-13).
    /// </summary>
    Task<int> EnsureYearFinancialDataSeededAsync(string? actorId = null, CancellationToken ct = default);
}
