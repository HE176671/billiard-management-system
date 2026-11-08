using Bms.Web.Models;

namespace Bms.Web.Services;

public interface IProductService
{
    Task<List<ProductViewModel>> GetAllProductsAsync(bool activeOnly = false, string? search = null, int? categoryId = null);
    Task<ProductViewModel?> GetProductByIdAsync(int id);
    Task<FandBServiceResult> CreateProductAsync(ProductInputModel model, string actorId);
    Task<FandBServiceResult> UpdateProductAsync(ProductInputModel model, string actorId);
    Task<FandBServiceResult> ToggleProductActiveAsync(int id);
    Task<List<ProductPriceHistoryViewModel>> GetPriceHistoryAsync(int? productId = null);
    Task<List<ProductCategoryViewModel>> GetAllCategoriesAsync();
    Task<FandBServiceResult> SaveCategoryAsync(int? id, string name);
}
