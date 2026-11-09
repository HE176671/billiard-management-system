namespace Bms.Web.Services;

/// <summary>Chính sách duy nhất cho hóa đơn và nạp trả trước. Hệ số lấy từ hạng trước giao dịch.</summary>
public static class LoyaltyPolicy
{
    public const decimal VndPerPoint = 20_000m;
    public static int EarnPoints(decimal eligibleAmount, int multiplier) =>
        checked((int)(Math.Floor(Math.Max(0m, eligibleAmount) / VndPerPoint) * Math.Max(1, multiplier)));
}
