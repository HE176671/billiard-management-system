using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager,Admin")]
public class CustomerManagementController : Controller
{
    private const int PageSize = 10;
    private const string PlaceholderEmailDomain = "@noemail.bms.local";
    private const string CardPrefix = "MB";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IPointLedgerService _ledger;
    private readonly ICustomerSegmentService _segmentService;
    private readonly IVoucherService _voucherService;

    public CustomerManagementController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IPointLedgerService ledger,
        ICustomerSegmentService segmentService,
        IVoucherService voucherService)
    {
        _userManager = userManager;
        _context = context;
        _ledger = ledger;
        _segmentService = segmentService;
        _voucherService = voucherService;
    }

    private string? CurrentActorId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.IsInRole("Manager") || User.IsInRole("Admin");

    // ═════════════════════════════════════════════════════════════════════
    // F1: TRA CỨU TẠI QUẦY (LOOKUP) & GHI DANH NHANH
    // ═════════════════════════════════════════════════════════════════════

    // GET /CustomerManagement/Lookup
    [HttpGet]
    public IActionResult Lookup(string? q = null)
    {
        ViewData["InitialQuery"] = q;
        return View();
    }

    // GET /CustomerManagement/Search?q=... (AJAX trả JSON tối đa 8 kết quả, SĐT che giữa 090****567)
    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
            return Json(Array.Empty<object>());

        var lower = term.ToLowerInvariant();
        var query = CustomersQuery()
            .Include(u => u.Tier)
            .Where(u => (u.PhoneNumber != null && u.PhoneNumber.Contains(term))
                     || (u.EmployeeCode != null && u.EmployeeCode.ToLower().Contains(lower))
                     || u.FullName.ToLower().Contains(lower));

        var matches = await query
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.FullName)
            .Take(8)
            .Select(u => new
            {
                id = u.Id,
                fullName = u.FullName,
                maskedPhone = MaskPhone(u.PhoneNumber),
                cardCode = u.EmployeeCode ?? "—",
                tierName = u.Tier != null ? u.Tier.TierName : "Member",
                rewardPoints = u.RewardPoints,
                isActive = u.IsActive
            })
            .ToListAsync();

        return Json(matches);
    }

    // POST /CustomerManagement/QuickEnroll (Ghi danh nhanh < 10 giây + tặng 10 điểm chào mừng)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickEnroll(QuickEnrollInputModel input)
    {
        input.FullName = (input.FullName ?? string.Empty).Trim();
        input.PhoneNumber = (input.PhoneNumber ?? string.Empty).Trim();
        input.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();

        if (!ModelState.IsValid)
        {
            var errMsg = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            TempData["Error"] = string.IsNullOrEmpty(errMsg) ? "Thông tin ghi danh không hợp lệ." : errMsg;
            return RedirectToAction(nameof(Lookup), new { q = input.PhoneNumber });
        }

        var existingByPhone = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == input.PhoneNumber);
        if (existingByPhone != null)
        {
            TempData["Error"] = $"Số điện thoại {input.PhoneNumber} đã tồn tại. Đã mở hồ sơ khách hàng.";
            return RedirectToAction(nameof(Profile), new { id = existingByPhone.Id });
        }

        if (input.Email != null)
        {
            var existingByEmail = await _userManager.FindByEmailAsync(input.Email);
            if (existingByEmail != null)
            {
                TempData["Error"] = $"Email {input.Email} đã được sử dụng cho hồ sơ hội viên khác.";
                return RedirectToAction(nameof(Lookup), new { q = input.PhoneNumber });
            }
        }

        var defaultTier = await DefaultTierAsync();
        var cardCode = await GenerateNextCardCode6Async();
        var now = DateTime.UtcNow;

        var user = new ApplicationUser
        {
            UserName = input.PhoneNumber,
            Email = input.Email ?? PlaceholderEmail(input.PhoneNumber),
            FullName = input.FullName,
            PhoneNumber = input.PhoneNumber,
            EmployeeCode = cardCode,
            DateOfBirth = input.DateOfBirth,
            MarketingOptIn = input.MarketingOptIn,
            AccountSource = ApplicationUser.SourceCounter,
            IsActive = true,
            CreatedAtUtc = now,
            TierId = defaultTier?.Id,
            TierAchievedAtUtc = now,
            TierReviewAtUtc = now.AddMonths(12),
            RewardPoints = 0,
            TotalSpend = 0
        };

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var create = await _userManager.CreateAsync(user);
            if (!create.Succeeded)
            {
                await tx.RollbackAsync();
                TempData["Error"] = string.Join(" ", create.Errors.Select(TranslateIdentityError));
                return RedirectToAction(nameof(Lookup), new { q = input.PhoneNumber });
            }

            var role = await _userManager.AddToRoleAsync(user, "Customer");
            if (!role.Succeeded)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "Không thể gán quyền Customer.";
                return RedirectToAction(nameof(Lookup), new { q = input.PhoneNumber });
            }

            _context.CustomerAuditLogs.Add(new CustomerAuditLog
            {
                CustomerId = user.Id,
                Action = "Create",
                Detail = JsonSerializer.Serialize(new { source = "Counter", phone = user.PhoneNumber, card = cardCode }),
                ActorId = CurrentActorId,
                CreatedAtUtc = now
            });
            await _context.SaveChangesAsync();

            // Tặng 10 điểm chào mừng qua sổ cái (type Bonus, ReferenceCode WELCOME-{userId})
            string welcomeRef = $"WELCOME-{user.Id}";
            if (welcomeRef.Length > 50) welcomeRef = welcomeRef[..50];

            await _ledger.PostAsync(
                customerId: user.Id,
                delta: 10,
                type: PointTransaction.Bonus,
                referenceCode: welcomeRef,
                invoiceId: null,
                reason: "Điểm thưởng chào mừng hội viên mới ghi danh tại quầy",
                actorId: CurrentActorId,
                spendAmount: null);

            _context.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = "Chào mừng hội viên mới (+10 điểm)",
                Content = $"Chào mừng {user.FullName} gia nhập CLB Bida! Mã thẻ của bạn là {cardCode}. Bạn được tặng 10 điểm chào mừng.",
                Type = "Welcome",
                IsRead = false,
                CreatedAt = now
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            _segmentService.InvalidateCache();
        }
        catch
        {
            await tx.RollbackAsync();
            TempData["Error"] = "Đã xảy ra lỗi khi ghi danh nhanh. Vui lòng thử lại.";
            return RedirectToAction(nameof(Lookup), new { q = input.PhoneNumber });
        }

        TempData["Success"] = $"Đã ghi danh nhanh khách hàng {user.FullName} (Thẻ {cardCode}) và tặng +10 điểm chào mừng!";
        return RedirectToAction(nameof(Profile), new { id = user.Id });
    }

    // ═════════════════════════════════════════════════════════════════════
    // F1: HỒ SƠ CUSTOMER 360 (/CustomerManagement/Profile/{id})
    // ═════════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> Profile(string id)
    {
        var user = await GetCustomerOrNullAsync(id);
        if (user == null) return NotFound();

        var rfm = await _segmentService.GetProfileAsync(id);
        var tiers = await _context.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync();

        var currentTier = user.Tier ?? (tiers.Count > 0 ? tiers[0] : null);
        decimal spend12M = rfm?.Monetary12M ?? 0m;
        decimal curThreshold = currentTier?.SpendThreshold ?? 0m;
        var nextTier = tiers.FirstOrDefault(t => t.SpendThreshold > curThreshold);

        int progressPct = 100;
        decimal toNext = 0m;
        if (nextTier != null)
        {
            decimal span = nextTier.SpendThreshold - curThreshold;
            progressPct = span <= 0 ? 100 : (int)Math.Clamp(Math.Floor((spend12M - curThreshold) / span * 100m), 0, 100);
            toNext = Math.Max(0m, nextTier.SpendThreshold - spend12M);
        }
        decimal toKeep = Math.Max(0m, curThreshold - spend12M);

        // Kiểm tra sinh nhật trong 7 ngày tới
        bool bdaySoon = false;
        int? daysUntilBday = null;
        if (user.DateOfBirth.HasValue)
        {
            var today = VnTime.Today;
            var dob = user.DateOfBirth.Value;
            int year = today.Year;
            var nextBday = new DateOnly(year, dob.Month, Math.Min(dob.Day, DateTime.DaysInMonth(year, dob.Month)));
            if (nextBday < today)
            {
                year++;
                nextBday = new DateOnly(year, dob.Month, Math.Min(dob.Day, DateTime.DaysInMonth(year, dob.Month)));
            }
            int diff = nextBday.DayNumber - today.DayNumber;
            daysUntilBday = diff;
            bdaySoon = diff >= 0 && diff <= 7;
        }

        var notes = await _context.CustomerNotes.AsNoTracking()
            .Include(n => n.CreatedBy)
            .Where(n => n.CustomerId == id)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ToListAsync();

        var vouchers = await _context.CustomerVouchers.AsNoTracking()
            .Where(v => v.CustomerId == id)
            .OrderByDescending(v => v.IssuedAtUtc)
            .ToListAsync();

        var rewardItems = await _context.RewardItems.AsNoTracking()
            .Include(r => r.MinTier)
            .Where(r => r.IsActive)
            .OrderBy(r => r.PointCost).ThenBy(r => r.Id)
            .ToListAsync();

        var ledgerEntries = await _context.PointTransactions.AsNoTracking()
            .Include(t => t.CreatedBy)
            .Where(t => t.CustomerId == id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(50)
            .ToListAsync();

        // Lịch sử ghé quán thật từ PlaySessions + Invoices (không mock)
        var visitHistory = new List<HistoryItemViewModel>();
        try
        {
            var rows = await _context.Database.SqlQuery<HistoryRow>($@"
                SELECT TOP 25 s.Id AS SessionId, t.TableCode AS TableCode, s.StartAtUtc AS StartAtUtc,
                       s.EndAtUtc AS EndAtUtc, s.Status AS SessionStatus,
                       i.Id AS InvoiceId, i.TotalPlaytime AS TotalPlaytime, i.TotalFandB AS TotalFandB,
                       i.DiscountAmount AS DiscountAmount, i.FinalAmount AS FinalAmount, i.Status AS InvoiceStatus
                FROM dbo.PlaySessions s
                LEFT JOIN dbo.Invoices i ON i.SessionId = s.Id
                LEFT JOIN dbo.BilliardTables t ON t.Id = s.TableId
                WHERE s.CustomerId = {id}
                ORDER BY s.StartAtUtc DESC, s.Id DESC").ToListAsync();

            visitHistory = rows.Select(r => new HistoryItemViewModel
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
            }).ToList();
        }
        catch { /* Bỏ qua nếu chưa có dữ liệu */ }

        var auditLogs = new List<CustomerAuditLog>();
        var mergeCandidates = new List<CustomerSelectItemViewModel>();
        if (IsAdmin)
        {
            auditLogs = await _context.CustomerAuditLogs.AsNoTracking()
                .Include(a => a.Actor)
                .Where(a => a.CustomerId == id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(50)
                .ToListAsync();

            mergeCandidates = await CustomersQuery()
                .Where(u => u.Id != id && u.IsActive)
                .OrderBy(u => u.FullName)
                .Take(100)
                .Select(u => new CustomerSelectItemViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    PhoneNumber = u.PhoneNumber,
                    CardCode = u.EmployeeCode,
                    RewardPoints = u.RewardPoints
                })
                .ToListAsync();
        }

        var (segStyle, segAction) = CustomerSegmentService.GetSegmentMeta(rfm?.Segment ?? "Regular");

        var vm = new Customer360ViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Email = IsPlaceholderEmail(user.Email) ? null : user.Email,
            CardCode = user.EmployeeCode,
            DateOfBirth = user.DateOfBirth,
            IsBirthdaySoon = bdaySoon,
            DaysUntilBirthday = daysUntilBday,
            IsActive = user.IsActive,
            MarketingOptIn = user.MarketingOptIn,
            AccountSource = user.AccountSource,
            CreatedAtUtc = user.CreatedAtUtc,
            ClaimedAtUtc = user.ClaimedAtUtc,
            TierName = currentTier?.TierName ?? "Member",
            TierDiscountPercent = currentTier?.DiscountPercent ?? 0m,
            TierMultiplier = currentTier?.PointMultiplier ?? 1,
            TierAchievedAtUtc = user.TierAchievedAtUtc,
            TierReviewAtUtc = user.TierReviewAtUtc,
            TierGraceUntilUtc = user.TierGraceUntilUtc,
            CurrentTierThreshold = curThreshold,
            NextTierName = nextTier?.TierName,
            AmountToNextTier = toNext,
            AmountToKeepTier = toKeep,
            TierProgressPercent = progressPct,
            RewardPoints = user.RewardPoints,
            Spend12Months = spend12M,
            TotalSpendLifetime = user.TotalSpend,
            Visits12Months = rfm?.Frequency12M ?? 0,
            LastPaidDate = rfm?.LastPaidDate ?? user.LastPaidDate,
            RecencyDays = rfm?.RecencyDays,
            Segment = rfm?.Segment ?? "Regular",
            SegmentBadgeStyle = segStyle,
            SegmentSuggestedAction = segAction,
            PinnedNotes = notes.Where(n => n.IsPinned).ToList(),
            Notes = notes,
            ActiveVouchers = vouchers.Where(v => v.Status == CustomerVoucher.StatusActive && v.ExpiresAtUtc > DateTime.UtcNow).ToList(),
            AllVouchers = vouchers,
            AvailableRewards = rewardItems,
            LedgerEntries = ledgerEntries,
            VisitHistory = visitHistory,
            AuditLogs = auditLogs,
            MergeCandidates = mergeCandidates
        };

        return View(vm);
    }

    // POST /CustomerManagement/RedeemReward (Nhân viên đổi điểm lấy voucher/quà cho khách tại quầy)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RedeemReward(string customerId, string rewardCode)
    {
        var res = await _voucherService.RedeemRewardForVoucherAsync(customerId, rewardCode);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Profile), new { id = customerId });
    }

    // POST /CustomerManagement/AddNote
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(string customerId, string content, bool isPinned = false)
    {
        var text = (content ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            TempData["Error"] = "Nội dung ghi chú không được để trống.";
            return RedirectToAction(nameof(Profile), new { id = customerId });
        }

        var actorId = CurrentActorId ?? (await _context.Users.Select(u => u.Id).FirstAsync());
        _context.CustomerNotes.Add(new CustomerNote
        {
            CustomerId = customerId,
            Content = text.Length > 500 ? text[..500] : text,
            IsPinned = isPinned,
            CreatedById = actorId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã thêm ghi chú chăm sóc khách hàng.";
        return RedirectToAction(nameof(Profile), new { id = customerId });
    }

    // POST /CustomerManagement/TogglePinNote
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePinNote(int noteId, string customerId)
    {
        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId);
        if (note != null)
        {
            note.IsPinned = !note.IsPinned;
            await _context.SaveChangesAsync();
            TempData["Success"] = note.IsPinned ? "Đã ghim ghi chú lên đầu hồ sơ." : "Đã bỏ ghim ghi chú.";
        }
        return RedirectToAction(nameof(Profile), new { id = customerId });
    }

    // POST /CustomerManagement/ConfirmVoucher (F4: Nhân viên xác nhận giao quà)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmVoucher(string voucherCode, string? returnCustomerId = null)
    {
        var actorId = CurrentActorId ?? (await _context.Users.Select(u => u.Id).FirstAsync());
        var res = await _voucherService.ConfirmGiftDeliveryAsync(voucherCode, actorId);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        var targetCustomerId = returnCustomerId ?? res.Voucher?.CustomerId;
        if (!string.IsNullOrEmpty(targetCustomerId))
            return RedirectToAction(nameof(Profile), new { id = targetCustomerId });

        return RedirectToAction(nameof(Lookup));
    }

    // POST /CustomerManagement/CancelVoucher (F4: Manager hủy voucher -> hoàn điểm Reverse)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> CancelVoucher(int voucherId, string customerId, string? reason = null)
    {
        var actorId = CurrentActorId ?? (await _context.Users.Select(u => u.Id).FirstAsync());
        var res = await _voucherService.CancelVoucherAsync(voucherId, actorId, reason);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Profile), new { id = customerId });
    }

    // POST /CustomerManagement/MergeDuplicate (F8: Manager gộp tài khoản trùng)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> MergeDuplicate(string targetCustomerId, string sourceCustomerId)
    {
        if (string.IsNullOrWhiteSpace(targetCustomerId) || string.IsNullOrWhiteSpace(sourceCustomerId) || targetCustomerId == sourceCustomerId)
        {
            TempData["Error"] = "Vui lòng chọn tài khoản nguồn hợp lệ khác với tài khoản đích.";
            return RedirectToAction(nameof(Profile), new { id = targetCustomerId });
        }

        var targetUser = await GetCustomerOrNullAsync(targetCustomerId);
        var sourceUser = await GetCustomerOrNullAsync(sourceCustomerId);
        if (targetUser == null || sourceUser == null)
            return NotFound();

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            int pointsToTransfer = sourceUser.RewardPoints;
            decimal spendToTransfer = sourceUser.TotalSpend;
            var actorId = CurrentActorId;
            var now = DateTime.UtcNow;

            // 1. Nếu tài khoản nguồn còn điểm -> trừ hết ở nguồn và cộng sang đích qua sổ cái Adjust
            if (pointsToTransfer > 0)
            {
                await _ledger.PostAsync(
                    customerId: sourceUser.Id,
                    delta: -pointsToTransfer,
                    type: PointTransaction.Adjust,
                    referenceCode: $"MERGE-OUT-{sourceUser.Id}"[..Math.Min(50, $"MERGE-OUT-{sourceUser.Id}".Length)],
                    invoiceId: null,
                    reason: $"Chuyển điểm do gộp sang tài khoản {targetUser.PhoneNumber ?? targetUser.FullName}",
                    actorId: actorId);

                await _ledger.PostAsync(
                    customerId: targetUser.Id,
                    delta: pointsToTransfer,
                    type: PointTransaction.Adjust,
                    referenceCode: $"MERGE-IN-{sourceUser.Id}"[..Math.Min(50, $"MERGE-IN-{sourceUser.Id}".Length)],
                    invoiceId: null,
                    reason: $"Gộp tài khoản từ {sourceUser.FullName} ({sourceUser.PhoneNumber})",
                    actorId: actorId);
            }

            // 2. Chuyển toàn bộ dữ liệu liên kết từ source sang target
            await _context.PointTransactions.Where(x => x.CustomerId == sourceUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, targetUser.Id));
            await _context.CustomerVouchers.Where(x => x.CustomerId == sourceUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, targetUser.Id));
            await _context.Reviews.Where(x => x.CustomerId == sourceUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, targetUser.Id));
            await _context.Notifications.Where(x => x.UserId == sourceUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, targetUser.Id));
            await _context.CustomerNotes.Where(x => x.CustomerId == sourceUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, targetUser.Id));

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.Invoices SET CustomerId = {targetUser.Id} WHERE CustomerId = {sourceUser.Id};
                UPDATE dbo.PlaySessions SET CustomerId = {targetUser.Id} WHERE CustomerId = {sourceUser.Id};
                UPDATE dbo.Bookings SET CustomerId = {targetUser.Id} WHERE CustomerId = {sourceUser.Id};
            ");

            // 3. Cộng dồn TotalSpend và khóa tài khoản nguồn
            await _context.Entry(targetUser).ReloadAsync();
            await _context.Entry(sourceUser).ReloadAsync();

            targetUser.TotalSpend += spendToTransfer;
            if (sourceUser.LastPaidDate.HasValue && (!targetUser.LastPaidDate.HasValue || sourceUser.LastPaidDate > targetUser.LastPaidDate))
            {
                targetUser.LastPaidDate = sourceUser.LastPaidDate;
            }
            sourceUser.IsActive = false;

            var detailJson = JsonSerializer.Serialize(new
            {
                sourceId = sourceUser.Id,
                sourcePhone = sourceUser.PhoneNumber,
                targetId = targetUser.Id,
                targetPhone = targetUser.PhoneNumber,
                pointsTransferred = pointsToTransfer,
                spendTransferred = spendToTransfer
            });

            _context.CustomerAuditLogs.Add(new CustomerAuditLog
            {
                CustomerId = targetUser.Id,
                Action = "Merge",
                Detail = detailJson,
                ActorId = actorId,
                CreatedAtUtc = now
            });
            _context.CustomerAuditLogs.Add(new CustomerAuditLog
            {
                CustomerId = sourceUser.Id,
                Action = "Merge",
                Detail = detailJson,
                ActorId = actorId,
                CreatedAtUtc = now
            });

            await _context.SaveChangesAsync();
            await _userManager.UpdateSecurityStampAsync(sourceUser);
            await tx.CommitAsync();
            _segmentService.InvalidateCache();

            TempData["Success"] = $"Đã gộp thành công tài khoản {sourceUser.FullName} ({sourceUser.PhoneNumber}) vào {targetUser.FullName} (+{pointsToTransfer} điểm) và khóa tài khoản nguồn.";
        }
        catch
        {
            await tx.RollbackAsync();
            TempData["Error"] = "Không thể gộp tài khoản. Vui lòng thử lại.";
        }

        return RedirectToAction(nameof(Profile), new { id = targetCustomerId });
    }

    // ═════════════════════════════════════════════════════════════════════
    // DANH SÁCH KHÁCH HÀNG 2 CỘT + LỌC PHÂN KHÚC + XUẤT CSV (BR-F1-4)
    // ═════════════════════════════════════════════════════════════════════

    // GET /CustomerManagement?search=&tierId=&segment=&page=&id=
    public async Task<IActionResult> Index(string? search, int? tierId, string? status, string? segment, int page = 1,
        string? id = null)
    {
        CustomerFormViewModel? form = null;
        if (!string.IsNullOrEmpty(id))
        {
            var customer = await GetCustomerOrNullAsync(id);
            if (customer == null) return NotFound();
            form = ToForm(customer);
        }

        return View(await BuildViewModelAsync(search, tierId, null, segment, page, form));
    }

    // GET /CustomerManagement/ExportCsv
    [HttpGet]
    public async Task<IActionResult> ExportCsv(string? search, int? tierId, string? status, string? segment)
    {
        var vm = await BuildViewModelAsync(search, tierId, status, segment, 1, null, exportAll: true);
        var sb = new StringBuilder();
        sb.AppendLine("FullName,PhoneNumber,CardCode,Tier,Segment,RewardPoints,Spend12Months,Status");
        foreach (var c in vm.Customers)
        {
            string name = (c.FullName ?? "").Replace("\"", "\"\"");
            sb.AppendLine($"\"{name}\",{c.PhoneNumber},{c.CardCode},{c.TierName},{c.Segment},{c.RewardPoints},{c.Spend12M:0},{(c.IsActive ? "Active" : "Locked")}");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"Customers_{DateTime.UtcNow.ToVn():yyyyMMdd_HHmm}.csv");
    }

    // POST /CustomerManagement/Save
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CustomerFormViewModel form, string? search, int? tierId,
        string? status, string? segment, int page = 1, string? returnTo = null)
    {
        form.FullName = (form.FullName ?? string.Empty).Trim();
        form.PhoneNumber = (form.PhoneNumber ?? string.Empty).Trim();
        form.Email = string.IsNullOrWhiteSpace(form.Email) ? null : form.Email.Trim();

        ApplicationUser? existing = null;
        if (!form.IsNew)
        {
            existing = await GetCustomerOrNullAsync(form.Id!);
            if (existing == null) return NotFound();
        }

        if (ModelState.IsValid)
        {
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == form.PhoneNumber && u.Id != form.Id))
                ModelState.AddModelError(nameof(form.PhoneNumber), "Số điện thoại đã được dùng bởi tài khoản khác.");

            if (form.Email != null)
            {
                var byEmail = await _userManager.FindByEmailAsync(form.Email);
                if (byEmail != null && byEmail.Id != form.Id)
                    ModelState.AddModelError(nameof(form.Email), "Email đã được dùng bởi tài khoản khác.");
            }
        }

        if (!ModelState.IsValid)
            return await RedisplayAsync(form, existing, search, tierId, status, segment, page);

        var now = DateTime.UtcNow;

        if (existing == null)
        {
            var defaultTier = await DefaultTierAsync();
            var cardCode = await GenerateNextCardCode6Async();
            var user = new ApplicationUser
            {
                UserName = form.PhoneNumber,
                Email = form.Email ?? PlaceholderEmail(form.PhoneNumber),
                FullName = form.FullName,
                PhoneNumber = form.PhoneNumber,
                EmployeeCode = cardCode,
                DateOfBirth = form.DateOfBirth,
                MarketingOptIn = form.MarketingOptIn,
                AccountSource = ApplicationUser.SourceCounter,
                IsActive = IsAdmin ? form.IsActive : true,
                CreatedAtUtc = now,
                TierId = defaultTier?.Id,
                TierAchievedAtUtc = now,
                TierReviewAtUtc = now.AddMonths(12),
                RewardPoints = 0,
                TotalSpend = 0
            };

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var create = await _userManager.CreateAsync(user);
                if (!create.Succeeded)
                {
                    await tx.RollbackAsync();
                    foreach (var err in create.Errors) ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
                    return await RedisplayAsync(form, null, search, tierId, status, segment, page);
                }
                var role = await _userManager.AddToRoleAsync(user, "Customer");
                if (!role.Succeeded)
                {
                    await tx.RollbackAsync();
                    ModelState.AddModelError(string.Empty, "Gán quyền Customer thất bại. Vui lòng thử lại.");
                    return await RedisplayAsync(form, null, search, tierId, status, segment, page);
                }

                _context.CustomerAuditLogs.Add(new CustomerAuditLog
                {
                    CustomerId = user.Id,
                    Action = "Create",
                    Detail = JsonSerializer.Serialize(new { source = "Counter", phone = user.PhoneNumber, card = cardCode }),
                    ActorId = CurrentActorId,
                    CreatedAtUtc = now
                });
                await _context.SaveChangesAsync();

                // Tặng 10 điểm chào mừng
                string welcomeRef = $"WELCOME-{user.Id}";
                if (welcomeRef.Length > 50) welcomeRef = welcomeRef[..50];
                await _ledger.PostAsync(user.Id, 10, PointTransaction.Bonus, welcomeRef, null,
                    "Điểm thưởng chào mừng hội viên mới", CurrentActorId);

                await tx.CommitAsync();
                _segmentService.InvalidateCache();
            }
            catch
            {
                await tx.RollbackAsync();
                ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống. Vui lòng thử lại.");
                return await RedisplayAsync(form, null, search, tierId, status, segment, page);
            }

            TempData["Success"] = $"Đã thêm khách hàng {user.FullName} (Thẻ {cardCode}, +10 điểm chào mừng).";
            return RedirectToIndex(user.Id, search, tierId, status, segment, page);
        }

        // ── Cập nhật hồ sơ liên lạc + Ghi Audit Diff JSON (BR-F1-2 & BR-F1-3) ──
        var changes = new Dictionary<string, object?[]>();
        if (existing.FullName != form.FullName)
            changes["FullName"] = new object?[] { existing.FullName, form.FullName };
        if (existing.PhoneNumber != form.PhoneNumber)
            changes["PhoneNumber"] = new object?[] { existing.PhoneNumber, form.PhoneNumber };
        var oldRealEmail = IsPlaceholderEmail(existing.Email) ? null : existing.Email;
        if (oldRealEmail != form.Email)
            changes["Email"] = new object?[] { oldRealEmail, form.Email };
        if (existing.DateOfBirth != form.DateOfBirth)
            changes["DateOfBirth"] = new object?[] { existing.DateOfBirth?.ToString("yyyy-MM-dd"), form.DateOfBirth?.ToString("yyyy-MM-dd") };
        if (existing.MarketingOptIn != form.MarketingOptIn)
            changes["MarketingOptIn"] = new object?[] { existing.MarketingOptIn, form.MarketingOptIn };

        var oldPhone = existing.PhoneNumber;
        var wasActive = existing.IsActive;

        existing.FullName = form.FullName;
        existing.PhoneNumber = form.PhoneNumber;
        if (existing.UserName == oldPhone) existing.UserName = form.PhoneNumber;
        existing.Email = form.Email ?? PlaceholderEmail(form.PhoneNumber);
        existing.DateOfBirth = form.DateOfBirth;
        existing.MarketingOptIn = form.MarketingOptIn;

        // BR-F1-2: Chỉ Admin mới được thay đổi trạng thái khóa
        if (IsAdmin && existing.IsActive != form.IsActive)
        {
            changes["IsActive"] = new object?[] { existing.IsActive, form.IsActive };
            existing.IsActive = form.IsActive;
        }

        var update = await _userManager.UpdateAsync(existing);
        if (!update.Succeeded)
        {
            foreach (var err in update.Errors) ModelState.AddModelError(string.Empty, TranslateIdentityError(err));
            _context.Entry(existing).State = EntityState.Detached;
            var fresh = await GetCustomerOrNullAsync(form.Id!);
            return await RedisplayAsync(form, fresh, search, tierId, status, segment, page);
        }

        if (changes.Count > 0)
        {
            _context.CustomerAuditLogs.Add(new CustomerAuditLog
            {
                CustomerId = existing.Id,
                Action = "UpdateProfile",
                Detail = JsonSerializer.Serialize(changes),
                ActorId = CurrentActorId,
                CreatedAtUtc = now
            });
            await _context.SaveChangesAsync();
        }

        if (wasActive && !existing.IsActive)
            await _userManager.UpdateSecurityStampAsync(existing);

        _segmentService.InvalidateCache();
        TempData["Success"] = $"Đã cập nhật thông tin {existing.FullName}.";

        if (returnTo == "profile")
            return RedirectToAction(nameof(Profile), new { id = existing.Id });

        return RedirectToIndex(existing.Id, search, tierId, status, segment, page);
    }

    // POST /CustomerManagement/ToggleLock (BR-F1-2: Chỉ Manager được khóa/mở khóa)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> ToggleLock(string id, string? search, int? tierId, string? status, string? segment, int page = 1, string? returnTo = null)
    {
        var user = await GetCustomerOrNullAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        if (!user.IsActive) await _userManager.UpdateSecurityStampAsync(user);

        _context.CustomerAuditLogs.Add(new CustomerAuditLog
        {
            CustomerId = user.Id,
            Action = user.IsActive ? "Unlock" : "Lock",
            Detail = JsonSerializer.Serialize(new { isActive = user.IsActive }),
            ActorId = CurrentActorId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _segmentService.InvalidateCache();

        TempData["Success"] = $"Đã {(user.IsActive ? "mở khóa" : "khóa")} tài khoản {user.FullName}.";
        if (returnTo == "profile")
            return RedirectToAction(nameof(Profile), new { id });

        return RedirectToIndex(id, search, tierId, status, segment, page);
    }

    // POST /CustomerManagement/AssignCard
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCard(string id, string? cardCode, string? search, int? tierId,
        string? status, string? segment, int page = 1)
    {
        var user = await GetCustomerOrNullAsync(id);
        if (user == null) return NotFound();

        var code = string.IsNullOrWhiteSpace(cardCode) ? await GenerateNextCardCode6Async() : cardCode.Trim().ToUpperInvariant();

        if (code.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9\-]+$"))
        {
            TempData["Error"] = "Mã thẻ chỉ gồm chữ, số, dấu gạch ngang và tối đa 20 ký tự.";
            return RedirectToIndex(id, search, tierId, status, segment, page);
        }
        if (await _context.Users.AnyAsync(u => u.EmployeeCode == code && u.Id != id))
        {
            TempData["Error"] = $"Mã thẻ {code} đã được gắn cho người khác.";
            return RedirectToIndex(id, search, tierId, status, segment, page);
        }

        user.EmployeeCode = code;
        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded) TempData["Success"] = $"Đã gắn thẻ {code} cho {user.FullName}.";
        else TempData["Error"] = "Không thể gắn thẻ: " + string.Join(" ", result.Errors.Select(TranslateIdentityError));

        return RedirectToIndex(id, search, tierId, status, segment, page);
    }

    // ── Helpers ──

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "—";
        var p = phone.Trim();
        if (p.Length < 7) return p;
        return $"{p[..3]}****{p[^3..]}";
    }

    private IActionResult RedirectToIndex(string? id, string? search, int? tierId, string? status, string? segment, int page) =>
        RedirectToAction(nameof(Index), new { search, tierId, status, segment, page, id });

    private async Task<IActionResult> RedisplayAsync(CustomerFormViewModel form, ApplicationUser? existing,
        string? search, int? tierId, string? status, string? segment, int page)
    {
        if (existing != null)
        {
            form.TierName = existing.Tier?.TierName;
            form.RewardPoints = existing.RewardPoints;
            form.TotalSpend = existing.TotalSpend;
            form.CardCode = existing.EmployeeCode;
        }
        else
        {
            form.TierName = (await DefaultTierAsync())?.TierName;
        }
        return View(nameof(Index), await BuildViewModelAsync(search, tierId, status, segment, page, form));
    }

    private async Task<CustomerManagementViewModel> BuildViewModelAsync(string? search, int? tierId,
        string? status, string? segment, int page, CustomerFormViewModel? form, bool exportAll = false)
    {
        var profiles = await _segmentService.GetAllProfilesAsync();
        IEnumerable<CustomerRfmProfile> filtered = profiles;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(u => u.FullName.ToLowerInvariant().Contains(s)
                                        || (u.PhoneNumber != null && u.PhoneNumber.Contains(s))
                                        || (u.CardCode != null && u.CardCode.ToLowerInvariant().Contains(s)));
        }
        if (tierId.HasValue) filtered = filtered.Where(u => u.TierId == tierId);
        if (status == "active") filtered = filtered.Where(u => u.IsActive);
        if (status == "locked") filtered = filtered.Where(u => !u.IsActive);
        if (!string.IsNullOrWhiteSpace(segment))
            filtered = filtered.Where(u => string.Equals(u.Segment, segment, StringComparison.OrdinalIgnoreCase));

        var ordered = filtered.OrderBy(u => u.FullName).ToList();
        var total = ordered.Count;
        var totalPages = (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var slice = exportAll ? ordered : ordered.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        var rows = slice.Select(u => new CustomerRowViewModel
        {
            Id = u.CustomerId,
            FullName = u.FullName,
            PhoneNumber = u.PhoneNumber,
            CardCode = u.CardCode,
            TierName = u.TierName,
            RewardPoints = u.RewardPoints,
            IsActive = u.IsActive,
            Segment = u.Segment,
            SegmentStyle = u.BadgeStyle,
            Spend12M = u.Monetary12M,
            CreatedAtUtc = u.CreatedAtUtc
        }).ToList();

        var tiers = await _context.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold)
            .Select(t => new TierOptionViewModel { Id = t.Id, Name = t.TierName })
            .ToListAsync();

        return new CustomerManagementViewModel
        {
            Customers = rows,
            Tiers = tiers,
            Search = search,
            TierId = tierId,
            Status = status,
            Segment = segment,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            Form = form
        };
    }

    private IQueryable<ApplicationUser> CustomersQuery()
    {
        var roleId = _context.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefault();
        return _context.Users.AsNoTracking()
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
    }

    private async Task<ApplicationUser?> GetCustomerOrNullAsync(string id)
    {
        var user = await _context.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;
        return await _userManager.IsInRoleAsync(user, "Customer") ? user : null;
    }

    private async Task<MembershipTier?> DefaultTierAsync()
    {
        var tiers = await _context.MembershipTiers.AsNoTracking().ToListAsync();
        return TierHelper.Resolve(tiers, 0);
    }

    private static CustomerFormViewModel ToForm(ApplicationUser u) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        PhoneNumber = u.PhoneNumber ?? string.Empty,
        Email = IsPlaceholderEmail(u.Email) ? null : u.Email,
        DateOfBirth = u.DateOfBirth,
        MarketingOptIn = u.MarketingOptIn,
        IsActive = u.IsActive,
        TierName = u.Tier?.TierName,
        RewardPoints = u.RewardPoints,
        TotalSpend = u.TotalSpend,
        CardCode = u.EmployeeCode
    };

    private static string PlaceholderEmail(string phone) => phone + PlaceholderEmailDomain;

    private static bool IsPlaceholderEmail(string? email) =>
        email != null && email.EndsWith(PlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sinh mã thẻ MB + 6 chữ số (vd: MB000001).</summary>
    private async Task<string> GenerateNextCardCode6Async()
    {
        var codes = await _context.Users
            .Where(u => u.EmployeeCode != null && u.EmployeeCode.StartsWith(CardPrefix))
            .Select(u => u.EmployeeCode!)
            .ToListAsync();

        int max = 0;
        foreach (var c in codes)
            if (int.TryParse(c.AsSpan(CardPrefix.Length), out var n) && n > max) max = n;
        return $"{CardPrefix}{(max + 1):D6}";
    }

    private static string TranslateIdentityError(IdentityError err) => err.Code switch
    {
        "DuplicateUserName" => "Số điện thoại đã được dùng làm tên đăng nhập của tài khoản khác.",
        "DuplicateEmail" => "Email đã được dùng bởi tài khoản khác.",
        "InvalidEmail" => "Email không hợp lệ.",
        _ => err.Description
    };
}
