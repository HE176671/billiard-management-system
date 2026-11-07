using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Bms.Web.Services;

public record CustomerRfmProfile(
    string CustomerId,
    string FullName,
    string? PhoneNumber,
    string? CardCode,
    string TierName,
    int? TierId,
    int RewardPoints,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastPaidDate,
    int? RecencyDays,
    int Frequency12M,
    decimal Monetary12M,
    string Segment,
    string BadgeStyle,
    string SuggestedAction,
    string? Email = null,
    DateOnly? DateOfBirth = null);

public interface ICustomerSegmentService
{
    /// <summary>Tính phân khúc RFM cố định theo thứ tự ưu tiên bảng F6.</summary>
    string ClassifySegment(DateTime createdAtUtc, DateTime? lastPaidDateUtc, int frequency12M, DateTime? nowUtc = null);

    /// <summary>Lấy danh sách RFM cho toàn bộ khách hàng (có cache IMemoryCache 10 phút).</summary>
    Task<IReadOnlyList<CustomerRfmProfile>> GetAllProfilesAsync(bool forceRefresh = false, DateTime? nowUtc = null, CancellationToken ct = default);

    /// <summary>Lấy thông tin RFM của 1 khách hàng.</summary>
    Task<CustomerRfmProfile?> GetProfileAsync(string customerId, DateTime? nowUtc = null, CancellationToken ct = default);

    /// <summary>Xóa cache phân khúc khi có giao dịch mới.</summary>
    void InvalidateCache();
}

public class CustomerSegmentService : ICustomerSegmentService
{
    public const string SegNew = "New";
    public const string SegChampion = "Champion";
    public const string SegLoyal = "Loyal";
    public const string SegAtRisk = "AtRisk";
    public const string SegLost = "Lost";
    public const string SegRegular = "Regular";

    private const string CacheKey = "TV2_CRM_RFM_PROFILES";
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public CustomerSegmentService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public string ClassifySegment(DateTime createdAtUtc, DateTime? lastPaidDateUtc, int frequency12M, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        double accountAgeDays = (now - createdAtUtc).TotalDays;
        int? rDays = lastPaidDateUtc.HasValue ? (int)Math.Floor((now - lastPaidDateUtc.Value).TotalDays) : null;

        // 1. Khách mới (New): Tạo hồ sơ <= 30 ngày và F <= 1
        if (accountAgeDays <= 30.0 && frequency12M <= 1)
            return SegNew;

        if (rDays.HasValue)
        {
            // 2. VIP (Champion): R <= 14 và F >= 8
            if (rDays.Value <= 14 && frequency12M >= 8)
                return SegChampion;

            // 3. Thân thiết (Loyal): Có ghé quán trong 30 ngày gần nhất (R <= 30)
            if (rDays.Value <= 30)
                return SegLoyal;
        }

        // 4. Lâu rồi chưa ghé (AtRisk): Đã quá 30 ngày chưa ghé quán (hoặc chưa từng phát sinh hóa đơn sau 30 ngày)
        return SegAtRisk;
    }

    public static (string BadgeStyle, string Action) GetSegmentMeta(string segment) => segment switch
    {
        SegNew      => ("background:#dbeafe;color:#1d4ed8", "Tặng ưu đãi lần ghé thứ 2"),
        SegChampion => ("background:#f3e8ff;color:#7e22ce", "Ưu tiên giữ bàn VIP"),
        SegLoyal    => ("background:#dcfce7;color:#15803d", "Duy trì chăm sóc định kỳ"),
        SegAtRisk   => ("background:#ffedd5;color:#c2410c", "Gửi voucher mời quay lại"),
        _           => ("background:#dcfce7;color:#15803d", "Duy trì chăm sóc định kỳ")
    };

    public async Task<IReadOnlyList<CustomerRfmProfile>> GetAllProfilesAsync(
        bool forceRefresh = false, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        if (!forceRefresh && nowUtc == null && _cache.TryGetValue(CacheKey, out IReadOnlyList<CustomerRfmProfile>? cached) && cached != null)
        {
            return cached;
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var since = now.AddDays(-365);

        var customerRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == "Customer")
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Tier)
            .Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId))
            .ToListAsync(ct);

        var earnStats = await _db.PointTransactions.AsNoTracking()
            .Where(t => t.TransactionType == PointTransaction.Earn && t.CreatedAt >= since && t.CreatedAt <= now)
            .GroupBy(t => t.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Frequency = g.Count(t => t.ReferenceCode != null),
                TotalCount = g.Count(),
                Monetary = g.Sum(t => t.SpendAmount ?? 0m),
                MaxDate = g.Max(t => (DateTime?)t.CreatedAt)
            })
            .ToDictionaryAsync(x => x.CustomerId, ct);

        var profiles = new List<CustomerRfmProfile>(users.Count);
        foreach (var u in users)
        {
            earnStats.TryGetValue(u.Id, out var st);
            int freq = st != null ? (st.Frequency > 0 ? st.Frequency : st.TotalCount) : 0;
            decimal monetary = st?.Monetary ?? 0m;
            DateTime? lastPaid = u.LastPaidDate ?? st?.MaxDate;
            int? rDays = lastPaid.HasValue ? Math.Max(0, (int)Math.Floor((now - lastPaid.Value).TotalDays)) : null;

            string seg = ClassifySegment(u.CreatedAtUtc, lastPaid, freq, now);
            var (style, action) = GetSegmentMeta(seg);

            profiles.Add(new CustomerRfmProfile(
                CustomerId: u.Id,
                FullName: u.FullName,
                PhoneNumber: u.PhoneNumber,
                CardCode: u.EmployeeCode,
                TierName: u.Tier?.TierName ?? "Member",
                TierId: u.TierId,
                RewardPoints: u.RewardPoints,
                IsActive: u.IsActive,
                CreatedAtUtc: u.CreatedAtUtc,
                LastPaidDate: lastPaid,
                RecencyDays: rDays,
                Frequency12M: freq,
                Monetary12M: monetary,
                Segment: seg,
                BadgeStyle: style,
                SuggestedAction: action,
                Email: u.Email != null && u.Email.EndsWith("@noemail.bms.local", StringComparison.OrdinalIgnoreCase) ? null : u.Email,
                DateOfBirth: u.DateOfBirth));
        }

        if (nowUtc == null)
        {
            _cache.Set(CacheKey, (IReadOnlyList<CustomerRfmProfile>)profiles, TimeSpan.FromMinutes(10));
        }

        return profiles;
    }

    public async Task<CustomerRfmProfile?> GetProfileAsync(string customerId, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var all = await GetAllProfilesAsync(forceRefresh: true, nowUtc: nowUtc, ct: ct);
        return all.FirstOrDefault(p => p.CustomerId == customerId);
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);
}

