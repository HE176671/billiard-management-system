using System.Security.Claims;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Admin")]
public class BookingController : Controller
{
    private readonly IBookingService _bookingService;

    public BookingController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    // GET: /Booking
    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, string? status, DateTime? date)
    {
        ViewData["Title"] = "Quản lý Đặt bàn & Check-in";
        var model = await _bookingService.SearchBookingsForStaffAsync(keyword, status, date);
        return View(model);
    }

    // POST: /Booking/CheckIn
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int bookingId, bool allowEarly = false, string? keyword = null, string? status = null, DateTime? date = null)
    {
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "staff";
        var result = await _bookingService.CheckInBookingAsync(bookingId, staffId, allowEarly);

        if (result.Success)
        {
            TempData["StaffBookingSuccess"] = result.Message;
        }
        else
        {
            TempData["StaffBookingError"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { keyword, status, date = date?.ToString("yyyy-MM-dd") });
    }

    // POST: /Booking/AssignTable
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTable(int bookingId, int selectedTableId, string? keyword = null, string? status = null, DateTime? date = null)
    {
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "staff";
        var result = await _bookingService.AssignTableAndOpenSessionAsync(bookingId, selectedTableId, staffId);

        if (result.Success)
        {
            TempData["StaffBookingSuccess"] = result.Message;
        }
        else
        {
            TempData["StaffBookingError"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { keyword, status, date = date?.ToString("yyyy-MM-dd") });
    }

    // POST: /Booking/Cancel
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int bookingId, string? reason = null, string? keyword = null, string? status = null, DateTime? date = null)
    {
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "staff";
        var result = await _bookingService.CancelBookingAsync(bookingId, staffId, string.IsNullOrWhiteSpace(reason) ? "Nhân viên hủy tại quầy" : reason);

        if (result.Success)
        {
            TempData["StaffBookingSuccess"] = result.Message;
        }
        else
        {
            TempData["StaffBookingError"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { keyword, status, date = date?.ToString("yyyy-MM-dd") });
    }
}
