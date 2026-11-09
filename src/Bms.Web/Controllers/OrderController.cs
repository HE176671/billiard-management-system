using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

/// <summary>
/// UC07 ΓÇö Kh├ích gß╗ìi ─æß╗ô ─ân/uß╗æng (QR tß║íi b├án & Staff quß║ún l├╜/gß╗ìi tß║íi quß║ºy).
/// </summary>
public class OrderController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrderController> _logger;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;

    public OrderController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<OrderController> logger)
        : this(db, userManager, logger, null!, null!)
    {
    }

    public OrderController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<OrderController> logger,
        IOrderService orderService,
        IProductService productService)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
        _orderService = orderService;
        _productService = productService;
    }

    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
    // 1. K├èNH QR Tß║áI B├ÇN (Kh├ích qu├⌐t QR ß║⌐n danh [AllowAnonymous])
    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

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

        // T├¼m phi├¬n ─æang ch╞íi (Active) cß╗ºa b├án
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

        ViewData["Title"] = $"Gß╗ìi m├│n - B├án {table.TableCode}";
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

        // 1. Kiß╗âm tra phi├¬n Active cß╗ºa b├án
        var activeSession = await _db.PlaySessions
            .FirstOrDefaultAsync(s => s.TableId == table.Id && s.Status == "Active");

        if (activeSession == null)
        {
            TempData["Error"] = "B├án n├áy ch╞░a c├│ phi├¬n ch╞íi ─æang diß╗àn ra hoß║╖c ─æ├ú kß║┐t th├║c. Vui l├▓ng li├¬n hß╗ç nh├ón vi├¬n.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        var paymentLocked = await _db.PaymentIntents.AsNoTracking()
            .AnyAsync(p => p.SessionId == activeSession.Id
                        && p.Status == PaymentIntent.StatusPending
                        && p.ExpiresAtUtc > DateTime.UtcNow);
        if (paymentLocked)
        {
            TempData["Error"] = "B├án ─æang chß╗¥ x├íc nhß║¡n thanh to├ín, kh├┤ng thß╗â gß╗ìi th├¬m m├│n. Vui l├▓ng li├¬n hß╗ç nh├ón vi├¬n.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }

        // 2. Kiß╗âm tra giß╗Å h├áng
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

        // 3. Kiß╗âm tra sß║ún phß║⌐m v├á tß║ío ─æ╞ín Pending
        var productIds = aggregatedItems.Select(i => i.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var item in aggregatedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
            {
                TempData["Error"] = $"M├│n {(product?.Name ?? "─æ├ú chß╗ìn")} hiß╗çn kh├┤ng kinh doanh. Vui l├▓ng chß╗ìn m├│n kh├íc.";
                return RedirectToAction(nameof(Table), new { tableCode });
            }
        }

        try
        {
            var order = new Order
            {
                SessionId    = activeSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status       = OrderConstants.StatusPending // Ph╞░╞íng ├ín 1: Trß║íng th├íi chß╗¥ Staff x├íc nhß║¡n
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
                    UnitPrice = prod.Price // Snapshot gi├í
                });
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "─É├ú gß╗¡i y├¬u cß║ºu gß╗ìi m├│n th├ánh c├┤ng! Nh├ón vi├¬n sß║╜ kiß╗âm tra v├á phß╗Ñc vß╗Ñ tß║íi b├án.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lß╗ùi khi l╞░u ─æ╞ín gß╗ìi m├│n cho b├án {TableCode}", tableCode);
            TempData["Error"] = "Hß╗ç thß╗æng ─æang bß║¡n. Vui l├▓ng thß╗¡ lß║íi sau.";
            return RedirectToAction(nameof(Table), new { tableCode });
        }
    }

    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
    // 2. K├èNH D├ÇNH CHO STAFF & QUß║ªY
    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    // GET /Order/Counter (Staff thao t├íc)
    [Authorize(Roles = "Staff")]
    [HttpGet]
    public async Task<IActionResult> Counter(int? sessionId)
    {
        // Lß║Ñy danh s├ích tß║Ñt cß║ú c├íc b├án ─æang c├│ phi├¬n Active
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

        ViewData["Title"] = "Gß╗ìi m├│n tß║íi quß║ºy & Quß║ún l├╜ b├án";
        return View("Counter", vm);
    }

    // POST /Order/SubmitStaffOrder (Staff gß╗ìi trß╗▒c tiß║┐p cho b├án)
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
            TempData["Error"] = "Phi├¬n ch╞íi kh├┤ng tß╗ôn tß║íi hoß║╖c ─æ├ú kß║┐t th├║c.";
            return RedirectToAction(nameof(Counter));
        }

        var paymentLocked = await _db.PaymentIntents.AsNoTracking()
            .AnyAsync(p => p.SessionId == session.Id
                        && p.Status == PaymentIntent.StatusPending
                        && p.ExpiresAtUtc > DateTime.UtcNow);
        if (paymentLocked)
        {
            TempData["Error"] = "B├án ─æang chß╗¥ x├íc nhß║¡n thanh to├ín, kh├┤ng thß╗â th├¬m m├│n v├áo h├│a ─æ╞ín.";
            return RedirectToAction(nameof(Counter), new { sessionId });
        }

        var validItems = input.Items?.Where(i => i.Quantity > 0).ToList() ?? new List<OrderItemInputModel>();
        if (!validItems.Any())
        {
            TempData["Error"] = OrderConstants.MsgEmptyCart;
            return RedirectToAction(nameof(Counter), new { sessionId });
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
                    TempData["Error"] = $"M├│n {(prod?.Name ?? "─æ├ú chß╗ìn")} kh├┤ng ─æß╗º sß╗æ l╞░ß╗úng tß╗ôn kho (c├▓n {prod?.StockQuantity ?? 0}).";
                    return RedirectToAction(nameof(Counter), new { sessionId });
                }
            }

            // ─É╞ín do Staff tß║ío v├áo thß║│ng trß║íng th├íi Served v├á trß╗½ tß╗ôn kho
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
                prod.StockQuantity -= item.Quantity; // Trß╗½ tß╗ôn kho
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = "─É├ú th├¬m m├│n th├ánh c├┤ng cho b├án " + session.BilliardTable.TableCode;
            return RedirectToAction(nameof(Counter), new { sessionId });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lß╗ùi khi staff ─æß║╖t m├│n cho phi├¬n {SessionId}", sessionId);
            TempData["Error"] = "C├│ lß╗ùi xß║úy ra khi tß║ío ─æ╞ín.";
            return RedirectToAction(nameof(Counter), new { sessionId });
        }
    }

    // POST /Order/SubmitCounterTakeaway (Kh├ích mua lß║╗ tß║íi quß║ºy / Mang vß╗ü kh├┤ng v├áo b├án)
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
                    TempData["Error"] = $"M├│n {(prod?.Name ?? "─æ├ú chß╗ìn")} kh├┤ng ─æß╗º sß╗æ l╞░ß╗úng tß╗ôn kho (c├▓n {prod?.StockQuantity ?? 0}).";
                    return RedirectToAction(nameof(Counter));
                }
                totalAmount += prod.Price * item.Quantity;
            }

            // 1. Lß║Ñy 1 TableId hß╗úp lß╗ç trong DB ─æß╗â gß║»n session "Closed" (tiß╗ün giß╗¥ = 0)
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
                Status = "Closed" // ─É├│ng ngay lß║¡p tß╗⌐c, ho├án to├án kh├┤ng chiß║┐m b├án
            };
            _db.PlaySessions.Add(counterSession);
            await _db.SaveChangesAsync();

            // 2. Tß║ío ─æ╞ín h├áng Served
            var order = new Order
            {
                SessionId = counterSession.Id,
                OrderTimeUtc = DateTime.UtcNow,
                Status = OrderConstants.StatusServed
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // 3. Th├¬m chi tiß║┐t m├│n & trß╗½ tß╗ôn kho
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
                prod.StockQuantity -= item.Quantity; // Trß╗½ tß╗ôn kho
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = $"─É├ú tß║ío ─æ╞ín b├ín mang vß╗ü th├ánh c├┤ng! Tß╗òng tiß╗ün: {totalAmount:#,##0 Γé½}.";
            return RedirectToAction(nameof(Counter));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lß╗ùi khi xß╗¡ l├╜ ─æ╞ín kh├ích mua tß║íi quß║ºy");
            TempData["Error"] = "C├│ lß╗ùi xß║úy ra: " + ex.Message;
            return RedirectToAction(nameof(Counter));
        }
    }

    // GET /Order/Manage (Quß║ún l├╜ c├íc ─æ╞ín Pending tß╗½ kh├ích qu├⌐t QR)
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

        ViewData["Title"] = "Duyß╗çt ─æ╞ín gß╗ìi m├│n tß╗½ b├án";
        return View("ManageOrders", pendingOrders);
    }

    // POST /Order/ApproveOrder (Staff duyß╗çt ─æ╞ín Pending -> Served v├á trß╗½ tß╗ôn kho)
    [Authorize(Roles = "Staff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrder(int orderId, string? returnUrl = null)
    {
        // Trong request b├¼nh th╞░ß╗¥ng action tß╗▒ mß╗ƒ transaction. Khi ─æ╞░ß╗úc gß╗ìi b├¬n trong
        // mß╗Öt transaction nghiß╗çp vß╗Ñ/test lß╗¢n h╞ín th├¼ tham gia transaction hiß╗çn hß╗»u.
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
                TempData["Error"] = "─É╞ín kh├┤ng tß╗ôn tß║íi hoß║╖c ─æ├ú ─æ╞░ß╗úc nh├ón vi├¬n kh├íc xß╗¡ l├╜.";
                return RedirectToReturnUrl(returnUrl);
            }

            // ExecuteUpdate chß║íy trß╗▒c tiß║┐p tr├¬n DB n├¬n loß║íi bß║ún Order c┼⌐ khß╗Åi tracker
            // tr╞░ß╗¢c khi nß║íp lß║íi trß║íng th├íi Preparing.
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
                TempData["Error"] = "─É╞ín kh├┤ng tß╗ôn tß║íi hoß║╖c kh├┤ng ß╗ƒ trß║íng th├íi Chß╗¥ xß╗¡ l├╜.";
                return RedirectToReturnUrl(returnUrl);
            }

            if (order.Session.Status != "Active")
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "Phi├¬n b├án ─æ├ú kß║┐t th├║c, kh├┤ng thß╗â duyß╗çt th├¬m m├│n.";
                return RedirectToReturnUrl(returnUrl);
            }

            var paymentLocked = await _db.PaymentIntents.AsNoTracking()
                .AnyAsync(p => p.SessionId == order.SessionId
                            && p.Status == PaymentIntent.StatusPending
                            && p.ExpiresAtUtc > DateTime.UtcNow);
            if (paymentLocked)
            {
                if (tx != null) await tx.RollbackAsync();
                TempData["Error"] = "B├án ─æang chß╗¥ x├íc nhß║¡n thanh to├ín. H├úy ho├án tß║Ñt hoß║╖c hß╗ºy y├¬u cß║ºu thanh to├ín tr╞░ß╗¢c khi duyß╗çt m├│n.";
                return RedirectToReturnUrl(returnUrl);
            }

            // Kiß╗âm tra tß╗ôn kho tr╞░ß╗¢c khi duyß╗çt
            foreach (var detail in order.OrderDetails)
            {
                if (detail.Product.StockQuantity < detail.Quantity)
                {
                    if (tx != null) await tx.RollbackAsync();
                    TempData["Error"] = $"Kh├┤ng thß╗â duyß╗çt: M├│n '{detail.Product.Name}' kh├┤ng ─æß╗º tß╗ôn kho (c├▓n {detail.Product.StockQuantity}, y├¬u cß║ºu {detail.Quantity}).";
                    return RedirectToReturnUrl(returnUrl);
                }
            }

            // Trß╗½ tß╗ôn kho v├á chuyß╗ân trß║íng th├íi Served
            foreach (var detail in order.OrderDetails)
            {
                detail.Product.StockQuantity -= detail.Quantity;
            }
            order.Status = OrderConstants.StatusServed;

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            TempData["Success"] = $"─É├ú duyß╗çt ─æ╞ín #{order.Id} v├á trß╗½ tß╗ôn kho th├ánh c├┤ng.";
            return RedirectToReturnUrl(returnUrl);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lß╗ùi khi duyß╗çt ─æ╞ín {OrderId}", orderId);
            TempData["Error"] = "C├│ lß╗ùi xß║úy ra khi duyß╗çt ─æ╞ín.";
            return RedirectToReturnUrl(returnUrl);
        }
    }

    // POST /Order/CancelOrder (Staff hß╗ºy ─æ╞ín)
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
            TempData["Error"] = "Chß╗ë c├│ thß╗â hß╗ºy ─æ╞ín ─æang chß╗¥ Staff x├íc nhß║¡n.";
            return RedirectToReturnUrl(returnUrl);
        }

        TempData["Success"] = $"─É├ú hß╗ºy ─æ╞ín #{orderId}.";
        return RedirectToReturnUrl(returnUrl);
    }

    // GET /Order (Mß║╖c ─æß╗ïnh cho Customer hoß║╖c Staff)
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

    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
    // HELPERS
    // ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

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

    // ─────────────────────────────────────────────────────────────
    // WF2: DUYỆT ĐƠN QR VÀ THAO TÁC SƠ ĐỒ BÀN (TÙNG)
    // ─────────────────────────────────────────────────────────────

    // UC-23: Thêm món vào phiên tại quầy (từ modal sơ đồ bàn)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCounterOrder([FromBody] CounterOrderRequest model)
    {
        if (model == null || model.SessionId <= 0 || model.Items == null || model.Items.Count == 0)
        {
            return Json(new { success = false, message = "Vui lòng chọn ít nhất 1 món để gọi" });
        }

        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Staff";
        var result = await _orderService.CreateCounterOrderAsync(model.SessionId, model.Items, userId, model.Note);

        return Json(new
        {
            success = result.Success,
            message = result.Message,
            orderId = result.OrderId
        });
    }

    // UC-25: Hủy món có lý do (hoàn kho một lần, ghi nhật ký)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelItem([FromBody] CancelOrderItemRequest model)
    {
        if (model == null || model.OrderDetailId <= 0 || model.CancelQuantity <= 0)
        {
            return Json(new { success = false, message = "Dữ liệu yêu cầu hủy món không hợp lệ" });
        }

        if (string.IsNullOrWhiteSpace(model.Reason))
        {
            return Json(new { success = false, message = "Vui lòng nhập lý do hủy món" });
        }

        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Staff";
        var result = await _orderService.CancelOrderDetailAsync(model.OrderDetailId, model.CancelQuantity, model.Reason.Trim(), userId);

        return Json(new
        {
            success = result.Success,
            message = result.Message
        });
    }

    // Lấy danh sách món trong phiên chơi (JSON API cho Modal bàn chơi)
    [HttpGet]
    public async Task<IActionResult> SessionOrders(int sessionId)
    {
        var orders = await _orderService.GetSessionOrdersAsync(sessionId);
        var totalFandB = orders.Where(o => o.Status == "Completed").Sum(o => o.TotalAmount);

        return Json(new
        {
            success = true,
            totalFandB,
            orders
        });
    }

    // UC-24: Màn hình nhân viên nhận đơn QR Realtime
    public async Task<IActionResult> Pending()
    {
        var pendingOrders = await _orderService.GetPendingOrdersAsync();
        return View(pendingOrders);
    }

    // UC-24: Nhân viên xác nhận đơn QR (trừ kho)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmQr(int orderId)
    {
        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Staff";
        var result = await _orderService.ConfirmQrOrderAsync(orderId, userId);

        if (IsAjaxRequest())
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Pending));
    }

    // UC-24: Nhân viên từ chối đơn QR (kèm lý do)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectQr(int orderId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Vui lòng nhập lý do từ chối đơn hàng" });
            }
            TempData["ErrorMessage"] = "Vui lòng nhập lý do từ chối đơn hàng";
            return RedirectToAction(nameof(Pending));
        }

        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Staff";
        var result = await _orderService.RejectQrOrderAsync(orderId, reason.Trim(), userId);

        if (IsAjaxRequest())
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Pending));
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (Request.Headers["Accept"].ToString().Contains("application/json"));
    }
}

public class CounterOrderRequest
{
    public int SessionId { get; set; }
    public List<OrderItemInputModel> Items { get; set; } = new();
    public string? Note { get; set; }
}

public class CancelOrderItemRequest
{
    public int OrderDetailId { get; set; }
    public int CancelQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}