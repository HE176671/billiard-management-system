using Bms.Web.Data.Entities;

namespace Bms.Web.Services;

/// <summary>
/// Kết quả trả về thống nhất từ tầng Service cho cả MVC Controller và API Controller.
/// Phân biệt rõ lỗi Validation (400), NotFound (404) và Concurrency Conflict (409).
/// </summary>
public record ServiceResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsConcurrencyConflict = false,
    bool IsNotFound = false)
{
    public static ServiceResult Success() => new(true);
    public static ServiceResult Failure(string errorCode, string message) => new(false, errorCode, message);
    public static ServiceResult Conflict(string message = "Dữ liệu đã bị thay đổi bởi người dùng khác. Vui lòng tải lại trang.")
        => new(false, "ERR_CONCURRENCY_CONFLICT", message, IsConcurrencyConflict: true);
    public static ServiceResult NotFound(string message = "Không tìm thấy dữ liệu yêu cầu.")
        => new(false, "ERR_NOT_FOUND", message, IsNotFound: true);
}

public record ServiceResult<T>(
    bool Succeeded,
    T? Data = default,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsConcurrencyConflict = false,
    bool IsNotFound = false)
{
    public static ServiceResult<T> Success(T data) => new(true, data);
    public static ServiceResult<T> Failure(string errorCode, string message) => new(false, default, errorCode, message);
    public static ServiceResult<T> Conflict(string message = "Dữ liệu đã bị thay đổi bởi người dùng khác. Vui lòng tải lại trang.")
        => new(false, default, "ERR_CONCURRENCY_CONFLICT", message, IsConcurrencyConflict: true);
    public static ServiceResult<T> NotFound(string message = "Không tìm thấy dữ liệu yêu cầu.")
        => new(false, default, "ERR_NOT_FOUND", message, IsNotFound: true);
}

/// <summary>
/// Interface nghiệp vụ Quản lý Menu, Kho hàng F&B (UC41) và Gói Combo (UC42).
/// </summary>
public interface IMenuService
{
    // ── 1. Danh mục Sản phẩm (ProductCategories - UC41) ──
    Task<List<ProductCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ServiceResult<ProductCategory>> CreateCategoryAsync(string name, CancellationToken ct = default);
    Task<ServiceResult<ProductCategory>> UpdateCategoryAsync(int id, string name, CancellationToken ct = default);
    Task<ServiceResult> DeleteCategoryIfEmptyAsync(int id, CancellationToken ct = default);

    // ── 2. Sản phẩm & Tồn kho (Products - UC41) ──
    Task<(List<Product> Items, int TotalCount)> GetProductsPagedAsync(
        string? search,
        int? categoryId,
        string? statusFilter,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);

    Task<List<Product>> GetActiveProductsAsync(int? categoryId = null, string? keyword = null, CancellationToken ct = default);
    Task<Product?> GetProductByIdAsync(int id, CancellationToken ct = default);

    Task<ServiceResult<Product>> CreateProductAsync(
        int categoryId,
        string name,
        decimal price,
        int stockQuantity,
        CancellationToken ct = default);

    Task<ServiceResult<Product>> UpdateProductAsync(
        int id,
        int categoryId,
        string name,
        decimal price,
        string rowVersionBase64,
        CancellationToken ct = default);

    Task<ServiceResult<Product>> AdjustStockAsync(
        int productId,
        int quantityDelta,
        string? reason,
        string rowVersionBase64,
        CancellationToken ct = default);

    Task<ServiceResult<Product>> ToggleProductStatusAsync(
        int id,
        string rowVersionBase64,
        CancellationToken ct = default);

    // ── 3. Gói Combo Giờ chơi & F&B (Combos & ComboItems - UC42) ──
    Task<List<Combo>> GetCombosAsync(string? search = null, string? statusFilter = null, CancellationToken ct = default);
    Task<Combo?> GetComboWithItemsAsync(int id, CancellationToken ct = default);

    Task<ServiceResult<Combo>> CreateComboAsync(
        string name,
        decimal price,
        decimal playtimeHours,
        IEnumerable<(int ProductId, int Quantity)> items,
        CancellationToken ct = default);

    Task<ServiceResult<Combo>> UpdateComboAsync(
        int id,
        string name,
        decimal price,
        decimal playtimeHours,
        IEnumerable<(int ProductId, int Quantity)> items,
        CancellationToken ct = default);

    Task<ServiceResult<Combo>> ToggleComboStatusAsync(int id, CancellationToken ct = default);
}
