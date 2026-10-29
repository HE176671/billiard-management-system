using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public class LoyaltyService : ILoyaltyService
{
    private readonly ApplicationDbContext _db;

    public LoyaltyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RedeemResult> RedeemRewardAsync(string customerId, string rewardCode, CancellationToken ct = default)
    {
        var reward = RewardCatalog.Find(rewardCode);
        if (reward == null)
            return new RedeemResult(false, "Phần quà không tồn tại.", await GetPointsAsync(customerId, ct));

        var cost = reward.Points;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Trừ điểm nguyên tử: chỉ thành công khi tài khoản còn hoạt động và đủ điểm (chống đổi trùng khi bấm 2 lần)
        var affected = await _db.Users
            .Where(u => u.Id == customerId && u.IsActive && u.RewardPoints >= cost)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.RewardPoints, u => u.RewardPoints - cost), ct);

        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            var points = await GetPointsAsync(customerId, ct);
            return new RedeemResult(false,
                points < cost
                    ? $"Bạn chưa đủ điểm để đổi \"{reward.Name}\" (cần {cost} điểm, hiện có {points})."
                    : "Tài khoản không khả dụng để đổi điểm.",
                points);
        }

        var now = DateTime.UtcNow;
        _db.PointTransactions.Add(new PointTransaction
        {
            CustomerId = customerId,
            Amount = -cost,
            TransactionType = PointTransaction.Redeem,
            CreatedAt = now
        });

        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            Title = "Đổi điểm thành công",
            Content = $"Bạn đã đổi {cost} điểm lấy \"{reward.Name}\" ({reward.Description}). Vui lòng đưa thông báo này cho nhân viên để nhận quà.",
            Type = "Redeem",
            IsRead = false,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var remaining = await GetPointsAsync(customerId, ct);
        return new RedeemResult(true, $"Đổi thành công \"{reward.Name}\" (-{cost} điểm).", remaining);
    }

    public async Task<PaymentLoyaltyResult> ProcessPaymentLoyaltyAsync(string customerId, decimal finalAmount, CancellationToken ct = default)
    {
        if (finalAmount < 0)
            return new PaymentLoyaltyResult(false, "Số tiền thanh toán không được âm.", 0, 0, 0, null, false);

        var user = await _db.Users
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.Id == customerId, ct);

        if (user == null)
            return new PaymentLoyaltyResult(false, "Không tìm thấy thông tin khách hàng.", 0, 0, 0, null, false);

        var tiers = await _db.MembershipTiers.AsNoTracking().OrderBy(t => t.SpendThreshold).ToListAsync(ct);
        var currentTier = user.Tier ?? TierHelper.Resolve(tiers, user.TotalSpend);

        // Quy tắc Tích điểm: 20,000 VNĐ = 1 điểm nhân với PointMultiplier của hạng hiện tại. Làm tròn xuống.
        int multiplier = currentTier?.PointMultiplier ?? 1;
        int earnedPoints = (int)Math.Floor(finalAmount / 20000m) * multiplier;

        // Cộng dồn tổng chi tiêu
        user.TotalSpend += finalAmount;

        // Quy tắc Phân hạng: Dựa trên TotalSpend lũy kế
        var newTier = TierHelper.Resolve(tiers, user.TotalSpend);
        bool upgraded = false;
        string? oldTierName = currentTier?.TierName;

        if (newTier != null && (user.TierId == null || newTier.SpendThreshold > (currentTier?.SpendThreshold ?? 0)))
        {
            if (user.TierId != newTier.Id)
            {
                user.TierId = newTier.Id;
                upgraded = true;
            }
        }

        user.RewardPoints += earnedPoints;
        user.LastPaidDate = DateTime.UtcNow;

        if (earnedPoints > 0)
        {
            _db.PointTransactions.Add(new PointTransaction
            {
                CustomerId = customerId,
                Amount = earnedPoints,
                TransactionType = PointTransaction.Earn,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (upgraded && newTier != null)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = customerId,
                Title = "Chúc mừng thăng hạng thành viên!",
                Content = $"Chúc mừng bạn đã được nâng từ hạng {oldTierName ?? "Thành viên"} lên hạng {newTier.TierName}! Ưu đãi hiện tại: giảm {newTier.DiscountPercent:0.##}% tiền giờ chơi và tích điểm nhân hệ số ×{newTier.PointMultiplier}.",
                Type = "TierUpgrade",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);

        var message = upgraded
            ? $"Thanh toán thành công {VnFormat.Money(finalAmount)}. Tích lũy +{earnedPoints} điểm. Chúc mừng khách hàng đã thăng hạng {newTier?.TierName}!"
            : $"Thanh toán thành công {VnFormat.Money(finalAmount)}. Tích lũy +{earnedPoints} điểm.";

        return new PaymentLoyaltyResult(
            true,
            message,
            earnedPoints,
            user.RewardPoints,
            user.TotalSpend,
            newTier?.TierName ?? currentTier?.TierName,
            upgraded);
    }

    public async Task<BillRedeemResult> RedeemPointsForBillAsync(string customerId, int pointsToRedeem, decimal currentBillAmount, CancellationToken ct = default)
    {
        if (pointsToRedeem <= 0)
            return new BillRedeemResult(false, "Số điểm đổi phải lớn hơn 0.", 0, 0);

        if (currentBillAmount <= 0)
            return new BillRedeemResult(false, "Giá trị hóa đơn phải lớn hơn 0.", 0, 0);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == customerId, ct);
        if (user == null)
            return new BillRedeemResult(false, "Không tìm thấy khách hàng.", 0, 0);

        if (user.RewardPoints < pointsToRedeem)
            return new BillRedeemResult(false, $"Khách hàng chỉ có {user.RewardPoints} điểm, không đủ để đổi {pointsToRedeem} điểm.", 0, user.RewardPoints);

        // Quy tắc Đổi điểm: 1 Điểm = 1.000 VNĐ
        decimal discountAmount = pointsToRedeem * 1000m;

        // Quy tắc: Tối đa không vượt quá 50% giá trị hóa đơn cần thanh toán
        decimal maxDiscount = currentBillAmount * 0.5m;
        if (discountAmount > maxDiscount)
        {
            int maxPointsAllowed = (int)Math.Floor(maxDiscount / 1000m);
            return new BillRedeemResult(
                false,
                $"Số tiền giảm ({VnFormat.Money(discountAmount)}) vượt quá 50% hóa đơn ({VnFormat.Money(maxDiscount)}). Tối đa có thể đổi {maxPointsAllowed} điểm.",
                0,
                user.RewardPoints);
        }

        user.RewardPoints -= pointsToRedeem;

        _db.PointTransactions.Add(new PointTransaction
        {
            CustomerId = customerId,
            Amount = -pointsToRedeem,
            TransactionType = PointTransaction.Redeem,
            CreatedAt = DateTime.UtcNow
        });

        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            Title = "Đổi điểm trừ hóa đơn",
            Content = $"Bạn đã đổi {pointsToRedeem} điểm để giảm trừ {VnFormat.Money(discountAmount)} trực tiếp vào hóa đơn trị giá {VnFormat.Money(currentBillAmount)}.",
            Type = "Redeem",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        return new BillRedeemResult(
            true,
            $"Đổi thành công {pointsToRedeem} điểm, giảm trừ {VnFormat.Money(discountAmount)} vào hóa đơn.",
            discountAmount,
            user.RewardPoints);
    }

    public async Task<int> ProcessPointExpirationsAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-180);

        var expiredUsers = await _db.Users
            .Where(u => u.RewardPoints > 0 && u.LastPaidDate != null && u.LastPaidDate < threshold)
            .ToListAsync(ct);

        if (expiredUsers.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var user in expiredUsers)
        {
            int expiredPoints = user.RewardPoints;
            user.RewardPoints = 0;

            _db.PointTransactions.Add(new PointTransaction
            {
                CustomerId = user.Id,
                Amount = -expiredPoints,
                TransactionType = PointTransaction.Expire,
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = "Điểm thưởng đã hết hạn",
                Content = $"Số điểm thưởng ({expiredPoints} điểm) của bạn đã hết hạn do tài khoản không phát sinh thanh toán trong vòng 180 ngày (tính từ lần cuối ngày {user.LastPaidDate?.ToString("dd/MM/yyyy")}). Hạng thành viên và Tổng chi tiêu của bạn vẫn được bảo lưu vĩnh viễn.",
                Type = "Expire",
                IsRead = false,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        return expiredUsers.Count;
    }

    private Task<int> GetPointsAsync(string customerId, CancellationToken ct) =>
        _db.Users.AsNoTracking().Where(u => u.Id == customerId)
            .Select(u => u.RewardPoints).FirstOrDefaultAsync(ct);
}
