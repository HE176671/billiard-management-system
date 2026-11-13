using System.Globalization;
using System.Text;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// Báo cáo vận hành dành cho Manager (thay thế phần quản lý ca làm).
/// Phân tích tỷ lệ lấp đầy bàn theo loại bàn & từng bàn, biểu đồ nhiệt (heatmap) giờ cao điểm
/// theo thứ trong tuần, doanh thu giờ chơi và top món F&B bán chạy.
/// </summary>
[Authorize(Roles = "Manager")]
public class OperationsReportController : Controller
{
    private readonly ApplicationDbContext _context;

    public const int MaxRangeDays = 92;
    public const int DefaultOpenHour = 8;
    public const int DefaultCloseHour = 24;

    public OperationsReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    #region Hien thi bao cao van hanh

    // GET /OperationsReport
    public async Task<IActionResult> Index(
        DateOnly? from = null,
        DateOnly? to = null,
        int openHour = DefaultOpenHour,
        int closeHour = DefaultCloseHour,
        int? tableTypeId = null)
    {
        var model = await ValidateAndCreateFilterAsync(from, to, openHour, closeHour, tableTypeId);
        if (ModelState.IsValid)
        {
            await PopulateReportAsync(model);
        }
        return View(model);
    }

    #endregion

    #region Xuat CSV (UTF-8 BOM)

    // GET /OperationsReport/Export
    public async Task<IActionResult> Export(
        DateOnly? from = null,
        DateOnly? to = null,
        int openHour = DefaultOpenHour,
        int closeHour = DefaultCloseHour,
        int? tableTypeId = null)
    {
        var model = await ValidateAndCreateFilterAsync(from, to, openHour, closeHour, tableTypeId);
        if (!ModelState.IsValid)
        {
            var errors = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return BadRequest(string.IsNullOrWhiteSpace(errors) ? "Bộ lọc báo cáo vận hành không hợp lệ." : errors);
        }

        await PopulateReportAsync(model);

        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder();
        csv.Append("Bao cao van hanh,").Append(model.From.ToString("yyyy-MM-dd")).Append(',')
           .Append(model.To.ToString("yyyy-MM-dd")).Append(',')
           .Append($"Khung gio {model.OpenHour:D2}:00-{model.CloseHour:D2}:00").Append("\r\n");
        csv.Append("So ngay,").Append(model.TotalDays)
           .Append(",So ban,").Append(model.TotalTables)
           .Append(",Phien hoan tat,").Append(model.ClosedSessionCount)
           .Append(",Tong gio choi,").Append(model.TotalPlayHours.ToString(inv))
           .Append(",Ty le lap day chung (%),").Append(model.OverallOccupancyPercent.ToString(inv))
           .Append(",Doanh thu gio choi VND,").Append(model.TotalPlaytimeRevenue.ToString(inv))
           .Append(",Doanh thu F&B VND,").Append(model.TotalFandBRevenue.ToString(inv)).Append("\r\n\r\n");

        csv.Append("Loai ban,So ban,So phien ket thuc,Gio co khach,Gio kha dung,Ty le lap day (%),Doanh thu gio choi VND\r\n");
        foreach (var r in model.TableTypeRows)
        {
            csv.Append($"{EscapeCsv(r.TableTypeName)},{r.TableCount},{r.ClosedSessionCount},{r.OccupiedHours.ToString(inv)},{r.AvailableHours.ToString(inv)},{r.OccupancyPercent.ToString(inv)},{r.PlaytimeRevenue.ToString(inv)}\r\n");
        }

        csv.Append("\r\nMa ban,Loai ban,Tang,Trang thai hien tai,So phien,Gio co khach,Ty le lap day (%),Doanh thu gio choi VND\r\n");
        foreach (var t in model.TableRows)
        {
            csv.Append($"{EscapeCsv(t.TableCode)},{EscapeCsv(t.TableTypeName)},{t.FloorNumber},{t.CurrentStatus},{t.SessionCount},{t.OccupiedHours.ToString(inv)},{t.OccupancyPercent.ToString(inv)},{t.PlaytimeRevenue.ToString(inv)}\r\n");
        }

        csv.Append("\r\nTop mon F&B,So luong goi,So luong huy,So luong thuc ban,Don gia VND,Doanh thu VND\r\n");
        foreach (var item in model.TopFandBItems)
        {
            csv.Append($"{EscapeCsv(item.ProductName)},{item.GrossQuantity},{item.CancelledQuantity},{item.NetQuantity},{item.UnitPrice.ToString(inv)},{item.Revenue.ToString(inv)}\r\n");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"van-hanh-{model.From:yyyyMMdd}-{model.To:yyyyMMdd}.csv");
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    #endregion

    #region Server-side Validation (kiem tra bo loc nghiem ngat)

    private async Task<OperationsReportViewModel> ValidateAndCreateFilterAsync(
        DateOnly? from,
        DateOnly? to,
        int openHour,
        int closeHour,
        int? tableTypeId)
    {
        // Lấy ngày hôm nay theo múi giờ Việt Nam (UTC+7)
        var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var defaultFrom = todayVn.AddDays(-6); // Mặc định 7 ngày gần nhất

        var tableTypes = await _context.TableTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TableTypeFilterOption(t.Id, t.Name))
            .ToListAsync();

        var model = new OperationsReportViewModel
        {
            From = from ?? defaultFrom,
            To = to ?? todayVn,
            OpenHour = openHour,
            CloseHour = closeHour,
            TableTypeId = tableTypeId,
            TableTypeOptions = tableTypes
        };

        // 1. Kiểm tra ngày hợp lệ: From <= To, tối đa 92 ngày, không chọn ngày tương lai
        if (model.From.Year < 2000 || model.To.Year >= 9999)
        {
            ModelState.AddModelError(nameof(model.From), "Khoảng ngày không hợp lệ.");
        }
        else
        {
            if (model.From > model.To)
            {
                ModelState.AddModelError(nameof(model.From), "Ngày bắt đầu không được lớn hơn ngày kết thúc.");
            }
            if (model.To > todayVn)
            {
                ModelState.AddModelError(nameof(model.To), "Không được chọn ngày trong tương lai.");
            }
            var spanDays = model.To.DayNumber - model.From.DayNumber + 1;
            if (spanDays > MaxRangeDays)
            {
                ModelState.AddModelError(nameof(model.To), $"Khoảng thời gian báo cáo tối đa là {MaxRangeDays} ngày.");
            }
        }

        // 2. Kiểm tra khung giờ mở cửa: openHour in [0..23], closeHour in [1..24], openHour < closeHour
        if (model.OpenHour < 0 || model.OpenHour > 23)
        {
            ModelState.AddModelError(nameof(model.OpenHour), "Giờ mở cửa phải từ 0 đến 23.");
        }
        if (model.CloseHour < 1 || model.CloseHour > 24)
        {
            ModelState.AddModelError(nameof(model.CloseHour), "Giờ đóng cửa phải từ 1 đến 24.");
        }
        if (model.OpenHour >= model.CloseHour)
        {
            ModelState.AddModelError(nameof(model.OpenHour), "Giờ mở cửa phải nhỏ hơn giờ đóng cửa.");
        }

        // 3. Kiểm tra loại bàn (nếu người dùng truyền tableTypeId thì ID phải tồn tại trong DB)
        if (model.TableTypeId.HasValue && !tableTypes.Any(t => t.Id == model.TableTypeId.Value))
        {
            ModelState.AddModelError(nameof(model.TableTypeId), "Loại bàn đã chọn không tồn tại.");
        }

        return model;
    }

    #endregion

    #region Cong thuc tinh ty le lap day, Heatmap gio cao diem, Doanh thu gio choi va Top F&B

    private async Task PopulateReportAsync(OperationsReportViewModel model)
    {
        // Khoảng thời gian nửa mở [startUtc, endUtc) theo giờ Việt Nam (UTC+7):
        // startUtc = 00:00 ngày From (giờ VN) đổi sang UTC (-7h)
        // endUtc   = 00:00 ngày (To + 1) (giờ VN) đổi sang UTC (-7h)
        var startUtc = model.From.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var endUtc = model.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var nowUtc = DateTime.UtcNow;

        model.TotalDays = model.To.DayNumber - model.From.DayNumber + 1;
        int openMinutesPerDay = (model.CloseHour - model.OpenHour) * 60;

        // 1. Lấy danh sách bàn (lọc theo loại bàn nếu có)
        var tablesQuery = _context.BilliardTables.AsNoTracking()
            .Include(t => t.TableType)
            .AsQueryable();

        if (model.TableTypeId.HasValue)
        {
            tablesQuery = tablesQuery.Where(t => t.TableTypeId == model.TableTypeId.Value);
        }

        var tables = await tablesQuery
            .OrderBy(t => t.TableCode)
            .ToListAsync();

        model.TotalTables = tables.Count;
        var tableIds = tables.Select(t => t.Id).ToHashSet();

        // 2. Lấy các segment bàn giao nhau với khoảng [startUtc, endUtc)
        // Nếu segment chưa kết thúc (EndAtUtc == null) thì cắt tại thời điểm hiện tại (nowUtc).
        var rawSegments = await _context.PlaySessionTableSegments.AsNoTracking()
            .Where(s => tableIds.Contains(s.TableId)
                        && s.StartAtUtc < endUtc
                        && (s.EndAtUtc == null || s.EndAtUtc > startUtc))
            .Select(s => new
            {
                s.Id,
                s.SessionId,
                s.TableId,
                s.StartAtUtc,
                s.EndAtUtc
            })
            .ToListAsync();

        // 3. Lấy các phiên chơi (PlaySessions) để tính KPI số phiên và doanh thu giờ chơi
        var sessionsInWindow = await _context.PlaySessions.AsNoTracking()
            .Where(p => tableIds.Contains(p.TableId)
                        && p.StartAtUtc < endUtc
                        && (p.EndAtUtc == null || p.EndAtUtc > startUtc))
            .Select(p => new
            {
                p.Id,
                p.TableId,
                p.StartAtUtc,
                p.EndAtUtc,
                p.PlaytimeAmount,
                p.Status
            })
            .ToListAsync();

        model.ActiveSessionCount = sessionsInWindow.Count(s => s.Status == "Active");

        // Cảnh báo phiên đang mở quá lâu (nghi quên đóng bàn): số giờ của phiên này
        // được tính tới hiện tại nên có thể làm tỷ lệ lấp đầy cao bất thường.
        var tableCodeById = tables.ToDictionary(t => t.Id, t => t.TableCode);
        model.LongOpenSessions = sessionsInWindow
            .Where(s => s.Status == "Active" && s.EndAtUtc == null
                        && (nowUtc - s.StartAtUtc).TotalHours > OperationsReportViewModel.LongOpenHours)
            .OrderBy(s => s.StartAtUtc)
            .Select(s => new LongOpenSessionRow(s.Id, tableCodeById.GetValueOrDefault(s.TableId, "?"), s.StartAtUtc,
                Math.Round((decimal)(nowUtc - s.StartAtUtc).TotalHours, 1)))
            .ToList();

        // Các phiên đã kết thúc trong khoảng [startUtc, endUtc)
        var closedSessions = sessionsInWindow
            .Where(s => s.Status == "Closed" && s.EndAtUtc.HasValue && s.EndAtUtc.Value >= startUtc && s.EndAtUtc.Value < endUtc)
            .ToList();

        model.ClosedSessionCount = closedSessions.Count;

        // 4. Chia từng segment thành từng lát theo giờ Việt Nam để cộng số phút có khách:
        //    - occupiedMinutesInOpenHoursByTable[tableId]: số phút có khách nằm trong khung giờ mở cửa [OpenHour, CloseHour)
        //    - totalOccupiedMinutesByTable[tableId]: tổng số phút có khách thực tế trong khoảng
        //    - heatmapMinutes[(dayOfWeek, hour)]: tổng số phút có khách của tất cả bàn tại (thứ, giờ)
        var occupiedOpenMinutesByTable = tables.ToDictionary(t => t.Id, _ => 0m);
        var totalOccupiedMinutesByTable = tables.ToDictionary(t => t.Id, _ => 0m);
        var heatmapMinutes = new Dictionary<(DayOfWeek Dow, int Hour), decimal>();

        // Hàm cộng phút có khách của 1 khoảng [fromUtc, toUtc) trên 1 bàn.
        // Cắt khoảng vào [startUtc, endUtc), đổi sang giờ VN rồi chia thành từng lát 1 giờ
        // (vd 19:40–21:10 → 20 phút ở 19h, 60 phút ở 20h, 10 phút ở 21h).
        void AddOccupiedTime(int tableId, DateTime fromUtc, DateTime? toUtc)
        {
            var clipStartUtc = fromUtc < startUtc ? startUtc : fromUtc;
            var effectiveEndUtc = toUtc ?? nowUtc;          // chưa kết thúc → tính đến hiện tại
            var clipEndUtc = effectiveEndUtc > endUtc ? endUtc : effectiveEndUtc;
            if (clipEndUtc <= clipStartUtc) return;

            var curVn = clipStartUtc.AddHours(7);
            var stopVn = clipEndUtc.AddHours(7);
            while (curVn < stopVn)
            {
                var nextHourVn = new DateTime(curVn.Year, curVn.Month, curVn.Day, curVn.Hour, 0, 0).AddHours(1);
                var sliceEndVn = nextHourVn < stopVn ? nextHourVn : stopVn;
                var minutes = (decimal)(sliceEndVn - curVn).TotalMinutes;

                totalOccupiedMinutesByTable[tableId] += minutes;

                // Chỉ tính vào tỷ lệ lấp đầy và heatmap nếu giờ nằm trong [OpenHour, CloseHour)
                int hour = curVn.Hour;
                if (hour >= model.OpenHour && hour < model.CloseHour)
                {
                    occupiedOpenMinutesByTable[tableId] += minutes;
                    var key = (curVn.DayOfWeek, hour);
                    heatmapMinutes[key] = heatmapMinutes.GetValueOrDefault(key) + minutes;
                }
                curVn = sliceEndVn;
            }
        }

        // Nguồn chính: segment (đúng cả khi khách chuyển bàn giữa phiên).
        foreach (var seg in rawSegments)
            AddOccupiedTime(seg.TableId, seg.StartAtUtc, seg.EndAtUtc);

        // Phiên cũ không có segment nào (dữ liệu nhập tay/seed cũ) → lấy thời gian của chính phiên.
        var sessionsWithSegment = rawSegments.Select(s => s.SessionId).ToHashSet();
        foreach (var sess in sessionsInWindow.Where(s => !sessionsWithSegment.Contains(s.Id)))
            AddOccupiedTime(sess.TableId, sess.StartAtUtc, sess.EndAtUtc);

        // 5. Công thức KPI tổng quan:
        //    - Tổng giờ chơi = tổng số phút có khách / 60
        //    - Tỷ lệ lấp đầy chung (%) = tổng phút có khách trong giờ mở cửa / (số bàn × số phút mở cửa mỗi ngày × số ngày) × 100
        decimal totalOccupiedOpenMinutes = occupiedOpenMinutesByTable.Values.Sum();
        decimal totalOccupiedAllMinutes = totalOccupiedMinutesByTable.Values.Sum();
        model.TotalPlayHours = Math.Round(totalOccupiedAllMinutes / 60m, 2);

        if (model.ClosedSessionCount > 0)
        {
            var closedDurationsMinutes = closedSessions
                .Where(s => s.EndAtUtc.HasValue && s.EndAtUtc.Value > s.StartAtUtc)
                .Select(s => (decimal)(s.EndAtUtc!.Value - s.StartAtUtc).TotalMinutes)
                .ToList();
            model.AvgSessionMinutes = closedDurationsMinutes.Count > 0
                ? Math.Round(closedDurationsMinutes.Average(), 1)
                : 0m;
        }

        model.AvgSessionsPerDay = model.TotalDays > 0
            ? Math.Round((decimal)model.ClosedSessionCount / model.TotalDays, 2)
            : 0m;

        model.OverallOccupancyPercent = CalculateOccupancyPercent(
            totalOccupiedOpenMinutes,
            model.TotalTables,
            openMinutesPerDay,
            model.TotalDays);

        // 6. Doanh thu giờ chơi theo bàn cuối cùng của phiên (PlaySession.TableId)
        var revenueByTable = closedSessions
            .GroupBy(s => s.TableId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.PlaytimeAmount ?? 0m));

        var sessionCountByTable = closedSessions
            .GroupBy(s => s.TableId)
            .ToDictionary(g => g.Key, g => g.Count());

        model.TotalPlaytimeRevenue = revenueByTable.Values.Sum();

        // 7. Bảng tỷ lệ lấp đầy & doanh thu giờ chơi theo loại bàn
        model.TableTypeRows = tables
            .GroupBy(t => new { t.TableTypeId, TypeName = t.TableType?.Name ?? "Chưa phân loại" })
            .OrderBy(g => g.Key.TypeName)
            .Select(g =>
            {
                int tableCount = g.Count();
                decimal occOpenMins = g.Sum(t => occupiedOpenMinutesByTable.GetValueOrDefault(t.Id));
                decimal availMins = (decimal)tableCount * openMinutesPerDay * model.TotalDays;
                decimal occPct = CalculateOccupancyPercent(occOpenMins, tableCount, openMinutesPerDay, model.TotalDays);
                int closedCnt = g.Sum(t => sessionCountByTable.GetValueOrDefault(t.Id));
                decimal rev = g.Sum(t => revenueByTable.GetValueOrDefault(t.Id));

                return new TableTypeOccupancyRow(
                    TableTypeId: g.Key.TableTypeId,
                    TableTypeName: g.Key.TypeName,
                    TableCount: tableCount,
                    ClosedSessionCount: closedCnt,
                    OccupiedHours: Math.Round(occOpenMins / 60m, 2),
                    AvailableHours: Math.Round(availMins / 60m, 2),
                    OccupancyPercent: occPct,
                    PlaytimeRevenue: rev
                );
            })
            .ToList();

        // 8. Bảng hiệu suất theo từng bàn (giúp Manager biết bàn nào đông khách nhất / vắng nhất)
        model.TableRows = tables
            .Select(t =>
            {
                decimal occOpenMins = occupiedOpenMinutesByTable.GetValueOrDefault(t.Id);
                decimal occPct = CalculateOccupancyPercent(occOpenMins, 1, openMinutesPerDay, model.TotalDays);
                int sessCnt = sessionCountByTable.GetValueOrDefault(t.Id);
                decimal rev = revenueByTable.GetValueOrDefault(t.Id);

                return new TableUtilizationRow(
                    TableId: t.Id,
                    TableCode: t.TableCode,
                    TableTypeName: t.TableType?.Name ?? "—",
                    FloorNumber: t.FloorNumber,
                    CurrentStatus: t.Status,
                    SessionCount: sessCnt,
                    OccupiedHours: Math.Round(occOpenMins / 60m, 2),
                    OccupancyPercent: occPct,
                    PlaytimeRevenue: rev
                );
            })
            .OrderByDescending(r => r.OccupancyPercent)
            .ThenByDescending(r => r.SessionCount)
            .ThenBy(r => r.TableCode)
            .ToList();

        model.MostUsedTables = model.TableRows.Take(3).ToList();
        model.LeastUsedTables = model.TableRows
            .OrderBy(r => r.OccupancyPercent)
            .ThenBy(r => r.SessionCount)
            .ThenBy(r => r.TableCode)
            .Take(3)
            .ToList();

        // 9. Heatmap giờ cao điểm (7 thứ T2..CN × các giờ mở cửa):
        //    Giá trị mỗi ô (%) = phút có khách tại (thứ, giờ) / (60 × số lần thứ đó xuất hiện trong khoảng × số bàn) × 100
        var dayOccurrences = CountDayOfWeekOccurrences(model.From, model.To);
        model.HeatmapHours = Enumerable.Range(model.OpenHour, model.CloseHour - model.OpenHour).ToList();

        var orderedDays = new (DayOfWeek Dow, string Label)[]
        {
            (DayOfWeek.Monday, "Thứ 2"),
            (DayOfWeek.Tuesday, "Thứ 3"),
            (DayOfWeek.Wednesday, "Thứ 4"),
            (DayOfWeek.Thursday, "Thứ 5"),
            (DayOfWeek.Friday, "Thứ 6"),
            (DayOfWeek.Saturday, "Thứ 7"),
            (DayOfWeek.Sunday, "Chủ nhật")
        };

        foreach (var (dow, label) in orderedDays)
        {
            int occCount = dayOccurrences.GetValueOrDefault(dow, 0);
            var cells = new List<HeatmapCell>(model.HeatmapHours.Count);

            foreach (int hr in model.HeatmapHours)
            {
                decimal mins = heatmapMinutes.GetValueOrDefault((dow, hr), 0m);
                decimal capacityMins = (decimal)60 * occCount * model.TotalTables;
                decimal pct = capacityMins > 0
                    ? Math.Min(100m, Math.Round(mins / capacityMins * 100m, 1))
                    : 0m;

                cells.Add(new HeatmapCell(hr, Math.Round(mins, 1), capacityMins, pct));

                if (pct > model.PeakSlotPercent)
                {
                    model.PeakSlotPercent = pct;
                    model.PeakSlotLabel = $"{label} ({hr:D2}:00 - {hr + 1:D2}:00)";
                }
            }

            model.HeatmapRows.Add(new HeatmapDayRow(dow, label, occCount, cells));
        }

        // 10. Top món F&B bán chạy:
        //     Chỉ lấy đơn có Orders.Status = "Completed" trong khoảng [startUtc, endUtc),
        //     Số lượng thực bán = Quantity - CancelledQuantity, Doanh thu = UnitPrice * Số lượng thực bán.
        var orderDetailsQuery =
            from od in _context.OrderDetails.AsNoTracking()
            join o in _context.Orders.AsNoTracking() on od.OrderId equals o.Id
            join p in _context.Products.AsNoTracking() on od.ProductId equals p.Id
            where o.Status == Order.StatusCompleted
                  && o.OrderTimeUtc >= startUtc
                  && o.OrderTimeUtc < endUtc
            select new
            {
                od.ProductId,
                ProductName = p.Name,
                od.Quantity,
                od.CancelledQuantity,
                od.UnitPrice,
                o.SessionId
            };

        if (model.TableTypeId.HasValue)
        {
            // Nếu lọc theo loại bàn, chỉ tính các đơn thuộc phiên chơi của bàn thuộc loại đó
            var sessionIdsOfFilteredTables = _context.PlaySessions.AsNoTracking()
                .Where(ps => tableIds.Contains(ps.TableId))
                .Select(ps => ps.Id);
            orderDetailsQuery = orderDetailsQuery.Where(x => sessionIdsOfFilteredTables.Contains(x.SessionId));
        }

        var rawOrderDetails = await orderDetailsQuery.ToListAsync();

        var fnbRows = rawOrderDetails
            .GroupBy(x => new { x.ProductId, x.ProductName })
            .Select(g =>
            {
                int gross = g.Sum(x => x.Quantity);
                int cancelled = g.Sum(x => x.CancelledQuantity);
                int net = Math.Max(0, gross - cancelled);
                decimal revenue = g.Sum(x => Math.Max(0, x.Quantity - x.CancelledQuantity) * x.UnitPrice);
                decimal unitPrice = g.Select(x => x.UnitPrice).FirstOrDefault();
                return new TopFandBRow(g.Key.ProductId, g.Key.ProductName, gross, cancelled, net, unitPrice, revenue);
            })
            .Where(r => r.NetQuantity > 0)
            .OrderByDescending(r => r.NetQuantity)
            .ThenByDescending(r => r.Revenue)
            .ToList();

        model.TotalFandBRevenue = fnbRows.Sum(r => r.Revenue);
        model.TopFandBItems = fnbRows.Take(10).ToList();
    }

    /// <summary>
    /// Công thức tính tỷ lệ lấp đầy (%):
    /// OccupancyPercent = (OccupiedMinutesInOpenHours / (TableCount × OpenMinutesPerDay × TotalDays)) × 100
    /// Giới hạn trong khoảng [0, 100] và làm tròn 1 chữ số thập phân.
    /// </summary>
    public static decimal CalculateOccupancyPercent(
        decimal occupiedMinutesInOpenHours,
        int tableCount,
        int openMinutesPerDay,
        int totalDays)
    {
        if (tableCount <= 0 || openMinutesPerDay <= 0 || totalDays <= 0 || occupiedMinutesInOpenHours <= 0)
            return 0m;

        decimal capacityMinutes = (decimal)tableCount * openMinutesPerDay * totalDays;
        decimal pct = occupiedMinutesInOpenHours / capacityMinutes * 100m;
        return Math.Min(100m, Math.Max(0m, Math.Round(pct, 1)));
    }

    /// <summary>
    /// Đếm số lần mỗi thứ trong tuần xuất hiện trong khoảng ngày [from, to] (bao gồm cả 2 đầu).
    /// </summary>
    public static Dictionary<DayOfWeek, int> CountDayOfWeekOccurrences(DateOnly from, DateOnly to)
    {
        var result = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => 0);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            result[d.DayOfWeek]++;
        }
        return result;
    }

    #endregion
}
