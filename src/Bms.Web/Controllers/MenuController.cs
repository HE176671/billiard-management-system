using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[AllowAnonymous]
public class MenuController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IOrderService _orderService;
    private readonly ILogger<MenuController> _logger;

    public MenuController(
        ApplicationDbContext db,
        IOrderService orderService,
        ILogger<MenuController> logger)
    {
        _db = db;
        _orderService = orderService;
        _logger = logger;
    }

    // UC-31: Trang thực đơn QR cho khách quét tại bàn (Không cần đăng nhập)
    [HttpGet]
    [Route("Menu")]
    [Route("Menu/Index")]
    [Route("Menu/Table/{tableCode}")]
    public async Task<IActionResult> Index([FromQuery] string? tableCode, [FromQuery] int? tableId)
    {
        var query = _db.BilliardTables.AsNoTracking().Include(t => t.TableType).AsQueryable();
        BilliardTable? table = null;

        if (!string.IsNullOrWhiteSpace(tableCode))
        {
            table = await query.FirstOrDefaultAsync(t => t.TableCode == tableCode.Trim());
        }
        else if (tableId.HasValue && tableId.Value > 0)
        {
            table = await query.FirstOrDefaultAsync(t => t.Id == tableId.Value);
        }

        if (table == null)
        {
            return View("TableNotFound", new QrMenuViewModel
            {
                ErrorMessage = "Không tìm thấy thông tin bàn. Vui lòng quét lại mã QR tại bàn của bạn!"
            });
        }

        var vm = new QrMenuViewModel
        {
            TableId = table.Id,
            TableCode = table.TableCode,
            TableTypeName = table.TableType?.Name ?? "Tiêu chuẩn",
            TableHourlyRate = table.TableType?.HourlyRate ?? 0m
        };

        // Tìm phiên đang mở (Active) của bàn
        var activeSession = await _db.PlaySessions.AsNoTracking()
            .Where(s => s.TableId == table.Id && s.Status == "Active")
            .OrderByDescending(s => s.StartAtUtc)
            .FirstOrDefaultAsync();

        if (activeSession == null)
        {
            vm.HasActiveSession = false;
            return View(vm);
        }

        vm.HasActiveSession = true;
        vm.SessionId = activeSession.Id;
        vm.StartAtUtc = activeSession.StartAtUtc;
        vm.StartAtVnDisplay = activeSession.StartAtUtc.AddHours(7).ToString("HH:mm - dd/MM/yyyy");

        var duration = DateTime.UtcNow - activeSession.StartAtUtc;
        vm.PlayedMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));

        // Nhúng phần tạm tính của WF3: Giờ chơi + Combo
        try
        {
            var playtime = await _db.Database
                .SqlQuery<decimal?>($"SELECT dbo.fn_CalcPlaytimeAmount({activeSession.Id}, CAST(SYSUTCDATETIME() AS datetime2(0))) AS [Value]")
                .FirstOrDefaultAsync();
            vm.PlaytimeAmount = playtime ?? 0m;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi tính tạm tính giờ chơi cho phiên #{SessionId}", activeSession.Id);
            vm.PlaytimeAmount = 0m;
        }

        // Tên gói Combo nếu có
        var activeCombo = await _db.SessionCombos.AsNoTracking()
            .Include(sc => sc.Combo)
            .Where(sc => sc.SessionId == activeSession.Id)
            .FirstOrDefaultAsync();
        if (activeCombo?.Combo != null)
        {
            vm.ComboName = activeCombo.Combo.Name;
        }

        // Đơn F&B đã gọi của phiên này
        var sessionOrders = await _orderService.GetSessionOrdersAsync(activeSession.Id);
        vm.SessionOrders = sessionOrders;
        vm.FandBAmount = sessionOrders.Where(o => o.Status == "Completed").Sum(o => o.TotalAmount);

        // Danh mục và Món ăn / Đồ uống (Món hết hàng tự ẩn: StockQuantity > 0)
        var categories = await _db.ProductCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0)
            .OrderBy(p => p.Name)
            .ToListAsync();

        foreach (var cat in categories)
        {
            var catProds = products.Where(p => p.CategoryId == cat.Id).Select(p => new QrProductItem
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity
            }).ToList();

            if (catProds.Any())
            {
                vm.Categories.Add(new QrCategoryItem
                {
                    Id = cat.Id,
                    Name = cat.Name,
                    Products = catProds
                });
            }
        }

        var otherProds = products.Where(p => !categories.Any(c => c.Id == p.CategoryId)).Select(p => new QrProductItem
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            StockQuantity = p.StockQuantity
        }).ToList();

        if (otherProds.Any())
        {
            vm.Categories.Add(new QrCategoryItem
            {
                Id = 0,
                Name = "Khác",
                Products = otherProds
            });
        }

        return View(vm);
    }

    // UC-31: Khách gửi đơn gọi món qua mã QR
    [HttpPost]
    [Route("Menu/PlaceOrder")]
    public async Task<IActionResult> PlaceOrder([FromBody] QrOrderRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TableCode) || request.Items == null || request.Items.Count == 0)
        {
            return Json(new { success = false, message = "Vui lòng chọn ít nhất 1 món để đặt" });
        }

        var result = await _orderService.CreateQrOrderAsync(request);
        return Json(new
        {
            success = result.Success,
            message = result.Message,
            orderId = result.OrderId
        });
    }

    // API lấy trạng thái realtime và tạm tính mới nhất cho giao diện QR
    [HttpGet]
    [Route("Menu/Status")]
    public async Task<IActionResult> Status([FromQuery] string tableCode, [FromQuery] int sessionId)
    {
        var session = await _db.PlaySessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null || session.Status != "Active")
        {
            return Json(new { active = false, message = "Phiên chơi của bàn đã kết thúc hoặc chuyển bàn mới" });
        }

        decimal playtime = 0m;
        try
        {
            playtime = await _db.Database
                .SqlQuery<decimal?>($"SELECT dbo.fn_CalcPlaytimeAmount({sessionId}, CAST(SYSUTCDATETIME() AS datetime2(0))) AS [Value]")
                .FirstOrDefaultAsync() ?? 0m;
        }
        catch { }

        var orders = await _orderService.GetSessionOrdersAsync(sessionId);
        var fnb = orders.Where(o => o.Status == "Completed").Sum(o => o.TotalAmount);

        return Json(new
        {
            active = true,
            playtimeAmount = playtime,
            fnbAmount = fnb,
            grandTotal = playtime + fnb,
            orders = orders.Select(o => new
            {
                id = o.Id,
                status = o.Status,
                statusDisplay = o.Status == "Completed" ? "Đã phục vụ" : (o.Status == "Rejected" ? "Bị từ chối" : "Đang chờ duyệt"),
                totalAmount = o.TotalAmount,
                rejectReason = o.RejectReason,
                details = o.Details.Select(d => new
                {
                    productName = d.ProductName,
                    quantity = d.Quantity,
                    unitPrice = d.UnitPrice,
                    subtotal = d.Subtotal
                })
            })
        });
    }
}
