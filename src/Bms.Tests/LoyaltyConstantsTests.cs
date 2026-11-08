using Bms.Web.Services;
using Xunit;

namespace Bms.Tests;

/// <summary>
/// Unit tests cho LoyaltyConstants — tính điểm, tính hạng, điểm lên hạng.
/// Bám sát Acceptance Criteria mục UC09/15 trong spec.md.
/// Không dùng database, không dùng EF — pure logic.
/// </summary>
public class LoyaltyConstantsTests
{
    // ══════════════════════════════════════════════════════
    // CalcSessionPoints — công thức FLOOR(amount / 10,000)
    // ══════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,        0)]       // 0 VND → 0 điểm
    [InlineData(9_999,    0)]       // dưới ngưỡng → 0
    [InlineData(10_000,   1)]       // đúng 1 điểm
    [InlineData(99_999,   9)]       // gần 10 điểm nhưng FLOOR
    [InlineData(100_000,  10)]      // spec: 100k = 10 điểm
    [InlineData(150_000,  15)]      // 1.5x ngưỡng
    [InlineData(250_000,  25)]      // 2.5x ngưỡng
    [InlineData(1_000_000, 100)]    // 1 triệu = 100 điểm (Kim cương)
    public void CalcSessionPoints_ReturnsCorrectFloor(decimal playtimeAmount, int expected)
    {
        var result = LoyaltyConstants.CalcSessionPoints(playtimeAmount);
        Assert.Equal(expected, result);
    }

    // ══════════════════════════════════════════════════════
    // GetRankName — phân hạng theo ngưỡng
    // ══════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,   "Đồng")]       // AC: khi 0 điểm → Đồng (FR-09.5)
    [InlineData(50,  "Đồng")]
    [InlineData(99,  "Đồng")]       // sát ngưỡng Bạc
    [InlineData(100, "Bạc")]        // đúng ngưỡng Bạc
    [InlineData(150, "Bạc")]
    [InlineData(199, "Bạc")]        // sát ngưỡng Vàng
    [InlineData(200, "Vàng")]       // đúng ngưỡng Vàng
    [InlineData(250, "Vàng")]
    [InlineData(299, "Vàng")]       // sát ngưỡng Kim cương
    [InlineData(300, "Kim cương")]  // đúng ngưỡng cao nhất
    [InlineData(999, "Kim cương")]  // vượt ngưỡng cao nhất
    public void GetRankName_ReturnsCorrectRank(int totalPoints, string expectedRank)
    {
        var result = LoyaltyConstants.GetRankName(totalPoints);
        Assert.Equal(expectedRank, result);
    }

    // ══════════════════════════════════════════════════════
    // PointsToNextRank — số điểm còn thiếu
    // ══════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,   100)]   // Đồng → Bạc cần 100
    [InlineData(50,  50)]    // còn 50 lên Bạc
    [InlineData(99,  1)]     // còn 1 lên Bạc
    [InlineData(100, 100)]   // Bạc → Vàng cần 100
    [InlineData(150, 50)]    // còn 50 lên Vàng
    [InlineData(199, 1)]     // còn 1 lên Vàng
    [InlineData(200, 100)]   // Vàng → Kim cương cần 100
    [InlineData(250, 50)]    // còn 50 lên Kim cương
    [InlineData(299, 1)]     // còn 1 lên Kim cương
    public void PointsToNextRank_NotNull_WhenBelowDiamond(int totalPoints, int expected)
    {
        var result = LoyaltyConstants.PointsToNextRank(totalPoints);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Theory]
    [InlineData(300)]   // đúng ngưỡng Kim cương → hạng cao nhất
    [InlineData(500)]   // vượt ngưỡng
    [InlineData(9999)]  // xa hơn
    public void PointsToNextRank_Null_WhenDiamond(int totalPoints)
    {
        var result = LoyaltyConstants.PointsToNextRank(totalPoints);
        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════
    // Idempotency — cùng PlaytimeAmount → cùng điểm (FR-09.4)
    // ══════════════════════════════════════════════════════

    [Fact]
    public void CalcSessionPoints_IsIdempotent()
    {
        decimal amount = 150_000m;
        var first  = LoyaltyConstants.CalcSessionPoints(amount);
        var second = LoyaltyConstants.CalcSessionPoints(amount);
        var third  = LoyaltyConstants.CalcSessionPoints(amount);
        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    // ══════════════════════════════════════════════════════
    // Tổng điểm nhiều phiên — đảm bảo không cộng trùng
    // ══════════════════════════════════════════════════════

    [Fact]
    public void TotalPoints_SumOfMultipleSessions_IsCorrect()
    {
        // Simulate: Customer có 3 phiên Closed với PlaytimeAmount khác nhau
        var amounts = new[] { 100_000m, 150_000m, 50_000m };
        var total = amounts.Sum(a => LoyaltyConstants.CalcSessionPoints(a));

        // 10 + 15 + 5 = 30 điểm → hạng Đồng (< 100)
        Assert.Equal(30, total);
        Assert.Equal("Đồng", LoyaltyConstants.GetRankName(total));
    }

    [Fact]
    public void TotalPoints_WhenSumExceedsThreshold_RankUpgrades()
    {
        // 10 phiên × 100k = 10 phiên × 10 điểm = 100 điểm → Bạc
        var amounts = Enumerable.Repeat(100_000m, 10);
        var total = amounts.Sum(a => LoyaltyConstants.CalcSessionPoints(a));

        Assert.Equal(100, total);
        Assert.Equal("Bạc", LoyaltyConstants.GetRankName(total));
        Assert.Equal(100, LoyaltyConstants.PointsToNextRank(total));  // cần thêm 100 lên Vàng
    }

    [Fact]
    public void TotalPoints_Diamond_HasNullNextRank()
    {
        // 30 phiên × 100k = 300 điểm → Kim cương
        var amounts = Enumerable.Repeat(100_000m, 30);
        var total = amounts.Sum(a => LoyaltyConstants.CalcSessionPoints(a));

        Assert.Equal(300, total);
        Assert.Equal("Kim cương", LoyaltyConstants.GetRankName(total));
        Assert.Null(LoyaltyConstants.PointsToNextRank(total));
    }
}
