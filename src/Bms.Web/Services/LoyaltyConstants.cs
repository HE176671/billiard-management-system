namespace Bms.Web.Services;

/// <summary>
/// Hằng số dùng cho hệ thống điểm thưởng và hạng thành viên BMS.
/// Mọi thay đổi quy tắc chỉ cần sửa tại file này.
/// Quy tắc hiện tại: 100,000 VND = 10 điểm; ngưỡng hạng: 0/100/200/300.
/// </summary>
public static class LoyaltyConstants
{
    // ── Quy tắc quy đổi điểm ──
    /// <summary>Cứ bao nhiêu VND thì được 1 điểm (10,000 VND = 1 điểm → 100,000 VND = 10 điểm).</summary>
    public const decimal VndPerPoint = 10_000m;

    // ── Ngưỡng hạng (điểm tối thiểu để đạt hạng) ──
    public const int BronzeThreshold  = 0;
    public const int SilverThreshold  = 100;
    public const int GoldThreshold    = 200;
    public const int DiamondThreshold = 300;

    // ── Tên hạng hiển thị ──
    public const string BronzeName  = "Đồng";
    public const string SilverName  = "Bạc";
    public const string GoldName    = "Vàng";
    public const string DiamondName = "Kim cương";

    /// <summary>
    /// Tính hạng thành viên từ tổng điểm.
    /// </summary>
    public static string GetRankName(int totalPoints) => totalPoints switch
    {
        >= DiamondThreshold => DiamondName,
        >= GoldThreshold    => GoldName,
        >= SilverThreshold  => SilverName,
        _                   => BronzeName
    };

    /// <summary>
    /// Số điểm còn thiếu để lên hạng kế tiếp.
    /// Trả về null nếu đã ở hạng cao nhất.
    /// </summary>
    public static int? PointsToNextRank(int totalPoints) => totalPoints switch
    {
        >= DiamondThreshold => null,
        >= GoldThreshold    => DiamondThreshold - totalPoints,
        >= SilverThreshold  => GoldThreshold - totalPoints,
        _                   => SilverThreshold - totalPoints
    };

    /// <summary>
    /// Tính điểm từ PlaytimeAmount (VND) theo công thức FLOOR(amount / VndPerPoint).
    /// </summary>
    public static int CalcSessionPoints(decimal playtimeAmount)
        => (int)Math.Floor(playtimeAmount / VndPerPoint);
}
