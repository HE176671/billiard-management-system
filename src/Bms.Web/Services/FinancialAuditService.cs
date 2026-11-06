using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public class FinancialAuditService : IFinancialAuditService
{
    private readonly ApplicationDbContext _db;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        OperatingExpense.CatMaintenance,
        OperatingExpense.CatInventoryCogs,
        OperatingExpense.CatUtilities,
        OperatingExpense.CatPayroll
    };

    private static readonly HashSet<string> AllowedShifts = new(StringComparer.Ordinal)
    {
        OperatingExpense.ShiftDay,
        OperatingExpense.ShiftNight,
        OperatingExpense.ShiftAll
    };

    public FinancialAuditService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. TỔNG HỢP BÁO CÁO P&L, MÙA VỤ, CHI PHÍ & KIỂM TOÁN THẤT THOÁT (UC-FIN-01 & 03)
    // ══════════════════════════════════════════════════════════════════════════
    public async Task PopulateReportAsync(RevenueReportViewModel model, CancellationToken ct = default)
    {
        // Khoảng nửa mở [startUtc, endUtc) tính theo múi giờ Việt Nam (UTC+7) — BR-FIN-01
        var startUtc = model.From.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var endUtc = model.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var format = model.GroupBy == "month" ? "yyyy-MM" : "yyyy-MM-dd";

        // ── Bước 1: Truy vấn Giao dịch thực thu (PaymentTransactions) ──
        var payments = await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.TransactionTimeUtc >= startUtc && p.TransactionTimeUtc < endUtc)
            .ToListAsync(ct);

        model.TotalRevenue = payments.Sum(p => p.AmountPaid);
        model.PaymentCount = payments.Count;
        model.InvoiceCount = payments.Select(p => p.InvoiceId).Distinct().Count();

        var groupedPayments = payments
            .GroupBy(p => p.TransactionTimeUtc.AddHours(7).ToString(format))
            .ToDictionary(g => g.Key, g => new RevenuePeriodRow(g.Key, g.Sum(p => p.AmountPaid), g.Count()));

        var cursorDate = model.GroupBy == "month"
            ? new DateOnly(model.From.Year, model.From.Month, 1)
            : model.From;

        while (cursorDate <= model.To)
        {
            var key = cursorDate.ToString(format);
            model.Periods.Add(groupedPayments.GetValueOrDefault(key, new RevenuePeriodRow(key, 0m, 0)));
            cursorDate = model.GroupBy == "month" ? cursorDate.AddMonths(1) : cursorDate.AddDays(1);
        }

        model.Methods = payments
            .GroupBy(p => p.PaymentMethod)
            .OrderBy(g => g.Key)
            .Select(g => new RevenueMethodRow(g.Key, g.Sum(p => p.AmountPaid), g.Count()))
            .ToList();

        // ── Bước 2: Truy vấn Hóa đơn & Phiên chơi để bóc tách Tiền Giờ vs Nước và Ca Ngày vs Đêm ──
        var invoiceRows = await _db.Database.SqlQuery<InvoiceSessionRow>($@"
            SELECT
                i.Id AS InvoiceId,
                i.SessionId AS SessionId,
                i.TotalPlaytime AS TotalPlaytime,
                i.TotalFandB AS TotalFandB,
                i.DiscountAmount AS DiscountAmount,
                i.Status AS InvoiceStatus,
                i.CreatedAtUtc AS InvoiceCreatedAtUtc,
                s.StartAtUtc AS SessionStartAtUtc
            FROM dbo.Invoices i
            JOIN dbo.PlaySessions s ON s.Id = i.SessionId
            WHERE i.CreatedAtUtc >= {startUtc} AND i.CreatedAtUtc < {endUtc}
              AND i.Status = 'Paid'
            ").ToListAsync(ct);

        model.TotalPlaytimeRevenue = invoiceRows.Sum(x => x.TotalPlaytime);
        model.TotalFnbRevenue = invoiceRows.Sum(x => x.TotalFandB);
        model.TotalDiscountAmount = invoiceRows.Sum(x => x.DiscountAmount);
        var grossInvoiced = model.TotalPlaytimeRevenue + model.TotalFnbRevenue;
        model.FnbSharePercent = grossInvoiced > 0
            ? Math.Round(model.TotalFnbRevenue / grossInvoiced * 100m, 1)
            : 0m;

        // ── Bước 3: Truy vấn Sổ cái Chi phí vận hành (OperatingExpenses) ──
        var expenses = await _db.OperatingExpenses.AsNoTracking()
            .Include(e => e.Table)
            .Include(e => e.CreatedBy)
            .Where(e => e.ExpenseDate >= model.From && e.ExpenseDate <= model.To)
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.Id)
            .ToListAsync(ct);

        model.TotalOperatingCost = expenses.Sum(e => e.Amount);

        model.ExpenseItems = expenses.Select(e => new OperatingExpenseItemRow
        {
            Id = e.Id,
            ExpenseCode = e.ExpenseCode,
            ExpenseDate = e.ExpenseDate,
            CategoryCode = e.Category,
            CategoryName = OperatingExpense.GetCategoryDisplayName(e.Category),
            Title = e.Title,
            Amount = e.Amount,
            TableCode = e.Table?.TableCode,
            ShiftPeriod = e.ShiftPeriod,
            ShiftDisplayName = OperatingExpense.GetShiftDisplayName(e.ShiftPeriod),
            Notes = e.Notes,
            CreatedByName = e.CreatedBy?.FullName ?? "Admin"
        }).ToList();

        model.ExpenseCategories = BuildExpenseCategories(expenses, model.TotalRevenue);

        model.TableOptions = await _db.BilliardTables.AsNoTracking()
            .OrderBy(t => t.TableCode)
            .Select(t => new TableSelectOption(t.Id, $"Bàn {t.TableCode} ({t.Status})"))
            .ToListAsync(ct);

        // ── Bước 4: Phân tích hiệu suất Ca Ban Ngày (08:00–18:00) vs Ca Ban Đêm (18:00–02:00) (BR-FIN-06) ──
        var dayInvoices = invoiceRows.Where(x => IsDayShiftHour(x.SessionStartAtUtc.AddHours(7).Hour)).ToList();
        var nightInvoices = invoiceRows.Where(x => !IsDayShiftHour(x.SessionStartAtUtc.AddHours(7).Hour)).ToList();

        var dayPayroll = expenses
            .Where(e => e.Category == OperatingExpense.CatPayroll && e.ShiftPeriod == OperatingExpense.ShiftDay)
            .Sum(e => e.Amount);
        var nightPayroll = expenses
            .Where(e => e.Category == OperatingExpense.CatPayroll && e.ShiftPeriod == OperatingExpense.ShiftNight)
            .Sum(e => e.Amount);

        model.DayNight = new DayNightComparisonSummary
        {
            DayRevenue = dayInvoices.Sum(x => x.TotalPlaytime + x.TotalFandB - x.DiscountAmount),
            NightRevenue = nightInvoices.Sum(x => x.TotalPlaytime + x.TotalFandB - x.DiscountAmount),
            DaySessionCount = dayInvoices.Count,
            NightSessionCount = nightInvoices.Count,
            DayPayrollCost = dayPayroll,
            NightPayrollCost = nightPayroll
        };

        model.DayNight.ProblemSolvedExplanation = model.DayNight.TotalSessionRevenue > 0
            ? $"Khung giờ Đêm (18:00–02:00) tạo ra {model.DayNight.NightRevenueSharePercent}% tổng doanh thu (chi phí lương chỉ chiếm {model.DayNight.NightPayrollToRevenuePercent}% doanh thu ca đêm), trong khi Ca Ban Ngày (08:00–18:00) chỉ đóng góp {model.DayNight.DayRevenueSharePercent}% doanh thu nhưng ngốn tới {model.DayNight.DayPayrollToRevenuePercent}% doanh thu để trả lương. Chỉ số này giải quyết bài toán định biên nhân sự: cần rút gọn nhân viên ca sáng/chiều ngày thường để cắt lỗ."
            : "Chưa có dữ liệu phiên chơi để phân tách hiệu suất Ca Ngày và Ca Đêm.";

        // ── Bước 5: Chạy 5 Quy tắc Kiểm toán & Truy vết Thất thoát Doanh thu (BR-FIN-08 -> BR-FIN-12) ──
        model.LeakageItems = await AuditRevenueLeakageAsync(startUtc, endUtc, ct);
        model.TotalLeakageAmount = model.LeakageItems.Sum(x => x.LeakageAmount);

        // ── Bước 6: Xây dựng Bảng P&L chi tiết từng kỳ & Phát hiện Mùa vụ (T3–T5, T4–T6) ──
        var invByPeriod = invoiceRows
            .GroupBy(x => x.InvoiceCreatedAtUtc.AddHours(7).ToString(format))
            .ToDictionary(g => g.Key, g => g.ToList());

        var expByPeriod = expenses
            .GroupBy(e => e.ExpenseDate.ToString(format))
            .ToDictionary(g => g.Key, g => g.ToList());

        var leakByPeriod = model.LeakageItems
            .GroupBy(l => l.OccurredAtVn.ToString(format))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.LeakageAmount));

        var avgPeriodRev = model.Periods.Where(p => p.Revenue > 0).Select(p => p.Revenue).DefaultIfEmpty(0m).Average();

        foreach (var p in model.Periods)
        {
            invByPeriod.TryGetValue(p.Period, out var pInvs);
            expByPeriod.TryGetValue(p.Period, out var pExps);
            leakByPeriod.TryGetValue(p.Period, out var pLeak);

            pInvs ??= new List<InvoiceSessionRow>();
            pExps ??= new List<OperatingExpense>();

            var playRev = pInvs.Sum(x => x.TotalPlaytime);
            var dayPlayRev = pInvs.Where(x => IsDayShiftHour(x.SessionStartAtUtc.AddHours(7).Hour)).Sum(x => x.TotalPlaytime);
            var nightPlayRev = playRev - dayPlayRev;
            var fnbRev = pInvs.Sum(x => x.TotalFandB);
            var discAmt = pInvs.Sum(x => x.DiscountAmount);

            var maint = pExps.Where(e => e.Category == OperatingExpense.CatMaintenance).Sum(e => e.Amount);
            var cogs = pExps.Where(e => e.Category == OperatingExpense.CatInventoryCogs).Sum(e => e.Amount);
            var util = pExps.Where(e => e.Category == OperatingExpense.CatUtilities).Sum(e => e.Amount);
            var pay = pExps.Where(e => e.Category == OperatingExpense.CatPayroll).Sum(e => e.Amount);

            var pnlRow = new FinancialPeriodPnlRow
            {
                Period = p.Period,
                PlaytimeRevenue = playRev,
                DayPlaytimeRevenue = dayPlayRev,
                NightPlaytimeRevenue = nightPlayRev,
                FnbRevenue = fnbRev,
                DiscountAmount = discAmt,
                CollectedRevenue = p.Revenue,
                PaymentCount = p.PaymentCount,
                MaintenanceCost = maint,
                InventoryCogsCost = cogs,
                UtilitiesCost = util,
                PayrollCost = pay,
                LeakageAmount = pLeak,
                IsPeakRevenue = avgPeriodRev > 0 && p.Revenue >= avgPeriodRev * 1.18m,
                IsPeakBeverage = (playRev + fnbRev) > 0 && (fnbRev / (playRev + fnbRev) * 100m) >= 33.0m
            };

            pnlRow.DiagnosticReason = BuildPeriodDiagnostic(pnlRow);
            model.PnlPeriods.Add(pnlRow);
        }

        model.ProfitablePeriodsCount = model.PnlPeriods.Count(x => x.CollectedRevenue > 0 && x.NetProfit >= 0);
        model.LossPeriodsCount = model.PnlPeriods.Count(x => x.TotalExpense > 0 && x.NetProfit < 0);

        var peakRevPeriods = model.PnlPeriods.Where(x => x.IsPeakRevenue).Select(x => x.Period).ToList();
        model.PeakRevenueMonthsSummary = peakRevPeriods.Count > 0
            ? $"Cao điểm doanh thu rơi vào [{string.Join(", ", peakRevPeriods)}] — giải quyết bài toán chuẩn bị bàn/cơ trước mùa giải đấu và tránh đóng bàn bảo trì vào các tháng này."
            : "Chưa phát hiện kỳ đột biến doanh thu vượt ngưỡng +18% trung bình.";

        var peakBevPeriods = model.PnlPeriods.Where(x => x.IsPeakBeverage).Select(x => x.Period).ToList();
        model.PeakBeverageMonthsSummary = peakBevPeriods.Count > 0
            ? $"Cao điểm tiêu thụ nước & bia rơi vào [{string.Join(", ", peakBevPeriods)}] (tỷ trọng F&B đạt 34%–41%) — giải quyết bài toán nhập tồn kho nước giải khát chủ lực để bù đắp chi phí điện điều hòa mùa hè."
            : "Tỷ trọng đồ uống duy trì ở mức tiêu chuẩn (<33% tổng doanh thu).";

        // ── Bước 7: Thống kê Top Đồ uống / Bia bán chạy nhất (Phục vụ phân tích F&B T4–T6) ──
        model.TopBeverages = await QueryTopBeveragesAsync(startUtc, endUtc, model.TotalFnbRevenue, ct);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. QUẢN LÝ SỔ CÁI CHI PHÍ VẬN HÀNH (UC-FIN-02: AddExpense & DeleteExpense)
    // ══════════════════════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message, OperatingExpense? Expense)> AddExpenseAsync(
        CreateExpenseInputModel input, string? actorId, CancellationToken ct = default)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 150
            || !AllowedCategories.Contains(input.Category))
        {
            return (false, FinancialMessages.MsgFin03InvalidTitleOrCategory, null);
        }

        if (input.Amount <= 0m || input.Amount > 1_000_000_000m)
        {
            return (false, FinancialMessages.MsgFin04InvalidAmount, null);
        }

        var shift = AllowedShifts.Contains(input.ShiftPeriod) ? input.ShiftPeriod : OperatingExpense.ShiftAll;

        if (input.TableId.HasValue)
        {
            var tableExists = await _db.BilliardTables.AnyAsync(t => t.Id == input.TableId.Value, ct);
            if (!tableExists) input.TableId = null;
        }

        var code = $"EXP-{input.ExpenseDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var entity = new OperatingExpense
        {
            ExpenseCode = code,
            ExpenseDate = input.ExpenseDate,
            Category = input.Category,
            Title = input.Title.Trim(),
            Amount = decimal.Round(input.Amount, 2),
            TableId = input.TableId,
            ShiftPeriod = shift,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedById = actorId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.OperatingExpenses.Add(entity);
        await _db.SaveChangesAsync(ct);

        return (true, FinancialMessages.FormatMsgFin05Created(code, entity.Amount), entity);
    }

    public async Task<(bool Success, string Message)> DeleteExpenseAsync(int id, CancellationToken ct = default)
    {
        var exp = await _db.OperatingExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (exp == null)
        {
            return (false, FinancialMessages.MsgFin11NotFound);
        }

        _db.OperatingExpenses.Remove(exp);
        await _db.SaveChangesAsync(ct);
        return (true, FinancialMessages.MsgFin06Deleted);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. BỘ MÁY KIỂM TOÁN 5 QUY TẮC TRUY VẾT THẤT THOÁT DOANH THU (UC-FIN-03)
    // ══════════════════════════════════════════════════════════════════════════
    private async Task<List<LeakageAuditRow>> AuditRevenueLeakageAsync(
        DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var results = new List<LeakageAuditRow>();

        // [LEAK-01] Phiên chơi đã đóng (Closed) nhưng KHÔNG LẬP HÓA ĐƠN (BR-FIN-08)
        var unbilledSessions = await _db.Database.SqlQuery<UnbilledSessionRow>($@"
            SELECT
                s.Id AS SessionId,
                t.TableCode AS TableCode,
                ISNULL(u.EmployeeCode + N' — ' + u.FullName, ISNULL(u.FullName, N'Không rõ')) AS StaffDisplay,
                ISNULL(s.EndAtUtc, s.StartAtUtc) AS ClosedAtUtc,
                ISNULL(s.PlaytimeAmount, 0) AS PlaytimeAmount
            FROM dbo.PlaySessions s
            JOIN dbo.BilliardTables t ON t.Id = s.TableId
            LEFT JOIN dbo.AspNetUsers u ON u.Id = ISNULL(s.ClosedById, s.OpenedById)
            LEFT JOIN dbo.Invoices i ON i.SessionId = s.Id
            WHERE s.Status = 'Closed'
              AND ISNULL(s.EndAtUtc, s.StartAtUtc) >= {startUtc}
              AND ISNULL(s.EndAtUtc, s.StartAtUtc) < {endUtc}
              AND i.Id IS NULL
              AND ISNULL(s.PlaytimeAmount, 0) > 0
            ").ToListAsync(ct);

        foreach (var row in unbilledSessions)
        {
            results.Add(new LeakageAuditRow
            {
                RuleCode = "LEAK-01",
                OccurredAtVn = row.ClosedAtUtc.AddHours(7),
                LeakageType = "Phiên đã đóng nhưng không lập Hóa đơn",
                LocationOrRef = $"Bàn {row.TableCode} · Phiên #{row.SessionId}",
                ResponsibleStaff = row.StaffDisplay,
                ExpectedAmount = row.PlaytimeAmount,
                RecordedAmount = 0m,
                LeakageAmount = row.PlaytimeAmount,
                EvidenceDetail = $"Phiên #{row.SessionId} đã chuyển trạng thái Closed (Tiền giờ chốt: {row.PlaytimeAmount:N0} ₫) nhưng không có bản ghi Invoices.",
                ProblemSolvedAction = "Giải quyết nạn 'mở bàn chui / thu tiền mặt ngoài sổ': Yêu cầu nhân viên đóng bàn giải trình và lập hóa đơn truy thu."
            });
        }

        // [LEAK-02] Hóa đơn treo Unpaid hoặc thu thiếu so với FinalAmount (BR-FIN-09)
        var unpaidInvoices = await _db.Database.SqlQuery<UnpaidInvoiceRow>($@"
            SELECT
                i.Id AS InvoiceId,
                i.SessionId AS SessionId,
                t.TableCode AS TableCode,
                ISNULL(u.EmployeeCode + N' — ' + u.FullName, ISNULL(u.FullName, N'Không rõ')) AS StaffDisplay,
                i.CreatedAtUtc AS CreatedAtUtc,
                i.FinalAmount AS FinalAmount,
                ISNULL((SELECT SUM(p.AmountPaid) FROM dbo.PaymentTransactions p WHERE p.InvoiceId = i.Id), 0) AS PaidAmount
            FROM dbo.Invoices i
            JOIN dbo.PlaySessions s ON s.Id = i.SessionId
            JOIN dbo.BilliardTables t ON t.Id = s.TableId
            LEFT JOIN dbo.AspNetUsers u ON u.Id = ISNULL(s.ClosedById, s.OpenedById)
            WHERE i.CreatedAtUtc >= {startUtc}
              AND i.CreatedAtUtc < {endUtc}
              AND i.FinalAmount > ISNULL((SELECT SUM(p.AmountPaid) FROM dbo.PaymentTransactions p WHERE p.InvoiceId = i.Id), 0)
            ").ToListAsync(ct);

        foreach (var row in unpaidInvoices)
        {
            var diff = row.FinalAmount - row.PaidAmount;
            results.Add(new LeakageAuditRow
            {
                RuleCode = "LEAK-02",
                OccurredAtVn = row.CreatedAtUtc.AddHours(7),
                LeakageType = "Hóa đơn treo Unpaid / Chưa thu đủ tiền",
                LocationOrRef = $"Bàn {row.TableCode} · HĐ #{row.InvoiceId} (Phiên #{row.SessionId})",
                ResponsibleStaff = row.StaffDisplay,
                ExpectedAmount = row.FinalAmount,
                RecordedAmount = row.PaidAmount,
                LeakageAmount = diff,
                EvidenceDetail = $"Hóa đơn #{row.InvoiceId} phải thu {row.FinalAmount:N0} ₫ nhưng thực thu trong PaymentTransactions chỉ có {row.PaidAmount:N0} ₫.",
                ProblemSolvedAction = "Giải quyết rủi ro khách bỏ về hoặc thu ngân giữ tiền mặt nhưng không bấm tất toán trên phần mềm."
            });
        }

        // [LEAK-03] Sửa giảm Tiền giờ hoặc Xóa món đồ uống đã Served khi lên Hóa đơn (BR-FIN-10)
        var underbilledRows = await _db.Database.SqlQuery<UnderbilledInvoiceRow>($@"
            SELECT
                i.Id AS InvoiceId,
                i.SessionId AS SessionId,
                t.TableCode AS TableCode,
                ISNULL(u.EmployeeCode + N' — ' + u.FullName, ISNULL(u.FullName, N'Không rõ')) AS StaffDisplay,
                i.CreatedAtUtc AS CreatedAtUtc,
                ISNULL(s.PlaytimeAmount, 0) AS SessionPlaytime,
                i.TotalPlaytime AS InvoicePlaytime,
                ISNULL((
                    SELECT SUM(od.Subtotal)
                    FROM dbo.Orders o
                    JOIN dbo.OrderDetails od ON od.OrderId = o.Id
                    WHERE o.SessionId = s.Id AND o.Status = 'Served'
                ), 0) AS ServedFnbTotal,
                i.TotalFandB AS InvoiceFnb
            FROM dbo.Invoices i
            JOIN dbo.PlaySessions s ON s.Id = i.SessionId
            JOIN dbo.BilliardTables t ON t.Id = s.TableId
            LEFT JOIN dbo.AspNetUsers u ON u.Id = ISNULL(s.ClosedById, s.OpenedById)
            WHERE i.CreatedAtUtc >= {startUtc}
              AND i.CreatedAtUtc < {endUtc}
              AND (
                  i.TotalPlaytime < ISNULL(s.PlaytimeAmount, 0)
                  OR i.TotalFandB < ISNULL((
                      SELECT SUM(od.Subtotal)
                      FROM dbo.Orders o
                      JOIN dbo.OrderDetails od ON od.OrderId = o.Id
                      WHERE o.SessionId = s.Id AND o.Status = 'Served'
                  ), 0)
              )
            ").ToListAsync(ct);

        foreach (var row in underbilledRows)
        {
            var expected = Math.Max(row.SessionPlaytime, row.InvoicePlaytime) + Math.Max(row.ServedFnbTotal, row.InvoiceFnb);
            var recorded = row.InvoicePlaytime + row.InvoiceFnb;
            var loss = expected - recorded;
            if (loss <= 0) continue;

            results.Add(new LeakageAuditRow
            {
                RuleCode = "LEAK-03",
                OccurredAtVn = row.CreatedAtUtc.AddHours(7),
                LeakageType = "Sửa giảm tiền giờ / Bỏ sót đồ uống đã phục vụ",
                LocationOrRef = $"Bàn {row.TableCode} · HĐ #{row.InvoiceId} (Phiên #{row.SessionId})",
                ResponsibleStaff = row.StaffDisplay,
                ExpectedAmount = expected,
                RecordedAmount = recorded,
                LeakageAmount = loss,
                EvidenceDetail = $"Giờ thực chơi: {row.SessionPlaytime:N0} ₫ (HĐ ghi {row.InvoicePlaytime:N0} ₫); Đồ uống đã Served: {row.ServedFnbTotal:N0} ₫ (HĐ ghi {row.InvoiceFnb:N0} ₫).",
                ProblemSolvedAction = "Giải quyết gian lận thu ngân tự bớt giờ cho người quen hoặc xuất bia/bò húc thu tiền riêng ngoài hóa đơn."
            });
        }

        // [LEAK-04] Thụt két tiền mặt khi kết ca làm việc (EndingCash < ExpectedCash — BR-FIN-11)
        var shiftShortages = await _db.WorkShifts.AsNoTracking()
            .Include(w => w.Staff)
            .Where(w => w.Status == WorkShift.StatusClosed
                     && w.StartTimeUtc >= startUtc
                     && w.StartTimeUtc < endUtc
                     && w.EndingCash != null
                     && w.ExpectedCash != null
                     && w.EndingCash < w.ExpectedCash)
            .ToListAsync(ct);

        foreach (var shift in shiftShortages)
        {
            var expected = shift.ExpectedCash ?? 0m;
            var actual = shift.EndingCash ?? 0m;
            var shortage = expected - actual;
            var staffLabel = shift.Staff == null
                ? shift.StaffId
                : (string.IsNullOrEmpty(shift.Staff.EmployeeCode)
                    ? shift.Staff.FullName
                    : $"{shift.Staff.EmployeeCode} — {shift.Staff.FullName}");

            results.Add(new LeakageAuditRow
            {
                RuleCode = "LEAK-04",
                OccurredAtVn = (shift.EndTimeUtc ?? shift.StartTimeUtc).AddHours(7),
                LeakageType = "Thụt két tiền mặt khi đóng ca",
                LocationOrRef = $"Ca làm việc #{shift.Id}",
                ResponsibleStaff = staffLabel,
                ExpectedAmount = expected,
                RecordedAmount = actual,
                LeakageAmount = shortage,
                EvidenceDetail = $"Tiền mặt kỳ vọng cuối ca: {expected:N0} ₫ nhưng kiểm đếm thực tế chỉ có {actual:N0} ₫. Ghi chú ca: {shift.Notes ?? "Không có"}",
                ProblemSolvedAction = "Giải quyết thất thoát tiền mặt tại két thu ngân: Quy trách nhiệm bồi hoàn trực tiếp theo ca trực.",
                RelatedShiftId = shift.Id
            });
        }

        // [LEAK-05] Áp dụng Giảm giá khống cho hóa đơn khách lẻ không có thẻ / voucher (BR-FIN-12)
        var fakeDiscounts = await _db.Database.SqlQuery<FakeDiscountInvoiceRow>($@"
            SELECT
                i.Id AS InvoiceId,
                i.SessionId AS SessionId,
                t.TableCode AS TableCode,
                ISNULL(u.EmployeeCode + N' — ' + u.FullName, ISNULL(u.FullName, N'Không rõ')) AS StaffDisplay,
                i.CreatedAtUtc AS CreatedAtUtc,
                (i.TotalPlaytime + i.TotalFandB) AS GrossAmount,
                i.FinalAmount AS FinalAmount,
                i.DiscountAmount AS DiscountAmount
            FROM dbo.Invoices i
            JOIN dbo.PlaySessions s ON s.Id = i.SessionId
            JOIN dbo.BilliardTables t ON t.Id = s.TableId
            LEFT JOIN dbo.AspNetUsers u ON u.Id = ISNULL(s.ClosedById, s.OpenedById)
            WHERE i.CreatedAtUtc >= {startUtc}
              AND i.CreatedAtUtc < {endUtc}
              AND i.CustomerId IS NULL
              AND i.DiscountAmount > 0
            ").ToListAsync(ct);

        foreach (var row in fakeDiscounts)
        {
            results.Add(new LeakageAuditRow
            {
                RuleCode = "LEAK-05",
                OccurredAtVn = row.CreatedAtUtc.AddHours(7),
                LeakageType = "Áp giảm giá khống không có thẻ / Voucher",
                LocationOrRef = $"Bàn {row.TableCode} · HĐ #{row.InvoiceId} (Phiên #{row.SessionId})",
                ResponsibleStaff = row.StaffDisplay,
                ExpectedAmount = row.GrossAmount,
                RecordedAmount = row.FinalAmount,
                LeakageAmount = row.DiscountAmount,
                EvidenceDetail = $"Hóa đơn khách vãng lai (CustomerId = NULL) nhưng bị trừ DiscountAmount = {row.DiscountAmount:N0} ₫ không có chứng từ Loyalty.",
                ProblemSolvedAction = "Giải quyết thủ đoạn thu ngân tự tạo khuyến mãi ảo để ăn chênh lệch tiền khách thanh toán."
            });
        }

        return results.OrderByDescending(x => x.OccurredAtVn).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. SEED DỮ LIỆU TÀI CHÍNH 12 THÁNG IDEMPOTENT (UC-FIN-05, BR-FIN-13)
    // ══════════════════════════════════════════════════════════════════════════
    public async Task<int> EnsureYearFinancialDataSeededAsync(string? actorId = null, CancellationToken ct = default)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "BMS_Database_Starter", "11_Financial_12Months_Seed.sql");
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "BMS_Database_Starter", "11_Financial_12Months_Seed.sql");
        }
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "BMS_Database_Starter", "11_Financial_12Months_Seed.sql");
        }

        if (File.Exists(scriptPath))
        {
            var rawSql = await File.ReadAllTextAsync(scriptPath, ct);
            var batches = System.Text.RegularExpressions.Regex.Split(
                rawSql,
                @"^\s*GO\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var batch in batches)
            {
                var trimmed = batch.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("USE ", StringComparison.OrdinalIgnoreCase)) continue;
                await _db.Database.ExecuteSqlRawAsync(trimmed, ct);
            }
        }

        return await _db.OperatingExpenses.CountAsync(ct);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. CÁC HÀM PHỤ TRỢ PHÂN TÍCH CHI PHÍ, ĐỒ UỐNG & CHẨN ĐOÁN P&L
    // ══════════════════════════════════════════════════════════════════════════
    private static bool IsDayShiftHour(int vnHour) => vnHour >= 8 && vnHour < 18;

    private static List<ExpenseCategorySummaryRow> BuildExpenseCategories(
        List<OperatingExpense> expenses, decimal totalRevenue)
    {
        var totalExp = expenses.Sum(x => x.Amount);
        var definitions = new (string Code, string Name, string Purpose)[]
        {
            (OperatingExpense.CatPayroll,
             OperatingExpense.GetCategoryDisplayName(OperatingExpense.CatPayroll),
             "Giải quyết bài toán định biên nhân sự: Đối chiếu chi phí lương Ca Ngày vs Ca Đêm với doanh thu thực mang về để cắt giảm ca dư thừa."),
            (OperatingExpense.CatUtilities,
             OperatingExpense.GetCategoryDisplayName(OperatingExpense.CatUtilities),
             "Giải quyết bài toán kiểm soát điện năng: Theo dõi mức tăng đột biến tiền điện máy lạnh & đèn bàn vào mùa nóng (T4–T7) để tắt bớt khu vực bàn trống."),
            (OperatingExpense.CatInventoryCogs,
             OperatingExpense.GetCategoryDisplayName(OperatingExpense.CatInventoryCogs),
             "Giải quyết bài toán biên lợi nhuận F&B: Kiểm soát giá vốn nhập thùng Bia Tiger, Bò Húc, Trà Đào và phát hiện hao hụt kho nước."),
            (OperatingExpense.CatMaintenance,
             OperatingExpense.GetCategoryDisplayName(OperatingExpense.CatMaintenance),
             "Giải quyết bài toán khấu hao thiết bị: Theo dõi chi phí thay vải nỉ bàn, lơ, đầu cơ, gậy bida, bóng Aramith để tránh dồn đại tu gây lỗ sốc.")
        };

        return definitions.Select(d =>
        {
            var catItems = expenses.Where(e => e.Category == d.Code).ToList();
            var sum = catItems.Sum(e => e.Amount);
            return new ExpenseCategorySummaryRow
            {
                CategoryCode = d.Code,
                CategoryName = d.Name,
                TotalAmount = sum,
                EntryCount = catItems.Count,
                ShareOfTotalExpensePercent = totalExp > 0 ? Math.Round(sum / totalExp * 100m, 1) : 0m,
                RatioToRevenuePercent = totalRevenue > 0 ? Math.Round(sum / totalRevenue * 100m, 1) : 0m,
                ProblemSolvedPurpose = d.Purpose
            };
        }).ToList();
    }

    private static string BuildPeriodDiagnostic(FinancialPeriodPnlRow row)
    {
        if (row.CollectedRevenue == 0 && row.TotalExpense == 0)
            return "Không phát sinh giao dịch hoặc chi phí trong kỳ.";

        if (row.NetProfit < 0)
        {
            if (row.MaintenanceCost >= 10_000_000m)
                return $"LỖ ({row.NetProfit:N0} ₫): Chi phí bảo trì bàn/vải nỉ/gậy tăng đột biến ({row.MaintenanceCost:N0} ₫) + thất thoát {row.LeakageAmount:N0} ₫.";
            if (row.LeakageAmount >= 3_000_000m)
                return $"LỖ ({row.NetProfit:N0} ₫): Thất thoát doanh thu & sai lệch hóa đơn/ca làm nặng ({row.LeakageAmount:N0} ₫) trên nền doanh thu thấp.";
            return $"LỖ ({row.NetProfit:N0} ₫): Doanh thu ca ban ngày thấp nhưng vẫn gánh nguyên chi phí lương cố định ({row.PayrollCost:N0} ₫).";
        }

        if (row.IsPeakRevenue && row.IsPeakBeverage)
            return $"ĐỈNH KÉP (Lãi +{row.NetProfit:N0} ₫): Giờ chơi đạt đỉnh + Tỷ trọng bán nước/bia vọt lên {row.FnbSharePercent}% bù đắp hoàn toàn tiền điện mùa nóng.";

        if (row.IsPeakRevenue)
            return $"ĐỈNH DOANH THU (Lãi +{row.NetProfit:N0} ₫): Công suất bàn ca đêm đạt {row.NightSharePercent}% tổng tiền giờ.";

        if (row.IsPeakBeverage)
            return $"ĐỈNH BÁN NƯỚC (Lãi +{row.NetProfit:N0} ₫): Doanh thu nước & bia chiếm {row.FnbSharePercent}% tổng thu, gánh chi phí điện điều hòa ({row.UtilitiesCost:N0} ₫).";

        return $"LÃI (+{row.NetProfit:N0} ₫): Vận hành ổn định, ca đêm chiếm {row.NightSharePercent}% tiền bàn.";
    }

    private async Task<List<BeveragePerformanceRow>> QueryTopBeveragesAsync(
        DateTime startUtc, DateTime endUtc, decimal totalFnbRevenue, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQuery<BeverageSqlRow>($@"
            SELECT
                p.Id AS ProductId,
                p.Name AS ProductName,
                SUM(od.Quantity) AS TotalQty,
                SUM(od.Subtotal) AS TotalRev,
                SUM(CASE WHEN MONTH(DATEADD(hour, 7, o.OrderTimeUtc)) IN (4, 5, 6) THEN od.Subtotal ELSE 0 END) AS SummerRev
            FROM dbo.OrderDetails od
            JOIN dbo.Orders o ON o.Id = od.OrderId
            JOIN dbo.Products p ON p.Id = od.ProductId
            WHERE o.Status = 'Served'
              AND o.OrderTimeUtc >= {startUtc}
              AND o.OrderTimeUtc < {endUtc}
            GROUP BY p.Id, p.Name
            ORDER BY TotalRev DESC
            ").ToListAsync(ct);

        return rows.Select(r => new BeveragePerformanceRow
        {
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            TotalQuantitySold = r.TotalQty,
            TotalRevenue = r.TotalRev,
            ShareOfFnbPercent = totalFnbRevenue > 0 ? Math.Round(r.TotalRev / totalFnbRevenue * 100m, 1) : 0m,
            PeakSummerRevenue = r.SummerRev,
            ProblemSolvedNote = r.SummerRev > r.TotalRev * 0.35m
                ? "Mặt hàng chủ lực mùa nóng (T4–T6): Cần ưu tiên nhập sỉ giữ tồn kho cao và ghép vào gói Combo Cú Đêm."
                : "Mặt hàng tiêu thụ ổn định quanh năm: Duy trì định mức tồn kho tiêu chuẩn."
        }).ToList();
    }

    // ── Các record nhận kết quả SQL thô ──
    private sealed class InvoiceSessionRow
    {
        public int InvoiceId { get; set; }
        public int SessionId { get; set; }
        public decimal TotalPlaytime { get; set; }
        public decimal TotalFandB { get; set; }
        public decimal DiscountAmount { get; set; }
        public string InvoiceStatus { get; set; } = string.Empty;
        public DateTime InvoiceCreatedAtUtc { get; set; }
        public DateTime SessionStartAtUtc { get; set; }
    }

    private sealed class UnbilledSessionRow
    {
        public int SessionId { get; set; }
        public string TableCode { get; set; } = string.Empty;
        public string StaffDisplay { get; set; } = string.Empty;
        public DateTime ClosedAtUtc { get; set; }
        public decimal PlaytimeAmount { get; set; }
    }

    private sealed class UnpaidInvoiceRow
    {
        public int InvoiceId { get; set; }
        public int SessionId { get; set; }
        public string TableCode { get; set; } = string.Empty;
        public string StaffDisplay { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal PaidAmount { get; set; }
    }

    private sealed class UnderbilledInvoiceRow
    {
        public int InvoiceId { get; set; }
        public int SessionId { get; set; }
        public string TableCode { get; set; } = string.Empty;
        public string StaffDisplay { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public decimal SessionPlaytime { get; set; }
        public decimal InvoicePlaytime { get; set; }
        public decimal ServedFnbTotal { get; set; }
        public decimal InvoiceFnb { get; set; }
    }

    private sealed class FakeDiscountInvoiceRow
    {
        public int InvoiceId { get; set; }
        public int SessionId { get; set; }
        public string TableCode { get; set; } = string.Empty;
        public string StaffDisplay { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
    }

    private sealed class BeverageSqlRow
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int TotalQty { get; set; }
        public decimal TotalRev { get; set; }
        public decimal SummerRev { get; set; }
    }
}
