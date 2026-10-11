using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public class MenuService : IMenuService
{
    private readonly ApplicationDbContext _context;

    public MenuService(ApplicationDbContext context)
    {
        _context = context;
    }

    // ── 1. Danh mục Sản phẩm (ProductCategories - UC41) ──

    public async Task<List<ProductCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _context.ProductCategories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<ProductCategory>> CreateCategoryAsync(string name, CancellationToken ct = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult<ProductCategory>.Failure("ERR_CATEGORY_NAME_EMPTY", "Tên danh mục không được để trống.");

        if (trimmed.Length > 60)
            return ServiceResult<ProductCategory>.Failure("ERR_CATEGORY_NAME_LENGTH", "Tên danh mục tối đa 60 ký tự.");

        var exists = await _context.ProductCategories
            .AnyAsync(c => c.Name.ToLower() == trimmed.ToLower(), ct);

        if (exists)
            return ServiceResult<ProductCategory>.Failure(
                "ERR_CATEGORY_NAME_DUPLICATE",
                $"Tên danh mục '{trimmed}' đã tồn tại trong hệ thống.");

        var category = new ProductCategory { Name = trimmed };
        _context.ProductCategories.Add(category);
        await _context.SaveChangesAsync(ct);

        return ServiceResult<ProductCategory>.Success(category);
    }

    public async Task<ServiceResult<ProductCategory>> UpdateCategoryAsync(int id, string name, CancellationToken ct = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult<ProductCategory>.Failure("ERR_CATEGORY_NAME_EMPTY", "Tên danh mục không được để trống.");

        if (trimmed.Length > 60)
            return ServiceResult<ProductCategory>.Failure("ERR_CATEGORY_NAME_LENGTH", "Tên danh mục tối đa 60 ký tự.");

        var category = await _context.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category == null)
            return ServiceResult<ProductCategory>.NotFound("Không tìm thấy danh mục cần cập nhật.");

        var duplicate = await _context.ProductCategories
            .AnyAsync(c => c.Id != id && c.Name.ToLower() == trimmed.ToLower(), ct);

        if (duplicate)
            return ServiceResult<ProductCategory>.Failure(
                "ERR_CATEGORY_NAME_DUPLICATE",
                $"Tên danh mục '{trimmed}' đã tồn tại trong hệ thống.");

        category.Name = trimmed;
        await _context.SaveChangesAsync(ct);

        return ServiceResult<ProductCategory>.Success(category);
    }

    public Task<ServiceResult> DeleteCategoryIfEmptyAsync(int id, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 6 (T022)");

    // ── 2. Sản phẩm & Tồn kho (Products - UC41) ──

    public async Task<(List<Product> Items, int TotalCount)> GetProductsPagedAsync(
        string? search,
        int? categoryId,
        string? statusFilter,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || p.Category.Name.ToLower().Contains(s));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (statusFilter == "active")
            query = query.Where(p => p.IsActive);
        else if (statusFilter == "inactive")
            query = query.Where(p => !p.IsActive);
        else if (statusFilter == "out-of-stock")
            query = query.Where(p => p.IsActive && p.StockQuantity == 0);

        var totalCount = await query.CountAsync(ct);
        page = Math.Max(1, page);

        var items = await query
            .OrderBy(p => p.Category.Name)
            .ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<List<Product>> GetActiveProductsAsync(int? categoryId = null, string? keyword = null, CancellationToken ct = default)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .AsNoTracking()
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value > 0)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(k));
        }

        return await query
            .OrderBy(p => p.Category.Name)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<Product?> GetProductByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<ServiceResult<Product>> CreateProductAsync(
        int categoryId,
        string name,
        decimal price,
        int stockQuantity,
        CancellationToken ct = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult<Product>.Failure("ERR_PRODUCT_NAME_EMPTY", "Tên sản phẩm không được để trống.");

        if (trimmed.Length > 100)
            return ServiceResult<Product>.Failure("ERR_PRODUCT_NAME_LENGTH", "Tên sản phẩm tối đa 100 ký tự.");

        if (price <= 0)
            return ServiceResult<Product>.Failure("ERR_INVALID_PRICE", "Giá sản phẩm phải lớn hơn 0 VNĐ.");

        if (stockQuantity < 0)
            return ServiceResult<Product>.Failure("ERR_INVALID_STOCK", "Số lượng tồn kho không được âm.");

        var categoryExists = await _context.ProductCategories.AnyAsync(c => c.Id == categoryId, ct);
        if (!categoryExists)
            return ServiceResult<Product>.Failure("ERR_CATEGORY_NOT_FOUND", "Danh mục sản phẩm đã chọn không tồn tại.");

        var duplicate = await _context.Products
            .AnyAsync(p => p.Name.ToLower() == trimmed.ToLower(), ct);

        if (duplicate)
            return ServiceResult<Product>.Failure(
                "ERR_PRODUCT_NAME_DUPLICATE",
                $"Tên sản phẩm '{trimmed}' đã tồn tại trong hệ thống.");

        var product = new Product
        {
            CategoryId = categoryId,
            Name = trimmed,
            Price = price,
            StockQuantity = stockQuantity,
            IsActive = true
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync(ct);

        return ServiceResult<Product>.Success(product);
    }

    public async Task<ServiceResult<Product>> UpdateProductAsync(
        int id,
        int categoryId,
        string name,
        decimal price,
        string rowVersionBase64,
        CancellationToken ct = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult<Product>.Failure("ERR_PRODUCT_NAME_EMPTY", "Tên sản phẩm không được để trống.");

        if (trimmed.Length > 100)
            return ServiceResult<Product>.Failure("ERR_PRODUCT_NAME_LENGTH", "Tên sản phẩm tối đa 100 ký tự.");

        if (price <= 0)
            return ServiceResult<Product>.Failure("ERR_INVALID_PRICE", "Giá sản phẩm phải lớn hơn 0 VNĐ.");

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product == null)
            return ServiceResult<Product>.NotFound("Không tìm thấy sản phẩm cần cập nhật.");

        var categoryExists = await _context.ProductCategories.AnyAsync(c => c.Id == categoryId, ct);
        if (!categoryExists)
            return ServiceResult<Product>.Failure("ERR_CATEGORY_NOT_FOUND", "Danh mục sản phẩm đã chọn không tồn tại.");

        var duplicate = await _context.Products
            .AnyAsync(p => p.Id != id && p.Name.ToLower() == trimmed.ToLower(), ct);

        if (duplicate)
            return ServiceResult<Product>.Failure(
                "ERR_PRODUCT_NAME_DUPLICATE",
                $"Tên sản phẩm '{trimmed}' đã tồn tại trong hệ thống.");

        byte[] originalRowVersion;
        try
        {
            originalRowVersion = Convert.FromBase64String(rowVersionBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return ServiceResult<Product>.Failure("ERR_INVALID_ROWVERSION", "Mã phiên bản dữ liệu (RowVersion) không hợp lệ.");
        }

        _context.Entry(product).Property(p => p.RowVersion).OriginalValue = originalRowVersion;

        product.CategoryId = categoryId;
        product.Name = trimmed;
        product.Price = price;

        try
        {
            await _context.SaveChangesAsync(ct);
            return ServiceResult<Product>.Success(product);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<Product>.Conflict(
                "Dữ liệu sản phẩm đã được thay đổi bởi người dùng khác. Vui lòng làm mới trang.");
        }
    }

    public Task<ServiceResult<Product>> AdjustStockAsync(
        int productId, int quantityDelta, string? reason, string rowVersionBase64, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 4 (T015)");

    public Task<ServiceResult<Product>> ToggleProductStatusAsync(
        int id, string rowVersionBase64, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 6 (T022)");

    // ── 3. Gói Combo Giờ chơi & F&B (Combos & ComboItems - UC42) ──

    public Task<List<Combo>> GetCombosAsync(string? search = null, string? statusFilter = null, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 5 (T019)");

    public Task<Combo?> GetComboWithItemsAsync(int id, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 5 (T019)");

    public Task<ServiceResult<Combo>> CreateComboAsync(
        string name, decimal price, decimal playtimeHours, IEnumerable<(int ProductId, int Quantity)> items, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 5 (T019)");

    public Task<ServiceResult<Combo>> UpdateComboAsync(
        int id, string name, decimal price, decimal playtimeHours, IEnumerable<(int ProductId, int Quantity)> items, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 5 (T019)");

    public Task<ServiceResult<Combo>> ToggleComboStatusAsync(int id, CancellationToken ct = default)
        => throw new NotImplementedException("Pending Phase 6 (T022)");
}
