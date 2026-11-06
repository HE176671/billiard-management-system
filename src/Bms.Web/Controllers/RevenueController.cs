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
/// Phân hệ Quản trị Tài chính (P&amp;L 12 tháng), Sổ cái Chi phí vận hành (OPEX/COGS)
/// và Kiểm toán Truy vết Thất thoát Doanh thu (UC-FIN-01 -> UC-FIN-05).
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

    #region UC-FIN-01 & UC-FIN-03 - Hien thi bao cao P&L 12 thang, Chi phi va Kiem toan that thoat

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? groupBy = null)
    {
        // Mặc định khi mở trang lần đầu: Hiển thị trọn vẹn 12 tháng của năm tài chính 2026 theo tháng
        var resolvedGroup = groupBy ?? (from == null && to == null ? "month" : "day");
        var model = CreateRange(from, to, resolvedGroup, defaultFullYearWhenNull: from == null && to == null);

        if (ModelState.IsValid)
        {
            await _financial.PopulateReportAsync(model);
        }

        return View(model);
    }

    #endregion

    #region UC-FIN-02 - Ghi nhan va xoa but toan Chi phi van hanh (OperatingExpenses)

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(
        int id,
        DateOnly? from = null,
        DateOnly? to = null,
        string groupBy = "month")
    {
        var (success, message) = await _financial.DeleteExpenseAsync(id);
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

    #region UC-FIN-05 - Dong bo du lieu tai chinh 12 thang (Idempotent Seed)

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

    #endregion

    #region UC-FIN-04 - Xuat CSV Bao cao P&L, Chi phi va Bien ban Kiem toan That thoat

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
           .Append(",Loi nhuan rong P&L VND,").Append(model.NetProfit.ToString(inv))
           .Append(",Tong that thoat phat hien VND,").Append(model.TotalLeakageAmount.ToString(inv))
           .Append(",So giao dich,").Append(model.PaymentCount)
           .Append(",So hoa don,").Append(model.InvoiceCount).Append("\r\n\r\n");

        // Phần 2: Bảng P&L theo kỳ
        csv.Append("Ky,Doanh thu thuc thu VND,Thu gio choi VND,Thu nuoc & bia VND,Chi bao tri VND,Chi gia von nuoc VND,Chi dien nuoc VND,Chi luong VND,Tong chi phi VND,Loi nhuan rong VND,That thoat VND,So giao dich,Chan doan\r\n");
        foreach (var row in model.PnlPeriods)
        {
            csv.Append($"{row.Period},{row.CollectedRevenue.ToString(inv)},{row.PlaytimeRevenue.ToString(inv)},{row.FnbRevenue.ToString(inv)},")
               .Append($"{row.MaintenanceCost.ToString(inv)},{row.InventoryCogsCost.ToString(inv)},{row.UtilitiesCost.ToString(inv)},{row.PayrollCost.ToString(inv)},")
               .Append($"{row.TotalExpense.ToString(inv)},{row.NetProfit.ToString(inv)},{row.LeakageAmount.ToString(inv)},{row.PaymentCount},\"{EscapeCsv(row.DiagnosticReason)}\"\r\n");
        }

        // Phần 3: Theo phương thức thanh toán
        csv.Append("\r\nPhuong thuc thanh toan,Doanh thu VND,So giao dich\r\n");
        foreach (var row in model.Methods)
            csv.Append($"{row.Method},{row.Revenue.ToString(inv)},{row.PaymentCount}\r\n");

        // Phần 4: Sổ cái 4 nhóm chi phí vận hành
        csv.Append("\r\nNhom chi phi van hanh,Tong chi VND,Ty trong chi phi %,Ty le tren doanh thu %,Muc dich quan tri\r\n");
        foreach (var cat in model.ExpenseCategories)
            csv.Append($"\"{EscapeCsv(cat.CategoryName)}\",{cat.TotalAmount.ToString(inv)},{cat.ShareOfTotalExpensePercent.ToString(inv)},{cat.RatioToRevenuePercent.ToString(inv)},\"{EscapeCsv(cat.ProblemSolvedPurpose)}\"\r\n");

        // Phần 5: Biên bản kiểm toán & truy vết thất thoát doanh thu
        csv.Append("\r\nMa loi,Thoi gian (UTC+7),Loai that thoat,Vi tri (Ban/Ca/HD),Nhan vien lien quan,So tien dung VND,So tien ghi nhan VND,So tien that thoat VND,Bang chung sai du lieu\r\n");
        foreach (var leak in model.LeakageItems)
        {
            csv.Append($"{leak.RuleCode},{leak.OccurredAtVn:yyyy-MM-dd HH:mm},\"{EscapeCsv(leak.LeakageType)}\",\"{EscapeCsv(leak.LocationOrRef)}\",")
               .Append($"\"{EscapeCsv(leak.ResponsibleStaff)}\",{leak.ExpectedAmount.ToString(inv)},{leak.RecordedAmount.ToString(inv)},{leak.LeakageAmount.ToString(inv)},\"{EscapeCsv(leak.EvidenceDetail)}\"\r\n");
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
