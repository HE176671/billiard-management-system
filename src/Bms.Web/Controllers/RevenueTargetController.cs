using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// TV5 — UC40 Quản lý mục tiêu doanh thu tháng (Manager).
/// Bảng ghi: RevenueTargets (+ AuditLogs). Bảng đọc: PaymentTransactions (thực thu).
///
/// Quy tắc nghiệp vụ:
///  BR-40.1 Mỗi tháng tối đa 1 mục tiêu (unique Year + Month).
///  BR-40.2 Chỉ đặt mục tiêu cho tháng hiện tại hoặc tối đa 12 tháng tới; tháng đã qua là lịch sử, không thêm/sửa.
///  BR-40.3 Mục tiêu từ 1 triệu đến 100 tỷ VND, làm tròn đến nghìn đồng.
///  BR-40.4 Sửa mục tiêu của tháng đang chạy phải ghi lý do (vì làm thay đổi cách đánh giá giữa chừng).
///  BR-40.5 Chỉ xóa được mục tiêu của tháng chưa bắt đầu.
///  BR-40.6 Mọi thêm/sửa/xóa đều ghi Nhật ký hệ thống.
/// </summary>
[Authorize(Roles = "Manager")]
public class RevenueTargetController : Controller
{
    private const int MaxMonthsAhead = 12;

    private readonly ApplicationDbContext _db;
    private readonly IRevenueTargetService _targets;
    private readonly IAuditLogService _audit;

    public RevenueTargetController(ApplicationDbContext db, IRevenueTargetService targets, IAuditLogService audit)
    {
        _db = db;
        _targets = targets;
        _audit = audit;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // GET /RevenueTarget
    public async Task<IActionResult> Index()
    {
        var today = VnTime.Today;
        var items = await _targets.GetAllProgressAsync();
        return View(new RevenueTargetIndexViewModel
        {
            Items = items,
            Current = items.FirstOrDefault(i => i.Year == today.Year && i.Month == today.Month)
        });
    }

    // GET /RevenueTarget/Create — mặc định gợi ý tháng gần nhất chưa có mục tiêu
    public async Task<IActionResult> Create()
    {
        var month = VnTime.Today;
        var first = new DateOnly(month.Year, month.Month, 1);
        var used = await _db.RevenueTargets.Select(t => t.Year * 12 + t.Month).ToListAsync();
        for (int i = 0; i <= MaxMonthsAhead && used.Contains(first.Year * 12 + first.Month); i++)
            first = first.AddMonths(1);
        return View("Form", new RevenueTargetFormViewModel { Year = first.Year, Month = first.Month });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RevenueTargetFormViewModel model)
    {
        ValidateCommon(model);
        if (ModelState.IsValid)
        {
            var state = RevenueTargetService.GetState(model.Year!.Value, model.Month!.Value, VnTime.Today);
            if (state == TargetMonthState.Ended)
                ModelState.AddModelError(nameof(model.Month), "Tháng này đã kết thúc, không đặt mục tiêu cho quá khứ.");
            if (await _db.RevenueTargets.AnyAsync(t => t.Year == model.Year && t.Month == model.Month))
                ModelState.AddModelError(nameof(model.Month), $"Tháng {model.Month:00}/{model.Year} đã có mục tiêu. Hãy sửa mục tiêu đó.");
        }
        if (!ModelState.IsValid) return View("Form", model);

        var target = new RevenueTarget
        {
            Year = model.Year!.Value,
            Month = model.Month!.Value,
            TargetAmount = model.TargetAmount!.Value,
            Note = Clean(model.Note),
            CreatedById = CurrentUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RevenueTargets.Add(target);
        await _audit.AddAsync(AuditActions.RevenueTargetChange, "RevenueTarget", $"{target.Year}-{target.Month:00}",
            $"Đặt mục tiêu doanh thu tháng {target.Month:00}/{target.Year}: {target.TargetAmount:N0} VND.",
            newValue: target.TargetAmount.ToString("0"));

        if (!await TrySaveAsync(model)) return View("Form", model);
        TempData["Success"] = $"Đã đặt mục tiêu tháng {target.Month:00}/{target.Year}: {target.TargetAmount:N0} VND.";
        return RedirectToAction(nameof(Index));
    }

    // GET /RevenueTarget/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var t = await _db.RevenueTargets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        var state = RevenueTargetService.GetState(t.Year, t.Month, VnTime.Today);
        if (state == TargetMonthState.Ended)
        {
            TempData["Error"] = $"Tháng {t.Month:00}/{t.Year} đã kết thúc, mục tiêu được giữ nguyên làm lịch sử.";
            return RedirectToAction(nameof(Index));
        }
        return View("Form", await BuildEditModelAsync(t, new RevenueTargetFormViewModel
        {
            Id = t.Id, Year = t.Year, Month = t.Month, TargetAmount = t.TargetAmount, Note = t.Note
        }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RevenueTargetFormViewModel model)
    {
        var t = await _db.RevenueTargets.FirstOrDefaultAsync(x => x.Id == model.Id);
        if (t == null) return NotFound();

        // Năm/tháng là khóa nghiệp vụ, không đổi khi sửa: luôn lấy từ DB, bỏ qua giá trị form gửi lên.
        model.Year = t.Year;
        model.Month = t.Month;
        ValidateCommon(model);

        var state = RevenueTargetService.GetState(t.Year, t.Month, VnTime.Today);
        if (state == TargetMonthState.Ended)
            ModelState.AddModelError(string.Empty, "Tháng này đã kết thúc, không sửa được mục tiêu.");
        if (state == TargetMonthState.Current && model.TargetAmount != t.TargetAmount
            && (model.Reason?.Trim().Length ?? 0) < 10)
            ModelState.AddModelError(nameof(model.Reason), "Tháng đang chạy: phải ghi lý do điều chỉnh (ít nhất 10 ký tự).");

        if (!ModelState.IsValid) return View("Form", await BuildEditModelAsync(t, model));

        var oldAmount = t.TargetAmount;
        var newNote = Clean(model.Note);
        if (oldAmount == model.TargetAmount && t.Note == newNote)
        {
            TempData["Success"] = "Không có thay đổi nào.";
            return RedirectToAction(nameof(Index));
        }

        t.TargetAmount = model.TargetAmount!.Value;
        t.Note = newNote;
        t.UpdatedById = CurrentUserId;
        t.UpdatedAtUtc = DateTime.UtcNow;
        await _audit.AddAsync(AuditActions.RevenueTargetChange, "RevenueTarget", $"{t.Year}-{t.Month:00}",
            $"Sửa mục tiêu doanh thu tháng {t.Month:00}/{t.Year} từ {oldAmount:N0} thành {t.TargetAmount:N0} VND.",
            oldValue: oldAmount.ToString("0"), newValue: t.TargetAmount.ToString("0"), reason: model.Reason);

        if (!await TrySaveAsync(model)) return View("Form", await BuildEditModelAsync(t, model));
        TempData["Success"] = $"Đã cập nhật mục tiêu tháng {t.Month:00}/{t.Year}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.RevenueTargets.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (RevenueTargetService.GetState(t.Year, t.Month, VnTime.Today) != TargetMonthState.Future)
        {
            TempData["Error"] = $"Chỉ xóa được mục tiêu của tháng chưa bắt đầu. Tháng {t.Month:00}/{t.Year} đã/đang được đánh giá.";
            return RedirectToAction(nameof(Index));
        }

        _db.RevenueTargets.Remove(t);
        await _audit.AddAsync(AuditActions.RevenueTargetChange, "RevenueTarget", $"{t.Year}-{t.Month:00}",
            $"Xóa mục tiêu doanh thu tháng {t.Month:00}/{t.Year} ({t.TargetAmount:N0} VND).",
            oldValue: t.TargetAmount.ToString("0"));
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa mục tiêu tháng {t.Month:00}/{t.Year}.";
        return RedirectToAction(nameof(Index));
    }

    // ── Kiểm tra dùng chung cho thêm và sửa ───────────────────────────────────
    private void ValidateCommon(RevenueTargetFormViewModel model)
    {
        if (model.Year is int y && model.Month is int m && m is >= 1 and <= 12)
        {
            var today = VnTime.Today;
            int monthsAhead = (y * 12 + m) - (today.Year * 12 + today.Month);
            if (monthsAhead > MaxMonthsAhead)
                ModelState.AddModelError(nameof(model.Month), $"Chỉ đặt mục tiêu tối đa {MaxMonthsAhead} tháng tới.");
        }
        if (model.TargetAmount is decimal amount && amount % 1000 != 0)
            ModelState.AddModelError(nameof(model.TargetAmount), "Mục tiêu phải làm tròn đến nghìn đồng (ví dụ 150.000.000).");
    }

    private async Task<RevenueTargetFormViewModel> BuildEditModelAsync(RevenueTarget t, RevenueTargetFormViewModel model)
    {
        model.State = RevenueTargetService.GetState(t.Year, t.Month, VnTime.Today);
        model.OldAmount = t.TargetAmount;
        model.ActualRevenue = model.State == TargetMonthState.Future ? 0 : await _targets.GetActualRevenueAsync(t.Year, t.Month);
        return model;
    }

    /// <summary>Hai Manager cùng thêm 1 tháng: unique index chặn lần thứ hai, báo lỗi thay vì lỗi 500.</summary>
    private async Task<bool> TrySaveAsync(RevenueTargetFormViewModel model)
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(model.Month), "Tháng này vừa được người khác đặt mục tiêu. Tải lại trang để xem.");
            return false;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
