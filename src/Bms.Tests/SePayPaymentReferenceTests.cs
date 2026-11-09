using Bms.Web.Services;
using Microsoft.Extensions.Configuration;

namespace Bms.Tests;

public class SePayPaymentReferenceTests
{
    private const string Reference = "NAPCK0123456789ABCDEF0123456789ABCD";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingCodeUsesFullWalletReferenceFromBankContent(string? code) =>
        Assert.Equal(Reference, SePayPaymentReference.Resolve(code, $"MB: {Reference.ToLowerInvariant()} chuyen tien"));

    [Fact]
    public void InvoiceReferenceStillUsesExistingCode() =>
        Assert.Equal("HD123456", SePayPaymentReference.Resolve(" hd123456 ", "THANH TOAN HOA DON"));

    [Fact]
    public void ServerIssuedCheckoutReferenceCanBeResolvedFromContent()
    {
        var reference = "HD" + new string('A', 32);
        Assert.Equal(reference, SePayPaymentReference.Resolve(null, "MB: " + reference.ToLowerInvariant()));
    }

    [Fact]
    public void MixedInvoiceAndWalletReferencesAreRejected() =>
        Assert.Null(SePayPaymentReference.Resolve(null, Reference + " HD" + new string('A', 32)));

    [Fact]
    public void ConflictingCheckoutReferenceIsRejected() =>
        Assert.Null(SePayPaymentReference.Resolve("HD" + new string('B', 32), "HD" + new string('A', 32)));

    [Fact]
    public void ConflictingCodeAndContentCannotCreditWallet() =>
        Assert.Null(SePayPaymentReference.Resolve("HD123456", Reference));

    [Fact]
    public void MultipleDifferentWalletReferencesCannotCreditWallet() =>
        Assert.Null(SePayPaymentReference.Resolve(null, Reference + " " + "NAPCK" + new string('F', 30)));

    [Theory]
    [InlineData("NAPCK0123")]
    [InlineData("NAPCK0123456789ABCDEF0123456789ABCDE")]
    [InlineData("XN APCK0123456789ABCDEF0123456789ABCD")]
    [InlineData("XN NAPCK0123456789ABCDEF0123456789ABCDZ")]
    public void MalformedBankContentCannotCreditWallet(string content) =>
        Assert.Null(SePayPaymentReference.Resolve(null, content));

    [Fact]
    public void PreviouslyIssuedReferencesStillResolve()
    {
        var legacy = "NAPCK" + new string('A', 32);
        Assert.Equal(legacy, SePayPaymentReference.Resolve(null, "MB: " + legacy));
    }

    [Fact]
    public void SharedAccountDoesNotRequireInventedAccountName()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:SePay:BankCode"] = "MB",
            ["Payments:SePay:AccountNumber"] = "2107072003",
            ["Payments:SePay:ApiKey"] = "test-key"
        }).Build();
        var settings = new BankTransferSettings(config);
        Assert.True(settings.CanCreateQr);
        Assert.True(settings.CanConfirmAutomatically);
        Assert.Empty(settings.AccountName);
        Assert.Contains("MB-2107072003-compact2.png", settings.QrBaseUrl);
        config["Payments:SePay:ApiKey"] = "";
        Assert.False(settings.CanConfirmAutomatically);
        Assert.True(settings.CanCreateQr);
        Assert.True(settings.HasReceivingAccount);
        config["Payments:SePay:AccountNumber"] = "";
        Assert.False(settings.CanCreateQr);
    }
}
