using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public partial class CheckoutService
{
    // Protect historical bank IDs; new requests wait for staff confirmation.
    private async Task<CheckoutPaymentResult?> CheckBankNotificationAsync(PaymentIntent intent, string providerId, decimal amount, CancellationToken ct)
    {
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > 9_999_999_999_999_999m)
            return new(false, false, false, "Số tiền ngân hàng không hợp lệ.", ReferenceCode: intent.ReferenceCode);
        var previousReview = await _db.PaymentReconciliations.AsNoTracking().SingleOrDefaultAsync(r => r.ProviderTransactionId == providerId, ct);
        if (previousReview != null)
            return previousReview.ReferenceCode == intent.ReferenceCode && previousReview.Amount == amount
                ? new(true, true, false, "Thông báo đã được ghi nhận trước đó.", intent.InvoiceId, amount, ReferenceCode: intent.ReferenceCode)
                : new(false, false, false, "Mã giao dịch ngân hàng đã được sử dụng.", ReferenceCode: intent.ReferenceCode);
        if (await _db.WalletTopUpIntents.AnyAsync(w => w.ProviderTransactionId == providerId, ct)
            || await _db.PaymentIntents.AnyAsync(i => i.Id != intent.Id && i.ProviderTransactionId == providerId, ct))
            return new(false, false, false, "Mã giao dịch ngân hàng đã được sử dụng.", ReferenceCode: intent.ReferenceCode);
        if (intent.Status == PaymentIntent.StatusConfirmed && (intent.ProviderTransactionId == providerId || intent.ConfirmationMethod == "Manual"))
            return amount == intent.ExpectedAmount
                ? new(true, true, false, "Giao dịch đã được ghi nhận trước đó.", intent.InvoiceId, amount, ReferenceCode: intent.ReferenceCode)
                : new(false, false, false, "Thông báo lặp có số tiền không khớp giao dịch đã lưu.", ReferenceCode: intent.ReferenceCode);

        var paid = await _db.PaymentTransactions.AnyAsync(p => p.InvoiceId == intent.InvoiceId, ct);
        if (paid || intent.Status is not (PaymentIntent.StatusPending or PaymentIntent.StatusExpired))
            return new(false, false, false, "Yêu cầu QR đã ngừng sử dụng hoặc hóa đơn đã thanh toán.", intent.InvoiceId, ReferenceCode: intent.ReferenceCode);
        if (amount != intent.ExpectedAmount)
            return new(false, false, false, "Số tiền chuyển khoản không khớp yêu cầu QR.", intent.InvoiceId, ReferenceCode: intent.ReferenceCode);
        if (intent.RequiresStaffConfirmation)
            return new(true, false, true, "Chờ nhân viên xác nhận đã nhận tiền.", intent.InvoiceId, amount, ReferenceCode: intent.ReferenceCode);
        return null; // Compatibility for historical automatic requests explicitly configured that way.
    }
}
