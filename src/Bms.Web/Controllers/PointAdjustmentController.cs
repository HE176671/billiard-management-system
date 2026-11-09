using System.Security.Claims;
using System.Text.Json;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager,Admin")]
public class PointAdjustmentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPointLedgerService _ledger;

    public PointAdjustmentController(ApplicationDbContext db, IPointLedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    private string CurrentActorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsManagerOrAdmin => User.IsInRole("Manager") || User.IsInRole("Admin");

    // GET /PointAdjustment?customerId=... (Màn hình dành cho Staff lập phiếu điều chỉnh điểm)
    [HttpGet]
    public async Task<IActionResult> Index(string? customerId = null)
    {
        return View(await BuildViewModelAsync(customerId));
    }

    // GET /PointAdjustment/Pending (Màn hình riêng dành cho Manager quản lý & phê duyệt phiếu điều chỉnh điểm của Staff)
    [HttpGet]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Pending()
    {
        return View("Manage", await BuildViewModelAsync(null));
    }

    // POST /PointAdjustment/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string customerId, int points, string reasonCategory, string? reasonNote, string? returnTo = null)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            TempData["Error"] = "Vui lòng chọn khách hàng cần điều chỉnh điểm.";
            return RedirectBack(customerId, returnTo);
        }

        if (points == 0)
        {
            TempData["Error"] = "Số điểm điều chỉnh phải khác 0 (dương để cộng, âm để trừ).";
            return RedirectBack(customerId, returnTo);
        }

        var category = (reasonCategory ?? string.Empty).Trim();
        var note = (reasonNote ?? string.Empty).Trim();
        var fullReason = string.IsNullOrEmpty(note) ? category : $"{category}: {note}";
        if (string.IsNullOrWhiteSpace(fullReason))
        {
            TempData["Error"] = "Vui lòng nhập lý do điều chỉnh điểm.";
            return RedirectBack(customerId, returnTo);
        }
        if (fullReason.Length > 300) fullReason = fullReason[..300];

        var customer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == customerId);
        if (customer == null) return NotFound();

        var actorId = CurrentActorId;
        if (string.IsNullOrEmpty(actorId))
        {
            actorId = await _db.Users.Select(u => u.Id).FirstAsync();
        }

        var actor = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId);
        var actorDisplay = actor != null
            ? (!string.IsNullOrWhiteSpace(actor.EmployeeCode) ? $"{actor.FullName} ({actor.EmployeeCode})" : actor.FullName)
            : (User.Identity?.Name ?? "Nhân viên");
        var actorRoleLabel = IsManagerOrAdmin ? "Manager yêu cầu" : "Nhân viên yêu cầu";

        var now = DateTime.UtcNow;

        // BR-F2-1: Nếu người lập là Manager -> ghi nhận tên người yêu cầu (Manager) và tự động phê duyệt ngay vào sổ cái điểm
        if (IsManagerOrAdmin)
        {
            var ownTx = _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync()
                : null;
            try
            {
                var req = new PointAdjustmentRequest
                {
                    CustomerId = customerId,
                    Points = points,
                    Reason = fullReason,
                    Status = PointAdjustmentRequest.StatusApproved,
                    RequestedById = actorId,
                    RequestedAtUtc = now,
                    ReviewedById = actorId,
                    ReviewedAtUtc = now,
                    ReviewNote = "Manager đã phê duyệt"
                };
                _db.PointAdjustmentRequests.Add(req);
                await _db.SaveChangesAsync();

                var ledgerReason = $"{fullReason} [Người yêu cầu: {actorDisplay} — {actorRoleLabel}]";
                var ledgerRes = await _ledger.PostAsync(
                    customerId: customerId,
                    delta: points,
                    type: PointTransaction.Adjust,
                    referenceCode: $"ADJ-{req.Id}",
                    invoiceId: null,
                    reason: ledgerReason,
                    actorId: actorId);

                if (!ledgerRes.Success)
                {
                    if (ownTx != null) await ownTx.RollbackAsync();
                    TempData["Error"] = ledgerRes.Message;
                    return RedirectBack(customerId, returnTo);
                }

                req.PointTransactionId = ledgerRes.TransactionId;
                AppendNotificationAndAudit(customerId, points, ledgerReason, ledgerRes.BalanceAfter, actorId, now);
                await _db.SaveChangesAsync();
                if (ownTx != null) await ownTx.CommitAsync();

                TempData["Success"] = $"Đã tạo và duyệt điều chỉnh {(points > 0 ? "+" : "")}{points} điểm cho {customer.FullName} (Người yêu cầu: {actorDisplay} — {actorRoleLabel}). Số dư mới: {ledgerRes.BalanceAfter} điểm.";
                return RedirectBack(customerId, returnTo);
            }
            finally
            {
                if (ownTx != null) await ownTx.DisposeAsync();
            }
        }

        // BR-F2-2: Phiếu do Staff lập -> gán RequestedById của nhân viên và chuyển sang trạng thái Pending chờ Manager phê duyệt
        var pendingReq = new PointAdjustmentRequest
        {
            CustomerId = customerId,
            Points = points,
            Reason = fullReason,
            Status = PointAdjustmentRequest.StatusPending,
            RequestedById = actorId,
            RequestedAtUtc = now
        };
        _db.PointAdjustmentRequests.Add(pendingReq);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã lập phiếu điều chỉnh {(points > 0 ? "+" : "")}{points} điểm cho {customer.FullName} (Người yêu cầu: {actorDisplay} — {actorRoleLabel}, đang chờ Manager phê duyệt).";
        return RedirectBack(customerId, returnTo);
    }

    // POST /PointAdjustment/Approve (Manager phê duyệt phiếu điều chỉnh điểm của Staff)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Approve(int id, string? reviewNote = null)
    {
        var req = await _db.PointAdjustmentRequests
            .Include(r => r.Customer)
            .Include(r => r.RequestedBy)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (req == null) return NotFound();
        if (req.Status != PointAdjustmentRequest.StatusPending)
        {
            TempData["Error"] = $"Phiếu #{id} đã được xử lý trước đó ({req.Status}).";
            return RedirectToAction(nameof(Pending));
        }

        var reviewerId = CurrentActorId;

        // BR-F2-3: Người lập không được tự duyệt yêu cầu của mình (Maker != Checker)
        if (!string.IsNullOrEmpty(reviewerId) && req.RequestedById == reviewerId)
        {
            TempData["Error"] = "Vi phạm nguyên tắc Maker ≠ Checker: Người lập phiếu không được tự phê duyệt phiếu của chính mình.";
            return RedirectToAction(nameof(Pending));
        }

        if (string.IsNullOrEmpty(reviewerId))
        {
            reviewerId = await _db.Users.Where(u => u.Id != req.RequestedById).Select(u => u.Id).FirstOrDefaultAsync() ?? req.RequestedById;
        }

        var reqActorDisplay = req.RequestedBy != null
            ? (!string.IsNullOrWhiteSpace(req.RequestedBy.EmployeeCode) ? $"{req.RequestedBy.FullName} ({req.RequestedBy.EmployeeCode})" : req.RequestedBy.FullName)
            : req.RequestedById;
        var isReqManager = await (from ur in _db.UserRoles.AsNoTracking()
                                  join rl in _db.Roles.AsNoTracking() on ur.RoleId equals rl.Id
                                  where ur.UserId == req.RequestedById && (rl.Name == "Manager" || rl.Name == "Admin")
                                  select rl.Name).AnyAsync();
        var reqRoleText = isReqManager ? "Manager yêu cầu" : "Nhân viên yêu cầu";
        var ledgerReason = $"{req.Reason} [Người yêu cầu: {reqActorDisplay} — {reqRoleText}]";

        var ownTx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync()
            : null;

        try
        {
            var ledgerRes = await _ledger.PostAsync(
                customerId: req.CustomerId,
                delta: req.Points,
                type: PointTransaction.Adjust,
                referenceCode: $"ADJ-{req.Id}",
                invoiceId: null,
                reason: ledgerReason,
                actorId: !string.IsNullOrEmpty(req.RequestedById) ? req.RequestedById : reviewerId);

            if (!ledgerRes.Success)
            {
                if (ownTx != null) await ownTx.RollbackAsync();
                TempData["Error"] = $"Không thể duyệt phiếu #{id}: {ledgerRes.Message}";
                return RedirectToAction(nameof(Pending));
            }

            var now = DateTime.UtcNow;
            req.Status = PointAdjustmentRequest.StatusApproved;
            req.ReviewedById = reviewerId;
            req.ReviewedAtUtc = now;
            req.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? $"Manager đã phê duyệt (Đơn của: {reqActorDisplay})" : reviewNote.Trim();
            req.PointTransactionId = ledgerRes.TransactionId;

            AppendNotificationAndAudit(req.CustomerId, req.Points, ledgerReason, ledgerRes.BalanceAfter, reviewerId, now);
            await _db.SaveChangesAsync();
            if (ownTx != null) await ownTx.CommitAsync();

            TempData["Success"] = $"Manager đã phê duyệt phiếu #{id} (Người yêu cầu: {reqActorDisplay}): {(req.Points > 0 ? "+" : "")}{req.Points} điểm cho {req.Customer?.FullName}.";
            return RedirectToAction(nameof(Pending));
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync();
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    // POST /PointAdjustment/Reject (Manager từ chối phiếu điều chỉnh điểm của Staff)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Reject(int id, string? reviewNote = null)
    {
        var req = await _db.PointAdjustmentRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req == null) return NotFound();
        if (req.Status != PointAdjustmentRequest.StatusPending)
        {
            TempData["Error"] = $"Phiếu #{id} đã được xử lý trước đó.";
            return RedirectToAction(nameof(Pending));
        }

        var reviewerId = CurrentActorId;
        if (!string.IsNullOrEmpty(reviewerId) && req.RequestedById == reviewerId)
        {
            TempData["Error"] = "Người lập phiếu không được tự duyệt/từ chối phiếu của mình.";
            return RedirectToAction(nameof(Pending));
        }

        req.Status = PointAdjustmentRequest.StatusRejected;
        req.ReviewedById = string.IsNullOrEmpty(reviewerId) ? null : reviewerId;
        req.ReviewedAtUtc = DateTime.UtcNow;
        req.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? "Manager từ chối" : reviewNote.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Manager đã từ chối phiếu điều chỉnh điểm #{id}.";
        return RedirectToAction(nameof(Pending));
    }

    private void AppendNotificationAndAudit(string customerId, int points, string reason, int balanceAfter, string actorId, DateTime now)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = customerId,
            Title = points > 0 ? $"Cộng +{points} điểm điều chỉnh" : $"Trừ {points} điểm điều chỉnh",
            Content = $"Điểm thưởng của bạn vừa được điều chỉnh {(points > 0 ? "+" : "")}{points} điểm. Lý do: {reason}. Số dư hiện tại: {balanceAfter} điểm.",
            Type = "PointAdjust",
            IsRead = false,
            CreatedAt = now
        });

        _db.CustomerAuditLogs.Add(new CustomerAuditLog
        {
            CustomerId = customerId,
            Action = "PointAdjust",
            Detail = JsonSerializer.Serialize(new { points, reason, balanceAfter }),
            ActorId = actorId,
            CreatedAtUtc = now
        });
    }

    private IActionResult RedirectBack(string? customerId, string? returnTo)
    {
        if (returnTo == "profile" && !string.IsNullOrEmpty(customerId))
            return RedirectToAction("Profile", "CustomerManagement", new { id = customerId });
        return RedirectToAction(nameof(Index), new { customerId });
    }

    private async Task<PointAdjustmentIndexViewModel> BuildViewModelAsync(string? customerId)
    {
        var pending = await _db.PointAdjustmentRequests.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.RequestedBy)
            .Where(r => r.Status == PointAdjustmentRequest.StatusPending)
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToListAsync();

        var recentHistory = await _db.PointAdjustmentRequests.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.RequestedBy)
            .Include(r => r.ReviewedBy)
            .Where(r => r.Status != PointAdjustmentRequest.StatusPending)
            .OrderByDescending(r => r.ReviewedAtUtc ?? r.RequestedAtUtc)
            .Take(25)
            .ToListAsync();

        var requesterIds = pending.Select(r => r.RequestedById)
            .Concat(recentHistory.Select(r => r.RequestedById))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var requesterRoleLabels = new Dictionary<string, string>();
        if (requesterIds.Count > 0)
        {
            var rolePairs = await (from ur in _db.UserRoles.AsNoTracking()
                                   join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                                   where requesterIds.Contains(ur.UserId)
                                   select new { ur.UserId, RoleName = r.Name })
                                   .ToListAsync();

            foreach (var id in requesterIds)
            {
                var roles = rolePairs.Where(x => x.UserId == id).Select(x => x.RoleName).ToList();
                if (roles.Contains("Manager") || roles.Contains("Admin"))
                    requesterRoleLabels[id] = "Manager yêu cầu";
                else
                    requesterRoleLabels[id] = "Nhân viên yêu cầu";
            }
        }

        foreach (var h in recentHistory)
        {
            if (!string.IsNullOrEmpty(h.ReviewNote) && h.ReviewNote.Contains("Admin", StringComparison.OrdinalIgnoreCase))
            {
                h.ReviewNote = h.ReviewNote.Replace("Admin", "Manager", StringComparison.OrdinalIgnoreCase);
            }
        }

        var roleId = await _db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).FirstOrDefaultAsync();
        var customers = await _db.Users.AsNoTracking()
            .Include(u => u.Tier)
            .Where(u => u.IsActive && _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
            .OrderBy(u => u.FullName)
            .Select(u => new CustomerSelectItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                CardCode = u.EmployeeCode,
                TierName = u.Tier != null ? u.Tier.TierName : "Member",
                RewardPoints = u.RewardPoints
            })
            .ToListAsync();

        var curActor = !string.IsNullOrEmpty(CurrentActorId)
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentActorId)
            : null;
        var curRequesterName = curActor != null
            ? (!string.IsNullOrWhiteSpace(curActor.EmployeeCode) ? $"{curActor.FullName} ({curActor.EmployeeCode})" : curActor.FullName)
            : (User.Identity?.Name ?? "Nhân viên");
        var curRequesterRoleLabel = IsManagerOrAdmin ? "Manager yêu cầu" : "Nhân viên yêu cầu";

        return new PointAdjustmentIndexViewModel
        {
            PendingRequests = pending,
            RecentHistory = recentHistory,
            Customers = customers,
            SelectedCustomerId = customerId,
            RequesterRoleLabels = requesterRoleLabels,
            CurrentRequesterName = curRequesterName,
            CurrentRequesterRoleLabel = curRequesterRoleLabel,
            IsManagerRequester = IsManagerOrAdmin
        };
    }
}
