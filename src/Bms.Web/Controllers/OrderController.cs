using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// UC07 — Khách gọi đồ ăn/uống (QR tại bàn & Staff quản lý/gọi tại quầy).
/// </summary>
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

    // ─────────────────────────────────────────────────────────────
    // 1. KÊNH QR TẠI BÀN (Khách quét QR ẩn danh [AllowAnonymous])
    // ─────────────────────────────────────────────────────────────

    // GET /Order/Table/{tableCode}
    [AllowAnonymous]
    [HttpGet("Order/Table/{tableCode}")]
    public async Task<IActionResult> Table(string tableCode)
    {
        if (string.IsNullOrWhiteSpace(tableCode)) return NotFound();

        var table = await _db.BilliardTables
            .Include(t => t.TableType)
            .FirstOrDefaultAsync(t => t.TableCode == tableCode);

        if (table == null) return NotFound();

        // Tìm phiên đang chơi (Active) của bàn
        var activeSession = await _db.PlaySessions
            .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(s => s.TableId == table.Id && s.Status == "Active");

        var categories = await GetActiveMenuCategoriesAsync();

        var vm = new OrderMenuViewModel
        {
            HasActiveSession = activeSession != null,
            ActiveSessionId  = activeSession?.Id,
            TableCode        = table.TableCode,
            TableTypeName    = table.TableType.Name,
            FloorNumber      = table.FloorNumber,
            StartAtUtc       = activeSession?.StartAtUtc,
            Categories       = categories,
            MyOrders         = activeSession != null ? MapSessionOrders(activeSession.Orders) : new()
        };

        ViewData["Title"] = $"Gọi món - Bàn {table.TableCode}";
        return View("TableOrder", vm);
    }

    // POST /Order/SubmitTable
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitTable(string tableCode, SubmitOrderInputModel input)
    {
        if (string.IsNullOrWhiteSpace(tableCode)) return NotFound();

        var table = await _db.BilliardTables
            .FirstOrDefaultAsync(t => t.TableCode == tableCode);
        if (table == null) return NotFound();

        // 1. Kiểm tra phiên Active của bàn
        var activeSession = await _db.PlaySessions
            .FirstOrDefaultAsync(s => s.TableId == table.Id && s.Status == "Active");

        if (activeSession == null)
        {
            TempData["Error"] = "Bàn này chưa có phiên chơi đang diễn ra hoặc đã kết thúc. Vui lòng liên hệ nhân viên.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        var paymentLocked = await _db.PaymentIntents.AsNoTracking()
            .AnyAsync(p => p.SessionId == activeSession.Id
                        && p.Status == PaymentIntent.StatusPending
                        && p.ExpiresAtUtc > DateTime.UtcNow);
        if (paymentLocked)
        {
            TempData["Error"] = "Bàn đang chờ xác nhận thanh toán, không thể gọi thêm món. Vui lòng liên hệ nhân viên.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        // 2. Kiểm tra giỏ hàng
        var validItems = input.Items?
            .Where(i => i.Quantity > 0)
            .ToList() ?? new List<OrderItemInputModel>();

        if (!validItems.Any())
        {
            TempData["Error"] = OrderConstants.MsgEmptyCart;
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        var aggregatedItems = validItems
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemInputModel
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        if (aggregatedItems.Any(i => !OrderConstants.IsQuantityValid(i.Quantity)))
        {
            TempData["Error"] = OrderConstants.MsgInvalidQuantity;
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        // 3. Kiểm tra sản phẩm và tạo đơn Pending
        var productIds = aggregatedItems.Select(i => i.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var item in aggregatedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
            {
                TempData["Error"] = $"Món {(product?.Name ?? "đã chọn")} hiện không kinh doanh. Vui lòng chọn món khác.";
                return RedirectToAction(nameof(Table), new { tableCode });
            }
        }

        try
        {
            var order = new Order
            {
                SessionId    = activeSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status       = OrderConstants.StatusPending // Phương án 1: Trạng thái chờ Staff xác nhận
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            foreach (var item in aggregatedItems)
            {
                var prod = products[item.ProductId];
                _db.OrderDetails.Add(new OrderDetail
                {
                    OrderId   = order.Id,
                    ProductId = prod.Id,
                    Quantity  = item.Quantity,
                    UnitPrice = prod.Price // Snapshot giá
                });
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "Đã gửi yêu cầu gọi món thành công! Nhân viên sẽ kiểm tra và phục vụ tại bàn.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu đơn gọi món cho bàn {TableCode}", tableCode);
            TempData["Error"] = "Hệ thống đang bận. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 2. KÊNH DÀNH CHO STAFF & QUẦY
    // ─────────────────────────────────────────────────────────────

    // GET /Order/Counter (Staff thao tác)
    [Authorize(Roles = "Staff")]
    [HttpGet]
    public async Task<IActionResult> Counter(int? sessionId)
    {
        // Lấy danh sách tất cả các bàn đang có phiên Active
        var activeSessions = await _db.PlaySessions
            .Include(s => s.BilliardTable).ThenInclude(t => t.TableType)
            .Where(s => s.Status == "Active")
            .OrderBy(s => s.BilliardTable.TableCode)
            .ToListAsync();

        PlaySession? selectedSession = null;
        if (sessionId.HasValue)
        {
            selectedSession = activeSessions.FirstOrDefault(s => s.Id == sessionId.Value);
        }
        else if (activeSessions.Any())
        {
            selectedSession = activeSessions.First();
        }

        var categories = await GetActiveMenuCategoriesAsync();

        ViewBag.ActiveSessions = activeSessions;
        ViewBag.SelectedSession = selectedSession;

        List<CustomerOrderSummaryViewModel> currentOrders = new();
        if (selectedSession != null)
        {
            var fullSession = await _db.PlaySessions
                .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(s => s.Id == selectedSession.Id);
            if (fullSession != null)
            {
                currentOrders = MapSessionOrders(fullSession.Orders);
            }
        }

        var vm = new OrderMenuViewModel
        {
            HasActiveSession = selectedSession != null,
            ActiveSessionId  = selectedSession?.Id,
            TableCode        = selectedSession?.BilliardTable.TableCode,
            TableTypeName    = selectedSession?.BilliardTable.TableType.Name,
            FloorNumber      = selectedSession?.BilliardTable.FloorNumber,
            StartAtUtc       = selectedSession?.StartAtUtc,
            Categories       = categories,
            MyOrders         = currentOrders
        };

        ViewData["Title"] = "Gọi món tại quầy & Quản lý bàn";
        return View("Counter", vm);
    }

    // POST /Order/SubmitStaffOrder (Staff gọi trực tiếp cho bàn)
    [Authorize(Roles = "Staff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitStaffOrder(int sessionId, SubmitOrderInputModel input)
    {
        var session = await _db.PlaySessions
            .Include(s => s.BilliardTable)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.Status == "Active");

        if (session == null)
        {
            TempData["Error"] = "Phiên chơi không tồn tại hoặc đã kết thúc.";
            return RedirectToAction(nameof(Counter), new { tab = "table" });
        }

        var paymentLocked = await _db.PaymentIntents.AsNoTracking()
            .AnyAsync(p => p.SessionId == session.Id
                        && p.Status == PaymentIntent.StatusPending
                        && p.ExpiresAtUtc > DateTime.UtcNow);
        if (paymentLocked)
        {
            TempData["Error"] = "Bàn đang chờ xác nhận thanh toán, không thể thêm món vào hóa đơn.";
            return RedirectToAction(nameof(Counter), new { sessionId, tab = "table" });
        }

        var validItems = input.Items?.Where(i => i.Quantity > 0).ToList() ?? new List<OrderItemInputModel>();
        if (!validItems.Any())
        {
            TempData["Error"] = OrderConstants.MsgEmptyCart;
            return RedirectToAction(nameof(Counter), new { sessionId, tab = "table" });
        }

        var aggregatedItems = validItems
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemInputModel { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var productIds = aggregatedItems.Select(i => i.ProductId).ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            foreach (var item in aggregatedItems)
            {
                if (!products.TryGetValue(item.ProductId, out var prod) || prod.StockQuantity < item.Quantity)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Món {(prod?.Name ?? "đã chọn")} không đủ số lượng tồn kho (còn {prod?.StockQuantity ?? 0}).";
                    return RedirectToAction(nameof(Counter), new { sessionId, tab = "table" });
                }
            }

            // Đơn do Staff tạo vào thẳng trạng thái Served và trừ tồn kho
            var order = new Order
            {
                SessionId = session.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status = OrderConstants.StatusServed
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            foreach (var item in aggregatedItems)
            {
                var prod = products[item.ProductId];
                _db.OrderDetails.Add(new OrderDetail
                {
                    OrderId = order.Id,
                    ProductId = prod.Id,
                    Quantity = item.Quantity,
                    UnitPrice = prod.Price
                });
                prod.StockQuantity -= item.Quantity; // Trừ tồn kho
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = "Đã thêm món thành công cho bàn " + session.BilliardTable.TableCode;
            return RedirectToAction(nameof(Counter), new { sessionId, tab = "table" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi staff đặt món cho phiên {SessionId}", sessionId);
            TempData["Error"] = "Có lỗi xảy ra khi tạo đơn.";
            return RedirectToAction(nameof(Counter), new { sessionId, tab = "table" });
        }
    }

    // POST /Order/SubmitCounterTakeaway (Khách mua lẻ tại quầy / Mang về không vào bàn)
    [Authorize(Roles = "Staff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitCounterTakeaway(SubmitOrderInputModel input)
    {
        var staffId = _userManager.GetUserId(User) 
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
            ?? "demo-staff-01";

        var validItems = input.Items?.Where(i => i.Quantity > 0).ToList() ?? new List<OrderItemInputModel>();
        if (!validItems.Any())
        {
            TempData["Error"] = OrderConstants.MsgEmptyCart;
            return RedirectToAction(nameof(Counter));
        }

        var aggregatedItems = validItems
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemInputModel { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var productIds = aggregatedItems.Select(i => i.ProductId).ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            decimal totalAmount = 0;
            foreach (var item in aggregatedItems)
            {
                if (!products.TryGetValue(item.ProductId, out var prod) || prod.StockQuantity < item.Quantity)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Món {(prod?.Name ?? "đã chọn")} không đủ số lượng tồn kho (còn {prod?.StockQuantity ?? 0}).";
                    return RedirectToAction(nameof(Counter));
                }
                totalAmount += prod.Price * item.Quantity;
            }

            // 1. Lấy 1 TableId hợp lệ trong DB để gắn session "Closed" (tiền giờ = 0)
            var defaultTableId = await _db.BilliardTables.Select(t => t.Id).FirstOrDefaultAsync();

            var counterSession = new PlaySession
            {
                TableId = defaultTableId,
                CustomerId = null,
                OpenedById = staffId,
                ClosedById = staffId,
                StartAtUtc = DateTime.UtcNow,
                EndAtUtc = DateTime.UtcNow,
                HourlyRateSnapshot = 0,
                PlaytimeAmount = 0,
                Status = "Closed" // Đóng ngay lập tức, hoàn toàn không chiếm bàn
            };
            _db.PlaySessions.Add(counterSession);
            await _db.SaveChangesAsync();

            // 2. Tạo đơn hàng Served
            var order = new Order
            {
                SessionId = counterSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status = OrderConstants.StatusServed
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // 3. Thêm chi tiết món & trừ tồn kho
            foreach (var item in aggregatedItems)
            {
                var prod = products[item.ProductId];
                _db.OrderDetails.Add(new OrderDetail
                {
                    OrderId = order.Id,
                    ProductId = prod.Id,
                    Quantity = item.Quantity,
                    UnitPrice = prod.Price
                });
                prod.StockQuantity -= item.Quantity; // Trừ tồn kho
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = $"Đã tạo đơn bán mang về thành công! Tổng tiền: {totalAmount:#,##0 ₫}.";
            return RedirectToAction(nameof(Counter));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi xử lý đơn khách mua tại quầy");
            TempData["Error"] = "Có lỗi xảy ra: " + ex.Message;
            return RedirectToAction(nameof(Counter));
        }
    }

    // GET /Order/Manage (Quản lý các đơn Pending từ khách quét QR)
    [Authorize(Roles = "Staff")]
    [HttpGet]
    public async Task<IActionResult> Manage()
    {
        var pendingOrders = await _db.Orders
            .Include(o => o.Session).ThenInclude(s => s.BilliardTable)
            .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
            .Where(o => o.Status == OrderConstants.StatusPending)
            .OrderBy(o => o.OrderTimeUtc)
            .ToListAsync();

        ViewData["Title"] = "Duyệt đơn gọi món từ bàn";
        return View("ManageOrders", pendingOrders);
    }

    // POST /Order/ApproveOrder (Staff duyệt đơn Pending -> Served và trừ tồn kho)
    [Authorize(Roles = "Staff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrder(int orderId, string? returnUrl = null)
    {
        // Trong request bình thường action tự mở transaction. Khi được gọi bên trong
        // một transaction nghiệp vụ/test lớn hơn thì tham gia transaction hiện hữu.
        await using var tx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        try
        {
            int claimed = await _db.Orders
                .Where(o => o.Id == orderId && o.Status == OrderConstants.StatusPending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.Status, OrderConstants.StatusPreparing));

            if (claimed == 0)
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "Đơn không tồn tại hoặc đã được nhân viên khác xử lý.";
                return RedirectToReturnUrl(returnUrl);
            }

            // ExecuteUpdate chạy trực tiếp trên DB nên loại bản Order cũ khỏi tracker
            // trước khi nạp lại trạng thái Preparing.
            var trackedOrder = _db.ChangeTracker.Entries<Order>()
                .FirstOrDefault(e => e.Entity.Id == orderId);
            if (trackedOrder != null)
            {
                trackedOrder.State = EntityState.Detached;
            }

            var order = await _db.Orders
                .Include(o => o.Session)
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null || order.Status != OrderConstants.StatusPreparing)
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "Đơn không tồn tại hoặc không ở trạng thái Chờ xử lý.";
                return RedirectToReturnUrl(returnUrl);
            }

            if (order.Session.Status != "Active")
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "Phiên bàn đã kết thúc, không thể duyệt thêm món.";
                return RedirectToReturnUrl(returnUrl);
            }

            var paymentLocked = await _db.PaymentIntents.AsNoTracking()
                .AnyAsync(p => p.SessionId == order.SessionId
                            && p.Status == PaymentIntent.StatusPending
                            && p.ExpiresAtUtc > DateTime.UtcNow);
            if (paymentLocked)
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "Bàn đang chờ xác nhận thanh toán. Hãy hoàn tất hoặc hủy yêu cầu thanh toán trước khi duyệt món.";
                return RedirectToReturnUrl(returnUrl);
            }

            // Kiểm tra tồn kho trước khi duyệt
            foreach (var detail in order.OrderDetails)
            {
                if (detail.Product.StockQuantity < detail.Quantity)
                {
                    if (tx != null) await tx.RollbackAsync();
                    TempData["Error"] = $"Không thể duyệt: Món '{detail.Product.Name}' không đủ tồn kho (còn {detail.Product.StockQuantity}, yêu cầu {detail.Quantity}).";
                    return RedirectToReturnUrl(returnUrl);
                }
            }

            // Trừ tồn kho và chuyển trạng thái Served
            foreach (var detail in order.OrderDetails)
            {
                detail.Product.StockQuantity -= detail.Quantity;
            }
            order.Status = OrderConstants.StatusServed;

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            TempData["Success"] = $"Đã duyệt đơn #{order.Id} và trừ tồn kho thành công.";
            return RedirectToReturnUrl(returnUrl);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi duyệt đơn {OrderId}", orderId);
            TempData["Error"] = "Có lỗi xảy ra khi duyệt đơn.";
            return RedirectToReturnUrl(returnUrl);
        }
    }

    // POST /Order/CancelOrder (Staff hủy đơn)
    [Authorize(Roles = "Staff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(int orderId, string? returnUrl = null)
    {
        int cancelled = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderConstants.StatusPending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, OrderConstants.StatusCancelled));

        if (cancelled == 0)
        {
            TempData["Error"] = "Chỉ có thể hủy đơn đang chờ Staff xác nhận.";
            return RedirectToReturnUrl(returnUrl);
        }

        TempData["Success"] = $"Đã hủy đơn #{orderId}.";
        return RedirectToReturnUrl(returnUrl);
    }

    // GET /Order (Mặc định cho Customer hoặc Staff)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Staff") || User.IsInRole("Admin"))
        {
            return RedirectToAction(nameof(Counter));
        }

        if (User.IsInRole("Customer"))
        {
            var userId = _userManager.GetUserId(User)!;
            var activeSession = await _db.PlaySessions
                .Include(s => s.BilliardTable)
                .FirstOrDefaultAsync(s => s.CustomerId == userId && s.Status == "Active");

            if (activeSession != null)
            {
                return RedirectToAction(nameof(Table), new { tableCode = activeSession.BilliardTable.TableCode });
            }
        }

        return View("NoSession");
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private async Task<List<CategoryMenuViewModel>> GetActiveMenuCategoriesAsync()
    {
        return await _db.ProductCategories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryMenuViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Products = c.Products
                    .Where(p => p.IsActive)
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
    }

    private static List<CustomerOrderSummaryViewModel> MapSessionOrders(IEnumerable<Order> orders)
    {
        return orders
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
    }

    private IActionResult RedirectToReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Manage));
    }
}
