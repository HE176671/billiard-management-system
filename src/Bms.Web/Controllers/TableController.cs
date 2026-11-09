using System.Security.Claims;
using Bms.Web.Filters;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Manager")]
[BroadcastTableChange] // TV5: thao tác bàn thành công thì đẩy realtime cho các màn sơ đồ bàn
public class TableController : Controller
{
    // =========================================================================
    // PHẦN 1: KHỞI TẠO CONTROLLER & DEPENDENCY INJECTION
    // =========================================================================

    private readonly ITableSessionService _tableSessionService;
    private readonly IProductService _productService;

    public TableController(ITableSessionService tableSessionService, IProductService productService)
    {
        _tableSessionService = tableSessionService;
        _productService = productService;
    }

    // =========================================================================
    // PHẦN 2: CÁC ACTION [HttpGet] — HIỂN THỊ TRANG, LƯỚI BÀN, CHI TIẾT, COMBO & KHÁCH HÀNG
    // =========================================================================

    // GET: /Table
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Quản lý bàn & Phiên chơi";
        var tableCards = await _tableSessionService.GetTableCardsAsync();
        ViewBag.AvailableProducts = await _productService.GetAllProductsAsync(activeOnly: true);
        var model = new TableManagementViewModel
        {
            Tables = tableCards,
            ServerTimeUtc = DateTime.UtcNow
        };
        return View(model);
    }

    // GET: /Table/GetTableCardsPartial
    [HttpGet]
    public async Task<IActionResult> GetTableCardsPartial()
    {
        var tableCards = await _tableSessionService.GetTableCardsAsync();
        return PartialView("_TableGridPartial", tableCards);
    }

    // GET: /Table/GetTableDetail?tableId=1
    [HttpGet]
    public async Task<IActionResult> GetTableDetail([FromQuery] int tableId)
    {
        var detail = await _tableSessionService.GetTableDetailAsync(tableId);
        if (detail == null)
        {
            return NotFound();
        }

        return Json(detail);
    }

    // GET: /Table/GetActiveCombos
    [HttpGet]
    public async Task<IActionResult> GetActiveCombos()
    {
        var combos = await _tableSessionService.GetActiveCombosAsync();
        return Json(TableOperationResult.Ok(combos));
    }

    // GET: /Table/SearchCustomers?keyword=090
    [HttpGet]
    public async Task<IActionResult> SearchCustomers([FromQuery] string? keyword)
    {
        var customers = await _tableSessionService.SearchCustomersAsync(keyword);
        return Json(TableOperationResult.Ok(customers));
    }

    // GET: /Table/GetCloseSummary?sessionId=1
    [HttpGet]
    public async Task<IActionResult> GetCloseSummary([FromQuery] int sessionId)
    {
        if (sessionId <= 0 || !ModelState.IsValid)
        {
            return Json(TableOperationResult.Fail(null, "Dữ liệu gửi lên không hợp lệ.", false));
        }

        var summary = await _tableSessionService.GetCloseSummaryAsync(sessionId);
        if (summary == null)
        {
            return Json(TableOperationResult.Fail(51502, "Phiên chơi không còn ở trạng thái Hoạt động (có thể đã được nhân viên khác đóng).", true));
        }

        return Json(TableOperationResult.Ok(summary));
    }

    // =========================================================================
    // PHẦN 3: CÁC ACTION [HttpPost] — MỞ BÀN, ĐÓNG BÀN, CHUYỂN BÀN & CHỌN COMBO
    // =========================================================================

    // POST: /Table/OpenSession
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenSession([FromForm] OpenSessionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return Json(TableOperationResult.Fail(null, "Dữ liệu gửi lên không hợp lệ.", false));
        }

        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(staffId))
        {
            return Unauthorized();
        }

        bool isComboMode = string.Equals(request.BillingMode, "combo", StringComparison.OrdinalIgnoreCase);
        if (isComboMode && (!request.ComboId.HasValue || request.ComboId.Value <= 0))
        {
            return Json(TableOperationResult.Fail(null, "Vui lòng chọn gói Combo khi mở bàn ở chế độ Combo.", false));
        }

        int? selectedComboId = isComboMode ? request.ComboId : null;

        var result = await _tableSessionService.OpenSessionAsync(request.TableId, staffId, null, selectedComboId);
        return Json(result);
    }

    // POST: /Table/CloseSession
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSession([FromForm] CloseSessionRequest request)
    {
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(staffId))
        {
            return Unauthorized();
        }

        var result = await _tableSessionService.CloseSessionAsync(request.SessionId, staffId);
        return Json(result);
    }

    // POST: /Table/TransferSession
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferSession([FromForm] TransferSessionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return Json(TableOperationResult.Fail(null, "Dữ liệu gửi lên không hợp lệ.", false));
        }

        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(staffId))
        {
            return Unauthorized();
        }

        var result = await _tableSessionService.TransferSessionAsync(request.SessionId, request.NewTableId, staffId);
        return Json(result);
    }

    // POST: /Table/AddSessionCombo
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddSessionCombo([FromForm] AddSessionComboRequest request)
    {
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(staffId))
        {
            return Task.FromResult<IActionResult>(Unauthorized());
        }

        return Task.FromResult<IActionResult>(Json(TableOperationResult.Fail(
            51715,
            "Chế độ Combo chỉ được chọn ngay lúc mở bàn (mỗi phiên chỉ áp dụng 1 gói Combo).",
            true)));
    }
}
