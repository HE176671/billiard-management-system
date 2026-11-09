using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Staff")]
public class OrderController : Controller
{
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrderController(
        IOrderService orderService,
        IProductService productService,
        UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _productService = productService;
        _userManager = userManager;
    }

    // UC-23: Thêm món vào phiên tại quầy
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
