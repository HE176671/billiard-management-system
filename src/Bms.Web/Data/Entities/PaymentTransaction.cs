namespace Bms.Web.Data;

public class PaymentTransaction
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string PaymentMethod { get; set; } = "";
    public decimal AmountPaid { get; set; }
    public DateTime TransactionTimeUtc { get; set; }
}
