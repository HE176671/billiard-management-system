namespace Bms.Web.Services;

public record RedeemResult(bool Success, string Message, int RemainingPoints, string? VoucherCode = null);

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

public record CheckoutLoyaltyRequest(
    string CustomerId,
    string ReferenceCode,
    int? InvoiceId,
    decimal PlaytimeAmount,
    decimal FandBAmount,
    int PointsToRedeem,
    string? VoucherCode,
    string? CashierId,
    bool PayWithWallet = false);

public record CheckoutLoyaltyPreview(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string TierName,
    decimal TierDiscountPercent,
    decimal TierDiscountAmount,
    decimal VoucherDiscountAmount,
    decimal PointsDiscountAmount,
    int MaxRedeemablePoints,
    decimal FinalAmount,
    int PointsToEarn,
    bool WillUpgradeTier,
    string? NextTierName,
    decimal Spend12MonthsAfter,
    decimal AmountNeededForNextTier);

public record CheckoutLoyaltyResult(
    bool Success,
    bool Duplicate,
    string Message,
    decimal Subtotal,
    decimal TierDiscountAmount,
    decimal VoucherDiscountAmount,
    decimal PointsDiscountAmount,
    decimal TotalDiscount,
    decimal FinalAmount,
    int PointsRedeemed,
    int PointsEarned,
    int NewRewardPoints,
    string TierName,
    bool UpgradedTier);

/// <summary>
/// Nghiệp vụ tích điểm / đổi điểm / phân hạng thành viên & hợp đồng Checkout Loyalty cho TV3.
/// </summary>
public interface ILoyaltyService
{
    /// <summary>Đổi điểm lấy quà/voucher trong bảng RewardItems.</summary>
    Task<RedeemResult> RedeemRewardAsync(string customerId, string rewardCode, CancellationToken ct = default);

    /// <summary>Xem trước toàn bộ giảm giá theo hạng, voucher, đổi điểm và số điểm sẽ tích lũy khi thanh toán.</summary>
    Task<CheckoutLoyaltyPreview> PreviewCheckoutAsync(CheckoutLoyaltyRequest req, CancellationToken ct = default);

    /// <summary>
    /// INTEGRATION: TV3 gọi CommitCheckoutAsync khi Invoice chuyển sang Paid.
    /// Xử lý trọn gói (1 transaction, idempotent theo ReferenceCode):
    /// Trừ điểm đổi hóa đơn -> Đánh dấu Voucher Used -> Cộng TotalSpend & LastPaidDate -> Cộng điểm Earn -> Xét hạng 12 tháng -> Bắn Notification tổng hợp.
    /// Trả về TotalDiscount để TV3 ghi vào Invoices.DiscountAmount.
    /// </summary>
    Task<CheckoutLoyaltyResult> CommitCheckoutAsync(CheckoutLoyaltyRequest req, CancellationToken ct = default);

    [Obsolete("Sử dụng CommitCheckoutAsync để đảm bảo idempotent và áp đầy đủ ưu đãi hạng/voucher.")]
    Task<PaymentLoyaltyResult> ProcessPaymentLoyaltyAsync(string customerId, decimal finalAmount, CancellationToken ct = default);

    [Obsolete("Sử dụng PreviewCheckoutAsync / CommitCheckoutAsync để đổi điểm trong cùng giao dịch thanh toán.")]
    Task<BillRedeemResult> RedeemPointsForBillAsync(string customerId, int pointsToRedeem, decimal currentBillAmount, CancellationToken ct = default);

    /// <summary>Quét và thu hồi điểm thưởng cho các hồ sơ không phát sinh giao dịch trên 100 ngày.</summary>
    Task<int> ProcessPointExpirationsAsync(CancellationToken ct = default);
}
