using System.Data;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record WalletResult(bool Success, bool Duplicate, string Message, string? ReferenceCode = null, bool Pending = false);

public interface IWalletService
{
    Task<WalletResult> CreateTransferIntentAsync(string customerId, decimal amount, string requestId, string actorId, CancellationToken ct = default);
    Task<WalletResult> ConfirmTransferAsync(string referenceCode, string providerTransactionId, decimal receivedAmount, CancellationToken ct = default);
    Task<WalletResult> ConfirmTransferManuallyAsync(string referenceCode, decimal receivedAmount, string actorId, CancellationToken ct = default);
    Task<WalletResult> TopUpAsync(string customerId, decimal amount, string referenceCode, string actorId, CancellationToken ct = default);
}

public partial class WalletService(ApplicationDbContext db, IPointLedgerService ledger,
    ITierEvaluationService tiers, ICustomerSegmentService segments) : IWalletService
{
    public async Task<WalletResult> TopUpAsync(string customerId, decimal amount, string referenceCode,
        string actorId, CancellationToken ct = default)
    {
        if (amount < 1000 || amount > 100_000_000 || amount != decimal.Truncate(amount))
            return new(false, false, "Số tiền nạp phải là số nguyên từ 1.000đ đến 100.000.000đ.");
        if (!Guid.TryParseExact(referenceCode, "N", out var requestId))
            return new(false, false, "Mã yêu cầu nạp không hợp lệ. Vui lòng tải lại trang.");
        var reference = "NAP-" + requestId.ToString("N");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Khóa khách trước mọi thay đổi số dư/điểm; chống hai thu ngân cùng cập nhật.
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={customerId}")
                .SingleOrDefaultAsync(ct);
            var customerRole = await db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (user == null || !user.IsActive || !await db.UserRoles.AnyAsync(r => r.UserId == customerId && r.RoleId == customerRole, ct))
                return new(false, false, "Không tìm thấy hội viên đang hoạt động.");
            var prior = await db.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(w => w.ReferenceCode == reference, ct);
            if (prior != null)
                return prior.CustomerId == customerId && prior.Amount == amount
                    ? new(true, true, "Phiếu nạp đã được ghi nhận; không cộng lại tiền hoặc điểm.")
                    : new(false, false, "Mã yêu cầu đã được sử dụng cho giao dịch khác.");
            var shift = await db.WorkShifts.FromSqlInterpolated($"SELECT * FROM dbo.WorkShifts WITH (UPDLOCK,HOLDLOCK) WHERE StaffId={actorId} AND Status='Active'")
                .SingleOrDefaultAsync(ct);
            // TV5: ca làm đã bỏ khỏi nghiệp vụ; nếu còn ca cũ đang mở thì vẫn gắn, không thì để trống.
            var result = await CreditAsync(user, amount, reference, actorId, shift?.Id, null, ct);
            await tx.CommitAsync(ct);
            segments.InvalidateCache();
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<WalletResult> CreditAsync(ApplicationUser user, decimal amount, string reference,
        string actorId, int? shiftId, string? providerTransactionId, CancellationToken ct)
    {
        var customerId = user.Id;
        var tier = await db.MembershipTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == user.TierId, ct);
        if (tier == null)
        {
            var configured = await db.MembershipTiers.AsNoTracking().ToListAsync(ct);
            tier = TierHelper.Resolve(configured, await tiers.GetSpendLast12MonthsAsync(customerId, ct: ct));
        }
        var points = LoyaltyPolicy.EarnPoints(amount, tier?.PointMultiplier ?? 1);
        var posted = await ledger.PostAsync(customerId, points, PointTransaction.Earn, reference,
            reason: "Tích điểm nạp tiền trả trước", actorId: actorId, spendAmount: amount, ct: ct);
        if (!posted.Success || posted.Duplicate) throw new InvalidOperationException("Không thể ghi sổ điểm cho phiếu nạp.");
        await db.Entry(user).ReloadAsync(ct);
        user.WalletBalance += amount;
        user.TotalSpend += amount;
        user.LastPaidDate = DateTime.UtcNow;
        db.WalletTransactions.Add(new WalletTransaction
        {
            CustomerId = customerId, Amount = amount, BalanceAfter = user.WalletBalance,
            TransactionType = WalletTransaction.TopUp, ReferenceCode = reference,
            CreatedById = actorId, WorkShiftId = shiftId, PaymentMethod = providerTransactionId == null ? "Cash" : "Transfer",
            ProviderTransactionId = providerTransactionId
        });
        var spend = await tiers.GetSpendLast12MonthsAsync(customerId, ct: ct);
        var result = await tiers.EvaluateUpgradeOnCheckoutAsync(user, spend, actorId, ct);
        db.Notifications.Add(new Notification
        {
            UserId = customerId, Title = "Nạp tiền thành công", Type = "WalletTopUp",
            Content = $"Đã nạp {VnFormat.Money(amount)}, cộng {points} điểm. Số dư trả trước: {VnFormat.Money(user.WalletBalance)}. Hạng: {result.CurrentTier?.TierName}.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new(true, false, $"Đã nạp {VnFormat.Money(amount)}, cộng {points} điểm. Hạng hiện tại: {result.CurrentTier?.TierName}. Mã phiếu: {reference}.");
    }
}
