using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
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
    private readonly IAuditLogService _audit;

    public PricingController(ApplicationDbContext context, IAuditLogService audit)
    {
        _context = context;
        _audit = audit;
    }

    #region Lay danh sach loai ban cho form

    private async Task<List<TableTypeOptionViewModel>> GetTableTypeOptionsAsync() =>
        await _context.TableTypes.OrderBy(t => t.Name)
            .Select(t => new TableTypeOptionViewModel { Id = t.Id, Name = t.Name })
            .ToListAsync();

    #endregion

    #region Danh sach bang gia tong hop (Ban + Gay cho thue + Combo gio choi)

    // GET /Pricing?editCueId=&editComboId=&editTableId=
    public async Task<IActionResult> Index(int? editCueId = null, int? editComboId = null, int? editTableId = null)
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
            var nowUtc = DateTime.UtcNow;
            var tables = await _context.BilliardTables.AsNoTracking()
                .Include(t => t.TableType)
                .OrderBy(t => t.FloorNumber)
                .ThenBy(t => t.TableCode)
                .ToListAsync();

            var overdueTableIds = new HashSet<int>();
            var activeComboByTable = await (from s in _context.PlaySessions.AsNoTracking()
                                            where s.Status == "Active"
                                            join sc in _context.SessionCombos.AsNoTracking() on s.Id equals sc.SessionId
                                            select new { s.TableId, s.StartAtUtc, sc.Quantity, sc.PlaytimeHoursSnapshot })
                                            .ToListAsync();
            foreach (var grp in activeComboByTable.GroupBy(x => new { x.TableId, x.StartAtUtc }))
            {
                double comboSecs = (double)grp.Sum(c => c.Quantity * c.PlaytimeHoursSnapshot * 60m) * 60.0;
                if (comboSecs > 0 && (nowUtc - DateTime.SpecifyKind(grp.Key.StartAtUtc, DateTimeKind.Utc)).TotalSeconds >= comboSecs)
                {
                    overdueTableIds.Add(grp.Key.TableId);
                }
            }

            vm.Tables = tables.Select(t => new BilliardTableRowViewModel
            {
                Id = t.Id,
                TableCode = t.TableCode,
                TableTypeId = t.TableTypeId,
                TableTypeName = t.TableType?.Name ?? "—",
                HourlyRate = t.TableType?.HourlyRate ?? 0m,
                FloorNumber = t.FloorNumber,
                Status = t.Status,
                IsComboOverdue = t.Status == "InUse" && overdueTableIds.Contains(t.Id)
            }).ToList();

            if (editTableId.HasValue)
            {
                var targetTable = vm.Tables.FirstOrDefault(x => x.Id == editTableId.Value);
                if (targetTable != null)
                {
                    vm.EditTable = new SaveBilliardTableInputModel
                    {
                        Id = targetTable.Id,
                        TableCode = targetTable.TableCode,
                        TableTypeId = targetTable.TableTypeId,
                        FloorNumber = targetTable.FloorNumber,
                        Status = targetTable.Status
                    };
                }
            }

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
        if (!ModelState.IsValid || hourlyRate < 0)
        {
            TempData["Error"] = "Đơn giá bàn theo giờ phải là số từ 0 VND trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
            return RedirectToAction(nameof(Index));
        }

        var tableType = await _context.TableTypes.FindAsync(id);
        if (tableType == null) return NotFound();

        var oldRate = tableType.HourlyRate;
        if (oldRate == hourlyRate)
        {
            TempData["Success"] = $"Giá bàn [{tableType.Name}] không thay đổi ({hourlyRate:N0} VND/giờ).";
            return RedirectToAction(nameof(Index));
        }

        tableType.HourlyRate = hourlyRate;
        // TV5: ghi nhật ký đổi giá, lưu cùng lần SaveChanges. Phiên đang chơi giữ giá snapshot cũ.
        await _audit.AddAsync(AuditActions.TableRateChange, "TableType", tableType.Id.ToString(),
            $"Đổi giá giờ loại bàn [{tableType.Name}] từ {oldRate:N0} thành {hourlyRate:N0} VND/giờ.",
            oldValue: oldRate.ToString("0"), newValue: hourlyRate.ToString("0"));
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
        if (!ModelState.IsValid || string.IsNullOrEmpty(name) || price < 0 || stockQuantity < 0)
        {
            TempData["Error"] = "Tên gậy cho thuê không được trống, giá thuê/lượt và số lượng phải là số từ 0 trở lên (không được nhập số âm hoặc ký tự đặc biệt).";
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

    #region 4. Quan ly Ban Bi-a (Manager tao them so luong ban & dieu chinh trang thai ban)

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTable(int id, string tableCode, int tableTypeId, int floorNumber = 1, string status = "Available", int quantity = 1)
    {
        tableCode = (tableCode ?? string.Empty).Trim();
        status = (status ?? "Available").Trim();
        string[] allowedManualStatuses = { "Available", "Maintenance" };

        if (!ModelState.IsValid || string.IsNullOrEmpty(tableCode) || tableTypeId <= 0 || floorNumber <= 0 || (id == 0 && (quantity <= 0 || quantity > 50)))
        {
            TempData["Error"] = "Thông tin bàn không hợp lệ: Vui lòng nhập Mã bàn, chọn Loại bàn, Tầng (>= 1) và Số lượng bàn cần thêm (1 - 50).";
            return RedirectToAction(nameof(Index), new { editTableId = id > 0 ? (int?)id : null });
        }

        if (!allowedManualStatuses.Contains(status))
        {
            status = "Available";
        }

        var tableType = await _context.TableTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tableTypeId);
        if (tableType == null)
        {
            TempData["Error"] = "Loại bàn đã chọn không tồn tại.";
            return RedirectToAction(nameof(Index), new { editTableId = id > 0 ? (int?)id : null });
        }

        if (id == 0)
        {
            if (quantity == 1)
            {
                if (await _context.BilliardTables.AnyAsync(t => t.TableCode == tableCode))
                {
                    TempData["Error"] = $"Mã bàn '{tableCode}' đã tồn tại trong hệ thống.";
                    return RedirectToAction(nameof(Index));
                }

                _context.BilliardTables.Add(new BilliardTable
                {
                    TableCode = tableCode,
                    TableTypeId = tableTypeId,
                    FloorNumber = floorNumber,
                    Status = status
                });
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Đã thêm bàn \"{tableCode}\" ({tableType.Name} · Tầng {floorNumber}).";
            }
            else
            {
                var existingCodes = new HashSet<string>(
                    await _context.BilliardTables.AsNoTracking().Select(t => t.TableCode).ToListAsync(),
                    StringComparer.OrdinalIgnoreCase);

                // Tách phần tiền tố và số bắt đầu nếu mã bàn có chứa số ở cuối (VD: B07 -> tiền tố B, bắt đầu từ 7)
                int splitIdx = tableCode.Length;
                while (splitIdx > 0 && char.IsDigit(tableCode[splitIdx - 1]))
                {
                    splitIdx--;
                }

                string prefix = splitIdx < tableCode.Length ? tableCode[..splitIdx] : tableCode;
                int nextNum = 1;
                int padWidth = 2;
                if (splitIdx < tableCode.Length && int.TryParse(tableCode[splitIdx..], out int parsedStart) && parsedStart > 0)
                {
                    nextNum = parsedStart;
                    padWidth = Math.Max(2, tableCode.Length - splitIdx);
                }

                var createdCodes = new List<string>();
                while (createdCodes.Count < quantity && nextNum < 10000)
                {
                    string candidate = $"{prefix}{nextNum.ToString().PadLeft(padWidth, '0')}";
                    if (!existingCodes.Contains(candidate))
                    {
                        existingCodes.Add(candidate);
                        createdCodes.Add(candidate);
                        _context.BilliardTables.Add(new BilliardTable
                        {
                            TableCode = candidate,
                            TableTypeId = tableTypeId,
                            FloorNumber = floorNumber,
                            Status = status
                        });
                    }
                    nextNum++;
                }

                if (createdCodes.Count > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Đã thêm {createdCodes.Count} bàn [{tableType.Name}] tại Tầng {floorNumber}: {string.Join(", ", createdCodes)}.";
                }
            }
        }
        else
        {
            if (await _context.BilliardTables.AnyAsync(t => t.TableCode == tableCode && t.Id != id))
            {
                TempData["Error"] = $"Mã bàn '{tableCode}' đã tồn tại trong hệ thống.";
                return RedirectToAction(nameof(Index), new { editTableId = id });
            }

            var existing = await _context.BilliardTables.FindAsync(id);
            if (existing == null) return NotFound();

            existing.TableCode = tableCode;
            existing.TableTypeId = tableTypeId;
            existing.FloorNumber = floorNumber;

            if (existing.Status is "Available" or "Maintenance")
            {
                existing.Status = status;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã cập nhật thông tin bàn \"{tableCode}\".";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTableStatus(int id, string status)
    {
        status = (status ?? string.Empty).Trim();
        string[] allowedStatuses = { "Available", "Maintenance" };
        if (!allowedStatuses.Contains(status))
        {
            TempData["Error"] = "Trạng thái bàn điều chỉnh không hợp lệ (chỉ hỗ trợ Còn trống hoặc Bảo trì).";
            return RedirectToAction(nameof(Index));
        }

        var table = await _context.BilliardTables.FindAsync(id);
        if (table == null) return NotFound();

        if (table.Status == "InUse")
        {
            TempData["Error"] = $"Bàn \"{table.TableCode}\" đang được sử dụng, không thể đổi trạng thái thủ công.";
            return RedirectToAction(nameof(Index));
        }

        table.Status = status;
        await _context.SaveChangesAsync();

        string statusLabel = status switch
        {
            "Available" => "Còn trống",
            "Maintenance" => "Bảo trì",
            _ => status
        };

        TempData["Success"] = $"Đã chuyển trạng thái bàn \"{table.TableCode}\" sang [{statusLabel}].";
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
