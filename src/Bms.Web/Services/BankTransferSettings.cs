namespace Bms.Web.Services;

/// <summary>The receiving account shared by invoice payments and wallet deposits.</summary>
public sealed class BankTransferSettings(IConfiguration configuration)
{
    private string Read(string key) => configuration["Payments:SePay:" + key]?.Trim() ?? "";

    public string BankCode => Read("BankCode");
    public string AccountNumber => Read("AccountNumber");
    public string AccountName => Read("AccountName");
    public bool HasReceivingAccount => BankCode.Length > 0 && AccountNumber.Length > 0;
    public bool HasWebhookKey => !string.IsNullOrWhiteSpace(configuration["Payments:SePay:ApiKey"]);
    public bool CanCreateQr => HasReceivingAccount;
    public bool CanConfirmAutomatically => HasReceivingAccount && HasWebhookKey;
    public string QrBaseUrl => HasReceivingAccount
        ? $"https://img.vietqr.io/image/{Uri.EscapeDataString(BankCode)}-{Uri.EscapeDataString(AccountNumber)}-compact2.png"
        : "";
}
