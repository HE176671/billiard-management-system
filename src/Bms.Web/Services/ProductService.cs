using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(ApplicationDbContext db, IInventoryService inventoryService, ILogger<ProductService> logger)
    {
        _db = db;
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task<List<ProductViewModel>> GetAllProductsAsync(bool activeOnly = false, string? search = null, int? categoryId = null)
    {
        var q = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .AsQueryable();

        if (activeOnly)
        {
            q = q.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(term));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            q = q.Where(p => p.CategoryId == categoryId.Value);
        }

        var priceChanges = await _db.ProductPriceHistories.AsNoTracking()
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count);

        var list = await q.OrderBy(p => p.CategoryId).ThenBy(p => p.Name).ToListAsync();

        return list.Select(p => new ProductViewModel
        {
            Id = p.Id,
            CategoryId = p.CategoryId,
            CategoryName = p.Category != null ? p.Category.Name : "Chưa phân loại",
            Name = p.Name,
            Price = p.Price,
            StockQuantity = p.StockQuantity,
            IsActive = p.IsActive,
            PriceChangeCount = priceChanges.GetValueOrDefault(p.Id, 0)
        }).ToList();
    }

    public async Task<ProductViewModel?> GetProductByIdAsync(int id)
    {
        return await _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.Id == id)
            .Select(p => new ProductViewModel
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : "Chưa phân loại",
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity,
                IsActive = p.IsActive
            })
            .FirstOrDefaultAsync();
    }

    public async Task<FandBServiceResult> CreateProductAsync(ProductInputModel model, string actorId)
    {
        var trimmedName = model.Name.Trim();
        bool dup = await _db.Products.AnyAsync(p => p.Name.ToLower() == trimmedName.ToLower());
        if (dup)
        {
            return FandBServiceResult.Fail($"Tên món '{trimmedName}' đã tồn tại trong thực đơn");
        }

        var product = new Product
        {
            CategoryId = model.CategoryId,
            Name = trimmedName,
            Price = model.Price,
            StockQuantity = 0,
            IsActive = model.IsActive
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        if (model.StockQuantity > 0)
        {
            await _inventoryService.ChangeStockAsync(product.Id, model.StockQuantity,
                StockMovement.TypeImport, "InitialCreation", product.Id, "Tạo món mới kèm tồn kho khởi tạo", actorId);
        }

        return FandBServiceResult.Ok($"Đã thêm món '{product.Name}' thành công!");
    }

    public async Task<FandBServiceResult> UpdateProductAsync(ProductInputModel model, string actorId)
    {
        var product = await _db.Products.FindAsync(model.Id);
        if (product == null)
        {
            return FandBServiceResult.Fail($"Không tìm thấy món #{model.Id}");
        }

        var trimmedName = model.Name.Trim();
        bool dup = await _db.Products.AnyAsync(p => p.Id != model.Id && p.Name.ToLower() == trimmedName.ToLower());
        if (dup)
        {
            return FandBServiceResult.Fail($"Tên món '{trimmedName}' đã được dùng cho món khác");
        }

        // BR-15: Đổi giá bán thì ghi nhận lịch sử vào ProductPriceHistory
        if (product.Price != model.Price)
        {
            var history = new ProductPriceHistory
            {
                ProductId = product.Id,
                OldPrice = product.Price,
                NewPrice = model.Price,
                EffectiveDateUtc = DateTime.UtcNow,
                ChangedById = actorId,
                Note = string.IsNullOrWhiteSpace(model.PriceChangeReason)
                    ? $"Cập nhật giá bán: {product.Price:N0}đ -> {model.Price:N0}đ"
                    : model.PriceChangeReason.Trim()
            };
            _db.ProductPriceHistories.Add(history);
            _logger.LogInformation("Price changed for Product {ProductId}: {OldPrice} -> {NewPrice} by {User}",
                product.Id, product.Price, model.Price, actorId);
        }

        product.CategoryId = model.CategoryId;
        product.Name = trimmedName;
        product.Price = model.Price;
        product.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        return FandBServiceResult.Ok($"Đã cập nhật thông tin món '{product.Name}' thành công!");
    }

    public async Task<FandBServiceResult> ToggleProductActiveAsync(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return FandBServiceResult.Fail("Không tìm thấy món");

        product.IsActive = !product.IsActive;
        await _db.SaveChangesAsync();

        var status = product.IsActive ? "Đang bán" : "Ngừng bán";
        return FandBServiceResult.Ok($"Đã chuyển trạng thái món '{product.Name}' sang: {status}");
    }

    public async Task<List<ProductPriceHistoryViewModel>> GetPriceHistoryAsync(int? productId = null)
    {
        var q = _db.ProductPriceHistories.AsNoTracking()
            .Include(h => h.Product)
            .Include(h => h.ChangedBy)
            .AsQueryable();

        if (productId.HasValue && productId.Value > 0)
        {
            q = q.Where(h => h.ProductId == productId.Value);
        }

        return await q.OrderByDescending(h => h.EffectiveDateUtc)
            .ThenByDescending(h => h.Id)
            .Select(h => new ProductPriceHistoryViewModel
            {
                Id = h.Id,
                ProductId = h.ProductId,
                ProductName = h.Product != null ? h.Product.Name : "Món #" + h.ProductId,
                OldPrice = h.OldPrice,
                NewPrice = h.NewPrice,
                EffectiveDateUtc = h.EffectiveDateUtc,
                ChangedByName = h.ChangedBy != null ? h.ChangedBy.FullName : "Quản trị viên",
                Note = h.Note
            })
            .ToListAsync();
    }

    public async Task<List<ProductCategoryViewModel>> GetAllCategoriesAsync()
    {
        return await _db.ProductCategories.AsNoTracking()
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .Select(c => new ProductCategoryViewModel
            {
                Id = c.Id,
                Name = c.Name,
                ProductCount = c.Products.Count
            })
            .ToListAsync();
    }

    public async Task<FandBServiceResult> SaveCategoryAsync(int? id, string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return FandBServiceResult.Fail("Tên danh mục không được để trống");
        }

        if (id.HasValue && id.Value > 0)
        {
            var cat = await _db.ProductCategories.FindAsync(id.Value);
            if (cat == null) return FandBServiceResult.Fail("Không tìm thấy danh mục");

            bool dup = await _db.ProductCategories.AnyAsync(c => c.Id != id.Value && c.Name.ToLower() == trimmed.ToLower());
            if (dup) return FandBServiceResult.Fail($"Danh mục '{trimmed}' đã tồn tại");

            cat.Name = trimmed;
            await _db.SaveChangesAsync();
            return FandBServiceResult.Ok($"Đã cập nhật danh mục '{trimmed}'");
        }
        else
        {
            bool dup = await _db.ProductCategories.AnyAsync(c => c.Name.ToLower() == trimmed.ToLower());
            if (dup) return FandBServiceResult.Fail($"Danh mục '{trimmed}' đã tồn tại");

            var cat = new ProductCategory { Name = trimmed };
            _db.ProductCategories.Add(cat);
            await _db.SaveChangesAsync();
            return FandBServiceResult.Ok($"Đã thêm danh mục '{trimmed}' thành công!");
        }
    }
}
