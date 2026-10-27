using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>TV5 — BMS-017: mở ca, đóng ca, đối soát tiền mặt, lịch sử ca.</summary>
[Authorize(Roles = "Staff,Admin")]
public class ShiftController : Controller
{
    private const int PageSize = 15;
    private readonly ApplicationDbContext _db;
    private readonly IShiftService _shifts;
    private readonly UserManager<ApplicationUser> _users;

    public ShiftController(ApplicationDbContext db, IShiftService shifts, UserManager<ApplicationUser> users)
    {
        _db = db;
        _shifts = shifts;
        _users = users;
    }

    private string CurrentUserId => _users.GetUserId(User)!;

    #region Ca cua toi: mo ca hoac xem ca dang mo

    // GET /Shift
    public async Task<IActionResult> Index()
    {
        var vm = new ShiftHomeViewModel();
        var active = await _shifts.GetActiveShiftAsync(CurrentUserId);
        if (active != null)
        {
            vm.Active = await _shifts.GetSummaryAsync(active.Id);
            vm.Close.ShiftId = active.Id;
        }
        vm.Recent = (await LoadRowsAsync(_db.WorkShifts.Where(s => s.StaffId == CurrentUserId)))
            .Take(5).ToList();
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open([Bind(Prefix = "Open")] OpenShiftViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError() ?? "Dữ liệu mở ca không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            var shift = await _shifts.OpenShiftAsync(CurrentUserId, model.StartingCash!.Value, model.Notes);
            TempData["Success"] = $"Đã mở ca #{shift.Id} với {shift.StartingCash:N0} ₫ đầu ca.";
        }
        catch (ShiftException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Dong ca va doi soat

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([Bind(Prefix = "Close")] CloseShiftViewModel model)
    {
        var shift = await _db.WorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == model.ShiftId);
        if (shift == null) return NotFound();
        var isOwner = shift.StaffId == CurrentUserId;
        if (!isOwner && !User.IsInRole("Admin")) return Forbid();

        var back = isOwner ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id = shift.Id });
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError() ?? "Dữ liệu đóng ca không hợp lệ.";
            return back;
        }
        try
        {
            var summary = await _shifts.CloseShiftAsync(shift.Id, CurrentUserId, model.EndingCash!.Value, model.Notes);
            var diff = summary.Difference ?? 0;
            TempData["Success"] = diff == 0
                ? $"Đã đóng ca #{shift.Id}. Tiền mặt khớp ({summary.ExpectedCash:N0} ₫)."
                : $"Đã đóng ca #{shift.Id}. Chênh lệch {(diff > 0 ? "thừa" : "thiếu")} {Math.Abs(diff):N0} ₫ đã được ghi nhận.";
            return RedirectToAction(nameof(Details), new { id = shift.Id });
        }
        catch (ShiftException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["CloseEndingCash"] = model.EndingCash?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return back;
        }
    }

    #endregion

    #region Chi tiet ca

    // GET /Shift/Details/5 — chủ ca hoặc Admin
    public async Task<IActionResult> Details(int id)
    {
        var summary = await _shifts.GetSummaryAsync(id);
        if (summary == null) return NotFound();
        if (summary.Shift.StaffId != CurrentUserId && !User.IsInRole("Admin")) return Forbid();

        var vm = new ShiftDetailsViewModel
        {
            Summary  = summary,
            CanClose = summary.IsActive,
            Close    = new CloseShiftViewModel { ShiftId = id },
            Payments = await _db.PaymentTransactions.AsNoTracking()
                .Where(p => p.WorkShiftId == id)
                .OrderBy(p => p.TransactionTimeUtc)
                .Select(p => new ShiftPaymentRowViewModel
                {
                    Id = p.Id, InvoiceId = p.InvoiceId, PaymentMethod = p.PaymentMethod,
                    AmountPaid = p.AmountPaid, TimeUtc = p.TransactionTimeUtc
                }).ToListAsync()
        };
        return View(vm);
    }

    #endregion

    #region Lich su ca cua toi

    // GET /Shift/History
    public async Task<IActionResult> History(DateOnly? from, DateOnly? to, int page = 1)
    {
        var vm = await BuildListAsync(_db.WorkShifts.Where(s => s.StaffId == CurrentUserId),
            from, to, null, null, false, page);
        return View("List", vm);
    }

    #endregion

    #region Admin: quan ly moi ca

    // GET /Shift/Manage
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Manage(DateOnly? from, DateOnly? to, string? staffId, string? status,
        bool onlyDifference = false, int page = 1)
    {
        var query = _db.WorkShifts.AsQueryable();
        if (!string.IsNullOrEmpty(staffId)) query = query.Where(s => s.StaffId == staffId);
        var vm = await BuildListAsync(query, from, to, staffId, status, onlyDifference, page);
        vm.IsManage = true;
        vm.StaffOptions = await _db.WorkShifts.AsNoTracking()
            .Select(s => new { s.StaffId, Name = s.Staff!.FullName, Code = s.Staff.EmployeeCode })
            .Distinct().OrderBy(s => s.Name)
            .Select(s => new StaffOptionViewModel { Id = s.StaffId, Name = s.Code == null ? s.Name : s.Code + " — " + s.Name })
            .ToListAsync();
        return View("List", vm);
    }

    #endregion

    #region Ham ho tro: loc, tinh tien mat, phan trang

    private async Task<ShiftListViewModel> BuildListAsync(IQueryable<WorkShift> query, DateOnly? from, DateOnly? to,
        string? staffId, string? status, bool onlyDifference, int page)
    {
        var today = VnTime.Today;
        var vm = new ShiftListViewModel
        {
            From = from ?? today.AddDays(-30),
            To = to ?? today,
            StaffId = staffId,
            Status = status is WorkShift.StatusActive or WorkShift.StatusClosed ? status : null,
            OnlyDifference = onlyDifference
        };
        if (vm.From > vm.To || vm.To.DayNumber - vm.From.DayNumber > 366)
        {
            ModelState.AddModelError("", "Chọn ngày bắt đầu không sau ngày kết thúc, tối đa 367 ngày.");
            return vm;
        }

        var start = VnTime.StartOfDayUtc(vm.From);
        var end = VnTime.StartOfDayUtc(vm.To.AddDays(1));
        // Ca đang mở luôn hiện, kể cả khi mở trước khoảng ngày (quên đóng ca).
        query = query.Where(s => (s.StartTimeUtc >= start && s.StartTimeUtc < end) || s.Status == WorkShift.StatusActive);
        if (vm.Status != null) query = query.Where(s => s.Status == vm.Status);

        var rows = await LoadRowsAsync(query);
        if (onlyDifference) rows = rows.Where(r => r.Difference is not null and not 0).ToList();

        vm.TotalCount = rows.Count;
        vm.ActiveCount = rows.Count(r => r.Status == WorkShift.StatusActive);
        vm.TotalCashCollected = rows.Sum(r => r.CashCollected);
        vm.TotalDifference = rows.Sum(r => r.Difference ?? 0);
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize));
        vm.Page = Math.Clamp(page, 1, vm.TotalPages);
        vm.Shifts = rows.Skip((vm.Page - 1) * PageSize).Take(PageSize).ToList();
        return vm;
    }

    /// <summary>Ca đã đóng dùng snapshot; ca đang mở tính tiền mặt trực tiếp từ giao dịch.</summary>
    private async Task<List<ShiftRowViewModel>> LoadRowsAsync(IQueryable<WorkShift> query)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(s => s.Status == WorkShift.StatusActive).ThenByDescending(s => s.StartTimeUtc)
            .Select(s => new
            {
                s.Id, s.StartTimeUtc, s.EndTimeUtc, s.Status, s.StartingCash, s.EndingCash,
                s.CashCollected, s.ExpectedCash,
                StaffName = s.Staff!.FullName, s.Staff.EmployeeCode,
                LiveCash = _db.PaymentTransactions
                    .Where(p => p.WorkShiftId == s.Id && p.PaymentMethod == PaymentTransaction.MethodCash)
                    .Sum(p => (decimal?)p.AmountPaid) ?? 0
            })
            .ToListAsync();
        return rows.Select(s =>
        {
            var cash = s.CashCollected ?? s.LiveCash;
            return new ShiftRowViewModel
            {
                Id = s.Id, StaffName = s.StaffName, EmployeeCode = s.EmployeeCode,
                StartTimeUtc = s.StartTimeUtc, EndTimeUtc = s.EndTimeUtc, Status = s.Status,
                StartingCash = s.StartingCash, EndingCash = s.EndingCash,
                CashCollected = cash, ExpectedCash = s.ExpectedCash ?? s.StartingCash + cash
            };
        }).ToList();
    }

    private string? FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m));

    #endregion
}
