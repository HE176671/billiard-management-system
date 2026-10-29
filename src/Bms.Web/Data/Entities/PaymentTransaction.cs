namespace Bms.Web.Data;

public class PaymentTransaction
{
    public const string MethodCash = "Cash";

    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string PaymentMethod { get; set; } = "";
    public decimal AmountPaid { get; set; }
    public DateTime TransactionTimeUtc { get; set; }
    /// <summary>Ca nhận giao dịch. Bắt buộc với tiền mặt (TV3 gọi IShiftService), NULL với VNPay/IPN.</summary>
    public int? WorkShiftId { get; set; }
    /// <summary>Nhân viên thu tiền. NULL khi cổng thanh toán tự xác nhận.</summary>
    public string? ReceivedById { get; set; }
}
