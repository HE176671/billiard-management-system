using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// Manager cấu hình chỉnh sửa bảng giá các dịch vụ:
/// 1. Bàn (Giá chuẩn theo giờ & Giá theo khung giờ)
/// 2. Gậy cho thuê (Giá theo lượt, số lượng, đóng/mở)
/// 3. Khuyến mãi Combo giờ chơi tặng kèm nước/đồ ăn (VD: Chơi 2 tiếng mua combo được tặng nước)
/// </summary>
[Authorize(Roles = "Manager,Admin")]
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

    #region Danh sach bang gia tong hop (Ban + Gay cho thue + Combo gio choi)

    // GET /Pricing?editCueId=&editComboId=
    public async Task<IActionResult> Index(int? editCueId = null, int? editComboId = null)
    {
        var configs = await _context.PricingConfigs
            .Include(p => p.TableType)
            .OrderBy(p => p.TableType!.Name)
            .ThenBy(p => p.StartHour)
            .ToListAsync();

        var tableTypes = await GetTableTypeOptionsAsync();
        var tableTypeRates = await _context.TableTypes.AsNoTracking().OrderBy(t => t.Id).ToListAsync();

        var vm = new PricingListViewModel
        {
            TableTypes = tableTypes,
            TableTypeRates = tableTypeRates,
            Configs = configs.Select(p => new PricingRowViewModel
            {
                Id = p.Id,
                TableTypeName = p.TableType?.Name ?? "—",
                HourlyRate = p.HourlyRate,
                StartHour = p.StartHour.ToString(@"hh\:mm"),
                EndHour = p.EndHour.ToString(@"hh\:mm")
            }).ToList()
        };

        try
        {
            var cueCategory = await _context.ProductCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == ProductCategory.RentalCueCategoryName);

            if (cueCategory != null)
            {
                vm.RentalCues = await _context.Products.AsNoTracking()
                    .Where(p => p.CategoryId == cueCategory.Id)
                    .OrderBy(p => p.Price)
                    .ToListAsync();

                if (editCueId.HasValue)
                {
                    vm.EditCue = vm.RentalCues.FirstOrDefault(x => x.Id == editCueId.Value);
                }
            }

            vm.AvailableBonusProducts = await _context.Products.AsNoTracking()
                .Include(p => p.Category)
                .Where(p => p.Category == null || p.Category.Name != ProductCategory.RentalCueCategoryName)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var combos = await _context.Combos.AsNoTracking()
                .Include(c => c.ComboItems)
                    .ThenInclude(i => i.Product)
                .OrderBy(c => c.PlaytimeHours)
                .ThenBy(c => c.Price)
                .ToListAsync();

            vm.Combos = combos.Select(c =>
            {
                var firstItem = c.ComboItems.FirstOrDefault();
                string summary = c.ComboItems.Any()
                    ? string.Join(", ", c.ComboItems.Select(i => $"Tặng {i.Quantity} {i.Product?.Name ?? "Món"}"))
                    : "Chưa chọn món tặng kèm";

                return new ComboRowViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    PlaytimeHours = c.PlaytimeHours,
                    Price = c.Price,
                    IsActive = c.IsActive,
                    IncludedItemsSummary = summary,
                    PrimaryProductId = firstItem?.ProductId,
                    PrimaryQuantity = firstItem?.Quantity ?? 1
                };
            }).ToList();

            if (editComboId.HasValue)
            {
                var target = vm.Combos.FirstOrDefault(x => x.Id == editComboId.Value);
                if (target != null)
                {
                    vm.EditCombo = new SaveComboInputModel
                    {
                        Id = target.Id,
                        Name = target.Name,
                        PlaytimeHours = target.PlaytimeHours,
                        Price = target.Price,
                        BonusProductId = target.PrimaryProductId,
                        BonusQuantity = target.PrimaryQuantity,
                        IsActive = target.IsActive
                    };
                }
            }
        }
        catch
        {
            // Bỏ qua nếu DB chưa có bảng phụ trong môi trường test cô lập
        }

        return View(vm);
    }

    #endregion

    #region 1. Cap nhat gia chuan theo gio cua Loai Ban (Pool / Carom / VIP)

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTableBaseRate(int id, decimal hourlyRate)
    {
        if (hourlyRate <= 0)
        {
            TempData["Error"] = "Đơn giá bàn theo giờ phải lớn hơn 0.";
            return RedirectToAction(nameof(Index));
        }

        var tableType = await _context.TableTypes.FindAsync(id);
        if (tableType == null) return NotFound();

        tableType.HourlyRate = hourlyRate;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Đã cập nhật giá chuẩn bàn [{tableType.Name}] thành {hourlyRate:N0} VND/giờ.";
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region 2. Quan ly gia Gay cho thue theo luot (Them / Sua / Dong-Mo)

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRentalCue(int id, string name, decimal price, int stockQuantity = 10, bool isActive = true)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || price <= 0 || stockQuantity < 0)
        {
            TempData["Error"] = "Tên gậy cho thuê không được trống, giá thuê/lượt phải > 0 và số lượng >= 0.";
            return RedirectToAction(nameof(Index), new { editCueId = id > 0 ? (int?)id : null });
        }

        var cueCategory = await _context.ProductCategories
            .FirstOrDefaultAsync(c => c.Name == ProductCategory.RentalCueCategoryName);
        if (cueCategory == null)
        {
            cueCategory = new ProductCategory { Name = ProductCategory.RentalCueCategoryName };
            _context.ProductCategories.Add(cueCategory);
            await _context.SaveChangesAsync();
        }

        if (await _context.Products.AnyAsync(p => p.Name == name && p.Id != id))
        {
            TempData["Error"] = $"Tên dịch vụ/gậy '{name}' đã tồn tại trong danh mục.";
            return RedirectToAction(nameof(Index), new { editCueId = id > 0 ? (int?)id : null });
        }

        if (id == 0)
        {
            _context.Products.Add(new Product
            {
                CategoryId = cueCategory.Id,
                Name = name,
                Price = price,
                StockQuantity = stockQuantity,
                IsActive = isActive
            });
            TempData["Success"] = $"Đã thêm gậy cho thuê \"{name}\" ({price:N0} VND/lượt).";
        }
        else
        {
            var existing = await _context.Products.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name = name;
            existing.Price = price;
            existing.StockQuantity = stockQuantity;
            existing.IsActive = isActive;
            TempData["Success"] = $"Đã cập nhật giá gậy cho thuê \"{name}\" ({price:N0} VND/lượt).";
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCueStatus(int id)
    {
        var cue = await _context.Products.FindAsync(id);
        if (cue == null) return NotFound();

        cue.IsActive = !cue.IsActive;
        await _context.SaveChangesAsync();

        TempData["Success"] = cue.IsActive
            ? $"Đã MỞ dịch vụ cho thuê \"{cue.Name}\"."
            : $"Đã TẠM NGƯNG cho thuê \"{cue.Name}\".";

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region 3. Quan ly Khuyen mai Combo Gio choi + Tang nuoc (Them / Sua / Dong-Mo)

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCombo(int id, string name, decimal playtimeHours, decimal price,
        int? bonusProductId, int bonusQuantity = 1, bool isActive = true)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || playtimeHours <= 0 || price < 0)
        {
            TempData["Error"] = "Tên Combo không được trống, số giờ chơi phải > 0 và giá Combo >= 0.";
            return RedirectToAction(nameof(Index), new { editComboId = id > 0 ? (int?)id : null });
        }

        if (await _context.Combos.AnyAsync(c => c.Name == name && c.Id != id))
        {
            TempData["Error"] = $"Gói khuyến mãi Combo '{name}' đã tồn tại.";
            return RedirectToAction(nameof(Index), new { editComboId = id > 0 ? (int?)id : null });
        }

        Combo combo;
        if (id == 0)
        {
            combo = new Combo
            {
                Name = name,
                PlaytimeHours = Math.Round(playtimeHours, 2),
                Price = price,
                IsActive = isActive
            };
            _context.Combos.Add(combo);
            await _context.SaveChangesAsync();
        }
        else
        {
            var existing = await _context.Combos.Include(c => c.ComboItems).FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null) return NotFound();

            existing.Name = name;
            existing.PlaytimeHours = Math.Round(playtimeHours, 2);
            existing.Price = price;
            existing.IsActive = isActive;

            if (existing.ComboItems.Any())
            {
                _context.ComboItems.RemoveRange(existing.ComboItems);
            }
            combo = existing;
            await _context.SaveChangesAsync();
        }

        if (bonusProductId.HasValue && bonusProductId.Value > 0 && bonusQuantity > 0)
        {
            if (await _context.Products.AnyAsync(p => p.Id == bonusProductId.Value))
            {
                _context.ComboItems.Add(new ComboItem
                {
                    ComboId = combo.Id,
                    ProductId = bonusProductId.Value,
                    Quantity = Math.Max(1, bonusQuantity)
                });
                await _context.SaveChangesAsync();
            }
        }

        TempData["Success"] = id == 0
            ? $"Đã tạo khuyến mãi \"{name}\" ({playtimeHours:0.#} giờ chơi — {price:N0} VND)."
            : $"Đã cập nhật khuyến mãi \"{name}\".";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleComboStatus(int id)
    {
        var combo = await _context.Combos.FindAsync(id);
        if (combo == null) return NotFound();

        combo.IsActive = !combo.IsActive;
        await _context.SaveChangesAsync();

        TempData["Success"] = combo.IsActive
            ? $"Đã MỞ áp dụng gói khuyến mãi \"{combo.Name}\"."
            : $"Đã ĐÓNG (tạm ngưng) gói khuyến mãi \"{combo.Name}\".";

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Them muc gia khung gio ban

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
            HourlyRate = model.HourlyRate,
            StartHour = start,
            EndHour = end
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
            Id = p.Id,
            TableTypeId = p.TableTypeId,
            HourlyRate = p.HourlyRate,
            StartHour = p.StartHour.ToString(@"hh\:mm"),
            EndHour = p.EndHour.ToString(@"hh\:mm"),
            TableTypes = await GetTableTypeOptionsAsync()
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
        p.HourlyRate = model.HourlyRate;
        p.StartHour = start;
        p.EndHour = end;
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
