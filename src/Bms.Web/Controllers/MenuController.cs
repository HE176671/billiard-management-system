using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class MenuController : Controller
{
    private readonly IMenuService _menuService;
    private const int PageSize = 10;

    public MenuController(IMenuService menuService)
    {
        _menuService = menuService;
    }

    // GET /Menu
    public async Task<IActionResult> Index(string? search, int? categoryId, string? status, int page = 1)
    {
        var categories = await _menuService.GetCategoriesAsync();
        var (products, totalCount) = await _menuService.GetProductsPagedAsync(
            search, categoryId, status, page, PageSize);

        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var allActiveProducts = await _menuService.GetActiveProductsAsync();

        var vm = new MenuIndexViewModel
        {
            Categories = categories.Select(c => new CategoryItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                ProductCount = c.Products.Count
            }).ToList(),
            Products = products.Select(p => new ProductRowViewModel
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category?.Name ?? "—",
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity,
                IsActive = p.IsActive,
                RowVersionBase64 = Convert.ToBase64String(p.RowVersion)
            }).ToList(),
            Search = search,
            CategoryId = categoryId,
            StatusFilter = status,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            ActiveCount = allActiveProducts.Count,
            OutOfStockCount = allActiveProducts.Count(p => p.StockQuantity == 0)
        };

        return View(vm);
    }

    // POST /Menu/CreateCategory
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CreateCategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Tên danh mục không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _menuService.CreateCategoryAsync(model.Name);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Đã tạo danh mục '{result.Data!.Name}' thành công.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Menu/EditCategory
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(EditCategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu danh mục không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _menuService.UpdateCategoryAsync(model.Id, model.Name);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Đã cập nhật danh mục thành '{result.Data!.Name}'.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Menu/Create
    public async Task<IActionResult> Create()
    {
        var categories = await _menuService.GetCategoriesAsync();
        var vm = new CreateProductViewModel
        {
            AvailableCategories = categories.Select(c => new CategoryItemViewModel
            {
                Id = c.Id,
                Name = c.Name
            }).ToList()
        };

        if (vm.AvailableCategories.Any())
        {
            vm.CategoryId = vm.AvailableCategories.First().Id;
        }

        return View(vm);
    }

    // POST /Menu/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(model);
            return View(model);
        }

        var result = await _menuService.CreateProductAsync(
            model.CategoryId,
            model.Name,
            model.Price,
            model.StockQuantity);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể tạo sản phẩm.");
            await PopulateCategoriesAsync(model);
            return View(model);
        }

        TempData["Success"] = $"Đã thêm món '{result.Data!.Name}' vào thực đơn thành công.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Menu/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _menuService.GetProductByIdAsync(id);
        if (product == null)
        {
            TempData["Error"] = "Không tìm thấy sản phẩm cần chỉnh sửa.";
            return RedirectToAction(nameof(Index));
        }

        var categories = await _menuService.GetCategoriesAsync();
        var vm = new EditProductViewModel
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Price = product.Price,
            CurrentStockQuantity = product.StockQuantity,
            IsActive = product.IsActive,
            RowVersionBase64 = Convert.ToBase64String(product.RowVersion),
            AvailableCategories = categories.Select(c => new CategoryItemViewModel
            {
                Id = c.Id,
                Name = c.Name
            }).ToList()
        };

        return View(vm);
    }

    // POST /Menu/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditProductViewModel model)
    {
        if (id != model.Id)
            return BadRequest();

        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(model);
            return View(model);
        }

        var result = await _menuService.UpdateProductAsync(
            model.Id,
            model.CategoryId,
            model.Name,
            model.Price,
            model.RowVersionBase64);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật sản phẩm.");
            // Nếu xung đột Concurrency, nạp lại thông tin mới nhất từ DB để người dùng thấy giá trị hiện tại
            if (result.IsConcurrencyConflict)
            {
                var latest = await _menuService.GetProductByIdAsync(model.Id);
                if (latest != null)
                {
                    ModelState.Remove(nameof(EditProductViewModel.RowVersionBase64));
                    model.RowVersionBase64 = Convert.ToBase64String(latest.RowVersion);
                    model.CurrentStockQuantity = latest.StockQuantity;
                    model.IsActive = latest.IsActive;
                }
            }
            await PopulateCategoriesAsync(model);
            return View(model);
        }

        TempData["Success"] = $"Đã cập nhật thông tin món '{result.Data!.Name}' thành công.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCategoriesAsync(CreateProductViewModel model)
    {
        var categories = await _menuService.GetCategoriesAsync();
        model.AvailableCategories = categories.Select(c => new CategoryItemViewModel
        {
            Id = c.Id,
            Name = c.Name
        }).ToList();
    }

    private async Task PopulateCategoriesAsync(EditProductViewModel model)
    {
        var categories = await _menuService.GetCategoriesAsync();
        model.AvailableCategories = categories.Select(c => new CategoryItemViewModel
        {
            Id = c.Id,
            Name = c.Name
        }).ToList();
    }
}
