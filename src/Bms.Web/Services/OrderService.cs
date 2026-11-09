using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Hubs;
using Bms.Web.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderEntity = Bms.Web.Data.Entities.Order;
using OrderDetailEntity = Bms.Web.Data.Entities.OrderDetail;

namespace Bms.Web.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryService _inventoryService;
    private readonly IHubContext<OrderHub>? _hubContext;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ApplicationDbContext db,
        IInventoryService inventoryService,
        ILogger<OrderService> logger,
        IHubContext<OrderHub>? hubContext = null)
    {
        _db = db;
        _inventoryService = inventoryService;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<OrderOperationResult> CreateCounterOrderAsync(int sessionId, List<OrderItemInputModel> items, string actorId, string? note = null)
    {
        if (items == null || items.Count == 0 || items.All(i => i.Quantity <= 0))
        {
            return OrderOperationResult.Fail("Vui lòng chọn ít nhất 1 món có số lượng lớn hơn 0");
        }

        var session = await _db.PlaySessions
            .Include(s => s.BilliardTable)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            return OrderOperationResult.Fail($"Không tìm thấy phiên chơi #{sessionId}");
        }

        if (session.Status != "Active")
        {
            return OrderOperationResult.Fail($"Phiên chơi bàn {session.BilliardTable?.TableCode} không còn hoạt động (Trạng thái: {session.Status})");
        }

        if (session.BilliardTable != null && session.BilliardTable.Status == "AwaitingPayment")
        {
            return OrderOperationResult.Fail($"Bàn {session.BilliardTable.TableCode} đang chờ thanh toán, không thể gọi thêm món!");
        }

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var order = new OrderEntity
            {
                SessionId = sessionId,
                OrderTimeUtc = DateTime.UtcNow,
                Status = OrderEntity.StatusCompleted,
                OrderSource = OrderEntity.SourceStaff,
                ClientSessionId = sessionId,
                Note = note?.Trim(),
                CreatedById = actorId
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            foreach (var item in items.Where(i => i.Quantity > 0))
            {
                // Khóa dòng sản phẩm và trừ kho nguyên tử
                var stockRes = await _inventoryService.ChangeStockAsync(
                    item.ProductId,
                    -item.Quantity,
                    StockMovement.TypeOrderDeduct,
                    "Order",
                    order.Id,
                    $"Gọi món tại quầy cho bàn {session.BilliardTable?.TableCode} (Phiên #{sessionId})",
                    actorId);

                if (!stockRes.Success)
                {
                    if (ownTx && tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail(stockRes.Message);
                }

                var product = await _db.Products.FindAsync(item.ProductId);
                if (product == null)
                {
                    if (ownTx && tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail($"Không tìm thấy món #{item.ProductId}");
                }

                var detail = new OrderDetailEntity
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price,
                    Status = OrderDetailEntity.StatusActive
                };
                _db.OrderDetails.Add(detail);
            }

            await _db.SaveChangesAsync();
            if (ownTx && tx != null) await tx.CommitAsync();

            _logger.LogInformation("Counter order #{OrderId} created for Session #{SessionId} by {User}", order.Id, sessionId, actorId);
            return OrderOperationResult.Ok(order.Id, "Đã thêm món vào bàn thành công và trừ tồn kho!");
        }
        catch (Exception ex)
        {
            if (ownTx && tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi gọi món tại quầy cho phiên #{SessionId}", sessionId);
            return OrderOperationResult.Fail("Lỗi hệ thống khi gọi món: " + (ex.InnerException?.Message ?? ex.Message));
        }
        finally
        {
            if (ownTx && tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<OrderOperationResult> CreateQrOrderAsync(QrOrderRequest request)
    {
        if (request.Items == null || request.Items.Count == 0 || request.Items.All(i => i.Quantity <= 0))
        {
            return OrderOperationResult.Fail("Vui lòng chọn ít nhất 1 món để đặt");
        }

        // 1. Kiểm tra phiên thực tế tại thời điểm gửi đơn
        var currentSession = await _db.PlaySessions
            .Include(s => s.BilliardTable)
            .Where(s => (s.TableId == request.TableId || (s.BilliardTable != null && s.BilliardTable.TableCode == request.TableCode)) && s.Status == "Active")
            .OrderByDescending(s => s.StartAtUtc)
            .FirstOrDefaultAsync();

        if (currentSession == null)
        {
            return OrderOperationResult.Fail("Bàn hiện chưa mở phiên chơi hoặc đã kết thúc. Vui lòng liên hệ nhân viên!");
        }

        // UC-31 BR: Kiểm tra ClientSessionId khớp với phiên hiện tại
        if (request.ClientSessionId > 0 && currentSession.Id != request.ClientSessionId)
        {
            return OrderOperationResult.Fail("Phiên chơi lúc bạn quét mã QR đã kết thúc (Bàn đã đổi khách mới). Đơn gọi món bị từ chối!");
        }

        // UC-24 BR: Bàn đã Chờ thanh toán thì không gọi thêm được
        if (currentSession.BilliardTable != null && currentSession.BilliardTable.Status == "AwaitingPayment")
        {
            return OrderOperationResult.Fail("Bàn đang trong quá trình thanh toán, không thể đặt thêm món!");
        }

        // Kiểm tra sơ bộ tồn kho các món
        foreach (var item in request.Items.Where(i => i.Quantity > 0))
        {
            var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ProductId);
            if (p == null || !p.IsActive)
            {
                return OrderOperationResult.Fail($"Món #{item.ProductId} hiện không khả dụng");
            }
            if (p.StockQuantity < item.Quantity)
            {
                return OrderOperationResult.Fail($"Món '{p.Name}' chỉ còn {p.StockQuantity} trong kho, không đủ số lượng {item.Quantity}");
            }
        }

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var order = new OrderEntity
            {
                SessionId = currentSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status = OrderEntity.StatusPending,
                OrderSource = OrderEntity.SourceQR,
                ClientSessionId = currentSession.Id,
                Note = request.Note?.Trim()
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            foreach (var item in request.Items.Where(i => i.Quantity > 0))
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                var detail = new OrderDetailEntity
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product!.Price,
                    Status = OrderDetailEntity.StatusActive
                };
                _db.OrderDetails.Add(detail);
            }

            await _db.SaveChangesAsync();
            if (ownTx && tx != null) await tx.CommitAsync();

            // Phát tín hiệu SignalR Realtime đến nhóm Staff (UC-24)
            if (_hubContext != null)
            {
                try
                {
                    await _hubContext.Clients.Group(OrderHub.GroupStaff).SendAsync("ReceiveNewOrder", new
                    {
                        orderId = order.Id,
                        tableCode = currentSession.BilliardTable?.TableCode ?? request.TableCode,
                        sessionId = currentSession.Id,
                        itemCount = request.Items.Where(i => i.Quantity > 0).Sum(i => i.Quantity),
                        orderTime = order.OrderTimeUtc.AddHours(7).ToString("HH:mm:ss")
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể gửi SignalR notification tới Staff");
                }
            }

            return OrderOperationResult.Ok(order.Id, "Đã gửi yêu cầu gọi món thành công! Nhân viên sẽ kiểm tra và phục vụ quý khách ngay.");
        }
        catch (Exception ex)
        {
            if (ownTx && tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi tạo đơn QR bàn {TableCode}", request.TableCode);
            return OrderOperationResult.Fail("Lỗi hệ thống khi gửi đơn: " + ex.Message);
        }
        finally
        {
            if (ownTx && tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<OrderOperationResult> ConfirmQrOrderAsync(int orderId, string staffId)
    {
        var order = await _db.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .Include(o => o.Session)
                .ThenInclude(s => s!.BilliardTable)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return OrderOperationResult.Fail($"Không tìm thấy đơn hàng #{orderId}");
        }

        if (order.Status != OrderEntity.StatusPending)
        {
            return OrderOperationResult.Fail($"Đơn hàng #{orderId} đã được xử lý trước đó (Trạng thái: {order.Status})");
        }

        // UC-24 BR: Bàn đã Chờ thanh toán thì không xác nhận được đơn
        if (order.Session != null)
        {
            var currentTableStatus = await _db.BilliardTables.AsNoTracking()
                .Where(t => t.Id == order.Session.TableId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync();

            if (currentTableStatus == "AwaitingPayment" || (order.Session.BilliardTable != null && order.Session.BilliardTable.Status == "AwaitingPayment"))
            {
                return OrderOperationResult.Fail($"Bàn {order.Session.BilliardTable?.TableCode} đã Chờ thanh toán, không thể xác nhận đơn gọi món!");
            }
        }

        if (order.Session != null && order.Session.Status != "Active")
        {
            return OrderOperationResult.Fail($"Phiên chơi của đơn hàng không còn ở trạng thái Hoạt động!");
        }

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            // Trừ kho nguyên tử từng món
            foreach (var detail in order.OrderDetails.Where(d => d.Status == OrderDetailEntity.StatusActive))
            {
                var stockRes = await _inventoryService.ChangeStockAsync(
                    detail.ProductId,
                    -detail.Quantity,
                    StockMovement.TypeOrderDeduct,
                    "Order",
                    order.Id,
                    $"Xác nhận đơn QR bàn {order.Session?.BilliardTable?.TableCode} (Đơn #{order.Id})",
                    staffId);

                if (!stockRes.Success)
                {
                    if (ownTx && tx != null) await tx.RollbackAsync();
                    return OrderOperationResult.Fail($"Xác nhận thất bại: {stockRes.Message}");
                }
            }

            order.Status = OrderEntity.StatusCompleted;
            order.CreatedById = staffId;
            await _db.SaveChangesAsync();

            if (ownTx && tx != null) await tx.CommitAsync();

            _logger.LogInformation("QR Order #{OrderId} confirmed by {Staff}", orderId, staffId);

            if (_hubContext != null)
            {
                try
                {
                    await _hubContext.Clients.Group(OrderHub.GroupStaff).SendAsync("OrderConfirmed", new
                    {
                        orderId = order.Id,
                        tableCode = order.Session?.BilliardTable?.TableCode,
                        sessionId = order.SessionId
                    });
                    await _hubContext.Clients.All.SendAsync("OrderStatusChanged", new
                    {
                        orderId = order.Id,
                        sessionId = order.SessionId,
                        status = OrderEntity.StatusCompleted
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi gửi SignalR sau khi xác nhận đơn #{OrderId}", orderId);
                }
            }

            return OrderOperationResult.Ok(order.Id, $"Đã xác nhận đơn #{order.Id} và trừ kho thành công!");
        }
        catch (Exception ex)
        {
            if (ownTx && tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi xác nhận đơn QR #{OrderId}", orderId);
            return OrderOperationResult.Fail("Lỗi khi xác nhận đơn: " + ex.Message);
        }
        finally
        {
            if (ownTx && tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<OrderOperationResult> RejectQrOrderAsync(int orderId, string reason, string staffId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            return OrderOperationResult.Fail($"Không tìm thấy đơn hàng #{orderId}");
        }

        if (order.Status != OrderEntity.StatusPending)
        {
            return OrderOperationResult.Fail($"Đơn hàng #{orderId} không còn ở trạng thái Chờ duyệt (Trạng thái: {order.Status})");
        }

        order.Status = OrderEntity.StatusRejected;
        order.RejectReason = string.IsNullOrWhiteSpace(reason) ? "Nhân viên từ chối" : reason.Trim();
        order.CreatedById = staffId;
        await _db.SaveChangesAsync();

        _logger.LogInformation("QR Order #{OrderId} rejected by {Staff}. Reason: {Reason}", orderId, staffId, order.RejectReason);

        if (_hubContext != null)
        {
            try
            {
                await _hubContext.Clients.Group(OrderHub.GroupStaff).SendAsync("OrderRejected", new
                {
                    orderId = order.Id,
                    reason = order.RejectReason
                });
                await _hubContext.Clients.All.SendAsync("OrderStatusChanged", new
                {
                    orderId = order.Id,
                    sessionId = order.SessionId,
                    status = OrderEntity.StatusRejected,
                    reason = order.RejectReason
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi gửi SignalR sau khi từ chối đơn #{OrderId}", orderId);
            }
        }

        return OrderOperationResult.Ok(order.Id, $"Đã từ chối đơn #{order.Id} (Kho không bị trừ)");
    }

    public async Task<OrderOperationResult> CancelOrderDetailAsync(int orderDetailId, int cancelQuantity, string reason, string staffId)
    {
        if (cancelQuantity <= 0)
        {
            return OrderOperationResult.Fail("Số lượng hủy phải lớn hơn 0");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OrderOperationResult.Fail("Vui lòng nhập lý do hủy món");
        }

        var ownTx = _db.Database.CurrentTransaction == null;
        var tx = ownTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var detail = await _db.OrderDetails
                .Include(d => d.Order)
                    .ThenInclude(o => o!.Session)
                        .ThenInclude(s => s!.BilliardTable)
                .Include(d => d.Product)
                .FirstOrDefaultAsync(d => d.Id == orderDetailId);

            if (detail == null)
            {
                return OrderOperationResult.Fail($"Không tìm thấy chi tiết món #{orderDetailId}");
            }

            if (detail.Order?.Status != OrderEntity.StatusCompleted)
            {
                return OrderOperationResult.Fail("Chỉ được hủy các món thuộc đơn hàng đã hoàn thành");
            }

            int availableToCancel = detail.Quantity - detail.CancelledQuantity;
            if (cancelQuantity > availableToCancel)
            {
                return OrderOperationResult.Fail($"Số lượng yêu cầu hủy ({cancelQuantity}) vượt quá số lượng có thể hủy ({availableToCancel})");
            }

            // Hoàn lại kho đúng 1 lần qua thẻ kho
            var stockRes = await _inventoryService.ChangeStockAsync(
                detail.ProductId,
                cancelQuantity,
                StockMovement.TypeOrderCancelReturn,
                "OrderDetail",
                detail.Id,
                $"Hủy {cancelQuantity}x {detail.Product?.Name} bàn {detail.Order?.Session?.BilliardTable?.TableCode}. Lý do: {reason.Trim()}",
                staffId);

            if (!stockRes.Success)
            {
                if (ownTx && tx != null) await tx.RollbackAsync();
                return OrderOperationResult.Fail(stockRes.Message);
            }

            detail.CancelledQuantity += cancelQuantity;
            if (detail.CancelledQuantity >= detail.Quantity)
            {
                detail.Status = OrderDetailEntity.StatusCancelled;
            }
            detail.CancelledAtUtc = DateTime.UtcNow;
            detail.CancelledById = staffId;
            detail.CancelReason = reason.Trim();

            await _db.SaveChangesAsync();
            if (ownTx && tx != null) await tx.CommitAsync();

            _logger.LogInformation("Cancelled {Qty} of Product {ProductId} on OrderDetail {DetailId} by {Staff}. Reason: {Reason}",
                cancelQuantity, detail.ProductId, orderDetailId, staffId, reason);

            return OrderOperationResult.Ok(detail.Id, $"Đã hủy {cancelQuantity}x {detail.Product?.Name} và hoàn lại kho thành công!");
        }
        catch (Exception ex)
        {
            if (ownTx && tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi hủy chi tiết món #{DetailId}", orderDetailId);
            return OrderOperationResult.Fail("Lỗi hệ thống khi hủy món: " + ex.Message);
        }
        finally
        {
            if (ownTx && tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<List<OrderViewModel>> GetSessionOrdersAsync(int sessionId)
    {
        return await _db.Orders.AsNoTracking()
            .Include(o => o.Session)
                .ThenInclude(s => s!.BilliardTable)
            .Include(o => o.CreatedBy)
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.CancelledBy)
            .Where(o => o.SessionId == sessionId)
            .OrderByDescending(o => o.OrderTimeUtc)
            .Select(o => new OrderViewModel
            {
                Id = o.Id,
                SessionId = o.SessionId,
                TableCode = o.Session != null && o.Session.BilliardTable != null ? o.Session.BilliardTable.TableCode : "—",
                OrderTimeUtc = o.OrderTimeUtc,
                Status = o.Status,
                OrderSource = o.OrderSource,
                ClientSessionId = o.ClientSessionId,
                RejectReason = o.RejectReason,
                Note = o.Note,
                CreatedByName = o.CreatedBy != null ? o.CreatedBy.FullName : (o.OrderSource == OrderEntity.SourceQR ? "Khách quét QR" : "Nhân viên"),
                Details = o.OrderDetails.Select(d => new OrderDetailViewModel
                {
                    Id = d.Id,
                    OrderId = d.OrderId,
                    ProductId = d.ProductId,
                    ProductName = d.Product != null ? d.Product.Name : "Món #" + d.ProductId,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Status = d.Status,
                    CancelledQuantity = d.CancelledQuantity,
                    CancelledAtUtc = d.CancelledAtUtc,
                    CancelledByName = d.CancelledBy != null ? d.CancelledBy.FullName : null,
                    CancelReason = d.CancelReason
                }).ToList()
            })
            .ToListAsync();
    }

    public async Task<List<OrderViewModel>> GetPendingOrdersAsync()
    {
        return await _db.Orders.AsNoTracking()
            .Include(o => o.Session)
                .ThenInclude(s => s!.BilliardTable)
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .Where(o => o.Status == OrderEntity.StatusPending)
            .OrderBy(o => o.OrderTimeUtc)
            .Select(o => new OrderViewModel
            {
                Id = o.Id,
                SessionId = o.SessionId,
                TableCode = o.Session != null && o.Session.BilliardTable != null ? o.Session.BilliardTable.TableCode : "—",
                TableStatus = o.Session != null && o.Session.BilliardTable != null ? o.Session.BilliardTable.Status : string.Empty,
                OrderTimeUtc = o.OrderTimeUtc,
                Status = o.Status,
                OrderSource = o.OrderSource,
                ClientSessionId = o.ClientSessionId,
                Note = o.Note,
                CreatedByName = "Khách quét QR",
                Details = o.OrderDetails.Select(d => new OrderDetailViewModel
                {
                    Id = d.Id,
                    OrderId = d.OrderId,
                    ProductId = d.ProductId,
                    ProductName = d.Product != null ? d.Product.Name : "Món #" + d.ProductId,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Status = d.Status,
                    CancelledQuantity = d.CancelledQuantity,
                    CurrentStock = d.Product != null ? d.Product.StockQuantity : 0
                }).ToList()
            })
            .ToListAsync();
    }
}
