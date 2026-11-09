using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public partial class CheckoutService
{
    public async Task<CheckoutPaymentResult> SwitchTransferToCashAsync(string referenceCode, decimal cashReceived,
        string reason, string actorId, CancellationToken ct = default)
    {
        if (!await IsActiveCashierAsync(actorId, ct)) return new(false, false, false, "Tài khoản không có quyền thu tiền.");
        reason = (reason ?? "").Trim();
        if (cashReceived <= 0 || cashReceived > 100_000_000 || cashReceived != decimal.Truncate(cashReceived) || reason.Length is < 5 or > 250)
            return new(false, false, false, "Nhập số tiền nguyên hợp lệ và lý do đổi phương thức (5–250 ký tự).");
        var reference = NormalizeReference(referenceCode);
        await using var tx = await BeginCheckoutTransactionAsync("SwitchCash", ct);
        try
        {
            await LockCheckoutAsync("REF", reference, ct);
            var intent = await _db.PaymentIntents.SingleOrDefaultAsync(i => i.ReferenceCode == reference, ct);
            if (intent == null) return new(false, false, false, "Không tìm thấy yêu cầu QR.");
            await _db.Entry(intent).ReloadAsync(ct);
            if (intent.SessionId is > 0) await LockCheckoutAsync("SESSION", intent.SessionId.Value.ToString(), ct);
            await LockCheckoutAsync("INVOICE", intent.InvoiceId.ToString(), ct);
            var prior = await _db.PaymentMethodChanges.AsNoTracking().SingleOrDefaultAsync(c => c.ReferenceCode == reference, ct);
            if (prior != null)
                return new(true, true, false, "Đã ghi nhận tiền mặt trước đó. Không thu thêm tiền.", prior.InvoiceId,
                    prior.Amount, prior.CashReceived - prior.Amount, reference);
            if (intent.Status == PaymentIntent.StatusConfirmed || await _db.PaymentTransactions.AnyAsync(p => p.InvoiceId == intent.InvoiceId, ct))
                return new(false, false, false, "Hóa đơn đã được thanh toán. Không thu thêm tiền mặt.", intent.InvoiceId, ReferenceCode: reference);
            if (intent.Status is not (PaymentIntent.StatusPending or PaymentIntent.StatusExpired))
                return new(false, false, false, "Yêu cầu QR đã ngừng sử dụng. Tải lại trạng thái hóa đơn.", ReferenceCode: reference);
            if (cashReceived < intent.ExpectedAmount)
                return new(false, false, true, "Tiền khách đưa chưa đủ. Yêu cầu QR vẫn được giữ nguyên.", intent.InvoiceId, intent.ExpectedAmount, ReferenceCode: reference);
            if (!await _db.WorkShifts.AnyAsync(s => s.StaffId == actorId && s.Status == WorkShift.StatusActive, ct))
                return new(false, false, true, "Bạn cần mở ca trước khi ghi nhận tiền mặt.", intent.InvoiceId, intent.ExpectedAmount, ReferenceCode: reference);

            var draft = await _db.CheckoutDrafts.SingleOrDefaultAsync(d => d.ReferenceCode == reference, ct);
            if (draft == null)
            {
                draft = new CheckoutDraft { ReferenceCode = reference, CreatedById = intent.CashierId ?? actorId,
                    CustomerId = intent.CustomerId, SessionId = intent.SessionId, InvoiceId = intent.InvoiceId,
                    PlaytimeAmount = intent.PlaytimeAmount, FandBAmount = intent.FandBAmount,
                    PointsToRedeem = intent.PointsToRedeem, VoucherCode = intent.VoucherCode };
                _db.CheckoutDrafts.Add(draft);
            }
            else await _db.Entry(draft).ReloadAsync(ct);
            draft.CashReceived = cashReceived;
            await _db.SaveChangesAsync(ct);
            // Use the original QR amounts, never the live session clock or posted prices.
            var command = new CheckoutCommand(intent.CustomerId, reference, intent.SessionId,
                intent.PlaytimeAmount, intent.FandBAmount, intent.PointsToRedeem, intent.VoucherCode, actorId, cashReceived);
            var prepared = new PreparedInvoice(true, intent.InvoiceId, intent.SessionId ?? 0, intent.PlaytimeAmount, intent.FandBAmount, null);
            var result = await SettleAsync(command, prepared, intent.ExpectedAmount, "Cash", reference, true, ct, lockedExpectedAmount: intent.ExpectedAmount);
            if (!result.Success)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return result;
            }
            intent.Status = PaymentIntent.StatusCancelled;
            _db.PaymentMethodChanges.Add(new PaymentMethodChange { InvoiceId = intent.InvoiceId, ReferenceCode = reference,
                Amount = intent.ExpectedAmount, CashReceived = cashReceived, Reason = reason, ChangedById = actorId });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result with { ChangeAmount = cashReceived - intent.ExpectedAmount,
                Message = "Đã thu tiền mặt và ngừng yêu cầu QR cũ. Hóa đơn được ghi nhận một lần." };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Không thể đổi QR sang tiền mặt. Reference={Reference}", reference);
            return new(false, false, false, "Chưa ghi nhận đổi sang tiền mặt. Kiểm tra trạng thái trước khi thử lại.", ReferenceCode: reference);
        }
    }
}
