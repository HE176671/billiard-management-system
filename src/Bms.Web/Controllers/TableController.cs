using System.Security.Claims;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Admin")]
public class TableController : Controller
{
    private readonly ITableSessionService _tableSessionService;

    public TableController(ITableSessionService tableSessionService)
    {
        _tableSessionService = tableSessionService;
    }

    // GET: /Table
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Quản lý bàn & Phiên chơi";
        var tableCards = await _tableSessionService.GetTableCardsAsync();
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

        string sessionMode = "Open";
        if (!string.IsNullOrWhiteSpace(request.SessionMode))
        {
            var trimmed = request.SessionMode.Trim();
            if (string.Equals(trimmed, "Open", StringComparison.OrdinalIgnoreCase))
            {
                sessionMode = "Open";
            }
            else if (string.Equals(trimmed, "Timed", StringComparison.OrdinalIgnoreCase))
            {
                sessionMode = "Timed";
            }
            else
            {
                sessionMode = trimmed;
            }
        }

        var result = await _tableSessionService.OpenSessionAsync(request.TableId, staffId, sessionMode, request.PlannedMinutes);
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

    // POST: /Table/ExtendSession
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendSession([FromForm] ExtendSessionRequest request)
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

        var result = await _tableSessionService.ExtendSessionAsync(request.SessionId, request.AddMinutes, staffId);
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
}
