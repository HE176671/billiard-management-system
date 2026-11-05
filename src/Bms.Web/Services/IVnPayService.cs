namespace Bms.Web.Services;

public interface IVnPayService
{
    string CreatePaymentUrl(HttpContext context, int invoiceId, decimal amount, string orderInfo);
    VnPayResponse ValidatePayment(IQueryCollection query);
}

public class VnPayResponse
{
    public bool IsSuccess { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ResponseCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

