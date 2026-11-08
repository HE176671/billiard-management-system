using Bms.Web.Models;

namespace Bms.Web.Services;

public interface IOrderService
{
    Task<OrderOperationResult> CreateCounterOrderAsync(int sessionId, List<OrderItemInputModel> items, string actorId, string? note = null);

    Task<OrderOperationResult> CreateQrOrderAsync(QrOrderRequest request);

    Task<OrderOperationResult> ConfirmQrOrderAsync(int orderId, string staffId);

    Task<OrderOperationResult> RejectQrOrderAsync(int orderId, string reason, string staffId);

    Task<OrderOperationResult> CancelOrderDetailAsync(int orderDetailId, int cancelQuantity, string reason, string staffId);

    Task<List<OrderViewModel>> GetSessionOrdersAsync(int sessionId);

    Task<List<OrderViewModel>> GetPendingOrdersAsync();
}
