using System.Globalization;
using System.Security.Claims;
using System.Text;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

/// <summary>
/// Phân hệ Quản trị Tài chính End-to-End (7 Màn hình + 1 Chức năng Xuất CSV):
/// 1. /Revenue/Index          — Bảng điều khiển P&amp;L 12 tháng &amp; Chẩn đoán mùa vụ (UC-FIN-01)
/// 2. /Revenue/Expenses       — Danh sách &amp; Bộ lọc đa chiều Sổ cái 4 luồng chi phí (UC-FIN-02)
/// 3. /Revenue/CreateExpense  — Lập phiếu chi vận hành mới &amp; Kiểm tra trần ngân sách (UC-FIN-03)
/// 4. /Revenue/ExpenseDetail  — Chi tiết, Chỉnh sửa (U) &amp; Phê duyệt/Từ chối phiếu chi (UC-FIN-04)
/// 5. /Revenue/AuditCases     — Danh sách Hồ sơ Kiểm toán Thất thoát &amp; Quét 5 quy tắc (UC-FIN-05)
/// 6. /Revenue/AuditDetail    — Điều tra &amp; Ra quyết định Thu hồi tiền / Khấu trừ lương (UC-FIN-06)
/// 7. /Revenue/Budgets        — Quản lý Định mức Ngân sách 4 luồng &amp; Kế hoạch mùa vụ (UC-FIN-07)
/// 8. /Revenue/Export         — Xuất báo cáo tổng hợp CSV chuẩn tiếng Việt (UC-FIN-08)
/// </summary>
[Authorize(Roles = "Admin")]
public class RevenueController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IFinancialAuditService _financial;

    public RevenueController(ApplicationDbContext context, IFinancialAuditService? financial = null)
    {
        _context = context;
        _financial = financial ?? new FinancialAuditService(context);
    }

    private string? CurrentUserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    #region Screen 1: UC-FIN-01 — Trung tam P&L 12 thang & Chan doan Mua vu (/Revenue/Index)

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? groupBy = null)
    {
        var resolvedGroup = groupBy ?? (from == null && to == null ? "month" : "day");
        var model = CreateRange(from, to, resolvedGroup, defaultFullYearWhenNull: from == null && to == null);

        if (ModelState.IsValid)
        {
            await _financial.PopulateReportAsync(model);
        }

        return View(model);
    }

    #endregion

    #region Screen 2: UC-FIN-02 — Danh sach & Loc da chieu So cai Chi phi Van hanh (/Revenue/Expenses)

    [HttpGet]
    public async Task<IActionResult> Expenses(
        DateOnly? from = null,
        DateOnly? to = null,
        string? category = null,
        string? shiftPeriod = null,
        string? status = null,
        string? keyword = null)
    {
        var f = from ?? new DateOnly(2026, 1, 1);
        var t = to ?? new DateOnly(2026, 12, 31);
        var vm = await _financial.BuildExpenseListAsync(f, t, category, shiftPeriod, status, keyword);
        return View(vm);
    }

    #endregion

    #region Screen 3: UC-FIN-03 — Lap Phieu Chi Van Hanh Moi (/Revenue/CreateExpense)

    [HttpGet]
    public async Task<IActionResult> CreateExpense(DateOnly? expenseDate = null)
    {
        var vm = await _financial.BuildCreateExpenseScreenAsync(expenseDate);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateExpense([Bind(Prefix = "Input")] CreateExpenseInputModel input)
    {
        var (success, message, created) = await _financial.AddExpenseAsync(input, CurrentUserId);
        if (!success || created == null)
        {
            TempData["Error"] = message;
            var vm = await _financial.BuildCreateExpenseScreenAsync(input?.ExpenseDate);
            if (input != null) vm.Input = input;
            return View(vm);
        }

        TempData["Success"] = message;
        return RedirectToAction(nameof(ExpenseDetail), new { id = created.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExpense(
        [Bind(Prefix = "NewExpense")] CreateExpenseInputModel input,
        DateOnly? from = null,
        DateOnly? to = null,
        string groupBy = "month")
    {
        var (success, message, _) = await _financial.AddExpenseAsync(input, CurrentUserId);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new
        {
            from = (from ?? new DateOnly(2026, 1, 1)).ToString("yyyy-MM-dd"),
            to = (to ?? new DateOnly(2026, 12, 31)).ToString("yyyy-MM-dd"),
            groupBy
        });
    }

    #endregion

    #region Screen 4: UC-FIN-04 — Chi tiet, Chinh sua (U) & Phe duyet Phieu Chi (/Revenue/ExpenseDetail)

    [HttpGet]
    public async Task<IActionResult> ExpenseDetail(int id)
    {
        var vm = await _financial.GetExpenseDetailAsync(id);
        if (vm == null)
        {
            TempData["Error"] = FinancialMessages.MsgFin11NotFound;
            return RedirectToAction(nameof(Expenses));
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExpense([Bind(Prefix = "EditInput")] UpdateExpenseInputModel input)
    {
        var (success, message) = await _financial.UpdateExpenseAsync(input, CurrentUserId);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(ExpenseDetail), new { id = input.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewExpense(int id, string targetStatus, string? reviewNote, string? returnUrl = null)
    {
        var (success, message) = await _financial.ReviewExpenseAsync(id, targetStatus, reviewNote, CurrentUserId);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(ExpenseDetail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(
        int id,
        DateOnly? from = null,
        DateOnly? to = null,
        string groupBy = "month",
        string? returnTo = null)
    {
        var (success, message) = await _financial.DeleteExpenseAsync(id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        if (string.Equals(returnTo, "Expenses", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Expenses));

        return RedirectToAction(nameof(Index), new
        {
            from = (from ?? new DateOnly(2026, 1, 1)).ToString("yyyy-MM-dd"),
            to = (to ?? new DateOnly(2026, 12, 31)).ToString("yyyy-MM-dd"),
            groupBy
        });
    }

    #endregion

    #region Screen 5: UC-FIN-05 — Danh sach Ho so Kiem toan That thoat & Quet 5 Quy tac (/Revenue/AuditCases)

    [HttpGet]
    public async Task<IActionResult> AuditCases(
        string? ruleCode = null,
        string? severity = null,
        string? status = null,
        string? keyword = null)
    {
        var vm = await _financial.BuildAuditCaseListAsync(ruleCode, severity, status, keyword);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncAuditCases()
    {
        await _financial.SyncAuditCasesAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        TempData["Success"] = FinancialMessages.MsgFin16AuditSynced;
        return RedirectToAction(nameof(AuditCases));
    }

    #endregion

    #region Screen 6: UC-FIN-06 — Dieu tra & Ra Quyet dinh Xu ly That thoat (/Revenue/AuditDetail)

    [HttpGet]
    public async Task<IActionResult> AuditDetail(int id)
    {
        var vm = await _financial.GetAuditCaseDetailAsync(id);
        if (vm == null)
        {
            TempData["Error"] = FinancialMessages.MsgFin11NotFound;
            return RedirectToAction(nameof(AuditCases));
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveAuditCase([Bind(Prefix = "ResolutionInput")] ResolveAuditCaseInputModel input)
    {
        var (success, message) = await _financial.ResolveAuditCaseAsync(input, CurrentUserId);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(AuditDetail), new { id = input.CaseId });
    }

    #endregion

    #region Screen 7: UC-FIN-07 — Quan ly Dinh muc Ngan sach & Ke hoach Mua vu 12 Thang (/Revenue/Budgets)

    [HttpGet]
    public async Task<IActionResult> Budgets(int year = 2026, string? selectedPeriod = null)
    {
        var vm = await _financial.BuildBudgetManagementAsync(year, selectedPeriod);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBudget([Bind(Prefix = "EditInput")] SaveBudgetInputModel input)
    {
        var (success, message) = await _financial.SaveMonthlyBudgetAsync(input, CurrentUserId);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Budgets), new { year = 2026, selectedPeriod = input?.PeriodMonth });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBudget(int id)
    {
        var (success, message) = await _financial.DeleteMonthlyBudgetAsync(id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Budgets));
    }

    #endregion

    #region Seed Year Data & Function 8: UC-FIN-08 — Xuat CSV Bao cao P&L, Ngan sach, Chi phi va Kiem toan

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedYearData()
    {
        await _financial.EnsureYearFinancialDataSeededAsync(CurrentUserId);
        TempData["Success"] = FinancialMessages.MsgFin07SeedSuccess;
        return RedirectToAction(nameof(Index), new
        {
            from = "2026-01-01",
            to = "2026-12-31",
            groupBy = "month"
        });
    }

    public async Task<IActionResult> Export(DateOnly? from, DateOnly? to, string groupBy = "day")
    {
        var model = CreateRange(from, to, groupBy, defaultFullYearWhenNull: from == null && to == null);
        if (!ModelState.IsValid)
            return BadRequest("Khoảng ngày hoặc kiểu nhóm không hợp lệ.");

        await _financial.PopulateReportAsync(model);

        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder();

        // Phần 1: Tổng quan P&L & Kiểm toán
        csv.Append("Bao cao Quan tri Tai chinh P&L va Kiem toan That thoat,")
           .Append(model.From.ToString("yyyy-MM-dd")).Append(',')
           .Append(model.To.ToString("yyyy-MM-dd")).Append(',')
           .Append(model.GroupBy).Append("\r\n");

        csv.Append("Tong doanh thu thuc thu VND,").Append(model.TotalRevenue.ToString(inv))
           .Append(",Doanh thu gio choi VND,").Append(model.TotalPlaytimeRevenue.ToString(inv))
           .Append(",Doanh thu nuoc & bia VND,").Append(model.TotalFnbRevenue.ToString(inv))
           .Append(",Tong chi phi van hanh VND,").Append(model.TotalOperatingCost.ToString(inv))
           .Append(",Tien thu hoi sau kiem toan VND,").Append(model.TotalRecoveredAmount.ToString(inv))
           .Append(",Loi nhuan rong P&L VND,").Append(model.NetProfit.ToString(inv))
           .Append(",Tong that thoat phat hien VND,").Append(model.TotalLeakageAmount.ToString(inv))
           .Append(",So giao dich,").Append(model.PaymentCount)
           .Append(",So hoa don,").Append(model.InvoiceCount).Append("\r\n\r\n");

        // Phần 2: Bảng P&L theo kỳ
        csv.Append("Ky,Doanh thu thuc thu VND,Thu gio choi VND,Thu nuoc & bia VND,Chi bao tri VND,Chi gia von nuoc VND,Chi dien nuoc VND,Chi luong VND,Tong chi phi VND,Dinh muc ngan sach VND,Tien thu hoi VND,Loi nhuan rong VND,That thoat VND,So giao dich,Chan doan\r\n");
        foreach (var row in model.PnlPeriods)
        {
            csv.Append($"{row.Period},{row.CollectedRevenue.ToString(inv)},{row.PlaytimeRevenue.ToString(inv)},{row.FnbRevenue.ToString(inv)},")
               .Append($"{row.MaintenanceCost.ToString(inv)},{row.InventoryCogsCost.ToString(inv)},{row.UtilitiesCost.ToString(inv)},{row.PayrollCost.ToString(inv)},")
               .Append($"{row.TotalExpense.ToString(inv)},{row.BudgetExpenseCap.ToString(inv)},{row.RecoveredAmount.ToString(inv)},{row.NetProfit.ToString(inv)},{row.LeakageAmount.ToString(inv)},{row.PaymentCount},\"{EscapeCsv(row.DiagnosticReason)}\"\r\n");
        }

        // Phần 3: Theo phương thức thanh toán
        csv.Append("\r\nPhuong thuc thanh toan,Doanh thu VND,So giao dich\r\n");
        foreach (var row in model.Methods)
            csv.Append($"{row.Method},{row.Revenue.ToString(inv)},{row.PaymentCount}\r\n");

        // Phần 4: Sổ cái 4 nhóm chi phí vận hành
        csv.Append("\r\nNhom chi phi van hanh,Tong chi VND,Dinh muc ngan sach VND,Ty trong chi phi %,Ty le tren doanh thu %,Muc dich quan tri\r\n");
        foreach (var cat in model.ExpenseCategories)
            csv.Append($"\"{EscapeCsv(cat.CategoryName)}\",{cat.TotalAmount.ToString(inv)},{cat.BudgetCap.ToString(inv)},{cat.ShareOfTotalExpensePercent.ToString(inv)},{cat.RatioToRevenuePercent.ToString(inv)},\"{EscapeCsv(cat.ProblemSolvedPurpose)}\"\r\n");

        // Phần 5: Biên bản kiểm toán & truy vết thất thoát doanh thu
        csv.Append("\r\nMa ho so,Ma loi,Thoi gian (UTC+7),Loai that thoat,Vi tri (Ban/Ca/HD),Nhan vien lien quan,So tien dung VND,So tien ghi nhan VND,So tien that thoat VND,So tien thu hoi VND,Trang thai xu ly,Bang chung sai du lieu\r\n");
        foreach (var leak in model.LeakageItems)
        {
            csv.Append($"{leak.CaseCode},{leak.RuleCode},{leak.OccurredAtVn:yyyy-MM-dd HH:mm},\"{EscapeCsv(leak.LeakageType)}\",\"{EscapeCsv(leak.LocationOrRef)}\",")
               .Append($"\"{EscapeCsv(leak.ResponsibleStaff)}\",{leak.ExpectedAmount.ToString(inv)},{leak.RecordedAmount.ToString(inv)},{leak.LeakageAmount.ToString(inv)},{leak.RecoveredAmount.ToString(inv)},\"{EscapeCsv(leak.StatusDisplayName)}\",\"{EscapeCsv(leak.EvidenceDetail)}\"\r\n");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"doanh-thu-pnl-{model.From:yyyyMMdd}-{model.To:yyyyMMdd}.csv");
    }

    #endregion

    #region Kiem tra khoang ngay va kieu nhom (BR-FIN-01, MSG-FIN-01, MSG-FIN-02)

    private RevenueReportViewModel CreateRange(
        DateOnly? from,
        DateOnly? to,
        string groupBy,
        bool defaultFullYearWhenNull)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        DateOnly defaultFrom = defaultFullYearWhenNull
            ? new DateOnly(2026, 1, 1)
            : new DateOnly(today.Year, today.Month, 1);
        DateOnly defaultTo = defaultFullYearWhenNull
            ? new DateOnly(2026, 12, 31)
            : today;

        var model = new RevenueReportViewModel
        {
            From = from ?? defaultFrom,
            To = to ?? defaultTo,
            GroupBy = groupBy
        };

        if (model.From > model.To || model.To.Year >= 9999 || model.From.Year < 2)
        {
            ModelState.AddModelError("", FinancialMessages.MsgFin01InvalidOrder);
        }
        else if (model.To.DayNumber - model.From.DayNumber > 366)
        {
            ModelState.AddModelError("", FinancialMessages.MsgFin02RangeTooLong);
        }

        if (groupBy is not ("day" or "month"))
        {
            ModelState.AddModelError("", "Chỉ hỗ trợ nhóm theo ngày (day) hoặc tháng (month).");
        }

        return model;
    }

    private static string EscapeCsv(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\"\"");

    #endregion
}
