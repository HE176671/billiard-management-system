using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>Admin quản lý bảng giá (PricingConfigs) theo loại bàn và khung giờ.</summary>
[Authorize(Roles = "Admin")]
public class PricingController : Controller
{
    private readonly ApplicationDbContext _context;

    public PricingController(ApplicationDbContext context) => _context = context;

    #region Lay danh sach loai ban cho form

    private async Task<List<TableTypeOptionViewModel>> GetTableTypeOptionsAsync() =>
        await _context.TableTypes.OrderBy(t => t.Name)
            .Select(t => new TableTypeOptionViewModel { Id = t.Id, Name = t.Name })
            .ToListAsync();

    #endregion

    #region Danh sach bang gia theo loai ban

    // GET /Pricing
    public async Task<IActionResult> Index()
    {
        var configs = await _context.PricingConfigs
            .Include(p => p.TableType)
            .OrderBy(p => p.TableType!.Name)
            .ThenBy(p => p.StartHour)
            .ToListAsync();

        var tableTypes = await GetTableTypeOptionsAsync();

        var vm = new PricingListViewModel
        {
            TableTypes = tableTypes,
            Configs = configs.Select(p => new PricingRowViewModel
            {
                Id            = p.Id,
                TableTypeName = p.TableType?.Name ?? "—",
                HourlyRate    = p.HourlyRate,
                StartHour     = p.StartHour.ToString(@"hh\:mm"),
                EndHour       = p.EndHour.ToString(@"hh\:mm")
            }).ToList()
        };
        return View(vm);
    }

    #endregion

    #region Them muc gia

    // GET /Pricing/Create
    public async Task<IActionResult> Create()
    {
        var vm = new CreateEditPricingViewModel { TableTypes = await GetTableTypeOptionsAsync() };
        return View("CreateEdit", vm);
    }

    // POST /Pricing/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEditPricingViewModel model)
    {
        model.Id = 0;
        model.TableTypes = await GetTableTypeOptionsAsync();
        if (!ModelState.IsValid) return View("CreateEdit", model);

        if (!TimeSpan.TryParseExact(model.StartHour, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParseExact(model.EndHour, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var end) || start >= end)
        {
            ModelState.AddModelError("", "Giờ bắt đầu phải nhỏ hơn giờ kết thúc.");
            return View("CreateEdit", model);
        }

        if (!await ValidateConfigAsync(model, start, end)) return View("CreateEdit", model);
        _context.PricingConfigs.Add(new PricingConfig
        {
            TableTypeId = model.TableTypeId,
            HourlyRate  = model.HourlyRate,
            StartHour   = start,
            EndHour     = end
        });
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã thêm cấu hình bảng giá.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Sua muc gia

    // GET /Pricing/Edit/{id}
    public async Task<IActionResult> Edit(int id)
    {
        var p = await _context.PricingConfigs.FindAsync(id);
        if (p == null) return NotFound();

        var vm = new CreateEditPricingViewModel
        {
            Id          = p.Id,
            TableTypeId = p.TableTypeId,
            HourlyRate  = p.HourlyRate,
            StartHour   = p.StartHour.ToString(@"hh\:mm"),
            EndHour     = p.EndHour.ToString(@"hh\:mm"),
            TableTypes  = await GetTableTypeOptionsAsync()
        };
        return View("CreateEdit", vm);
    }

    // POST /Pricing/Edit
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CreateEditPricingViewModel model)
    {
        model.TableTypes = await GetTableTypeOptionsAsync();
        if (!ModelState.IsValid) return View("CreateEdit", model);

        if (!TimeSpan.TryParseExact(model.StartHour, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParseExact(model.EndHour, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var end) || start >= end)
        {
            ModelState.AddModelError("", "Giờ bắt đầu phải nhỏ hơn giờ kết thúc.");
            return View("CreateEdit", model);
        }

        var p = await _context.PricingConfigs.FindAsync(model.Id);
        if (p == null) return NotFound();

        if (!await ValidateConfigAsync(model, start, end)) return View("CreateEdit", model);
        p.TableTypeId = model.TableTypeId;
        p.HourlyRate  = model.HourlyRate;
        p.StartHour   = start;
        p.EndHour     = end;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã cập nhật bảng giá.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Xoa muc gia

    // POST /Pricing/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _context.PricingConfigs.FindAsync(id);
        if (p == null) return NotFound();

        _context.PricingConfigs.Remove(p);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đã xóa cấu hình bảng giá.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Kiem tra loai ban va khung gio chong nhau

    private async Task<bool> ValidateConfigAsync(CreateEditPricingViewModel model, TimeSpan start, TimeSpan end)
    {
        if (!await _context.TableTypes.AnyAsync(t => t.Id == model.TableTypeId))
            ModelState.AddModelError(nameof(model.TableTypeId), "Loại bàn không tồn tại.");
        if (await _context.PricingConfigs.AnyAsync(p => p.TableTypeId == model.TableTypeId &&
            p.Id != model.Id && p.StartHour < end && start < p.EndHour))
            ModelState.AddModelError("", "Khung giờ bị trùng với mức giá hiện có của loại bàn này.");
        return ModelState.IsValid;
    }
    #endregion
}
