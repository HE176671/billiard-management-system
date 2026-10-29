using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Customer")]
public class CustomerPortalController : Controller
{
    private const int HistoryPageSize = 8;
    private const int PointLogLimit = 20;
    private const int PointExpireDays = 180;

    private readonly ApplicationDbContext _db;
    private readonly ILoyaltyService _loyalty;
    private readonly ILogger<CustomerPortalController> _logger;

    public CustomerPortalController(ApplicationDbContext db, ILoyaltyService loyalty,
        ILogger<CustomerPortalController> logger)
    {
        _db = db;
        _loyalty = loyalty;
        _logger = logger;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /CustomerPortal -> Chuyển hướng tới Profile
    public IActionResult Index() => RedirectToAction(nameof(Profile));

    // GET /CustomerPortal/Profile  — Hồ sơ + Điểm + Hạng
    public async Task<IActionResult> Profile()
    {
        var uid = CurrentUserId;
        var user = await _db.Users.AsNoTracking().Include(u => u.Tier).FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null) return NotFound();

        var tiers = await _db.MembershipTiers.AsNoTracking().OrderBy(t => t.SpendThreshold).ToListAsync();
        var current = user.Tier ?? TierHelper.Resolve(tiers, user.TotalSpend);
        var next = tiers.FirstOrDefault(t => t.SpendThreshold > user.TotalSpend);

        // Tiến trình: từ ngưỡng hạng hiện tại đến ngưỡng hạng kế tiếp
        int progress = 100;
        decimal remaining = 0;
        if (next != null)
        {
            var floor = TierHelper.Resolve(tiers, user.TotalSpend)?.SpendThreshold ?? 0;
            var span = next.SpendThreshold - floor;
            progress = span <= 0 ? 0 : (int)Math.Clamp(Math.Floor((user.TotalSpend - floor) / span * 100), 0, 100);
            remaining = next.SpendThreshold - user.TotalSpend;
        }

        var log = await _db.PointTransactions.AsNoTracking()
            .Where(t => t.CustomerId == uid)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(PointLogLimit)
            .Select(t => new PointTransactionRowViewModel
            {
                CreatedAt = t.CreatedAt,
                TransactionType = t.TransactionType,
                Amount = t.Amount
            })
            .ToListAsync();

        var vm = new CustomerProfileViewModel
        {
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            CardCode = user.EmployeeCode,
            TierName = current?.TierName,
            DiscountPercent = current?.DiscountPercent ?? 0,
            PointMultiplier = current?.PointMultiplier ?? 1,
            RewardPoints = user.RewardPoints,
            TotalSpend = user.TotalSpend,
            NextTierName = next?.TierName,
            AmountToNextTier = remaining,
            ProgressPercent = progress,
            PointsExpireAtUtc = user.RewardPoints > 0 && user.LastPaidDate.HasValue
                ? user.LastPaidDate.Value.AddDays(PointExpireDays) : null,
            Transactions = log
        };
        return View(vm);
    }

    // GET /CustomerPortal/History?page=1  — Lịch sử chơi & hóa đơn
    public async Task<IActionResult> History(int page = 1)
    {
        var uid = CurrentUserId;
        page = Math.Max(1, page);

        HistoryViewModel? vm = null;
        try
        {
            // Dữ liệu thật: PlaySessions / Invoices thuộc phạm vi thành viên khác nên đọc bằng SQL thô, không map entity
            var total = (await _db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.PlaySessions WHERE CustomerId = {uid}")
                .ToListAsync()).FirstOrDefault();

            if (total > 0)
            {
                var totalPages = (int)Math.Ceiling(total / (double)HistoryPageSize);
                page = Math.Min(page, totalPages);
                var skip = (page - 1) * HistoryPageSize;

                var rows = await _db.Database.SqlQuery<HistoryRow>($@"
                    SELECT s.Id AS SessionId, t.TableCode AS TableCode, s.StartAtUtc AS StartAtUtc,
                           s.EndAtUtc AS EndAtUtc, s.Status AS SessionStatus,
                           i.Id AS InvoiceId, i.TotalPlaytime AS TotalPlaytime, i.TotalFandB AS TotalFandB,
                           i.DiscountAmount AS DiscountAmount, i.FinalAmount AS FinalAmount, i.Status AS InvoiceStatus
                    FROM dbo.PlaySessions s
                    LEFT JOIN dbo.Invoices i ON i.SessionId = s.Id
                    LEFT JOIN dbo.BilliardTables t ON t.Id = s.TableId
                    WHERE s.CustomerId = {uid}
                    ORDER BY s.StartAtUtc DESC, s.Id DESC
                    OFFSET {skip} ROWS FETCH NEXT {HistoryPageSize} ROWS ONLY").ToListAsync();

                vm = new HistoryViewModel
                {
                    Page = page,
                    TotalPages = totalPages,
                    TotalCount = total,
                    Items = rows.OrderByDescending(r => r.StartAtUtc).ThenByDescending(r => r.SessionId)
                        .Select(r => new HistoryItemViewModel
                        {
                            SessionId = r.SessionId,
                            TableCode = r.TableCode ?? "—",
                            StartAtUtc = r.StartAtUtc,
                            EndAtUtc = r.EndAtUtc,
                            SessionStatus = r.SessionStatus,
                            InvoiceId = r.InvoiceId,
                            TotalPlaytime = r.TotalPlaytime,
                            TotalFandB = r.TotalFandB,
                            DiscountAmount = r.DiscountAmount,
                            FinalAmount = r.FinalAmount,
                            InvoiceStatus = r.InvoiceStatus
                        }).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được PlaySessions/Invoices, chuyển sang dữ liệu mẫu.");
        }

        // TODO: Connect real data from Member X (PlaySessions/Invoices) — hiện dùng Mock Data khi chưa có dữ liệu thật
        vm ??= BuildMockHistory(page);
        return View(vm);
    }

    // GET /CustomerPortal/Promotions  — Combo ưu đãi + danh mục đổi điểm
    public async Task<IActionResult> Promotions()
    {
        var uid = CurrentUserId;
        var points = await _db.Users.AsNoTracking().Where(u => u.Id == uid)
            .Select(u => u.RewardPoints).FirstOrDefaultAsync();

        var vm = new PromotionsViewModel { RewardPoints = points };
        try
        {
            vm.Combos = (await _db.Database
                .SqlQuery<ComboCardViewModel>($"SELECT Id, Name, Price, PlaytimeHours FROM dbo.Combos WHERE IsActive = 1")
                .ToListAsync()).OrderBy(c => c.Price).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được bảng Combos, chuyển sang dữ liệu mẫu.");
        }

        if (vm.Combos.Count == 0)
        {
            // TODO: Connect real data from Member X (Combos) — Mock Data để demo giao diện
            vm.IsMockCombos = true;
            vm.Combos = new List<ComboCardViewModel>
            {
                new() { Id = 0, Name = "Combo Chill 2 giờ",  Price = 150_000, PlaytimeHours = 2 },
                new() { Id = 0, Name = "Combo Bạn bè 3 giờ", Price = 210_000, PlaytimeHours = 3 },
                new() { Id = 0, Name = "Combo Cày đêm 5 giờ", Price = 320_000, PlaytimeHours = 5 }
            };
        }
        return View(vm);
    }

    // POST /CustomerPortal/Redeem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeem(string rewardCode)
    {
        var result = await _loyalty.RedeemRewardAsync(CurrentUserId, rewardCode);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;
        return RedirectToAction(nameof(Promotions));
    }

    // ── Mock ──
    private static HistoryViewModel BuildMockHistory(int page)
    {
        const int total = 19;
        var now = DateTime.UtcNow;
        var all = new List<HistoryItemViewModel>();
        for (int n = 0; n < total; n++)
        {
            var start = now.AddDays(-(n * 2 + 1)).AddHours(-(n % 5));
            var hours = 1 + (n % 3);
            var playtime = hours * 60_000m;
            var fnb = (n % 4) * 25_000m;
            var discount = n % 3 == 0 ? 10_000m : 0m;
            all.Add(new HistoryItemViewModel
            {
                SessionId = 9000 + total - n,
                TableCode = $"B{(n % 8) + 1:D2}",
                StartAtUtc = start,
                EndAtUtc = start.AddHours(hours),
                SessionStatus = "Closed",
                InvoiceId = 5000 + total - n,
                TotalPlaytime = playtime,
                TotalFandB = fnb,
                DiscountAmount = discount,
                FinalAmount = playtime + fnb - discount,
                InvoiceStatus = "Paid"
            });
        }

        var totalPages = (int)Math.Ceiling(total / (double)HistoryPageSize);
        page = Math.Min(Math.Max(1, page), totalPages);
        return new HistoryViewModel
        {
            IsMock = true,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            Items = all.Skip((page - 1) * HistoryPageSize).Take(HistoryPageSize).ToList()
        };
    }
}
