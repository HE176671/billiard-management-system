namespace Bms.Web.Data;

/// <summary>A receipt requiring review, never a second invoice settlement or wallet credit.</summary>
public class PaymentReconciliation
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string ReferenceCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Source { get; set; } = "Manual";
    public string Reason { get; set; } = "ManualReview";
    public string Status { get; set; } = "Open";
    public string? ProviderTransactionId { get; set; }
    public string? RequestId { get; set; }
    public DateTime? BankReceivedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
    public string Notes { get; set; } = "";
    public string? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public string? ResolvedById { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
