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
/// BR-23: cả quán chỉ có MỘT ca đang mở (một két tiền). Mọi nhân viên trực đều thao tác trên ca đó.
/// TV3 khi ghi nhận thanh toán: mở transaction → <see cref="RequireActiveShiftIdForCashAsync"/>
/// → insert PaymentTransactions với WorkShiftId + ReceivedById → commit.
/// </summary>
public interface IShiftService
{
    /// <summary>Ca đang mở của cả quán, hoặc null.</summary>
    Task<WorkShift?> GetStoreActiveShiftAsync(CancellationToken ct = default);

    /// <summary>
    /// Giữ lại để tương thích code cũ. Theo BR-23 trả về ca đang mở của cả quán,
    /// không phụ thuộc <paramref name="staffId"/>.
    /// </summary>
    Task<WorkShift?> GetActiveShiftAsync(string staffId, CancellationToken ct = default);

    /// <summary>
    /// Khóa (UPDLOCK) ca đang mở của quán và trả về Id để gắn vào giao dịch thanh toán.
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
    private readonly IAuditLogService _audit;

    public ShiftService(ApplicationDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    private static string AlreadyOpenMessage(WorkShift current) =>
        $"Quán đang có ca #{current.Id} do {current.Staff?.FullName ?? "nhân viên khác"} mở lúc " +
        $"{current.StartTimeUtc.ToVn():HH:mm dd/MM}. Mỗi lúc chỉ được một ca (BR-23), hãy đóng ca đó trước.";

    public Task<WorkShift?> GetStoreActiveShiftAsync(CancellationToken ct = default) =>
        _db.WorkShifts.AsNoTracking()
            .Include(s => s.Staff)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Status == WorkShift.StatusActive, ct);

    public Task<WorkShift?> GetActiveShiftAsync(string staffId, CancellationToken ct = default) =>
        GetStoreActiveShiftAsync(ct);

    public async Task<int> RequireActiveShiftIdForCashAsync(string staffId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                "RequireActiveShiftIdForCashAsync phải được gọi trong transaction đang mở của bên ghi giao dịch.");

        var ids = await _db.Database.SqlQuery<int>($@"
            SELECT Id AS Value FROM dbo.WorkShifts WITH (UPDLOCK, HOLDLOCK)
            WHERE Status = 'Active'").ToListAsync(ct);
        if (ids.Count == 0)
            throw new ShiftException("NO_ACTIVE_SHIFT", "Quán chưa mở ca. Hãy mở ca trước khi thu tiền.");
        return ids.Min();
    }

    public async Task<WorkShift> OpenShiftAsync(string staffId, decimal startingCash, string? notes, CancellationToken ct = default)
    {
        if (startingCash < 0 || startingCash > MaxCash)
            throw new ShiftException("INVALID_CASH", "Tiền đầu ca phải từ 0 đến 1.000.000.000 VND.");

        // BR-23: cả quán chỉ một ca đang mở.
        var current = await GetStoreActiveShiftAsync(ct);
        if (current != null)
            throw new ShiftException("ALREADY_OPEN", AlreadyOpenMessage(current));

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
            // UX_WorkShifts_SingleActive chặn hai request mở ca cùng lúc.
            _db.Entry(shift).State = EntityState.Detached;
            var other = await GetStoreActiveShiftAsync(ct);
            throw new ShiftException("ALREADY_OPEN",
                other != null ? AlreadyOpenMessage(other) : "Quán đang có một ca chưa đóng. Hãy đóng ca đó trước.");
        }

        await _audit.LogAsync(AuditActions.ShiftOpen, "WorkShift", shift.Id.ToString(),
            $"Mở ca #{shift.Id} với {shift.StartingCash:N0} VND đầu ca.",
            newValue: shift.StartingCash.ToString("0"), reason: shift.Notes, actorId: staffId, ct: ct);
        return shift;
    }

    public async Task<ShiftSummary> CloseShiftAsync(int shiftId, string closedById, decimal endingCash, string? notes, CancellationToken ct = default)
    {
        if (endingCash < 0 || endingCash > MaxCash)
            throw new ShiftException("INVALID_CASH", "Tiền đếm thực tế phải từ 0 đến 1.000.000.000 VND.");

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
                    $"Tiền thực tế lệch {(endingCash - expected):N0} VND so với kỳ vọng {expected:N0} VND. Vui lòng ghi lý do.");

            shift.EndTimeUtc    = TruncateToSecond(DateTime.UtcNow);
            shift.EndingCash    = decimal.Round(endingCash, 2);
            shift.CashCollected = cash;
            shift.ExpectedCash  = expected;
            shift.ClosedById    = closedById;
            shift.Status        = WorkShift.StatusClosed;
            if (cleanNotes != null)
                shift.Notes = shift.Notes == null ? cleanNotes : Truncate(shift.Notes + " | Đóng ca: " + cleanNotes);

            // Nhật ký lưu cùng transaction với việc đóng ca.
            var diff = shift.EndingCash.Value - expected;
            await _audit.AddAsync(AuditActions.ShiftClose, "WorkShift", shiftId.ToString(),
                $"Đóng ca #{shiftId}: kỳ vọng {expected:N0} VND, đếm thực tế {shift.EndingCash:N0} VND.",
                oldValue: expected.ToString("0"), newValue: shift.EndingCash.Value.ToString("0"),
                reason: cleanNotes, actorId: closedById, ct: ct);
            if (diff != 0)
                await _audit.AddAsync(AuditActions.ShiftCashDiff, "WorkShift", shiftId.ToString(),
                    $"Ca #{shiftId} {(diff > 0 ? "thừa" : "thiếu")} {Math.Abs(diff):N0} VND tiền mặt.",
                    oldValue: expected.ToString("0"), newValue: shift.EndingCash.Value.ToString("0"),
                    reason: cleanNotes, actorId: closedById, ct: ct);
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
