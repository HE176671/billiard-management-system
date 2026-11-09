using System.Globalization;
using System.Text;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager")]
public class RevenueController : Controller
{
    private readonly ApplicationDbContext _context;
    public RevenueController(ApplicationDbContext context) => _context = context;

    #region UC46 - Hien thi bao cao

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string groupBy = "day")
    {
        var model = CreateRange(from, to, groupBy);
        if (ModelState.IsValid) await PopulateAsync(model);
        return View(model);
    }

    #endregion

    #region UC46 - Xuat CSV

    public async Task<IActionResult> Export(DateOnly? from, DateOnly? to, string groupBy = "day")
    {
        var model = CreateRange(from, to, groupBy);
        if (!ModelState.IsValid) return BadRequest("Khoảng ngày hoặc kiểu nhóm không hợp lệ.");
        await PopulateAsync(model);
        // Cùng ba phần như màn hình: tổng quan, theo kỳ, theo phương thức.
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder();
        csv.Append("Bao cao doanh thu,").Append(model.From.ToString("yyyy-MM-dd")).Append(',')
           .Append(model.To.ToString("yyyy-MM-dd")).Append(',').Append(model.GroupBy).Append("\r\n");
        csv.Append("Tong doanh thu VND,").Append(model.TotalRevenue.ToString(inv))
           .Append(",So giao dich,").Append(model.PaymentCount)
           .Append(",So hoa don,").Append(model.InvoiceCount).Append("\r\n\r\n");
        csv.Append("Ky,Doanh thu VND,So giao dich\r\n");
        foreach (var row in model.Periods)
            csv.Append($"{row.Period},{row.Revenue.ToString(inv)},{row.PaymentCount}\r\n");
        csv.Append("\r\nPhuong thuc,Doanh thu VND,So giao dich\r\n");
        foreach (var row in model.Methods)
            csv.Append($"{row.Method},{row.Revenue.ToString(inv)},{row.PaymentCount}\r\n");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"doanh-thu-{model.From:yyyyMMdd}-{model.To:yyyyMMdd}.csv");
    }

    #endregion

    #region Kiem tra khoang ngay va kieu nhom

    private RevenueReportViewModel CreateRange(DateOnly? from, DateOnly? to, string groupBy)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var model = new RevenueReportViewModel {
            From = from ?? new DateOnly(today.Year, today.Month, 1), To = to ?? today, GroupBy = groupBy
        };
        if (model.From > model.To || model.To.DayNumber - model.From.DayNumber > 366 || model.To.Year >= 9999 || model.From.Year < 2)
            ModelState.AddModelError("", "Chọn ngày bắt đầu không sau ngày kết thúc, tối đa 367 ngày.");
        if (groupBy is not ("day" or "month" or "shift"))
            ModelState.AddModelError("", "Chỉ hỗ trợ nhóm theo ngày, tháng hoặc ca.");
        return model;
    }

    #endregion

    #region Truy van, tong hop doanh thu va UTC+7

    private async Task PopulateAsync(RevenueReportViewModel model)
    {
        // Half-open interval: include the whole final day in Vietnam (UTC+7).
        var start = model.From.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var end = model.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var payments = await _context.PaymentTransactions.AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= start && p.TransactionTimeUtc < end).ToListAsync();
        model.TotalRevenue = payments.Sum(p => p.AmountPaid);
        model.PaymentCount = payments.Count;
        model.InvoiceCount = payments.Select(p => p.InvoiceId).Distinct().Count();
        model.Methods = payments.GroupBy(p => p.PaymentMethod).OrderBy(g => g.Key)
            .Select(g => new RevenueMethodRow(g.Key, g.Sum(p => p.AmountPaid), g.Count())).ToList();

        if (model.GroupBy == "shift")
        {
            // TV5 — UC37: doanh thu theo ca (BR-23: mỗi khoản thu gắn với ca chung của quán).
            var shiftIds = payments.Where(p => p.WorkShiftId != null).Select(p => p.WorkShiftId!.Value).Distinct().ToList();
            var shifts = await _context.WorkShifts.AsNoTracking()
                .Where(w => shiftIds.Contains(w.Id))
                .Select(w => new { w.Id, w.StartTimeUtc, StaffName = w.Staff == null ? "" : w.Staff.FullName })
                .ToDictionaryAsync(w => w.Id);
            model.Periods = payments
                .GroupBy(p => p.WorkShiftId)
                .OrderBy(g => g.Key == null ? DateTime.MaxValue : shifts.TryGetValue(g.Key.Value, out var w) ? w.StartTimeUtc : DateTime.MaxValue)
                .Select(g =>
                {
                    var label = g.Key == null
                        ? "Không gắn ca"
                        : shifts.TryGetValue(g.Key.Value, out var w)
                            ? $"Ca #{w.Id} · {w.StaffName} · {w.StartTimeUtc.ToVn():dd/MM HH:mm}"
                            : $"Ca #{g.Key}";
                    return new RevenuePeriodRow(label, g.Sum(p => p.AmountPaid), g.Count());
                }).ToList();
            return;
        }

        var format = model.GroupBy == "month" ? "yyyy-MM" : "yyyy-MM-dd";
        var grouped = payments.GroupBy(p => p.TransactionTimeUtc.AddHours(7).ToString(format));
        var totals = grouped.ToDictionary(g => g.Key, g => new RevenuePeriodRow(g.Key, g.Sum(p => p.AmountPaid), g.Count()));
        var date = model.GroupBy == "month" ? new DateOnly(model.From.Year, model.From.Month, 1) : model.From;
        while (date <= model.To)
        {
            var key = date.ToString(format);
            model.Periods.Add(totals.GetValueOrDefault(key, new RevenuePeriodRow(key, 0, 0)));
            date = model.GroupBy == "month" ? date.AddMonths(1) : date.AddDays(1);
        }
    }
    #endregion
}
