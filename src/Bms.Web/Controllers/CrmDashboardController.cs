using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager")]
public class CrmDashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICustomerSegmentService _segmentService;
    private readonly ICampaignService _campaignService;

    public CrmDashboardController(
        ApplicationDbContext db,
        ICustomerSegmentService segmentService,
        ICampaignService campaignService)
    {
        _db = db;
        _segmentService = segmentService;
        _campaignService = campaignService;
    }

    /// <summary>TV5: màu hạng theo thứ tự (thấp → cao), dùng chung cho Dashboard và Thống kê hội viên.</summary>
    public static readonly string[] TierPalette = { "#b45309", "#94a3b8", "#f59e0b", "#0ea5e9", "#7c3aed", "#db2777" };

    /// <summary>TV5: nhóm hiển thị riêng trên màn thống kê — có tài khoản nhưng chưa thanh toán lần nào.</summary>
    public const string NeverVisitedKey = "NeverVisited";

    private static bool IsNeverVisited(CustomerRfmProfile p) =>
        p.Segment == CustomerSegmentService.SegAtRisk && p.RecencyDays == null && p.Frequency12M == 0;

    // GET /CrmDashboard
    [HttpGet]
    public async Task<IActionResult> Index(int topPage = 1, int riskPage = 1)
    {
        const int pageSize = 10;
        var now = DateTime.UtcNow;
        var todayVn = VnTime.Today;
        var monthStartUtc = VnTime.StartOfDayUtc(new DateOnly(todayVn.Year, todayVn.Month, 1));
        var thirtyDaysAgoUtc = now.AddDays(-30);

        var rawProfiles = await _segmentService.GetAllProfilesAsync(forceRefresh: true);

        // TV5: hội viên chưa gán TierId được coi là hạng thấp nhất (giống TierEvaluationService),
        // hiển thị đúng tên hạng trong DB thay cho chữ "Member" mặc định.
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ToListAsync();
        var lowestTier = tiers.FirstOrDefault();
        var profiles = rawProfiles
            .Select(p => lowestTier != null && (p.TierId == null || !tiers.Any(t => t.Id == p.TierId))
                ? p with { TierId = lowestTier.Id, TierName = lowestTier.TierName }
                : p)
            .ToList();

        int totalMembers = profiles.Count;
        int newThisMonth = profiles.Count(p => p.CreatedAtUtc >= monthStartUtc);
        int totalCirculatingPoints = profiles.Sum(p => p.RewardPoints);

        // Tỷ lệ quay lại 30 ngày (= khách có >= 2 hóa đơn trong 30 ngày / khách có >= 1)
        var earn30Days = await _db.PointTransactions.AsNoTracking()
            .Where(t => t.TransactionType == PointTransaction.Earn && t.CreatedAt >= thirtyDaysAgoUtc)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToListAsync();

        int active30 = earn30Days.Count(x => x.Count >= 1);
        int repeat30 = earn30Days.Count(x => x.Count >= 2);
        decimal repeatRate = active30 > 0
            ? Math.Round((decimal)repeat30 / active30 * 100m, 1)
            : 0m;

        // Phân bố phân khúc RFM (4 nhóm: VIP, Thân thiết, Khách mới, Lâu rồi chưa ghé)
        var segOrder = new[]
        {
            CustomerSegmentService.SegChampion,
            CustomerSegmentService.SegLoyal,
            CustomerSegmentService.SegNew,
            CustomerSegmentService.SegAtRisk
        };
        var segCounts = segOrder.ToDictionary(
            s => s,
            s => profiles.Count(p => string.Equals(p.Segment, s, StringComparison.OrdinalIgnoreCase)
                                     && !(s == CustomerSegmentService.SegAtRisk && IsNeverVisited(p))));
        // TV5: tách riêng hội viên chưa thanh toán lần nào khỏi nhóm "Lâu rồi chưa ghé"
        segCounts[NeverVisitedKey] = profiles.Count(IsNeverVisited);

        // Phân bố hạng
        // TV5: màu theo thứ tự hạng (thấp → cao); khách chưa gán TierId tính vào hạng thấp nhất,
        // để tổng các cột luôn bằng tổng số hội viên.
        var palette = TierPalette;
        var tierPoints = tiers.Select((t, i) => new TierDistributionPoint
        {
            TierName = t.TierName,
            Count = profiles.Count(p => p.TierId == t.Id),
            ColorHex = palette[i % palette.Length]
        }).ToList();

        var allSpenders = profiles
            .Where(p => p.Monetary12M > 0)
            .OrderByDescending(p => p.Monetary12M)
            .ToList();
        int topTotalPages = Math.Max(1, (int)Math.Ceiling(allSpenders.Count / (double)pageSize));
        topPage = Math.Clamp(topPage, 1, topTotalPages);
        var topSpenders = allSpenders
            .Skip((topPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var allAtRisk = profiles
            .Where(p => p.Segment == CustomerSegmentService.SegAtRisk && !IsNeverVisited(p))
            .OrderByDescending(p => p.Monetary12M)
            .ToList();
        int riskTotalPages = Math.Max(1, (int)Math.Ceiling(allAtRisk.Count / (double)pageSize));
        riskPage = Math.Clamp(riskPage, 1, riskTotalPages);
        var atRiskTop = allAtRisk
            .Skip((riskPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var vm = new CrmDashboardViewModel
        {
            TotalMembers = totalMembers,
            NewMembersThisMonth = newThisMonth,
            RepeatRate30DaysPercent = repeatRate,
            RepeatCustomers30Days = repeat30,
            ActiveCustomers30Days = active30,
            TotalCirculatingPoints = totalCirculatingPoints,
            SegmentCounts = segCounts,
            TierCounts = tierPoints,
            TopSpenders12M = topSpenders,
            TopSpendersPage = topPage,
            TopSpendersTotalPages = topTotalPages,
            TopSpendersTotalCount = allSpenders.Count,
            HighValueAtRisk = atRiskTop,
            AtRiskPage = riskPage,
            AtRiskTotalPages = riskTotalPages,
            AtRiskTotalCount = allAtRisk.Count,
            PageSize = pageSize
        };

        return View(vm);
    }

    // POST /CrmDashboard/RunDailyJobNow — TV5 BR-17h: Manager chạy chu kỳ cuối ngày ngay (không phải chờ 00:05).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunDailyJobNow([FromServices] IDailyCycleService cycle)
    {
        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var result = await cycle.RunAsync(actorId, manual: true);
        var summary = result.Summary;
        _segmentService.InvalidateCache();

        TempData["Success"] =
            $"Đã chạy chu kỳ cuối ngày. " +
            $"Voucher sinh nhật: +{summary.BirthdayVouchersIssued} · " +
            $"Voucher quá hạn: {summary.VouchersExpired} · " +
            $"Xét hạng (giữ hạng: {summary.TierSummary.RenewedCount}, ân hạn: {summary.TierSummary.EnteredGraceCount}, hạ hạng: {summary.TierSummary.DowngradedCount}) · " +
            $"Email: gửi {result.EmailsSent}, chế độ demo/bỏ qua {result.EmailsSkipped}, lỗi {result.EmailsFailed}. Đã ghi Nhật ký hệ thống.";

        return RedirectToAction(nameof(Index));
    }
}

