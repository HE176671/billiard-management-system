using System.Security.Claims;
using System.Text.Json;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin,Staff")]
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
    private bool IsAdmin => User.IsInRole("Admin");

    // GET /PointAdjustment?customerId=...
    [HttpGet]
    public async Task<IActionResult> Index(string? customerId = null)
    {
        return View(await BuildViewModelAsync(customerId));
    }

    // GET /PointAdjustment/Pending (Admin duyệt các yêu cầu Pending)
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Pending()
    {
        return View("Index", await BuildViewModelAsync(null));
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

        var now = DateTime.UtcNow;

        // BR-F2-1: |điểm| <= 50 và người tạo là Admin -> tự duyệt, ghi ledger ngay
        if (IsAdmin && Math.Abs(points) <= 50)
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
                    ReviewNote = "Tự động duyệt (Admin, |điểm| <= 50)"
                };
                _db.PointAdjustmentRequests.Add(req);
                await _db.SaveChangesAsync();

                var ledgerRes = await _ledger.PostAsync(
                    customerId: customerId,
                    delta: points,
                    type: PointTransaction.Adjust,
                    referenceCode: $"ADJ-{req.Id}",
                    invoiceId: null,
                    reason: fullReason,
                    actorId: actorId);

                if (!ledgerRes.Success)
                {
                    if (ownTx != null) await ownTx.RollbackAsync();
                    TempData["Error"] = ledgerRes.Message;
                    return RedirectBack(customerId, returnTo);
                }

                req.PointTransactionId = ledgerRes.TransactionId;
                AppendNotificationAndAudit(customerId, points, fullReason, ledgerRes.BalanceAfter, actorId, now);
                await _db.SaveChangesAsync();
                if (ownTx != null) await ownTx.CommitAsync();

                TempData["Success"] = $"Đã điều chỉnh {(points > 0 ? "+" : "")}{points} điểm cho {customer.FullName} (tự động duyệt). Số dư mới: {ledgerRes.BalanceAfter} điểm.";
                return RedirectBack(customerId, returnTo);
            }
            finally
            {
                if (ownTx != null) await ownTx.DisposeAsync();
            }
        }

        // BR-F2-2: Còn lại -> Pending chờ Admin duyệt (maker != checker)
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

        TempData["Success"] = $"Đã gửi yêu cầu điều chỉnh {(points > 0 ? "+" : "")}{points} điểm cho {customer.FullName} (Trạng thái: Chờ Admin phê duyệt).";
        return RedirectBack(customerId, returnTo);
    }

    // POST /PointAdjustment/Approve
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(int id, string? reviewNote = null)
    {
        var req = await _db.PointAdjustmentRequests
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (req == null) return NotFound();
        if (req.Status != PointAdjustmentRequest.StatusPending)
        {
            TempData["Error"] = $"Yêu cầu #{id} đã được xử lý trước đó ({req.Status}).";
            return RedirectToAction(nameof(Index));
        }

        var reviewerId = CurrentActorId;

        // BR-F2-3: Người tạo không được tự duyệt yêu cầu của mình (maker != checker)
        if (!string.IsNullOrEmpty(reviewerId) && req.RequestedById == reviewerId)
        {
            TempData["Error"] = "Vi phạm nguyên tắc Maker ≠ Checker: Người tạo yêu cầu không được tự phê duyệt yêu cầu của chính mình.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrEmpty(reviewerId))
        {
            reviewerId = await _db.Users.Where(u => u.Id != req.RequestedById).Select(u => u.Id).FirstOrDefaultAsync() ?? req.RequestedById;
        }

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
                reason: req.Reason,
                actorId: reviewerId);

            if (!ledgerRes.Success)
            {
                if (ownTx != null) await ownTx.RollbackAsync();
                TempData["Error"] = $"Không thể duyệt yêu cầu #{id}: {ledgerRes.Message}";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.UtcNow;
            req.Status = PointAdjustmentRequest.StatusApproved;
            req.ReviewedById = reviewerId;
            req.ReviewedAtUtc = now;
            req.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? "Đã duyệt" : reviewNote.Trim();
            req.PointTransactionId = ledgerRes.TransactionId;

            AppendNotificationAndAudit(req.CustomerId, req.Points, req.Reason, ledgerRes.BalanceAfter, reviewerId, now);
            await _db.SaveChangesAsync();
            if (ownTx != null) await ownTx.CommitAsync();

            TempData["Success"] = $"Đã phê duyệt yêu cầu #{id}: {(req.Points > 0 ? "+" : "")}{req.Points} điểm cho {req.Customer?.FullName}.";
            return RedirectToAction(nameof(Index));
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

    // POST /PointAdjustment/Reject
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(int id, string? reviewNote = null)
    {
        var req = await _db.PointAdjustmentRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req == null) return NotFound();
        if (req.Status != PointAdjustmentRequest.StatusPending)
        {
            TempData["Error"] = $"Yêu cầu #{id} đã được xử lý trước đó.";
            return RedirectToAction(nameof(Index));
        }

        var reviewerId = CurrentActorId;
        if (!string.IsNullOrEmpty(reviewerId) && req.RequestedById == reviewerId)
        {
            TempData["Error"] = "Người tạo yêu cầu không được tự duyệt/từ chối yêu cầu của mình.";
            return RedirectToAction(nameof(Index));
        }

        req.Status = PointAdjustmentRequest.StatusRejected;
        req.ReviewedById = string.IsNullOrEmpty(reviewerId) ? null : reviewerId;
        req.ReviewedAtUtc = DateTime.UtcNow;
        req.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? "Từ chối" : reviewNote.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã từ chối yêu cầu điều chỉnh điểm #{id}.";
        return RedirectToAction(nameof(Index));
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

        var history = await _db.PointAdjustmentRequests.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.RequestedBy)
            .Include(r => r.ReviewedBy)
            .Where(r => r.Status != PointAdjustmentRequest.StatusPending)
            .OrderByDescending(r => r.ReviewedAtUtc ?? r.RequestedAtUtc)
            .Take(25)
            .ToListAsync();

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

        return new PointAdjustmentIndexViewModel
        {
            PendingRequests = pending,
            RecentHistory = history,
            Customers = customers,
            SelectedCustomerId = customerId
        };
    }
}
