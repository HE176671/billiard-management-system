using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class ComboService : IComboService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ComboService> _logger;

    public ComboService(ApplicationDbContext db, ILogger<ComboService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<ComboViewModel>> GetAllCombosAsync(bool activeOnly = false)
    {
        var q = _db.Combos.AsNoTracking()
            .Include(c => c.TableType)
            .Include(c => c.ComboItems)
                .ThenInclude(ci => ci.Product)
            .AsQueryable();

        if (activeOnly)
        {
            q = q.Where(c => c.IsActive);
        }

        return await q.OrderBy(c => c.Name)
            .Select(c => new ComboViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Price = c.Price,
                PlaytimeHours = c.PlaytimeHours,
                TableTypeId = c.TableTypeId,
                TableTypeName = c.TableType != null ? c.TableType.Name : "Tất cả các loại bàn",
                IsActive = c.IsActive,
                Items = c.ComboItems.Select(ci => new ComboItemViewModel
                {
                    ProductId = ci.ProductId,
                    ProductName = ci.Product != null ? ci.Product.Name : "Món #" + ci.ProductId,
                    Quantity = ci.Quantity,
                    CurrentStock = ci.Product != null ? ci.Product.StockQuantity : 0
                }).ToList()
            })
            .ToListAsync();
    }

    public async Task<ComboViewModel?> GetComboByIdAsync(int id)
    {
        return await _db.Combos.AsNoTracking()
            .Include(c => c.TableType)
            .Include(c => c.ComboItems)
                .ThenInclude(ci => ci.Product)
            .Where(c => c.Id == id)
            .Select(c => new ComboViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Price = c.Price,
                PlaytimeHours = c.PlaytimeHours,
                TableTypeId = c.TableTypeId,
                TableTypeName = c.TableType != null ? c.TableType.Name : "Tất cả các loại bàn",
                IsActive = c.IsActive,
                Items = c.ComboItems.Select(ci => new ComboItemViewModel
                {
                    ProductId = ci.ProductId,
                    ProductName = ci.Product != null ? ci.Product.Name : "Món #" + ci.ProductId,
                    Quantity = ci.Quantity,
                    CurrentStock = ci.Product != null ? ci.Product.StockQuantity : 0
                }).ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<FandBServiceResult> SaveComboAsync(ComboInputModel model, string actorId)
    {
        var trimmedName = model.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return FandBServiceResult.Fail("Tên gói Combo không được để trống");
        }

        if (model.Items == null || model.Items.Count == 0)
        {
            return FandBServiceResult.Fail("Combo phải có ít nhất 1 món kèm (nước/đồ ăn)");
        }

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            Combo? combo;
            if (model.Id > 0)
            {
                combo = await _db.Combos.Include(c => c.ComboItems).FirstOrDefaultAsync(c => c.Id == model.Id);
                if (combo == null) return FandBServiceResult.Fail("Không tìm thấy Combo");

                combo.Name = trimmedName;
                combo.Price = model.Price;
                combo.PlaytimeHours = model.PlaytimeHours;
                combo.TableTypeId = model.TableTypeId;
                combo.IsActive = model.IsActive;

                // Xóa các món kèm cũ
                _db.ComboItems.RemoveRange(combo.ComboItems);
            }
            else
            {
                bool dup = await _db.Combos.AnyAsync(c => c.Name.ToLower() == trimmedName.ToLower());
                if (dup) return FandBServiceResult.Fail($"Tên gói Combo '{trimmedName}' đã tồn tại");

                combo = new Combo
                {
                    Name = trimmedName,
                    Price = model.Price,
                    PlaytimeHours = model.PlaytimeHours,
                    TableTypeId = model.TableTypeId,
                    IsActive = model.IsActive
                };
                _db.Combos.Add(combo);
                await _db.SaveChangesAsync();
            }

            foreach (var item in model.Items)
            {
                if (item.Quantity > 0 && item.ProductId > 0)
                {
                    _db.ComboItems.Add(new ComboItem
                    {
                        ComboId = combo.Id,
                        ProductId = item.ProductId,
                        Quantity = item.Quantity
                    });
                }
            }

            await _db.SaveChangesAsync();
            if (ownTx && tx != null) await tx.CommitAsync();

            return FandBServiceResult.Ok($"Đã lưu gói Combo '{combo.Name}' thành công!");
        }
        catch (Exception ex)
        {
            if (ownTx && tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi lưu gói Combo {Name}", model.Name);
            return FandBServiceResult.Fail("Lỗi hệ thống khi lưu Combo: " + ex.Message);
        }
        finally
        {
            if (ownTx && tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<FandBServiceResult> ToggleComboActiveAsync(int id)
    {
        var combo = await _db.Combos.FindAsync(id);
        if (combo == null) return FandBServiceResult.Fail("Không tìm thấy Combo");

        combo.IsActive = !combo.IsActive;
        await _db.SaveChangesAsync();

        var status = combo.IsActive ? "Đang mở bán" : "Tạm ngưng";
        return FandBServiceResult.Ok($"Đã chuyển trạng thái Combo '{combo.Name}' sang: {status}");
    }
}
