using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Bms.Web.Services;

public class VnPayService : IVnPayService
{
    private readonly IConfiguration _configuration;

    public VnPayService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreatePaymentUrl(HttpContext context, int invoiceId, decimal amount, string orderInfo)
    {
        var tmnCode = _configuration["VnPay:TmnCode"] ?? "SANDBOX01";
        var hashSecret = _configuration["VnPay:HashSecret"] ?? "SECRETKEY1234567890ABCDEF";
        var baseUrl = _configuration["VnPay:BaseUrl"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        var returnUrl = _configuration["VnPay:ReturnUrl"] ?? $"{context.Request.Scheme}://{context.Request.Host}/Checkout/VNPayReturn";

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        var txnRef = $"{invoiceId}_{DateTime.UtcNow.Ticks}";
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        if (ipAddress == "::1") ipAddress = "127.0.0.1";

        var vnpParams = new SortedDictionary<string, string>(new VnPayCompare())
        {
            { "vnp_Version", "2.1.0" },
            { "vnp_Command", "pay" },
            { "vnp_TmnCode", tmnCode },
            { "vnp_Amount", ((long)(amount * 100)).ToString() },
            { "vnp_CreateDate", now.ToString("yyyyMMddHHmmss") },
            { "vnp_CurrCode", "VND" },
            { "vnp_IpAddr", ipAddress },
            { "vnp_Locale", "vn" },
            { "vnp_OrderInfo", string.IsNullOrEmpty(orderInfo) ? $"Thanh toan hoa don #{invoiceId}" : orderInfo },
            { "vnp_OrderType", "other" },
            { "vnp_ReturnUrl", returnUrl },
            { "vnp_TxnRef", txnRef }
        };

        var rawData = new StringBuilder();
        var query = new StringBuilder();

        foreach (var (key, value) in vnpParams)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (rawData.Length > 0)
                {
                    rawData.Append('&');
                    query.Append('&');
                }
                rawData.Append(WebUtility.UrlEncode(key) + "=" + WebUtility.UrlEncode(value));
                query.Append(WebUtility.UrlEncode(key) + "=" + WebUtility.UrlEncode(value));
            }
        }

        var signData = rawData.ToString();
        var secureHash = HmacSha512(hashSecret, signData);
        return $"{baseUrl}?{query}&vnp_SecureHash={secureHash}";
    }

    public VnPayResponse ValidatePayment(IQueryCollection query)
    {
        var hashSecret = _configuration["VnPay:HashSecret"] ?? "SECRETKEY1234567890ABCDEF";
        var vnpParams = new SortedDictionary<string, string>(new VnPayCompare());
        string vnpSecureHash = string.Empty;

        foreach (var (key, value) in query)
        {
            if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
            {
                if (key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase))
                {
                    vnpSecureHash = value.ToString();
                }
                else if (!key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
                {
                    vnpParams[key] = value.ToString();
                }
            }
        }

        var rawData = new StringBuilder();
        foreach (var (key, value) in vnpParams)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (rawData.Length > 0) rawData.Append('&');
                rawData.Append(WebUtility.UrlEncode(key) + "=" + WebUtility.UrlEncode(value));
            }
        }

        var calculatedHash = HmacSha512(hashSecret, rawData.ToString());
        var isSignatureValid = string.Equals(calculatedHash, vnpSecureHash, StringComparison.OrdinalIgnoreCase);

        var responseCode = query.TryGetValue("vnp_ResponseCode", out var respVal) ? respVal.ToString() : "";
        var transactionId = query.TryGetValue("vnp_TransactionNo", out var transVal) ? transVal.ToString() : "";
        var txnRef = query.TryGetValue("vnp_TxnRef", out var refVal) ? refVal.ToString() : "";
        var amountStr = query.TryGetValue("vnp_Amount", out var amtVal) ? amtVal.ToString() : "0";
        decimal amount = 0;
        if (long.TryParse(amountStr, out var rawAmount))
        {
            amount = rawAmount / 100m;
        }

        // Tách invoiceId từ txnRef (dạng invoiceId_ticks)
        var invoiceIdStr = txnRef.Split('_')[0];

        var isSuccess = isSignatureValid && responseCode == "00";

        return new VnPayResponse
        {
            IsSuccess = isSuccess,
            TransactionId = transactionId,
            OrderId = invoiceIdStr,
            Amount = amount,
            ResponseCode = responseCode,
            Message = isSuccess ? "Thanh toán thành công" : GetVnPayErrorMessage(responseCode)
        };
    }

    private static string HmacSha512(string key, string inputData)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(inputData);
        using var hmac = new HMACSHA512(keyBytes);
        var hashValue = hmac.ComputeHash(inputBytes);
        return BitConverter.ToString(hashValue).Replace("-", "").ToLowerInvariant();
    }

    private static string GetVnPayErrorMessage(string responseCode) => responseCode switch
    {
        "00" => "Giao dịch thành công",
        "07" => "Trừ tiền thành công. Giao dịch bị nghi ngờ (liên quan tới lừa đảo, giao dịch bất thường).",
        "09" => "Thẻ/Tài khoản chưa đăng ký dịch vụ InternetBanking tại ngân hàng.",
        "10" => "Khách hàng xác thực thông tin thẻ/tài khoản không đúng quá 3 lần",
        "11" => "Đã hết hạn chờ thanh toán. Vui lòng thực hiện lại giao dịch.",
        "12" => "Thẻ/Tài khoản của khách hàng bị khóa.",
        "13" => "Khách hàng nhập sai mật khẩu xác thực giao dịch (OTP).",
        "24" => "Khách hàng hủy giao dịch",
        "51" => "Tài khoản của quý khách không đủ số dư để thực hiện giao dịch.",
        "65" => "Tài khoản của Quý khách đã vượt quá hạn mức giao dịch trong ngày.",
        "75" => "Ngân hàng thanh toán đang bảo trì.",
        "79" => "Khách hàng nhập sai mật khẩu thanh toán quá số lần quy định.",
        _ => "Giao dịch không thành công hoặc chữ ký không hợp lệ."
    };

    private class VnPayCompare : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            var compare = CompareInfo.GetCompareInfo("en-US");
            return compare.Compare(x, y, CompareOptions.Ordinal);
        }
    }
}

