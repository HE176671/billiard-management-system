namespace Bms.Web.Services;

/// <summary>Compatibility for older callers; ranking must use MembershipTiers through TierHelper.</summary>
public static class LoyaltyConstants
{
    public const decimal VndPerPoint = LoyaltyPolicy.VndPerPoint;
    public static int CalcSessionPoints(decimal amount) => LoyaltyPolicy.EarnPoints(amount, 1);
}
