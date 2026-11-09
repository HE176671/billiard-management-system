using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// TV5 — UC38 View Audit Log: Manager xem nhật ký thao tác nhạy cảm, lọc theo ngày, loại thao tác, người thao tác.
/// Chỉ đọc; nhật ký không sửa, không xóa được từ giao diện.
/// </summary>
[Authorize(Roles = "Manager")]
public class AuditLogController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;

    public AuditLogController(ApplicationDbContext db) => _db = db;

    // GET /AuditLog
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? actionType, string? actorId,
        string? search, int page = 1)
    {
        var today = VnTime.Today;
        var vm = new AuditLogListViewModel
        {
            From = from ?? today.AddDays(-7),
            To = to ?? today,
            ActionType = string.IsNullOrWhiteSpace(actionType) ? null : actionType.Trim(),
            ActorId = string.IsNullOrWhiteSpace(actorId) ? null : actorId,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            ActionOptions = AuditActions.Labels
        };

        vm.ActorOptions = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorId != null)
            .Select(a => new { a.ActorId, a.ActorName })
            .Distinct()
            .OrderBy(a => a.ActorName)
            .Select(a => new StaffOptionViewModel { Id = a.ActorId!, Name = a.ActorName ?? a.ActorId! })
            .ToListAsync();

        if (vm.From > vm.To || vm.To.DayNumber - vm.From.DayNumber > 366)
        {
            ModelState.AddModelError("", "Ngày bắt đầu không được sau ngày kết thúc, tối đa 367 ngày.");
            return View(vm);
        }

        var start = VnTime.StartOfDayUtc(vm.From);
        var end = VnTime.StartOfDayUtc(vm.To.AddDays(1));
        var query = _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= start && a.CreatedAtUtc < end);
        if (vm.ActionType != null) query = query.Where(a => a.ActionType == vm.ActionType);
        if (vm.ActorId != null) query = query.Where(a => a.ActorId == vm.ActorId);
        if (vm.Search != null)
        {
            var term = vm.Search;
            query = query.Where(a => a.Description.Contains(term)
                || (a.Reason != null && a.Reason.Contains(term))
                || (a.EntityId != null && a.EntityId == term));
        }

        vm.TotalCount = await query.CountAsync();
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(vm.TotalCount / (double)PageSize));
        vm.Page = Math.Clamp(page, 1, vm.TotalPages);

        var rows = await query
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Skip((vm.Page - 1) * PageSize).Take(PageSize)
            .ToListAsync();
        vm.Rows = rows.Select(a => new AuditLogRowViewModel
        {
            Id = a.Id,
            CreatedAtUtc = a.CreatedAtUtc,
            ActionType = a.ActionType,
            ActionLabel = AuditActions.Label(a.ActionType),
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            Description = a.Description,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            Reason = a.Reason,
            ActorName = a.ActorName
        }).ToList();

        return View(vm);
    }
}
