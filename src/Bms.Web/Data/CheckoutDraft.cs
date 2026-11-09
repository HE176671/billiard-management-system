namespace Bms.Web.Data;

/// <summary>A server-issued checkout identity survives retries and page reloads.</summary>
public class CheckoutDraft
{
    public const string Draft = "Draft", Pending = "Pending", Paid = "Paid", Superseded = "Superseded";
    public string ReferenceCode { get; set; } = "HD" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    public string Status { get; set; } = Draft;
    public string? ReplacementReference { get; set; }
    public string CreatedById { get; set; } = "";
    public string? CustomerId { get; set; }
    public int? SessionId { get; set; }
    public int? InvoiceId { get; set; }
    public bool IsManualEntry { get; set; }
    public string PaymentMethod { get; set; } = "VietQR";
    public decimal PlaytimeAmount { get; set; }
    public decimal FandBAmount { get; set; }
    public int PointsToRedeem { get; set; }
    public string? VoucherCode { get; set; }
    public decimal? CashReceived { get; set; }
    public decimal? FinalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
}
