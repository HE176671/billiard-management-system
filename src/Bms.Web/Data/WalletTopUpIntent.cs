namespace Bms.Web.Data;

public class WalletTopUpIntent
{
    public const string ReferencePrefix = "NAPCK";
    // SePay accepts a 2–5 letter prefix and at most 30 alphanumeric suffix characters.
    public static string BuildReference(Guid requestId) => ReferencePrefix + requestId.ToString("N")[..30].ToUpperInvariant();
    public int Id { get; set; }
    public string CustomerId { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public string ReferenceCode { get; set; } = "";
    public decimal ExpectedAmount { get; set; }
    public string Status { get; set; } = PaymentIntent.StatusPending;
    public string? ProviderTransactionId { get; set; }
    public string? ConfirmationMethod { get; set; }
    public string? ConfirmedById { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool RequiresStaffConfirmation { get; set; } = true;
    public DateTime? ConfirmedAtUtc { get; set; }
}
