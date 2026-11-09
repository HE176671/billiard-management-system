using System.Net;
using System.Net.Mail;

namespace Bms.Web.Services;

/// <summary>Cấu hình SMTP (appsettings "Smtp" hoặc user-secrets). Để trống Host thì chạy chế độ demo: chỉ ghi log.</summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Billiard Management System";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public enum EmailSendStatus { Sent, Skipped, Failed }

/// <summary>TV5 — API-02: gửi email ra SMTP. Dùng chung cho thông báo hạng, voucher sinh nhật, email ưu đãi.</summary>
public interface IEmailService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Gửi một email HTML, thử lại tối đa 3 lần. Không ném lỗi: lỗi được ghi log và trả về Failed,
    /// để email hỏng không làm hỏng nghiệp vụ chính (thanh toán, đổi hạng...).
    /// </summary>
    Task<EmailSendStatus> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default);
}

public class SmtpEmailService : IEmailService
{
    private const int MaxAttempts = 3;
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _options = config.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<EmailSendStatus> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress) || !MailAddress.TryCreate(toAddress.Trim(), out var to))
        {
            _logger.LogWarning("Bỏ qua email '{Subject}': địa chỉ người nhận không hợp lệ.", subject);
            return EmailSendStatus.Skipped;
        }

        if (!_options.IsConfigured)
        {
            // Chế độ demo: chưa cấu hình SMTP thì chỉ ghi log, không coi là lỗi.
            _logger.LogInformation("[EMAIL DEMO] Tới {To} | {Subject}", to.Address, subject);
            return EmailSendStatus.Skipped;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(_options.FromAddress!, _options.FromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(to);

                using var client = new SmtpClient(_options.Host!, _options.Port)
                {
                    EnableSsl = _options.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };
                if (!string.IsNullOrEmpty(_options.UserName))
                    client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

                await client.SendMailAsync(message, ct);
                return EmailSendStatus.Sent;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Gửi email '{Subject}' tới {To} lỗi lần {Attempt}, thử lại.", subject, to.Address, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gửi email '{Subject}' tới {To} thất bại sau {Max} lần.", subject, to.Address, MaxAttempts);
            }
        }
        return EmailSendStatus.Failed;
    }
}
