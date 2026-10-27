namespace Bms.Web.Data;

public class MembershipTier
{
    public int     Id              { get; set; }
    public string  TierName        { get; set; } = "";
    public int     MinPoints       { get; set; }
    public decimal DiscountPercent { get; set; }
}
