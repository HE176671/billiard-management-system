using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager")]
public class ComboController : Controller
{
    private readonly IComboService _comboService;
    private readonly IProductService _productService;
    private readonly ApplicationDbContext _db;

    public ComboController(IComboService comboService, IProductService productService, ApplicationDbContext db)
    {
        _comboService = comboService;
        _productService = productService;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Hệ thống";

    // GET: /Combo
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Quản lý Gói Combo";
        var combos = await _comboService.GetAllCombosAsync();
        return View(combos);
    }

    // GET: /Combo/Create
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Tạo gói Combo mới";
        await PrepareViewBagAsync();
        return View("CreateEdit", new ComboInputModel { IsActive = true, PlaytimeHours = 2.0m });
    }

    // POST: /Combo/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Create(ComboInputModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Tạo gói Combo mới";
            await PrepareViewBagAsync();
            return View("CreateEdit", model);
        }

        var res = await _comboService.SaveComboAsync(model, CurrentUserId);
        if (!res.Success)
        {
            TempData["Error"] = res.Message;
            ViewData["Title"] = "Tạo gói Combo mới";
            await PrepareViewBagAsync();
            return View("CreateEdit", model);
        }

        TempData["Success"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Combo/Edit/5
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Edit(int id)
    {
        var combo = await _comboService.GetComboByIdAsync(id);
        if (combo == null)
        {
            TempData["Error"] = "Không tìm thấy Combo";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"Chỉnh sửa Combo: {combo.Name}";
        await PrepareViewBagAsync();

        var model = new ComboInputModel
        {
            Id = combo.Id,
            Name = combo.Name,
            Price = combo.Price,
            PlaytimeHours = combo.PlaytimeHours,
            TableTypeId = combo.TableTypeId,
            IsActive = combo.IsActive,
            Items = combo.Items.Select(i => new ComboItemInputModel
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity
            }).ToList()
        };

        return View("CreateEdit", model);
    }

    // POST: /Combo/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Edit(ComboInputModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Chỉnh sửa Combo";
            await PrepareViewBagAsync();
            return View("CreateEdit", model);
        }

        var res = await _comboService.SaveComboAsync(model, CurrentUserId);
        if (!res.Success)
        {
            TempData["Error"] = res.Message;
            ViewData["Title"] = "Chỉnh sửa Combo";
            await PrepareViewBagAsync();
            return View("CreateEdit", model);
        }

        TempData["Success"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    // POST: /Combo/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var res = await _comboService.ToggleComboActiveAsync(id);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Index));
    }

    private async Task PrepareViewBagAsync()
    {
        ViewBag.TableTypes = await _db.TableTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        ViewBag.Products = await _productService.GetAllProductsAsync(activeOnly: true);
    }
}
