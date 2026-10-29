namespace Bms.Web.Services;

public record RedeemResult(bool Success, string Message, int RemainingPoints);

public record PaymentLoyaltyResult(
    bool Success,
    string Message,
    int EarnedPoints,
    int NewRewardPoints,
    decimal NewTotalSpend,
    string? NewTierName,
    bool Upgraded);

public record BillRedeemResult(
    bool Success,
    string Message,
    decimal DiscountAmount,
    int RemainingPoints);

/// <summary>
/// Nghiệp vụ tích điểm / đổi điểm / phân hạng thành viên.
/// </summary>
public interface ILoyaltyService
{
    /// <summary>Đổi điểm lấy quà/dịch vụ trong <see cref="RewardCatalog"/>.</summary>
    Task<RedeemResult> RedeemRewardAsync(string customerId, string rewardCode, CancellationToken ct = default);

    /// <summary>
    /// Xử lý tích điểm và xét thăng hạng sau khi thanh toán hóa đơn.
    /// Tỷ lệ: 20.000 VNĐ = 1 điểm * PointMultiplier của hạng hiện tại.
    /// Cộng dồn TotalSpend, xét thăng hạng TierId, cập nhật LastPaidDate, ghi log PointTransactions('Earn'),
    /// và gửi Notifications nếu khách được thăng hạng.
    /// </summary>
    Task<PaymentLoyaltyResult> ProcessPaymentLoyaltyAsync(string customerId, decimal finalAmount, CancellationToken ct = default);

    /// <summary>
    /// Đổi điểm để giảm trừ trực tiếp vào hóa đơn cần thanh toán.
    /// Quy tắc: 1 Điểm = 1.000 VNĐ; Số tiền giảm tối đa không vượt quá 50% giá trị hóa đơn.
    /// Trừ RewardPoints, ghi log PointTransactions('Redeem', số âm) và trả về số tiền được giảm.
    /// </summary>
    Task<BillRedeemResult> RedeemPointsForBillAsync(string customerId, int pointsToRedeem, decimal currentBillAmount, CancellationToken ct = default);

    /// <summary>
    /// Quét và xử lý hết hạn điểm thưởng cho các tài khoản không phát sinh hóa đơn quá 180 ngày.
    /// </summary>
    Task<int> ProcessPointExpirationsAsync(CancellationToken ct = default);
}
