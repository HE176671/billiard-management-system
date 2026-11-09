namespace Bms.Web.Data;

/// <summary>Sổ tiền trả trước, độc lập với sổ điểm. Số tiền dương: nạp; âm: trả hóa đơn.</summary>
public class WalletTransaction
{
    public const string TopUp = "TopUp";
    public const string Payment = "Payment";
    public string PaymentMethod { get; set; } = "Cash";
    public string? ProviderTransactionId { get; set; }
    public int Id { get; set; }
    public string CustomerId { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceCode { get; set; } = "";
    public int? InvoiceId { get; set; }
    public string CreatedById { get; set; } = "";
    public int? WorkShiftId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
