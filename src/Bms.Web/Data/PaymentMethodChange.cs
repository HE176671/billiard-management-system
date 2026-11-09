namespace Bms.Web.Data;

public class PaymentMethodChange
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string ReferenceCode { get; set; } = "";
    public string FromMethod { get; set; } = "Transfer";
    public string ToMethod { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public decimal CashReceived { get; set; }
    public string Reason { get; set; } = "";
    public string ChangedById { get; set; } = "";
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
