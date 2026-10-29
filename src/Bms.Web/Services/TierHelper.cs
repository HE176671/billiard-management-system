using System.Globalization;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Tiện ích xếp hạng thành viên dùng chung (TierConfig, CustomerManagement, và LoyaltyService ở Bước 5).</summary>
public static class TierHelper
{
    /// <summary>Hạng cao nhất có SpendThreshold &lt;= totalSpend. Trả về null nếu chưa có hạng nào.</summary>
    public static MembershipTier? Resolve(IEnumerable<MembershipTier> tiers, decimal totalSpend) =>
        tiers.Where(t => t.SpendThreshold <= totalSpend)
             .OrderByDescending(t => t.SpendThreshold)
             .FirstOrDefault();

    /// <summary>
    /// Quét toàn bộ khách hàng (role Customer) và cập nhật lại TierId theo TotalSpend hiện có.
    /// Trả về số khách có hạng thay đổi.
    /// </summary>
    public static async Task<int> RecalculateUserTiersAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var tiers = await db.MembershipTiers.AsNoTracking().ToListAsync(ct);
        if (tiers.Count == 0) return 0;

        var roleId = await db.Roles.Where(r => r.Name == "Customer")
                                   .Select(r => r.Id).FirstOrDefaultAsync(ct);

        var users = await db.Users
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
            .ToListAsync(ct);

        int changed = 0;
        foreach (var u in users)
        {
            var tier = Resolve(tiers, u.TotalSpend);
            if (tier != null && u.TierId != tier.Id)
            {
                u.TierId = tier.Id;
                changed++;
            }
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}

/// <summary>Định dạng tiền VNĐ.</summary>
public static class VnFormat
{
    private static readonly CultureInfo Vi = new("vi-VN");
    public static string Money(decimal value) => value.ToString("N0", Vi) + " ₫";
    public static string Number(decimal value) => value.ToString("N0", Vi);

    /// <summary>UTC (từ DB) → giờ địa phương, dạng dd/MM/yyyy HH:mm.</summary>
    public static string LocalDateTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    /// <summary>Style inline cho badge hạng thành viên.</summary>
    public static string TierBadgeStyle(string? tierName) => tierName switch
    {
        "Gold"   => "background:#fef3c7;color:#b45309",
        "Silver" => "background:#e2e8f0;color:#475569",
        "Member" => "background:#dbeafe;color:#1d4ed8",
        _        => "background:#f1f5f9;color:#64748b"
    };
}
