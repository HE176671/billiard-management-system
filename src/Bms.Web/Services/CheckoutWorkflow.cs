using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public partial class CheckoutService
{
    private Task<bool> IsActiveCashierAsync(string? actorId, CancellationToken ct) =>
        (from user in _db.Users
         join userRole in _db.UserRoles on user.Id equals userRole.UserId
         join role in _db.Roles on userRole.RoleId equals role.Id
         where user.Id == actorId && user.IsActive && (role.Name == "Staff" || role.Name == "Admin")
         select user.Id).AnyAsync(ct);

    private async Task LockCheckoutAsync(string kind, string key, CancellationToken ct)
    {
        var resource = "BMS-CHECKOUT-" + kind + "-" + key.ToUpperInvariant();
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Cannot lock checkout', 1;
            """, ct);
    }

    private async Task LockCommandAsync(CheckoutCommand command, CancellationToken ct)
    {
        await LockCheckoutAsync("REF", command.ReferenceCode, ct);
        if (command.SessionId is > 0)
            await LockCheckoutAsync("SESSION", command.SessionId.Value.ToString(), ct);
    }

    public async Task<CheckoutPaymentResult> SubmitDraftAsync(CheckoutCommand command, string paymentMethod, CancellationToken ct = default)
    {
        if (!await IsActiveCashierAsync(command.CashierId, ct))
            return new(false, false, false, "Tài khoản không có quyền thanh toán.");
        if (paymentMethod is not ("Cash" or "Wallet" or "VietQR"))
            return new(false, false, false, "Hình thức thanh toán không hợp lệ.");
        command = command with { ReferenceCode = NormalizeReference(command.ReferenceCode) };
        await using var tx = await BeginCheckoutTransactionAsync("Draft", ct);
        try
        {
            await LockCheckoutAsync("REF", command.ReferenceCode, ct);
            var draft = await _db.CheckoutDrafts.SingleOrDefaultAsync(d => d.ReferenceCode == command.ReferenceCode, ct);
            if (draft == null) return new(false, false, false, "Phiếu thanh toán không tồn tại. Hãy mở giao dịch mới.");
            await _db.Entry(draft).ReloadAsync(ct);
            if (draft.Status != CheckoutDraft.Draft)
                return new(draft.Status == CheckoutDraft.Paid, true, draft.Status == CheckoutDraft.Pending,
                    draft.Status == CheckoutDraft.Paid ? "Giao dịch đã được thanh toán." : "Phiếu đã được chốt. Hãy kiểm tra trạng thái hoặc chọn Sửa thanh toán.",
                    draft.InvoiceId, draft.FinalAmount ?? 0, ReferenceCode: draft.ReferenceCode);

            if (command.SessionId != draft.SessionId)
                return new(false, false, false, "Phiên chơi không khớp phiếu thanh toán. Hãy tải lại trang.", ReferenceCode: draft.ReferenceCode);
            command = command with { ManualAmounts = draft.IsManualEntry };

            var amounts = new[] { command.SubmittedPlaytimeAmount, command.SubmittedFandBAmount, command.CashReceived ?? 0 };
            if (amounts.Any(a => a < 0 || a > 100_000_000 || a != decimal.Truncate(a)) || command.PointsToRedeem < 0
                || (command.VoucherCode?.Trim().Length ?? 0) > 20 || command.SessionId is <= 0)
                return new(false, false, false, "Số tiền, điểm hoặc thông tin phiên chơi không hợp lệ.", ReferenceCode: draft.ReferenceCode);
            if (command.CustomerId != null && !await (from u in _db.Users
                join ur in _db.UserRoles on u.Id equals ur.UserId join r in _db.Roles on ur.RoleId equals r.Id
                where u.Id == command.CustomerId && u.IsActive && r.Name == "Customer" select u.Id).AnyAsync(ct))
                return new(false, false, false, "Hội viên không tồn tại hoặc đang bị khóa.", ReferenceCode: draft.ReferenceCode);

            draft.CustomerId = command.CustomerId;
            draft.SessionId = command.SessionId;
            draft.PlaytimeAmount = command.SubmittedPlaytimeAmount;
            draft.FandBAmount = command.SubmittedFandBAmount;
            draft.PointsToRedeem = command.PointsToRedeem;
            draft.VoucherCode = NormalizeOptional(command.VoucherCode);
            draft.CashReceived = command.CashReceived;
            draft.PaymentMethod = paymentMethod;
            await _db.SaveChangesAsync(ct);

            var result = paymentMethod switch
            {
                "Cash" => await CompleteCashAsync(command, ct),
                "Wallet" => await CompleteWalletAsync(command, ct),
                _ => await CreateTransferIntentAsync(command, ct)
            };
            if (!result.Success)
            {
                // The payment operation rolls back its savepoint. Keep only the
                // editable draft inputs so a validation retry does not lose them.
                await tx.CommitAsync(ct);
                _db.ChangeTracker.Clear();
                return result;
            }
            if (result.Pending)
            {
                var intent = await _db.PaymentIntents.AsNoTracking().SingleAsync(i => i.ReferenceCode == draft.ReferenceCode, ct);
                draft.Status = CheckoutDraft.Pending;
                draft.InvoiceId = intent.InvoiceId;
                draft.SessionId = intent.SessionId;
                draft.PlaytimeAmount = intent.PlaytimeAmount;
                draft.FandBAmount = intent.FandBAmount;
                draft.FinalAmount = intent.ExpectedAmount;
                await _db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Không thể xử lý phiếu {Reference}", command.ReferenceCode);
            return new(false, false, false, "Chưa ghi nhận thanh toán. Hãy kiểm tra trạng thái trước khi thử lại.", ReferenceCode: command.ReferenceCode);
        }
    }

    public async Task<CheckoutPaymentResult> ConfirmTransferManuallyAsync(string referenceCode, string actorId, CancellationToken ct = default)
    {
        if (!await IsActiveCashierAsync(actorId, ct))
            return new(false, false, false, "Tài khoản không có quyền xác nhận chuyển khoản.");
        var reference = NormalizeReference(referenceCode);
        return await ConfirmTransferCoreAsync(reference, "MANUAL-" + reference, null, actorId, ct);
    }

    public async Task<CheckoutPaymentResult> ReopenTransferAsync(string referenceCode, string actorId, CancellationToken ct = default)
    {
        if (!await IsActiveCashierAsync(actorId, ct))
            return new(false, false, false, "Tài khoản không có quyền sửa thanh toán.");
        var reference = NormalizeReference(referenceCode);
        await using var tx = await BeginCheckoutTransactionAsync("Reopen", ct);
        try
        {
            await LockCheckoutAsync("REF", reference, ct);
            var draft = await _db.CheckoutDrafts.SingleOrDefaultAsync(d => d.ReferenceCode == reference, ct);
            if (draft != null)
            {
                await _db.Entry(draft).ReloadAsync(ct);
                if (draft.Status == CheckoutDraft.Superseded)
                    return new(true, true, false, "Phiếu đã được mở lại để sửa.", ReferenceCode: draft.ReplacementReference);
            }
            var intent = await _db.PaymentIntents.SingleOrDefaultAsync(i => i.ReferenceCode == reference, ct);
            if (intent == null) return new(false, false, false, "Không tìm thấy yêu cầu chuyển khoản.", ReferenceCode: reference);
            await _db.Entry(intent).ReloadAsync(ct);
            if (intent.SessionId is > 0)
                await LockCheckoutAsync("SESSION", intent.SessionId.Value.ToString(), ct);
            await LockCheckoutAsync("INVOICE", intent.InvoiceId.ToString(), ct);
            if (intent.Status == PaymentIntent.StatusConfirmed || await _db.PaymentTransactions.AnyAsync(p => p.InvoiceId == intent.InvoiceId, ct))
                return new(false, false, false, "Hóa đơn đã thanh toán, không thể sửa.", intent.InvoiceId, ReferenceCode: reference);
            if (intent.Status is not (PaymentIntent.StatusPending or PaymentIntent.StatusExpired))
                return new(false, false, false, "Yêu cầu này đã ngừng sử dụng.", ReferenceCode: reference);

            var replacement = new CheckoutDraft
            {
                CreatedById = actorId, CustomerId = intent.CustomerId, SessionId = intent.SessionId,
                IsManualEntry = draft?.IsManualEntry ?? false,
                InvoiceId = intent.InvoiceId, PlaytimeAmount = intent.PlaytimeAmount, FandBAmount = intent.FandBAmount,
                PointsToRedeem = intent.PointsToRedeem, VoucherCode = intent.VoucherCode
            };
            _db.CheckoutDrafts.Add(replacement);
            intent.Status = PaymentIntent.StatusFailed;
            if (draft != null)
            {
                draft.Status = CheckoutDraft.Superseded;
                draft.ReplacementReference = replacement.ReferenceCode;
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(true, false, false, "Đã ngừng yêu cầu QR cũ. Kiểm tra thông tin rồi thanh toán lại.", ReferenceCode: replacement.ReferenceCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Không thể sửa phiếu {Reference}", reference);
            return new(false, false, false, "Chưa sửa được phiếu. Hãy kiểm tra trạng thái và thử lại.", ReferenceCode: reference);
        }
    }

    private async Task FinishDraftAsync(CheckoutCommand command, PreparedInvoice prepared, decimal amount, string method, CancellationToken ct)
    {
        var draft = await _db.CheckoutDrafts.SingleOrDefaultAsync(d => d.ReferenceCode == command.ReferenceCode, ct);
        if (draft == null) return; // Other checkout entry points and older requests remain supported.
        draft.Status = CheckoutDraft.Paid;
        draft.CustomerId = command.CustomerId;
        draft.SessionId = prepared.SessionId;
        draft.InvoiceId = prepared.InvoiceId;
        draft.PlaytimeAmount = prepared.PlaytimeAmount;
        draft.FandBAmount = prepared.FandBAmount;
        draft.FinalAmount = amount;
        draft.PaymentMethod = method == "Transfer" ? "VietQR" : method;
        draft.PaidAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
