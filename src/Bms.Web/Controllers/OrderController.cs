using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace Bms.Web.Controllers;

/// <summary>
/// UC07 — Khách gọi đồ ăn/uống (Iter 2).
/// CustomerId luôn lấy từ claims (FR-00.2).
/// Chỉ cho phép gọi đồ khi có phiên chơi đang diễn ra (FR-07.1, FR-07.2).
/// </summary>
[Authorize(Roles = "Customer")]
public class OrderController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<OrderController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // GET /Order (FR-07.1, FR-07.2, FR-07.3, FR-07.6)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;

        // Tìm phiên chơi đang diễn ra của khách hàng
        var activeSession = await _db.PlaySessions
            .Include(s => s.Table).ThenInclude(t => t.TableType)
            .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(s => s.CustomerId == userId && s.Status == "Active");

        if (activeSession == null)
        {
            // FR-07.2: Không có phiên đang chơi -> ẩn thực đơn và thông báo
            var emptyVm = new OrderMenuViewModel
            {
                HasActiveSession = false
            };
            ViewData["Title"] = "Gọi đồ ăn / uống";
            return View(emptyVm);
        }

        // FR-07.3: Hiển thị các món nhóm theo danh mục
        var categories = await _db.ProductCategories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryMenuViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Products = c.Products
                    .OrderBy(p => p.Name)
                    .Select(p => new ProductMenuItemViewModel
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Price = p.Price,
                        StockQuantity = p.StockQuantity,
                        IsActive = p.IsActive
                    })
                    .ToList()
            })
            .ToListAsync();

        // FR-07.6: Đơn của tôi trong phiên đang chơi
        var myOrders = activeSession.Orders
            .OrderByDescending(o => o.OrderTimeUtc)
            .Select(o => new CustomerOrderSummaryViewModel
            {
                OrderId = o.Id,
                OrderTimeUtc = o.OrderTimeUtc,
                Status = o.Status,
                Items = o.OrderDetails.Select(d => new CustomerOrderItemViewModel
                {
                    ProductId = d.ProductId,
                    ProductName = d.Product.Name,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice
                }).ToList()
            })
            .ToList();

        var vm = new OrderMenuViewModel
        {
            HasActiveSession = true,
            ActiveSessionId  = activeSession.Id,
            TableCode        = activeSession.Table.TableCode,
            TableTypeName    = activeSession.Table.TableType.Name,
            FloorNumber      = activeSession.Table.FloorNumber,
            StartAtUtc       = activeSession.StartAtUtc,
            Categories       = categories,
            MyOrders         = myOrders
        };

        ViewData["Title"] = "Gọi đồ ăn / uống";
        return View(vm);
    }

    // POST /Order/Submit (FR-07.4, FR-07.5, FR-07.7)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitOrderInputModel input)
    {
        var userId = _userManager.GetUserId(User)!;

        // 1. Kiểm tra phiên chơi vẫn đang diễn ra và thuộc về Customer (FR-07.5)
        var activeSession = await _db.PlaySessions
            .Include(s => s.Table)
            .FirstOrDefaultAsync(s => s.CustomerId == userId && s.Status == "Active");

        if (activeSession == null)
        {
            TempData["Error"] = OrderConstants.MsgSessionEnded;
            return RedirectToAction(nameof(Index));
        }

        // 2. Kiểm tra giỏ không rỗng (FR-07.5)
        var validItems = input.Items?
            .Where(i => i.Quantity > 0)
            .ToList() ?? new List<OrderItemInputModel>();

        if (!validItems.Any())
        {
            TempData["Error"] = OrderConstants.MsgEmptyCart;
            return RedirectToAction(nameof(Index));
        }

        // Gộp các món trùng ProductId nếu có
        var aggregatedItems = validItems
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemInputModel
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        // 3. Kiểm tra số lượng mỗi món từ 1 đến 20 (FR-07.4)
        if (aggregatedItems.Any(i => !OrderConstants.IsQuantityValid(i.Quantity)))
        {
            TempData["Error"] = OrderConstants.MsgInvalidQuantity;
            return RedirectToAction(nameof(Index));
        }

        // 4. Chống gửi đơn trùng do nhấn 2 lần liên tiếp (Idempotency)
        var fiveSecondsAgo = DateTime.UtcNow.AddSeconds(-5);
        var recentOrder = await _db.Orders
            .Include(o => o.OrderDetails)
            .Where(o => o.SessionId == activeSession.Id && o.OrderTimeUtc >= fiveSecondsAgo)
            .OrderByDescending(o => o.OrderTimeUtc)
            .FirstOrDefaultAsync();

        if (recentOrder != null)
        {
            var recentItems = recentOrder.OrderDetails
                .Select(d => (d.ProductId, d.Quantity));
            var currentItems = aggregatedItems
                .Select(i => (i.ProductId, i.Quantity));

            if (OrderConstants.IsDuplicateOrder(recentItems, currentItems, recentOrder.OrderTimeUtc, DateTime.UtcNow))
            {
                TempData["Success"] = OrderConstants.MsgOrderSuccess;
                return RedirectToAction(nameof(Index));
            }
        }

        // 5. Transaction: Kiểm tra tồn kho, lưu đơn, snapshot giá, trừ kho (FR-07.5, FR-07.7)
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var productIds = aggregatedItems.Select(i => i.ProductId).ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            // Kiểm tra từng món: tồn tại, đang bán, còn hàng
            foreach (var item in aggregatedItems)
            {
                if (!products.TryGetValue(item.ProductId, out var product) 
                    || !OrderConstants.IsProductAvailable(product.IsActive, product.StockQuantity, item.Quantity))
                {
                    await tx.RollbackAsync();
                    var name = product?.Name ?? "đã chọn";
                    // FR-07.7: Từ chối TOÀN BỘ đơn, giữ nguyên giỏ và hiển thị đúng câu thông báo
                    TempData["Error"] = OrderConstants.MsgProductUnavailable(name);
                    return RedirectToAction(nameof(Index));
                }
            }

            // Tạo đơn hàng mới với trạng thái ban đầu "Pending" (Chờ xử lý)
            var order = new Order
            {
                SessionId    = activeSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status       = OrderConstants.StatusPending
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // Lưu từng chi tiết đơn với UnitPriceSnapshot và trừ tồn kho
            foreach (var item in aggregatedItems)
            {
                var prod = products[item.ProductId];
                var detail = new OrderDetail
                {
                    OrderId   = order.Id,
                    ProductId = prod.Id,
                    Quantity  = item.Quantity,
                    UnitPrice = prod.Price // Snapshot đơn giá tại thời điểm đặt
                };
                _db.OrderDetails.Add(detail);

                // Cập nhật tồn kho
                prod.StockQuantity -= item.Quantity;
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = OrderConstants.MsgOrderSuccess;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi transaction khi gửi đơn cho Customer {UserId}, Session {SessionId}", userId, activeSession.Id);
            TempData["Error"] = "Hệ thống đang bận. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index));
        }
    }
}
