using System.Security.Cryptography;
using System.Text.Json;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public record VoucherActionResult(bool Success, string Message, CustomerVoucher? Voucher, int RemainingPoints);

public interface IVoucherService
{
    /// <summary>Đổi điểm lấy quà trong bảng RewardItems -> phát hành CustomerVoucher kèm mã 8 ký tự.</summary>
    Task<VoucherActionResult> RedeemRewardForVoucherAsync(string customerId, string rewardCode, CancellationToken ct = default);

    /// <summary>Nhân viên xác nhận đã giao quà (chỉ áp dụng cho VoucherType == 'Gift').</summary>
    Task<VoucherActionResult> ConfirmGiftDeliveryAsync(string voucherCode, string staffId, CancellationToken ct = default);

    /// <summary>Admin hủy voucher chưa dùng -> hoàn điểm qua sổ cái loại Reverse (nếu voucher được đổi bằng điểm).</summary>
    Task<VoucherActionResult> CancelVoucherAsync(int voucherId, string adminId, string? reason = null, CancellationToken ct = default);

    /// <summary>Job hằng ngày chuyển các voucher quá hạn sang trạng thái Expired (không hoàn điểm).</summary>
    Task<int> ExpireOverdueVouchersAsync(DateTime? nowUtc = null, CancellationToken ct = default);

    /// <summary>Sinh mã voucher duy nhất (vd: BMS-7KQ2-9XHD, loại bỏ 0/O/1/I).</summary>
    Task<string> GenerateUniqueVoucherCodeAsync(CancellationToken ct = default);
}

public class VoucherService : IVoucherService
{
    private const string SafeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly ApplicationDbContext _db;
    private readonly IPointLedgerService _ledger;

    public VoucherService(ApplicationDbContext db, IPointLedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<VoucherActionResult> RedeemRewardForVoucherAsync(string customerId, string rewardCode, CancellationToken ct = default)
    {
        var code = (rewardCode ?? string.Empty).Trim();
        var item = await _db.RewardItems.AsNoTracking()
            .Include(r => r.MinTier)
            .FirstOrDefaultAsync(r => r.Code == code && r.IsActive, ct);

        var currentPoints = await GetPointsAsync(customerId, ct);
        if (item == null)
            return new VoucherActionResult(false, "Phần quà không tồn tại hoặc đã ngừng áp dụng.", null, currentPoints);

        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.Id == customerId, ct);

        if (user == null || !user.IsActive)
            return new VoucherActionResult(false, "Tài khoản không khả dụng để đổi điểm.", null, currentPoints);

        // Kiểm tra điều kiện hạng tối thiểu (MinTierId)
        if (item.MinTierId.HasValue && item.MinTier != null)
        {
            var userThreshold = user.Tier?.SpendThreshold ?? 0m;
            if (userThreshold < item.MinTier.SpendThreshold)
            {
                return new VoucherActionResult(false,
                    $"Ưu đãi \"{item.Name}\" chỉ dành cho hội viên hạng {item.MinTier.TierName} trở lên.",
                    null, user.RewardPoints);
            }
        }

        if (user.RewardPoints < item.PointCost)
        {
            return new VoucherActionResult(false,
                $"Bạn chưa đủ điểm để đổi \"{item.Name}\" (cần {item.PointCost} điểm, hiện có {user.RewardPoints}).",
                null, user.RewardPoints);
        }

        if (item.PointCost == 0)
        {
            var nowCheck = DateTime.UtcNow;
            bool hasActiveFreeVoucher = await _db.CustomerVouchers.AsNoTracking()
                .AnyAsync(v => v.CustomerId == customerId
                            && v.RewardItemId == item.Id
                            && v.Status == CustomerVoucher.StatusActive
                            && v.ExpiresAtUtc > nowCheck, ct);
            if (hasActiveFreeVoucher)
            {
                return new VoucherActionResult(false,
                    $"Hội viên đang có mã ưu đãi \"{item.Name}\" (0 điểm) còn hiệu lực chưa sử dụng.",
                    null, user.RewardPoints);
            }
        }

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var voucherCode = await GenerateUniqueVoucherCodeAsync(ct);
            var ledgerRes = await _ledger.PostAsync(
                customerId: customerId,
                delta: -item.PointCost,
                type: PointTransaction.Redeem,
                referenceCode: $"VOUCHER-{voucherCode}",
                invoiceId: null,
                reason: $"Đổi quà: {item.Name} ({voucherCode})",
                actorId: customerId,
                spendAmount: null,
                ct: ct);

            if (!ledgerRes.Success)
            {
                if (ownTx != null) await ownTx.RollbackAsync(ct);
                return new VoucherActionResult(false, ledgerRes.Message, null, ledgerRes.BalanceAfter);
            }

            var now = DateTime.UtcNow;
            var voucher = new CustomerVoucher
            {
                Code = voucherCode,
                CustomerId = customerId,
                RewardItemId = item.Id,
                Title = item.Name,
                VoucherType = item.RewardType,
                DiscountValue = item.DiscountValue,
                Status = CustomerVoucher.StatusActive,
                IssuedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7),
                PointTransactionId = ledgerRes.TransactionId
            };

            _db.CustomerVouchers.Add(voucher);

            _db.Notifications.Add(new Notification
            {
                UserId = customerId,
                Title = "Đổi điểm thành công — Mã voucher của bạn",
                Content = $"Bạn đã đổi {item.PointCost} điểm lấy \"{item.Name}\". Mã voucher: {voucherCode} (hạn dùng {voucher.ExpiresAtUtc.ToVn():dd/MM/yyyy}). Đưa mã hoặc QR cho nhân viên tại quầy.",
                Type = "Redeem",
                IsRead = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            if (ownTx != null) await ownTx.CommitAsync(ct);

            return new VoucherActionResult(true,
                $"Đổi thành công \"{item.Name}\" (-{item.PointCost} điểm). Mã voucher: {voucherCode}",
                voucher,
                ledgerRes.BalanceAfter);
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

    public async Task<VoucherActionResult> ConfirmGiftDeliveryAsync(string voucherCode, string staffId, CancellationToken ct = default)
    {
        var norm = (voucherCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(norm))
            return new VoucherActionResult(false, "Vui lòng nhập mã voucher.", null, 0);

        var voucher = await _db.CustomerVouchers
            .Include(v => v.Customer)
                .ThenInclude(u => u!.Tier)
            .Include(v => v.RewardItem)
                .ThenInclude(r => r!.MinTier)
            .FirstOrDefaultAsync(v => v.Code == norm, ct);

        if (voucher == null)
            return new VoucherActionResult(false, $"Không tìm thấy voucher có mã {norm}.", null, 0);

        var now = DateTime.UtcNow;
        if (voucher.Status == CustomerVoucher.StatusUsed)
            return new VoucherActionResult(false, $"Voucher {norm} đã được sử dụng lúc {voucher.UsedAtUtc?.ToVn():dd/MM/yyyy HH:mm}.", voucher, voucher.Customer?.RewardPoints ?? 0);

        if (voucher.Status == CustomerVoucher.StatusCancelled)
            return new VoucherActionResult(false, $"Voucher {norm} đã bị hủy.", voucher, voucher.Customer?.RewardPoints ?? 0);

        if (voucher.Status == CustomerVoucher.StatusExpired || voucher.ExpiresAtUtc <= now)
        {
            if (voucher.Status == CustomerVoucher.StatusActive)
            {
                voucher.Status = CustomerVoucher.StatusExpired;
                await _db.SaveChangesAsync(ct);
            }
            return new VoucherActionResult(false, $"Voucher {norm} đã hết hạn vào {voucher.ExpiresAtUtc.ToVn():dd/MM/yyyy}.", voucher, voucher.Customer?.RewardPoints ?? 0);
        }

        if (voucher.RewardItem != null && !voucher.RewardItem.IsActive)
        {
            return new VoucherActionResult(false,
                $"Voucher {norm} ({voucher.Title}) thuộc danh mục quà tặng đang tạm đóng, không thể sử dụng.",
                voucher, voucher.Customer?.RewardPoints ?? 0);
        }

        if (voucher.RewardItem?.MinTier != null)
        {
            decimal userThreshold = voucher.Customer?.Tier?.SpendThreshold ?? 0m;
            if (userThreshold < voucher.RewardItem.MinTier.SpendThreshold)
            {
                string currentTierName = voucher.Customer?.Tier?.TierName ?? "Member";
                return new VoucherActionResult(false,
                    $"Không thể sử dụng voucher {norm} ({voucher.Title}): Voucher này yêu cầu từ Hạng {voucher.RewardItem.MinTier.TierName} trở lên, nhưng hội viên hiện đang ở Hạng {currentTierName}.",
                    voucher, voucher.Customer?.RewardPoints ?? 0);
            }
        }

        if (voucher.VoucherType != RewardItemEntity.TypeGift)
        {
            return new VoucherActionResult(false,
                $"Voucher {norm} thuộc loại giảm hóa đơn ({voucher.VoucherType}), chỉ được áp dụng khi Thanh toán (Checkout), không xác nhận giao quà tay.",
                voucher, voucher.Customer?.RewardPoints ?? 0);
        }

        // Atomic update để tránh 2 nhân viên bấm cùng lúc
        int updated = await _db.CustomerVouchers
            .Where(v => v.Id == voucher.Id && v.Status == CustomerVoucher.StatusActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.Status, CustomerVoucher.StatusUsed)
                .SetProperty(v => v.UsedAtUtc, now)
                .SetProperty(v => v.UsedById, staffId), ct);

        if (updated == 0)
            return new VoucherActionResult(false, $"Voucher {norm} đã được sử dụng hoặc không còn hiệu lực.", voucher, voucher.Customer?.RewardPoints ?? 0);

        await _db.Entry(voucher).ReloadAsync(ct);

        _db.CustomerAuditLogs.Add(new CustomerAuditLog
        {
            CustomerId = voucher.CustomerId,
            Action = "VoucherUsed",
            Detail = JsonSerializer.Serialize(new { code = voucher.Code, title = voucher.Title }),
            ActorId = staffId,
            CreatedAtUtc = now
        });

        await _db.SaveChangesAsync(ct);

        return new VoucherActionResult(true,
            $"Đã xác nhận giao quà \"{voucher.Title}\" (mã {voucher.Code}) cho khách {voucher.Customer?.FullName}.",
            voucher, voucher.Customer?.RewardPoints ?? 0);
    }

    public async Task<VoucherActionResult> CancelVoucherAsync(int voucherId, string adminId, string? reason = null, CancellationToken ct = default)
    {
        var voucher = await _db.CustomerVouchers
            .Include(v => v.PointTransaction)
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == voucherId, ct);

        if (voucher == null)
            return new VoucherActionResult(false, "Không tìm thấy voucher.", null, 0);

        if (voucher.Status != CustomerVoucher.StatusActive)
            return new VoucherActionResult(false, $"Chỉ có thể hủy voucher đang Active (trạng thái hiện tại: {voucher.Status}).", voucher, voucher.Customer?.RewardPoints ?? 0);

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var now = DateTime.UtcNow;
            voucher.Status = CustomerVoucher.StatusCancelled;
            voucher.UsedAtUtc = now;
            voucher.UsedById = adminId;

            int refundedPoints = 0;
            int balanceAfter = voucher.Customer?.RewardPoints ?? 0;

            if (voucher.PointTransaction != null && voucher.PointTransaction.Amount < 0)
            {
                refundedPoints = -voucher.PointTransaction.Amount;
            }
            else if (voucher.RewardItemId.HasValue)
            {
                var itemCost = await _db.RewardItems.AsNoTracking()
                    .Where(r => r.Id == voucher.RewardItemId.Value)
                    .Select(r => r.PointCost)
                    .FirstOrDefaultAsync(ct);
                refundedPoints = itemCost;
            }

            if (refundedPoints > 0)
            {
                var ledgerRes = await _ledger.PostAsync(
                    customerId: voucher.CustomerId,
                    delta: refundedPoints,
                    type: PointTransaction.Reverse,
                    referenceCode: $"REV-VOUCHER-{voucher.Code}",
                    invoiceId: null,
                    reason: string.IsNullOrWhiteSpace(reason) ? $"Hoàn điểm hủy voucher {voucher.Code}" : reason,
                    actorId: adminId,
                    spendAmount: null,
                    ct: ct);

                balanceAfter = ledgerRes.BalanceAfter;
            }

            _db.CustomerAuditLogs.Add(new CustomerAuditLog
            {
                CustomerId = voucher.CustomerId,
                Action = "VoucherCancel",
                Detail = JsonSerializer.Serialize(new { code = voucher.Code, refundedPoints, reason }),
                ActorId = adminId,
                CreatedAtUtc = now
            });

            await _db.SaveChangesAsync(ct);
            if (ownTx != null) await ownTx.CommitAsync(ct);

            var msg = refundedPoints > 0
                ? $"Đã hủy voucher {voucher.Code} và hoàn +{refundedPoints} điểm cho khách hàng."
                : $"Đã hủy voucher {voucher.Code}.";

            return new VoucherActionResult(true, msg, voucher, balanceAfter);
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

    public async Task<int> ExpireOverdueVouchersAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return await _db.CustomerVouchers
            .Where(v => v.Status == CustomerVoucher.StatusActive && v.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CustomerVoucher.StatusExpired), ct);
    }

    public async Task<string> GenerateUniqueVoucherCodeAsync(CancellationToken ct = default)
    {
        var chars = new char[8];
        for (int attempt = 0; attempt < 20; attempt++)
        {
            for (int i = 0; i < 8; i++)
            {
                chars[i] = SafeAlphabet[RandomNumberGenerator.GetInt32(SafeAlphabet.Length)];
            }
            var code = $"BMS-{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
            bool exists = await _db.CustomerVouchers.AnyAsync(v => v.Code == code, ct);
            if (!exists) return code;
        }
        var fallback = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"BMS-{fallback[..4]}-{fallback[4..8]}";
    }

    private Task<int> GetPointsAsync(string customerId, CancellationToken ct) =>
        _db.Users.AsNoTracking().Where(u => u.Id == customerId)
            .Select(u => u.RewardPoints).FirstOrDefaultAsync(ct);
}
