using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class ShiftController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ShiftController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET /Shift
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var query = _context.WorkShifts
            .Include(x => x.Staff)
            .AsQueryable();

        // Tìm kiếm theo tên hoặc mã nhân viên
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x =>
                (x.Staff.FullName != null && x.Staff.FullName.ToLower().Contains(s)) ||
                (x.Staff.EmployeeCode != null && x.Staff.EmployeeCode.ToLower().Contains(s)));
        }

        // Lọc trạng thái
        if (status == "active") query = query.Where(x => x.Status == "Active");
        if (status == "closed") query = query.Where(x => x.Status == "Closed");

        var totalCount  = await _context.WorkShifts.CountAsync();
        var activeCount = await _context.WorkShifts.CountAsync(x => x.Status == "Active");

        var rows = await query
            .OrderByDescending(x => x.StartTimeUtc)
            .Select(x => new ShiftRowViewModel
            {
                Id           = x.Id,
                StaffId      = x.StaffId,
                StaffName    = x.Staff.FullName,
                EmployeeCode = x.Staff.EmployeeCode,
                StartTimeUtc = x.StartTimeUtc,
                EndTimeUtc   = x.EndTimeUtc,
                StartingCash = x.StartingCash,
                EndingCash   = x.EndingCash,
                Status       = x.Status,
                Notes        = x.Notes
            })
            .ToListAsync();

        var vm = new ShiftListViewModel
        {
            Shifts       = rows,
            Search       = search,
            StatusFilter = status,
            TotalCount   = totalCount,
            ActiveCount  = activeCount
        };
        return View(vm);
    }

    // GET /Shift/Create
    public async Task<IActionResult> Create()
    {
        var vm = new CreateShiftViewModel
        {
            StartTimeLocal = DateTime.UtcNow.AddHours(7),
            StaffList      = await GetStaffListAsync()
        };
        return View(vm);
    }

    // POST /Shift/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateShiftViewModel model)
    {
        model.StaffList = await GetStaffListAsync();
        if (!ModelState.IsValid) return View(model);

        var entity = new WorkShift
        {
            StaffId      = model.StaffId,
            // model.StartTimeLocal là giờ VN (UTC+7) → trừ 7h để lưu UTC
            StartTimeUtc = model.StartTimeLocal.AddHours(-7),
            StartingCash = model.StartingCash,
            Status       = "Active",
            Notes        = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim()
        };

        _context.WorkShifts.Add(entity);
        await _context.SaveChangesAsync();

        var staff = await _userManager.FindByIdAsync(model.StaffId);
        TempData["Success"] = $"Đã mở ca cho nhân viên {staff?.FullName ?? "—"} thành công.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Shift/Edit/id
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await _context.WorkShifts
            .Include(x => x.Staff)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound();

        var vm = new EditShiftViewModel
        {
            Id             = entity.Id,
            StaffName      = entity.Staff.FullName,
            EmployeeCode   = entity.Staff.EmployeeCode,
            StartTimeLocal = entity.StartTimeUtc.AddHours(7),
            EndTimeLocal   = entity.EndTimeUtc?.AddHours(7),
            StartingCash   = entity.StartingCash,
            EndingCash     = entity.EndingCash,
            Status         = entity.Status,
            Notes          = entity.Notes
        };
        return View(vm);
    }

    // POST /Shift/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditShiftViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var entity = await _context.WorkShifts
            .Include(x => x.Staff)
            .FirstOrDefaultAsync(x => x.Id == model.Id);
        if (entity == null) return NotFound();

        // model.*Local là giờ VN (UTC+7) → trừ 7h để lưu UTC
        entity.StartTimeUtc = model.StartTimeLocal.AddHours(-7);
        entity.EndTimeUtc   = model.EndTimeLocal?.AddHours(-7);
        entity.StartingCash = model.StartingCash;
        entity.EndingCash   = model.EndingCash;
        entity.Status       = model.Status;
        entity.Notes        = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();

        // Nếu đóng ca mà chưa có EndTime thì tự điền giờ hiện tại (UTC)
        if (entity.Status == "Closed" && entity.EndTimeUtc == null)
            entity.EndTimeUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã cập nhật ca #{entity.Id} thành công.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Shift/ToggleStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var entity = await _context.WorkShifts.FindAsync(id);
        if (entity == null) return NotFound();

        if (entity.Status == "Active")
        {
            entity.Status     = "Closed";
            entity.EndTimeUtc = DateTime.UtcNow;
            TempData["Success"] = $"Đã đóng ca #{entity.Id}.";
        }
        else
        {
            entity.Status     = "Active";
            entity.EndTimeUtc = null;
            TempData["Success"] = $"Đã mở lại ca #{entity.Id}.";
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // POST /Shift/Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.WorkShifts.FindAsync(id);
        if (entity == null) return NotFound();

        try
        {
            _context.WorkShifts.Remove(entity);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã xóa ca #{id} thành công.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"Không thể xóa ca #{id} vì đã có dữ liệu liên quan.";
        }
        catch (Exception)
        {
            TempData["Error"] = "Đã xảy ra lỗi khi xóa ca làm việc.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ──

    private async Task<List<StaffSelectItem>> GetStaffListAsync()
    {
        var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
        return staffUsers
            .OrderBy(u => u.FullName)
            .Select(u => new StaffSelectItem
            {
                Id          = u.Id,
                DisplayName = !u.IsActive
                    ? $"{u.FullName} ({u.EmployeeCode ?? u.UserName}) (Ngưng hoạt động)"
                    : $"{u.FullName} ({u.EmployeeCode ?? u.UserName})"
            })
            .ToList();
    }
}
