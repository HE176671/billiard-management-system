using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Lỗi nghiệp vụ ca làm; Message là tiếng Việt, hiển thị thẳng cho người dùng.</summary>
public class ShiftException : Exception
{
    public string Code { get; }
    public ShiftException(string code, string message) : base(message) => Code = code;
}

/// <summary>Số liệu đối soát của một ca. Ca đang mở: tính trực tiếp; ca đã đóng: dùng snapshot.</summary>
public record ShiftSummary(
    WorkShift Shift,
    string    StaffName,
    string?   ClosedByName,
    decimal   CashCollected,
    int       CashCount,
    decimal   NonCashCollected,
    int       NonCashCount)
{
    public decimal  ExpectedCash => Shift.ExpectedCash ?? Shift.StartingCash + CashCollected;
    public decimal? Difference   => Shift.EndingCash - ExpectedCash;
    public bool     IsActive     => Shift.Status == WorkShift.StatusActive;
}

/// <summary>
/// Hợp đồng ca làm TV5 cung cấp cho cả nhóm.
/// TV3 khi ghi nhận tiền mặt: mở transaction → <see cref="RequireActiveShiftIdForCashAsync"/>
/// → insert PaymentTransactions với WorkShiftId + ReceivedById → commit.
/// VNPay/IPN không gọi hàm này và để WorkShiftId = NULL.
/// </summary>
public interface IShiftService
{
    /// <summary>Ca đang mở của nhân viên, hoặc null.</summary>
    Task<WorkShift?> GetActiveShiftAsync(string staffId, CancellationToken ct = default);

    /// <summary>
    /// Khóa (UPDLOCK) ca đang mở của nhân viên và trả về Id để gắn vào giao dịch tiền mặt.
    /// Bắt buộc gọi bên trong transaction của bên gọi để đóng ca không "lọt" giao dịch.
    /// Ném <see cref="ShiftException"/> mã NO_ACTIVE_SHIFT nếu chưa mở ca.
    /// </summary>
    Task<int> RequireActiveShiftIdForCashAsync(string staffId, CancellationToken ct = default);

    Task<WorkShift> OpenShiftAsync(string staffId, decimal startingCash, string? notes, CancellationToken ct = default);

    /// <summary>Đóng ca, chốt snapshot tiền mặt. Lệch tiền thì bắt buộc có ghi chú.</summary>
    Task<ShiftSummary> CloseShiftAsync(int shiftId, string closedById, decimal endingCash, string? notes, CancellationToken ct = default);

    Task<ShiftSummary?> GetSummaryAsync(int shiftId, CancellationToken ct = default);
}

public class ShiftService : IShiftService
{
    public const decimal MaxCash = 1_000_000_000m;
    private readonly ApplicationDbContext _db;
    public ShiftService(ApplicationDbContext db) => _db = db;

    public Task<WorkShift?> GetActiveShiftAsync(string staffId, CancellationToken ct = default) =>
        _db.WorkShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Status == WorkShift.StatusActive, ct);

    public async Task<int> RequireActiveShiftIdForCashAsync(string staffId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                "RequireActiveShiftIdForCashAsync phải được gọi trong transaction đang mở của bên ghi giao dịch.");

        var ids = await _db.Database.SqlQuery<int>($@"
            SELECT Id AS Value FROM dbo.WorkShifts WITH (UPDLOCK, HOLDLOCK)
            WHERE StaffId = {staffId} AND Status = 'Active'").ToListAsync(ct);
        if (ids.Count == 0)
            throw new ShiftException("NO_ACTIVE_SHIFT", "Bạn chưa mở ca. Hãy mở ca trước khi thu tiền mặt.");
        return ids[0];
    }

    public async Task<WorkShift> OpenShiftAsync(string staffId, decimal startingCash, string? notes, CancellationToken ct = default)
    {
        if (startingCash < 0 || startingCash > MaxCash)
            throw new ShiftException("INVALID_CASH", "Tiền đầu ca phải từ 0 đến 1.000.000.000 ₫.");

        if (await _db.WorkShifts.AnyAsync(s => s.StaffId == staffId && s.Status == WorkShift.StatusActive, ct))
            throw new ShiftException("ALREADY_OPEN", "Bạn đang có một ca chưa đóng. Hãy đóng ca đó trước.");

        var shift = new WorkShift
        {
            StaffId      = staffId,
            StartTimeUtc = TruncateToSecond(DateTime.UtcNow),
            StartingCash = decimal.Round(startingCash, 2),
            Status       = WorkShift.StatusActive,
            Notes        = Clean(notes)
        };
        _db.WorkShifts.Add(shift);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // UX_WorkShifts_ActiveStaff chặn hai request mở ca cùng lúc.
            _db.Entry(shift).State = EntityState.Detached;
            throw new ShiftException("ALREADY_OPEN", "Bạn đang có một ca chưa đóng. Hãy đóng ca đó trước.");
        }
        return shift;
    }

    public async Task<ShiftSummary> CloseShiftAsync(int shiftId, string closedById, decimal endingCash, string? notes, CancellationToken ct = default)
    {
        if (endingCash < 0 || endingCash > MaxCash)
            throw new ShiftException("INVALID_CASH", "Tiền đếm thực tế phải từ 0 đến 1.000.000.000 ₫.");

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            // Cùng khóa với RequireActiveShiftIdForCashAsync: giao dịch tiền mặt đang ghi
            // phải xong trước, giao dịch đến sau sẽ thấy ca đã Closed.
            var locked = await _db.Database.SqlQuery<int>($@"
                SELECT Id AS Value FROM dbo.WorkShifts WITH (UPDLOCK, HOLDLOCK) WHERE Id = {shiftId}")
                .ToListAsync(ct);
            if (locked.Count == 0)
                throw new ShiftException("NOT_FOUND", "Không tìm thấy ca làm.");

            var shift = await _db.WorkShifts.SingleAsync(s => s.Id == shiftId, ct);
            await _db.Entry(shift).ReloadAsync(ct);
            if (shift.Status != WorkShift.StatusActive)
                throw new ShiftException("ALREADY_CLOSED", "Ca này đã được đóng trước đó.");

            var (cash, _, _, _) = await SumPaymentsAsync(shiftId, ct);
            var expected = shift.StartingCash + cash;
            var cleanNotes = Clean(notes);
            if (decimal.Round(endingCash, 2) != expected && cleanNotes == null)
                throw new ShiftException("NOTE_REQUIRED",
                    $"Tiền thực tế lệch {(endingCash - expected):N0} ₫ so với kỳ vọng {expected:N0} ₫. Vui lòng ghi lý do.");

            shift.EndTimeUtc    = TruncateToSecond(DateTime.UtcNow);
            shift.EndingCash    = decimal.Round(endingCash, 2);
            shift.CashCollected = cash;
            shift.ExpectedCash  = expected;
            shift.ClosedById    = closedById;
            shift.Status        = WorkShift.StatusClosed;
            if (cleanNotes != null)
                shift.Notes = shift.Notes == null ? cleanNotes : Truncate(shift.Notes + " | Đóng ca: " + cleanNotes);
            await _db.SaveChangesAsync(ct);

            if (tx != null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
        return (await GetSummaryAsync(shiftId, ct))!;
    }

    public async Task<ShiftSummary?> GetSummaryAsync(int shiftId, CancellationToken ct = default)
    {
        var row = await _db.WorkShifts.AsNoTracking().Where(s => s.Id == shiftId)
            .Select(s => new
            {
                Shift = s,
                StaffName = s.Staff == null ? "" : s.Staff.FullName,
                ClosedByName = s.ClosedBy == null ? null : s.ClosedBy.FullName
            })
            .SingleOrDefaultAsync(ct);
        if (row == null) return null;

        var (cash, cashCount, nonCash, nonCashCount) = await SumPaymentsAsync(shiftId, ct);
        // Ca đã đóng: tiền mặt đối soát dùng snapshot, không đổi nếu có ai sửa dữ liệu sau này.
        if (row.Shift.CashCollected.HasValue) cash = row.Shift.CashCollected.Value;
        return new ShiftSummary(row.Shift, row.StaffName, row.ClosedByName, cash, cashCount, nonCash, nonCashCount);
    }

    private async Task<(decimal Cash, int CashCount, decimal NonCash, int NonCashCount)> SumPaymentsAsync(int shiftId, CancellationToken ct)
    {
        var groups = await _db.PaymentTransactions.AsNoTracking()
            .Where(p => p.WorkShiftId == shiftId)
            .GroupBy(p => p.PaymentMethod == PaymentTransaction.MethodCash)
            .Select(g => new { IsCash = g.Key, Total = g.Sum(p => p.AmountPaid), Count = g.Count() })
            .ToListAsync(ct);
        var cash = groups.FirstOrDefault(g => g.IsCash);
        var other = groups.FirstOrDefault(g => !g.IsCash);
        return (cash?.Total ?? 0, cash?.Count ?? 0, other?.Total ?? 0, other?.Count ?? 0);
    }

    private static DateTime TruncateToSecond(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Kind);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : Truncate(s.Trim());
    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
