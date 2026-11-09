using Bms.Web.Data;

namespace Bms.Tests;

public class WalletTransferReferenceTests
{
    [Fact]
    public void ReferenceFitsSePayRecognitionLimits()
    {
        var reference = WalletTopUpIntent.BuildReference(Guid.NewGuid());
        Assert.Matches("^NAPCK[A-F0-9]{30}$", reference);
        Assert.Equal(35, reference.Length);
    }

    [Fact]
    public void SameRequestProducesSameBankReference()
    {
        var request = Guid.NewGuid();
        Assert.Equal(WalletTopUpIntent.BuildReference(request), WalletTopUpIntent.BuildReference(request));
    }

    [Fact]
    public void DifferentRequestsHaveDifferentReferences()
    {
        var references = Enumerable.Range(0, 1000).Select(_ => WalletTopUpIntent.BuildReference(Guid.NewGuid()));
        Assert.Equal(1000, references.Distinct().Count());
    }
}
