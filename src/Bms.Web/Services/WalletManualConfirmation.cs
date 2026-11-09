using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public partial class WalletService
{
    public async Task<WalletResult> ConfirmTransferManuallyAsync(string referenceCode,
        decimal receivedAmount, string actorId, CancellationToken ct = default)
    {
        var reference = (referenceCode ?? "").Trim().ToUpperInvariant();
        if (reference.Length is 0 or > 50)
            return new(false, false, "Mã yêu cầu nạp không hợp lệ.");
        if (receivedAmount < 1000 || receivedAmount > 100_000_000 || receivedAmount != decimal.Truncate(receivedAmount))
            return new(false, false, "Số tiền thực nhận phải là số nguyên từ 1.000đ đến 100.000.000đ.");

        // Do not trust a posted staff ID: the controller supplies the signed-in identity,
        // and the service verifies the current account and role before crediting funds.
        var authorized = await (from user in db.Users
                                join userRole in db.UserRoles on user.Id equals userRole.UserId
                                join role in db.Roles on userRole.RoleId equals role.Id
                                where user.Id == actorId && user.IsActive &&
                                    (role.Name == "Staff" || role.Name == "Manager" || role.Name == "Admin")
                                select user.Id).AnyAsync(ct);
        if (!authorized) return new(false, false, "Tài khoản không có quyền xác nhận tiền nạp.");

        // Stable internal confirmation ID, not a bank transaction ID. Reusing the
        // same deposit request must never credit the customer's wallet twice.
        return await ConfirmTransferCoreAsync(reference, "MANUAL-" + reference, receivedAmount, actorId, ct);
    }
}
