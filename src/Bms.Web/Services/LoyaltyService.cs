using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public class LoyaltyService : ILoyaltyService
{
    private readonly ApplicationDbContext _db;
    private readonly IPointLedgerService _ledger;
    private readonly ITierEvaluationService _tierEval;
    private readonly IVoucherService _voucherService;

    public LoyaltyService(
        ApplicationDbContext db,
        IPointLedgerService ledger,
        ITierEvaluationService tierEval,
        IVoucherService voucherService)
    {
        _db = db;
        _ledger = ledger;
        _tierEval = tierEval;
        _voucherService = voucherService;
    }

    public async Task<RedeemResult> RedeemRewardAsync(string customerId, string rewardCode, CancellationToken ct = default)
    {
        var res = await _voucherService.RedeemRewardForVoucherAsync(customerId, rewardCode, ct);
        return new RedeemResult(res.Success, res.Message, res.RemainingPoints, res.Voucher?.Code);
    }

    public async Task<CheckoutLoyaltyPreview> PreviewCheckoutAsync(CheckoutLoyaltyRequest req, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (req.PlaytimeAmount < 0) errors.Add("Tiền giờ chơi không được âm.");
        if (req.FandBAmount < 0) errors.Add("Tiền dịch vụ ăn uống (F&B) không được âm.");
        if (req.PointsToRedeem < 0) errors.Add("Số điểm muốn đổi không được âm.");

        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.Id == req.CustomerId, ct);

        if (user == null)
        {
            errors.Add("Không tìm thấy thông tin khách hàng.");
            return new CheckoutLoyaltyPreview(false, errors, "—", 0, 0, 0, 0, 0, 0, 0, false, null, 0, 0);
        }

        if (!user.IsActive)
            errors.Add("Tài khoản khách hàng đang bị khóa.");
        var role = await _db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (!await _db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == role, ct))
            errors.Add("Tài khoản không phải hội viên.");

        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

        var currentSpend12M = await _tierEval.GetSpendLast12MonthsAsync(user.Id, null, ct);
        var currentTier = user.Tier ?? TierHelper.Resolve(tiers, currentSpend12M) ?? tiers.FirstOrDefault();
        string tierName = currentTier?.TierName ?? "Member";
        decimal tierDiscountPercent = currentTier?.DiscountPercent ?? 0m;
        int multiplier = currentTier?.PointMultiplier ?? 1;

        decimal subtotal = Math.Max(0m, req.PlaytimeAmount) + Math.Max(0m, req.FandBAmount);

        // Bước 1: Giảm theo hạng — chỉ áp dụng trên tiền giờ chơi (PlaytimeAmount)
        decimal tierDiscountAmount = Math.Round(Math.Max(0m, req.PlaytimeAmount) * (tierDiscountPercent / 100m), 0, MidpointRounding.AwayFromZero);
        decimal remainingAfterTier = Math.Max(0m, subtotal - tierDiscountAmount);

        // Bước 2: Mã quà tặng Manager (tối đa 1 mã/hóa đơn).
        // Staff nhập RewardItems.Code; hệ thống tự chọn voucher còn hiệu lực mà hội viên sở hữu.
        decimal voucherDiscountAmount = 0m;
        if (!string.IsNullOrWhiteSpace(req.VoucherCode))
        {
            var code = req.VoucherCode.Trim().ToUpperInvariant();
            var now = DateTime.UtcNow;
            decimal userTierThreshold = currentTier?.SpendThreshold ?? 0m;
            var rewardItem = await _db.RewardItems.AsNoTracking()
                .Include(r => r.MinTier)
                .FirstOrDefaultAsync(r => r.Code == code, ct);

            if (rewardItem == null)
            {
                errors.Add($"Mã quà tặng \"{code}\" không tồn tại.");
            }
            else if (!rewardItem.IsActive)
            {
                errors.Add($"Mã quà tặng \"{code}\" ({rewardItem.Name}) đang bị Manager đóng.");
            }
            else if (rewardItem.MinTier != null && userTierThreshold < rewardItem.MinTier.SpendThreshold)
            {
                errors.Add($"Không thể áp dụng mã \"{code}\" ({rewardItem.Name}): yêu cầu từ Hạng {rewardItem.MinTier.TierName} trở lên, nhưng hội viên hiện đang ở Hạng {tierName}.");
            }
            else
            {
                var ownedVoucher = await _db.CustomerVouchers.AsNoTracking()
                    .Where(v => v.CustomerId == user.Id
                             && v.RewardItemId == rewardItem.Id
                             && v.Status == CustomerVoucher.StatusActive
                             && v.ExpiresAtUtc > now)
                    .OrderBy(v => v.ExpiresAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (ownedVoucher == null)
                {
                    errors.Add($"Hội viên chưa sở hữu mã quà tặng \"{code}\" ({rewardItem.Name}) còn hiệu lực.");
                }
                else if (rewardItem.RewardType == RewardItemEntity.TypeFreeHour)
                {
                    decimal playAfterTier = Math.Max(0m, req.PlaytimeAmount - tierDiscountAmount);
                    decimal cap = rewardItem.DiscountValue ?? 80000m;
                    voucherDiscountAmount = Math.Min(playAfterTier, cap);
                }
                else
                {
                    decimal val = rewardItem.DiscountValue ?? 0m;
                    voucherDiscountAmount = Math.Min(remainingAfterTier, val);
                }
            }
        }

        decimal remainingAfterVoucher = Math.Max(0m, remainingAfterTier - voucherDiscountAmount);

        // Bước 3: Đổi điểm — tối đa 50% số tiền còn lại sau bước 1-2, và không vượt quá RewardPoints hiện có
        int maxByHalfBill = (int)Math.Floor((remainingAfterVoucher * 0.5m) / 1000m);
        int maxRedeemablePoints = Math.Max(0, Math.Min(user.RewardPoints, maxByHalfBill));

        if (req.PointsToRedeem > user.RewardPoints)
        {
            errors.Add($"Khách hàng chỉ có {user.RewardPoints} điểm, không đủ để đổi {req.PointsToRedeem} điểm.");
        }
        else if (req.PointsToRedeem > maxByHalfBill)
        {
            errors.Add($"Số điểm đổi ({req.PointsToRedeem} điểm = {VnFormat.Money(req.PointsToRedeem * 1000m)}) vượt quá 50% số tiền còn lại ({VnFormat.Money(remainingAfterVoucher * 0.5m)}). Tối đa được đổi {maxRedeemablePoints} điểm.");
        }

        decimal pointsDiscountAmount = req.PointsToRedeem >= 0 ? req.PointsToRedeem * 1000m : 0m;
        if (pointsDiscountAmount > remainingAfterVoucher)
            pointsDiscountAmount = remainingAfterVoucher;

        // Bước 4: FinalAmount & Điểm tích lũy
        decimal finalAmount = Math.Max(0m, remainingAfterVoucher - pointsDiscountAmount);
        if (req.PayWithWallet && user.WalletBalance < finalAmount)
            errors.Add("Số dư ví không đủ. Vui lòng nạp thêm hoặc chọn phương thức thanh toán khác.");
        decimal eligibleAmount = finalAmount;
        int pointsToEarn = LoyaltyPolicy.EarnPoints(eligibleAmount, multiplier);

        decimal spend12MonthsAfter = currentSpend12M + eligibleAmount;
        int pointsAfter = Math.Max(0, user.RewardPoints - req.PointsToRedeem) + pointsToEarn;
        var resolvedTierAfter = TierHelper.Resolve(tiers, spend12MonthsAfter, pointsAfter);
        bool willUpgrade = resolvedTierAfter != null && resolvedTierAfter.SpendThreshold > (currentTier?.SpendThreshold ?? 0m);

        var nextTier = tiers.FirstOrDefault(t => t.SpendThreshold > (resolvedTierAfter?.SpendThreshold ?? currentTier?.SpendThreshold ?? 0m));
        string? nextTierName = willUpgrade ? resolvedTierAfter!.TierName : nextTier?.TierName;
        decimal amountNeededForNext = nextTier != null ? Math.Max(0m, nextTier.SpendThreshold - spend12MonthsAfter) : 0m;

        return new CheckoutLoyaltyPreview(
            IsValid: errors.Count == 0,
            Errors: errors,
            TierName: tierName,
            TierDiscountPercent: tierDiscountPercent,
            TierDiscountAmount: tierDiscountAmount,
            VoucherDiscountAmount: voucherDiscountAmount,
            PointsDiscountAmount: pointsDiscountAmount,
            MaxRedeemablePoints: maxRedeemablePoints,
            FinalAmount: finalAmount,
            PointsToEarn: pointsToEarn,
            WillUpgradeTier: willUpgrade,
            NextTierName: nextTierName,
            Spend12MonthsAfter: spend12MonthsAfter,
            AmountNeededForNextTier: amountNeededForNext);
    }

    // INTEGRATION: TV3 gọi CommitCheckoutAsync khi Invoice chuyển Paid
    public async Task<CheckoutLoyaltyResult> CommitCheckoutAsync(CheckoutLoyaltyRequest req, CancellationToken ct = default)
    {
        var refCode = string.IsNullOrWhiteSpace(req.ReferenceCode)
            ? (req.InvoiceId.HasValue ? $"INV-{req.InvoiceId.Value}" : $"SIM-{DateTime.UtcNow:yyyyMMddHHmmssfff}")
            : req.ReferenceCode.Trim();

        // 1. Kiểm tra Idempotent theo ReferenceCode trên bút toán Earn
        var existingEarn = await _db.PointTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ReferenceCode == refCode && t.TransactionType == PointTransaction.Earn, ct);

        if (existingEarn != null)
        {
            var existingRedeem = await _db.PointTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReferenceCode == refCode && t.TransactionType == PointTransaction.Redeem, ct);
            var uSnap = await _db.Users.AsNoTracking().Include(u => u.Tier)
                .FirstOrDefaultAsync(u => u.Id == req.CustomerId, ct);

            int redeemedPrev = existingRedeem != null ? -existingRedeem.Amount : 0;
            decimal finalPrev = existingEarn.SpendAmount ?? (req.PlaytimeAmount + req.FandBAmount);
            decimal subPrev = req.PlaytimeAmount + req.FandBAmount;

            return new CheckoutLoyaltyResult(
                Success: true,
                Duplicate: true,
                Message: $"Hóa đơn [{refCode}] đã được ghi nhận trước đó (không cộng/trừ điểm trùng).",
                Subtotal: subPrev,
                TierDiscountAmount: 0,
                VoucherDiscountAmount: 0,
                PointsDiscountAmount: redeemedPrev * 1000m,
                TotalDiscount: Math.Max(0m, subPrev - finalPrev),
                FinalAmount: finalPrev,
                PointsRedeemed: redeemedPrev,
                PointsEarned: existingEarn.Amount,
                NewRewardPoints: uSnap?.RewardPoints ?? existingEarn.BalanceAfter,
                TierName: uSnap?.Tier?.TierName ?? "Member",
                UpgradedTier: false);
        }

        // 2. Validate lại toàn bộ như Preview
        var preview = await PreviewCheckoutAsync(req with { ReferenceCode = refCode }, ct);
        if (!preview.IsValid)
        {
            return new CheckoutLoyaltyResult(
                Success: false,
                Duplicate: false,
                Message: string.Join(" ", preview.Errors),
                Subtotal: req.PlaytimeAmount + req.FandBAmount,
                TierDiscountAmount: preview.TierDiscountAmount,
                VoucherDiscountAmount: preview.VoucherDiscountAmount,
                PointsDiscountAmount: preview.PointsDiscountAmount,
                TotalDiscount: 0,
                FinalAmount: preview.FinalAmount,
                PointsRedeemed: 0,
                PointsEarned: 0,
                NewRewardPoints: 0,
                TierName: preview.TierName,
                UpgradedTier: false);
        }

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var now = DateTime.UtcNow;

            // 3. Trừ điểm Redeem qua sổ cái (nếu có dùng điểm)
            if (req.PointsToRedeem > 0)
            {
                var redeemLedger = await _ledger.PostAsync(
                    customerId: req.CustomerId,
                    delta: -req.PointsToRedeem,
                    type: PointTransaction.Redeem,
                    referenceCode: refCode,
                    invoiceId: req.InvoiceId,
                    reason: $"Đổi {req.PointsToRedeem} điểm giảm hóa đơn {refCode}",
                    actorId: req.CashierId,
                    spendAmount: null,
                    ct: ct);

                if (!redeemLedger.Success)
                {
                    if (ownTx != null) await ownTx.RollbackAsync(ct);
                    return new CheckoutLoyaltyResult(false, false, redeemLedger.Message,
                        req.PlaytimeAmount + req.FandBAmount, 0, 0, 0, 0, preview.FinalAmount, 0, 0, redeemLedger.BalanceAfter, preview.TierName, false);
                }
            }

            // 4. Staff nhập mã quà Manager; đánh dấu đúng voucher sở hữu của hội viên là Used.
            if (!string.IsNullOrWhiteSpace(req.VoucherCode))
            {
                var vCode = req.VoucherCode.Trim().ToUpperInvariant();
                int voucherId = await _db.CustomerVouchers
                    .Where(v => v.CustomerId == req.CustomerId
                             && v.RewardItem != null
                             && v.RewardItem.Code == vCode
                             && v.RewardItem.IsActive
                             && v.Status == CustomerVoucher.StatusActive
                             && v.ExpiresAtUtc > now)
                    .OrderBy(v => v.ExpiresAtUtc)
                    .Select(v => v.Id)
                    .FirstOrDefaultAsync(ct);

                int vUpdated = await _db.CustomerVouchers
                    .Where(v => v.Id == voucherId && v.Status == CustomerVoucher.StatusActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.Status, CustomerVoucher.StatusUsed)
                        .SetProperty(v => v.UsedAtUtc, now)
                        .SetProperty(v => v.UsedById, req.CashierId)
                        .SetProperty(v => v.UsedReferenceCode, refCode), ct);

                if (vUpdated == 0)
                {
                    if (ownTx != null) await ownTx.RollbackAsync(ct);
                    return new CheckoutLoyaltyResult(false, false, $"Mã quà tặng {vCode} đã được sử dụng, hết hạn hoặc không thuộc hội viên này.",
                        req.PlaytimeAmount + req.FandBAmount, 0, 0, 0, 0, preview.FinalAmount, 0, 0, 0, preview.TierName, false);
                }
            }

            // 5. Ghi bút toán Earn (luôn ghi kể cả PointsToEarn == 0 để lưu SpendAmount 12 tháng và khóa Idempotent UX_PointTxn_Earn_Ref)
            var earnLedger = await _ledger.PostAsync(
                customerId: req.CustomerId,
                delta: preview.PointsToEarn,
                type: PointTransaction.Earn,
                referenceCode: refCode,
                invoiceId: req.InvoiceId,
                reason: $"Tích điểm thanh toán hóa đơn {refCode}",
                actorId: req.CashierId,
                spendAmount: preview.FinalAmount,
                ct: ct);

            if (!earnLedger.Success) throw new InvalidOperationException(earnLedger.Message);
            if (earnLedger.Duplicate)
            {
                if (ownTx != null) await ownTx.RollbackAsync(ct);
                return new CheckoutLoyaltyResult(
                    true, true, $"Hóa đơn [{refCode}] đã được ghi nhận trước đó.",
                    req.PlaytimeAmount + req.FandBAmount, preview.TierDiscountAmount, preview.VoucherDiscountAmount,
                    preview.PointsDiscountAmount, preview.TierDiscountAmount + preview.VoucherDiscountAmount + preview.PointsDiscountAmount,
                    preview.FinalAmount, req.PointsToRedeem, preview.PointsToEarn, earnLedger.BalanceAfter, preview.TierName, false);
            }

            // 6. Cập nhật TotalSpend trọn đời, LastPaidDate và xét hạng 12 tháng (F5)
            var user = await _db.Users.Include(u => u.Tier).FirstAsync(u => u.Id == req.CustomerId, ct);
            await _db.Entry(user).ReloadAsync(ct);
            user.TotalSpend += preview.FinalAmount;
            user.LastPaidDate = DateTime.UtcNow;

            var spend12M = await _tierEval.GetSpendLast12MonthsAsync(user.Id, null, ct);
            var (upgraded, _, finalTier) = await _tierEval.EvaluateUpgradeOnCheckoutAsync(user, spend12M, req.CashierId, ct);

            // 7. Gửi 1 Notification tổng hợp cho khách hàng
            decimal subtotal = req.PlaytimeAmount + req.FandBAmount;
            decimal totalDiscount = preview.TierDiscountAmount + preview.VoucherDiscountAmount + preview.PointsDiscountAmount;
            var parts = new List<string>();
            if (preview.TierDiscountAmount > 0) parts.Add($"giảm hạng {VnFormat.Money(preview.TierDiscountAmount)}");
            if (preview.VoucherDiscountAmount > 0) parts.Add($"voucher {VnFormat.Money(preview.VoucherDiscountAmount)}");
            if (req.PointsToRedeem > 0) parts.Add($"dùng {req.PointsToRedeem} điểm (-{VnFormat.Money(preview.PointsDiscountAmount)})");
            parts.Add($"+{preview.PointsToEarn} điểm");

            string summaryText = $"Hóa đơn {VnFormat.Money( preview.FinalAmount )} (gốc {VnFormat.Money(subtotal)}): {string.Join(", ", parts)}. Số dư hiện tại: {earnLedger.BalanceAfter} điểm.";

            _db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = $"Thanh toán thành công ({refCode})",
                Content = summaryText,
                Type = "Checkout",
                IsRead = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            if (ownTx != null) await ownTx.CommitAsync(ct);

            string finalTierName = finalTier?.TierName ?? preview.TierName;
            string resultMsg = upgraded
                ? $"{summaryText} Chúc mừng thăng hạng {finalTierName}!"
                : summaryText;

            return new CheckoutLoyaltyResult(
                Success: true,
                Duplicate: false,
                Message: resultMsg,
                Subtotal: subtotal,
                TierDiscountAmount: preview.TierDiscountAmount,
                VoucherDiscountAmount: preview.VoucherDiscountAmount,
                PointsDiscountAmount: preview.PointsDiscountAmount,
                TotalDiscount: totalDiscount,
                FinalAmount: preview.FinalAmount,
                PointsRedeemed: req.PointsToRedeem,
                PointsEarned: preview.PointsToEarn,
                NewRewardPoints: earnLedger.BalanceAfter,
                TierName: finalTierName,
                UpgradedTier: upgraded);
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    public async Task<PaymentLoyaltyResult> ProcessPaymentLoyaltyAsync(string customerId, decimal finalAmount, CancellationToken ct = default)
    {
        if (finalAmount < 0)
            return new PaymentLoyaltyResult(false, "Số tiền thanh toán không được âm.", 0, 0, 0, null, false);

        var refCode = $"LEGACY-PAY-{Guid.NewGuid():N}"[..32];
        var commit = await CommitCheckoutAsync(new CheckoutLoyaltyRequest(
            CustomerId: customerId,
            ReferenceCode: refCode,
            InvoiceId: null,
            PlaytimeAmount: 0m,
            FandBAmount: finalAmount,
            PointsToRedeem: 0,
            VoucherCode: null,
            CashierId: null), ct);

        if (!commit.Success)
            return new PaymentLoyaltyResult(false, commit.Message, 0, 0, 0, null, false);

        var totalSpend = await _db.Users.AsNoTracking().Where(u => u.Id == customerId).Select(u => u.TotalSpend).FirstOrDefaultAsync(ct);
        return new PaymentLoyaltyResult(
            true,
            commit.Message,
            commit.PointsEarned,
            commit.NewRewardPoints,
            totalSpend,
            commit.TierName,
            commit.UpgradedTier);
    }

    public async Task<BillRedeemResult> RedeemPointsForBillAsync(string customerId, int pointsToRedeem, decimal currentBillAmount, CancellationToken ct = default)
    {
        if (pointsToRedeem <= 0)
            return new BillRedeemResult(false, "Số điểm đổi phải lớn hơn 0.", 0, 0);

        if (currentBillAmount <= 0)
            return new BillRedeemResult(false, "Giá trị hóa đơn phải lớn hơn 0.", 0, 0);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == customerId, ct);
        if (user == null)
            return new BillRedeemResult(false, "Không tìm thấy khách hàng.", 0, 0);

        if (user.RewardPoints < pointsToRedeem)
            return new BillRedeemResult(false, $"Khách hàng chỉ có {user.RewardPoints} điểm, không đủ để đổi {pointsToRedeem} điểm.", 0, user.RewardPoints);

        decimal discountAmount = pointsToRedeem * 1000m;
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

        var ledgerRes = await _ledger.PostAsync(
            customerId: customerId,
            delta: -pointsToRedeem,
            type: PointTransaction.Redeem,
            referenceCode: $"BILL-REDEEM-{Guid.NewGuid():N}"[..32],
            invoiceId: null,
            reason: $"Đổi điểm giảm trừ hóa đơn {VnFormat.Money(currentBillAmount)}",
            actorId: null,
            spendAmount: null,
            ct: ct);

        if (!ledgerRes.Success)
            return new BillRedeemResult(false, ledgerRes.Message, 0, ledgerRes.BalanceAfter);

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
            ledgerRes.BalanceAfter);
    }

    public async Task<int> ProcessPointExpirationsAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-100);

        var expiredUsers = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.RewardPoints > 0 && u.LastPaidDate != null && u.LastPaidDate < threshold)
            .Select(u => new { u.Id, u.RewardPoints, u.LastPaidDate })
            .ToListAsync(ct);

        if (expiredUsers.Count == 0) return 0;

        var now = DateTime.UtcNow;
        int processed = 0;

        foreach (var u in expiredUsers)
        {
            var res = await _ledger.PostAsync(
                customerId: u.Id,
                delta: -u.RewardPoints,
                type: PointTransaction.Expire,
                referenceCode: $"EXP-{now:yyyyMMdd}-{u.Id}"[..Math.Min(50, $"EXP-{now:yyyyMMdd}-{u.Id}".Length)],
                invoiceId: null,
                reason: "Thu hồi điểm thưởng do không giao dịch trên 100 ngày",
                actorId: null,
                spendAmount: null,
                ct: ct);

            if (res.Success && !res.Duplicate)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = u.Id,
                    Title = "Thu hồi điểm thưởng (>100 ngày không giao dịch)",
                    Content = $"Số điểm thưởng ({u.RewardPoints} điểm) của bạn đã bị thu hồi do hồ sơ không phát sinh giao dịch trên 100 ngày (tính từ lần cuối ngày {u.LastPaidDate?.ToVn():dd/MM/yyyy}).",
                    Type = "Expire",
                    IsRead = false,
                    CreatedAt = now
                });
                processed++;
            }
        }

        if (processed > 0)
            await _db.SaveChangesAsync(ct);

        return processed;
    }
}
