using Bms.Web.Models;

namespace Bms.Web.Services;

public interface IInventoryService
{
    Task<StockOperationResult> ChangeStockAsync(int productId, int quantityChange, string movementType,
        string? referenceType = null, int? referenceId = null, string? note = null, string? actorId = null);

    Task<StockOperationResult> AdjustStockAsync(int productId, int newQuantity, string reason, string actorId);

    Task<StockOperationResult> CreateReceiptAsync(StockReceiptInputModel model, string actorId);

    Task<List<StockReceiptViewModel>> GetReceiptsAsync(int take = 50);

    Task<StockReceiptViewModel?> GetReceiptDetailAsync(int receiptId);

    Task<List<StockMovementViewModel>> GetStockMovementsAsync(int? productId = null, int take = 100);

    Task<List<ProductViewModel>> GetInventoryListAsync(string? search = null, int? categoryId = null);
}
