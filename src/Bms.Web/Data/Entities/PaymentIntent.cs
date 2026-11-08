namespace Bms.Web.Data;

/// <summary>
/// Yêu cầu chuyển khoản đang chờ ngân hàng xác nhận. PaymentTransactions chỉ được tạo
/// sau khi webhook hợp lệ xác nhận đã nhận đủ tiền.
/// </summary>
public class PaymentIntent
{
    public const string StatusPending = "Pending";
    public const string StatusConfirmed = "Confirmed";
    public const string StatusExpired = "Expired";
    public const string StatusFailed = "Failed";

    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string ReferenceCode { get; set; } = "";
    public decimal ExpectedAmount { get; set; }
    public string Provider { get; set; } = "SePay";
    public string Status { get; set; } = StatusPending;
    public string? ProviderTransactionId { get; set; }
    public string? CustomerId { get; set; }
    public int? SessionId { get; set; }
    public decimal PlaytimeAmount { get; set; }
    public decimal FandBAmount { get; set; }
    public int PointsToRedeem { get; set; }
    public string? VoucherCode { get; set; }
    public string? CashierId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}
