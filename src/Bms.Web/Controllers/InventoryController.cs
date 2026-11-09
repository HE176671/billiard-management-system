using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Admin,Staff")]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventoryService;
    private readonly IProductService _productService;
    private readonly UserManager<ApplicationUser> _userManager;

    public InventoryController(
        IInventoryService inventoryService,
        IProductService productService,
        UserManager<ApplicationUser> userManager)
    {
        _inventoryService = inventoryService;
        _productService = productService;
        _userManager = userManager;
    }

    // UC-13: Quản lý tồn kho hàng hóa F&B
    public async Task<IActionResult> Index(string? search, int? categoryId)
    {
        var inventory = await _inventoryService.GetInventoryListAsync(search, categoryId);
        var categories = await _productService.GetAllCategoriesAsync();

        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.Categories = categories;

        // Thống kê nhanh
        ViewBag.TotalItems = inventory.Count;
        ViewBag.OutOfStockCount = inventory.Count(p => p.StockQuantity <= 0);
        ViewBag.LowStockCount = inventory.Count(p => p.StockQuantity > 0 && p.StockQuantity <= 5);
        ViewBag.TotalQuantityInStock = inventory.Sum(p => p.StockQuantity);

        return View(inventory);
    }

    // UC-11: Danh sách phiếu nhập kho
    public async Task<IActionResult> Receipts()
    {
        var receipts = await _inventoryService.GetReceiptsAsync();
        return View(receipts);
    }

    // UC-11: Lập phiếu nhập kho (GET)
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> CreateReceipt()
    {
        var products = await _productService.GetAllProductsAsync(activeOnly: true);
        ViewBag.Products = products;

        var model = new StockReceiptInputModel
        {
            ReceiptCode = $"PN-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}"
        };

        return View(model);
    }

    // UC-11: Lập phiếu nhập kho (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> CreateReceipt(StockReceiptInputModel model)
    {
        // Loại bỏ các dòng trống
        model.Items = model.Items?.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList() ?? new();

        if (model.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Vui lòng thêm ít nhất 1 mặt hàng với số lượng lớn hơn 0");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Products = await _productService.GetAllProductsAsync(activeOnly: true);
            return View(model);
        }

        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Staff";
        var result = await _inventoryService.CreateReceiptAsync(model, userId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
            return RedirectToAction(nameof(Receipts));
        }

        ModelState.AddModelError(string.Empty, result.Message);
        ViewBag.Products = await _productService.GetAllProductsAsync(activeOnly: true);
        return View(model);
    }

    // UC-11: Xem chi tiết phiếu nhập kho
    public async Task<IActionResult> ReceiptDetail(int id)
    {
        var receipt = await _inventoryService.GetReceiptDetailAsync(id);
        if (receipt == null)
        {
            TempData["ErrorMessage"] = $"Không tìm thấy phiếu nhập kho #{id}";
            return RedirectToAction(nameof(Receipts));
        }

        return View(receipt);
    }

    // UC-13: Thẻ kho / Sổ cái biến động kho (Stock Movements Ledger)
    public async Task<IActionResult> Ledger(int? productId)
    {
        var movements = await _inventoryService.GetStockMovementsAsync(productId);
        var products = await _productService.GetAllProductsAsync();

        ViewBag.Products = products;
        ViewBag.SelectedProductId = productId;

        return View(movements);
    }

    // UC-13: Điều chỉnh kiểm kê tồn kho (Audit Adjustment)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Adjust(int productId, int newQuantity, string reason, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Vui lòng nhập lý do điều chỉnh tồn kho" });
            }
            TempData["ErrorMessage"] = "Vui lòng nhập lý do điều chỉnh tồn kho";
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        if (newQuantity < 0)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Số lượng tồn sau điều chỉnh không được nhỏ hơn 0" });
            }
            TempData["ErrorMessage"] = "Số lượng tồn sau điều chỉnh không được nhỏ hơn 0";
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        var userId = _userManager.GetUserId(User) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Manager";
        var result = await _inventoryService.AdjustStockAsync(productId, newQuantity, reason.Trim(), userId);

        if (IsAjaxRequest())
        {
            return Json(new { success = result.Success, message = result.Message, newBalance = result.BalanceAfter });
        }

        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (Request.Headers["Accept"].ToString().Contains("application/json"));
    }
}
