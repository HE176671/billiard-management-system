using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payments/sepay/webhook")]
public class PaymentWebhookController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        ICheckoutService checkout,
        IConfiguration configuration,
        ILogger<PaymentWebhookController> logger)
    {
        _checkout = checkout;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] SePayWebhookRequest request, CancellationToken ct)
    {
        var configuredKey = _configuration["Payments:SePay:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogError("Webhook SePay bị từ chối vì Payments:SePay:ApiKey chưa được cấu hình.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Webhook chưa được cấu hình." });
        }

        var authorization = Request.Headers.Authorization.ToString();
        const string prefix = "Apikey ";
        var suppliedKey = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : string.Empty;

        if (!FixedTimeEquals(configuredKey, suppliedKey))
            return Unauthorized(new { success = false, message = "Xác thực webhook không hợp lệ." });

        if (request.Id <= 0 || request.TransferAmount <= 0 || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { success = false, message = "Payload webhook thiếu mã giao dịch, mã thanh toán hoặc số tiền." });

        if (!string.IsNullOrWhiteSpace(request.TransferType)
            && !string.Equals(request.TransferType, "in", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Chỉ chấp nhận giao dịch tiền vào." });

        var result = await _checkout.ConfirmTransferAsync(
            request.Code,
            $"SEPAY-{request.Id}",
            request.TransferAmount,
            ct);

        if (!result.Success)
        {
            _logger.LogWarning("Webhook SePay chưa thể xử lý. Code={Code}, TransactionId={TransactionId}, Reason={Reason}",
                request.Code, request.Id, result.Message);
            return UnprocessableEntity(new { success = false, message = result.Message });
        }

        return StatusCode(StatusCodes.Status201Created, new { success = true, duplicate = result.Duplicate });
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

public sealed class SePayWebhookRequest
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("transferAmount")]
    public decimal TransferAmount { get; set; }

    [JsonPropertyName("transferType")]
    public string? TransferType { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
