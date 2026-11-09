using System.Data;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public partial class WalletService
{
    public async Task<WalletResult> CreateTransferIntentAsync(string customerId, decimal amount,
        string requestId, string actorId, CancellationToken ct = default)
    {
        if (amount < 1000 || amount > 100_000_000 || amount != decimal.Truncate(amount))
            return new(false, false, "Số tiền nạp phải là số nguyên từ 1.000đ đến 100.000.000đ.");
        if (!Guid.TryParseExact(requestId, "N", out var id))
            return new(false, false, "Mã yêu cầu không hợp lệ. Vui lòng tải lại trang.");
        var reference = WalletTopUpIntent.BuildReference(id);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={customerId}").SingleOrDefaultAsync(ct);
        var role = await db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (user == null || !user.IsActive || !await db.UserRoles.AnyAsync(r => r.UserId == customerId && r.RoleId == role, ct))
            return new(false, false, "Không tìm thấy hội viên đang hoạt động.");
        var existing = await db.WalletTopUpIntents.SingleOrDefaultAsync(i => i.ReferenceCode == reference, ct);
        if (existing != null)
        {
            if (existing.CustomerId != customerId || existing.ExpectedAmount != amount)
                return new(false, false, "Mã yêu cầu đã được dùng cho giao dịch khác.");
            var pending = existing.Status is PaymentIntent.StatusPending or PaymentIntent.StatusExpired;
            return new(pending || existing.Status == PaymentIntent.StatusConfirmed, true,
                pending ? "Yêu cầu đang chờ xác nhận đã nhận tiền." : existing.Status == PaymentIntent.StatusConfirmed ? "Giao dịch đã được ghi nhận." : "Yêu cầu đã ngừng sử dụng.", reference, pending);
        }
        var now = DateTime.UtcNow;
        db.WalletTopUpIntents.Add(new WalletTopUpIntent
        {
            CustomerId = customerId, CreatedById = actorId, ReferenceCode = reference,
            ExpectedAmount = amount, CreatedAtUtc = now, ExpiresAtUtc = null
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, false, "Đã tạo mã QR chuyển khoản. Yêu cầu nạp đang chờ xác nhận; chưa cộng số dư, điểm hoặc nâng hạng.", reference, true);
    }

    public Task<WalletResult> ConfirmTransferAsync(string referenceCode, string providerTransactionId,
        decimal receivedAmount, CancellationToken ct = default) =>
        ConfirmTransferCoreAsync(referenceCode, providerTransactionId, receivedAmount, null, ct);

    private async Task<WalletResult> ConfirmTransferCoreAsync(string referenceCode, string providerTransactionId,
        decimal receivedAmount, string? confirmedById, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await BankTransactionGuard.LockAsync(db, providerTransactionId, ct);
            var reference = (referenceCode ?? "").Trim().ToUpperInvariant();
            var intent = await db.WalletTopUpIntents.FromSqlInterpolated($"SELECT * FROM dbo.WalletTopUpIntents WITH (UPDLOCK,HOLDLOCK) WHERE ReferenceCode={reference}").SingleOrDefaultAsync(ct);
            if (intent == null) return new(false, false, "Không tìm thấy yêu cầu nạp ví.");
            await db.Entry(intent).ReloadAsync(ct);
            if (intent.Status == PaymentIntent.StatusConfirmed)
                return (intent.ProviderTransactionId == providerTransactionId || confirmedById != null) && receivedAmount == intent.ExpectedAmount
                    ? new(true, true, "Giao dịch nạp đã được ghi nhận, không cộng lại tiền/điểm.", reference)
                    : new(false, false, "Yêu cầu đã được thanh toán bằng một giao dịch khác.");
            // Legacy Expired requests also remain available for staff confirmation.
            if (intent.Status != PaymentIntent.StatusPending && intent.Status != PaymentIntent.StatusExpired)
                return new(false, false, "Yêu cầu nạp không ở trạng thái có thể xác nhận.");
            if (receivedAmount != intent.ExpectedAmount)
                return new(false, false, "Số tiền chuyển khoản không khớp yêu cầu nạp. Chưa cộng tiền vào ví.");
            if (await db.PaymentIntents.AnyAsync(p => p.ProviderTransactionId == providerTransactionId, ct) ||
                await db.PaymentReconciliations.AnyAsync(p => p.ProviderTransactionId == providerTransactionId, ct) ||
                await db.WalletTopUpIntents.AnyAsync(p => p.ProviderTransactionId == providerTransactionId, ct))
                return new(false, false, "Mã giao dịch ngân hàng đã được sử dụng.");
            if (confirmedById == null && intent.RequiresStaffConfirmation)
                return new(true, false, "Chờ nhân viên xác nhận đã nhận tiền.", reference, true);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={intent.CustomerId}").SingleOrDefaultAsync(ct);
            if (user == null || !user.IsActive) return new(false, false, "Hội viên không còn hoạt động. Cần đối soát giao dịch.");
            var credit = await CreditAsync(user, intent.ExpectedAmount, reference, confirmedById ?? intent.CreatedById, null, providerTransactionId, ct);
            intent.Status = PaymentIntent.StatusConfirmed;
            intent.ProviderTransactionId = providerTransactionId;
            intent.ConfirmationMethod = confirmedById == null ? "SePay" : "Manual";
            intent.ConfirmedById = confirmedById;
            intent.ConfirmedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            segments.InvalidateCache();
            return credit with { ReferenceCode = reference };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
