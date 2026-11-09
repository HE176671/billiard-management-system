using Bms.Web.Data;
using Bms.Web.Services;

namespace Bms.Tests;

public class LoyaltyConstantsTests
{
    private static readonly MembershipTier[] Tiers =
    [
        new() { Id = 1, TierName = "Member", SpendThreshold = 0 },
        new() { Id = 2, TierName = "Silver", SpendThreshold = 2_000_000, MinPoints = 500 },
        new() { Id = 3, TierName = "Gold", SpendThreshold = 5_000_000, MinPoints = 2000 }
    ];
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(19999, 1, 0)]
    [InlineData(20000, 1, 1)]
    [InlineData(2000000, 1, 100)]
    [InlineData(2000000, 2, 200)]
    [InlineData(59999, 2, 4)]
    public void EarnUsesSameFormulaForTopUpsAndPayments(decimal amount, int multiplier, int expected)
        => Assert.Equal(expected, LoyaltyPolicy.EarnPoints(amount, multiplier));

    [Theory]
    [InlineData(0, "Member")]
    [InlineData(1999999, "Member")]
    [InlineData(2000000, "Silver")]
    [InlineData(4999999, "Silver")]
    [InlineData(5000000, "Gold")]
    public void RankUsesConfiguredMoneyThreshold(decimal amount, string expected)
        => Assert.Equal(expected, TierHelper.Resolve(Tiers, amount)?.TierName);

    [Fact]
    public void RewardPointsCannotBypassSpendThreshold()
        => Assert.Equal("Member", TierHelper.Resolve(Tiers, 0, 100000)?.TierName);

    [Fact]
    public void ChangedConfigurationIsUsedImmediately()
    {
        var tiers = new[] { new MembershipTier { TierName = "Member", SpendThreshold = 0 },
            new MembershipTier { TierName = "Silver", SpendThreshold = 3_000_000 } };
        Assert.Equal("Member", TierHelper.Resolve(tiers, 2_000_000)?.TierName);
        Assert.Equal("Silver", TierHelper.Resolve(tiers, 3_000_000)?.TierName);
    }
}
