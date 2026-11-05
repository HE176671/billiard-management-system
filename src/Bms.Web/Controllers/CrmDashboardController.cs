using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
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

    // GET /CrmDashboard
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var todayVn = VnTime.Today;
        var monthStartUtc = VnTime.StartOfDayUtc(new DateOnly(todayVn.Year, todayVn.Month, 1));
        var thirtyDaysAgoUtc = now.AddDays(-30);

        var profiles = await _segmentService.GetAllProfilesAsync(forceRefresh: true);

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
            s => profiles.Count(p => string.Equals(p.Segment, s, StringComparison.OrdinalIgnoreCase)));

        // Phân bố hạng
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ToListAsync();
        var tierColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Member"] = "#3b82f6",
            ["Silver"] = "#94a3b8",
            ["Gold"] = "#f59e0b"
        };
        var tierPoints = tiers.Select(t => new TierDistributionPoint
        {
            TierName = t.TierName,
            Count = profiles.Count(p => p.TierId == t.Id),
            ColorHex = tierColors.GetValueOrDefault(t.TierName, "#64748b")
        }).ToList();

        // Line chart Điểm Earn vs Redeem 6 tháng gần nhất
        var monthlyFlow = new List<MonthlyPointsFlowPoint>();
        for (int i = -5; i <= 0; i++)
        {
            var mDate = new DateOnly(todayVn.Year, todayVn.Month, 1).AddMonths(i);
            var mStart = VnTime.StartOfDayUtc(mDate);
            var mEnd = VnTime.StartOfDayUtc(mDate.AddMonths(1));

            var txns = await _db.PointTransactions.AsNoTracking()
                .Where(t => t.CreatedAt >= mStart && t.CreatedAt < mEnd)
                .GroupBy(t => t.TransactionType)
                .Select(g => new { Type = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync();

            int earned = txns.Where(x => x.Type == PointTransaction.Earn || x.Type == PointTransaction.Bonus || (x.Type == PointTransaction.Adjust && x.Sum > 0)).Sum(x => Math.Max(0, x.Sum));
            int redeemed = txns.Where(x => x.Type == PointTransaction.Redeem).Sum(x => Math.Abs(x.Sum));

            monthlyFlow.Add(new MonthlyPointsFlowPoint
            {
                MonthLabel = $"{mDate.Month:D2}/{mDate.Year}",
                EarnedPoints = earned,
                RedeemedPoints = redeemed
            });
        }

        var topSpenders = profiles
            .Where(p => p.Monetary12M > 0)
            .OrderByDescending(p => p.Monetary12M)
            .Take(10)
            .ToList();

        var atRiskTop = profiles
            .Where(p => p.Segment == CustomerSegmentService.SegAtRisk)
            .OrderByDescending(p => p.Monetary12M)
            .Take(10)
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
            MonthlyPointsFlow = monthlyFlow,
            TopSpenders12M = topSpenders,
            HighValueAtRisk = atRiskTop
        };

        return View(vm);
    }

    // POST /CrmDashboard/RunDailyJobNow (Nút Admin "Chạy job ngay" để demo không phải chờ 2h sáng)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunDailyJobNow()
    {
        var summary = await _campaignService.RunAllDailyTasksAsync();
        _segmentService.InvalidateCache();

        TempData["Success"] =
            $"Đã chạy LoyaltyDailyJob thành công! " +
            $"Sinh nhật: +{summary.BirthdayVouchersIssued} voucher · " +
            $"Cảnh báo sắp hết hạn: {summary.ExpiryWarningsSent} · " +
            $"Hết hạn điểm: {summary.PointsExpiredUsers} khách · " +
            $"Voucher quá hạn: {summary.VouchersExpired} · " +
            $"Xét hạng (Gia hạn: {summary.TierSummary.RenewedCount}, Ân hạn: {summary.TierSummary.EnteredGraceCount}, Hạ bậc: {summary.TierSummary.DowngradedCount}).";

        return RedirectToAction(nameof(Index));
    }
}

