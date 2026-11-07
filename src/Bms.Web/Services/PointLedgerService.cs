using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record LedgerResult(bool Success, bool Duplicate, int BalanceAfter, int? TransactionId, string Message);

public interface IPointLedgerService
{
    /// <summary>
    /// Ghi 1 bút toán vào sổ cái điểm thưởng.
    /// Atomic: UPDATE AspNetUsers SET RewardPoints = RewardPoints + @delta WHERE Id=@id AND IsActive=1 AND RewardPoints + @delta >= 0.
    /// Rồi INSERT PointTransactions với BalanceAfter = số dư mới.
    /// Nếu vi phạm UX_PointTxn_Earn_Ref (cùng ReferenceCode + TransactionType) -> trả về Duplicate=true, không throw.
    /// </summary>
    Task<LedgerResult> PostAsync(
        string customerId,
        int delta,
        string type,
        string? referenceCode = null,
        int? invoiceId = null,
        string? reason = null,
        string? actorId = null,
        decimal? spendAmount = null,
        CancellationToken ct = default);
}

public class PointLedgerService : IPointLedgerService
{
    private readonly ApplicationDbContext _db;

    public PointLedgerService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<LedgerResult> PostAsync(
        string customerId,
        int delta,
        string type,
        string? referenceCode = null,
        int? invoiceId = null,
        string? reason = null,
        string? actorId = null,
        decimal? spendAmount = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return new LedgerResult(false, false, 0, null, "Thiếu mã khách hàng.");

        var normalizedRef = string.IsNullOrWhiteSpace(referenceCode) ? null : referenceCode.Trim();

        // 1. Kiểm tra Idempotency trước khi trừ/cộng điểm nếu có ReferenceCode
        if (normalizedRef != null)
        {
            var existingTx = await _db.PointTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReferenceCode == normalizedRef && t.TransactionType == type, ct);

            if (existingTx != null)
            {
                return new LedgerResult(true, true, existingTx.BalanceAfter, existingTx.Id,
                    $"Giao dịch điểm ({type}) với mã tham chiếu {normalizedRef} đã được xử lý trước đó.");
            }
        }

        if (type == PointTransaction.Adjust && string.IsNullOrWhiteSpace(reason))
            return new LedgerResult(false, false, 0, null, "Điều chỉnh điểm bắt buộc phải có lý do.");

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            // 2. Cập nhật nguyên tử (Atomic UPDATE) trên SQL Server để chống race condition & âm điểm
            int affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.AspNetUsers
                SET RewardPoints = RewardPoints + {delta}
                WHERE Id = {customerId}
                  AND IsActive = 1
                  AND RewardPoints + {delta} >= 0
            ", ct);

            if (affected == 0)
            {
                if (ownTx != null) await ownTx.RollbackAsync(ct);

                var snapshot = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == customerId)
                    .Select(u => new { u.IsActive, u.RewardPoints })
                    .FirstOrDefaultAsync(ct);

                if (snapshot == null)
                    return new LedgerResult(false, false, 0, null, "Không tìm thấy khách hàng.");
                if (!snapshot.IsActive)
                    return new LedgerResult(false, false, snapshot.RewardPoints, null, "Tài khoản khách hàng đang bị khóa.");
                return new LedgerResult(false, false, snapshot.RewardPoints, null,
                    $"Số dư điểm không đủ (hiện có {snapshot.RewardPoints} điểm, cần trừ {-delta} điểm).");
            }

            // Đồng bộ lại entity đang được EF tracking trong cùng DbContext (nếu có)
            var trackedUser = _db.ChangeTracker.Entries<ApplicationUser>()
                .FirstOrDefault(e => e.Entity.Id == customerId);
            if (trackedUser != null)
            {
                await trackedUser.ReloadAsync(ct);
            }

            var newBalance = await _db.Users.AsNoTracking()
                .Where(u => u.Id == customerId)
                .Select(u => u.RewardPoints)
                .FirstOrDefaultAsync(ct);

            var pt = new PointTransaction
            {
                CustomerId = customerId,
                Amount = delta,
                BalanceAfter = newBalance,
                TransactionType = type,
                ReferenceCode = normalizedRef,
                InvoiceId = invoiceId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                CreatedById = string.IsNullOrWhiteSpace(actorId) ? null : actorId,
                SpendAmount = spendAmount,
                CreatedAt = DateTime.UtcNow
            };

            _db.PointTransactions.Add(pt);
            await _db.SaveChangesAsync(ct);

            if (ownTx != null) await ownTx.CommitAsync(ct);

            return new LedgerResult(true, false, newBalance, pt.Id,
                delta >= 0 ? $"Đã cộng +{delta} điểm." : $"Đã trừ {delta} điểm.");
        }
        catch (DbUpdateException) when (normalizedRef != null)
        {
            if (ownTx != null) await ownTx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            var dup = await _db.PointTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReferenceCode == normalizedRef && t.TransactionType == type, ct);

            var bal = dup?.BalanceAfter ?? await _db.Users.AsNoTracking()
                .Where(u => u.Id == customerId).Select(u => u.RewardPoints).FirstOrDefaultAsync(ct);

            return new LedgerResult(true, true, bal, dup?.Id,
                $"Giao dịch điểm ({type}) với mã tham chiếu {normalizedRef} đã được xử lý trước đó.");
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }
}

