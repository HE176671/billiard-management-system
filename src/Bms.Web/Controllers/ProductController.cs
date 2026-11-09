using System.Security.Claims;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager,Admin")]
public class ProductController : Controller
{
    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Hệ thống";

    // GET: /Product
    public async Task<IActionResult> Index(string? search, int? categoryId, bool? activeOnly)
    {
        ViewData["Title"] = "Quản lý Thực đơn & Món";
        var products = await _productService.GetAllProductsAsync(activeOnly == true, search, categoryId);
        var categories = await _productService.GetAllCategoriesAsync();

        ViewBag.Categories = categories;
        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.ActiveOnly = activeOnly;

        return View(products);
    }

    // GET: /Product/Create
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Thêm món mới vào thực đơn";
        ViewBag.Categories = await _productService.GetAllCategoriesAsync();
        return View("CreateEdit", new ProductInputModel { IsActive = true });
    }

    // POST: /Product/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Create(ProductInputModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Thêm món mới vào thực đơn";
            ViewBag.Categories = await _productService.GetAllCategoriesAsync();
            return View("CreateEdit", model);
        }

        var res = await _productService.CreateProductAsync(model, CurrentUserId);
        if (!res.Success)
        {
            TempData["Error"] = res.Message;
            ViewBag.Categories = await _productService.GetAllCategoriesAsync();
            return View("CreateEdit", model);
        }

        TempData["Success"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Product/Edit/5
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _productService.GetProductByIdAsync(id);
        if (product == null)
        {
            TempData["Error"] = "Không tìm thấy món cần sửa";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"Chỉnh sửa món: {product.Name}";
        ViewBag.Categories = await _productService.GetAllCategoriesAsync();

        var model = new ProductInputModel
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            IsActive = product.IsActive
        };

        return View("CreateEdit", model);
    }

    // POST: /Product/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Edit(ProductInputModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Chỉnh sửa món";
            ViewBag.Categories = await _productService.GetAllCategoriesAsync();
            return View("CreateEdit", model);
        }

        var res = await _productService.UpdateProductAsync(model, CurrentUserId);
        if (!res.Success)
        {
            TempData["Error"] = res.Message;
            ViewBag.Categories = await _productService.GetAllCategoriesAsync();
            return View("CreateEdit", model);
        }

        TempData["Success"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    // POST: /Product/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var res = await _productService.ToggleProductActiveAsync(id);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Index));
    }

    // GET: /Product/PriceHistory?productId=5 (BR-15)
    public async Task<IActionResult> PriceHistory(int? productId)
    {
        ViewData["Title"] = "Nhật ký Lịch sử thay đổi giá bán";
        var history = await _productService.GetPriceHistoryAsync(productId);
        ViewBag.ProductId = productId;

        if (productId.HasValue && productId.Value > 0)
        {
            var p = await _productService.GetProductByIdAsync(productId.Value);
            ViewBag.ProductName = p?.Name;
        }

        return View(history);
    }

    // GET: /Product/Categories
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Categories()
    {
        ViewData["Title"] = "Quản lý Danh mục món";
        var categories = await _productService.GetAllCategoriesAsync();
        return View(categories);
    }

    // POST: /Product/SaveCategory
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> SaveCategory(int? id, string name)
    {
        var res = await _productService.SaveCategoryAsync(id, name);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Categories));
    }
}
