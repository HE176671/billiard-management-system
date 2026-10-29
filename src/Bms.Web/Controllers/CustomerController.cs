using System.Security.Claims;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Customer")]
public class CustomerController : Controller
{
    // =========================================================================
    // PHẦN 1: KHỞI TẠO CONTROLLER & DEPENDENCY INJECTION
    // =========================================================================

    private readonly IBookingService _bookingService;

    public CustomerController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    // =========================================================================
    // PHẦN 2: CÁC ACTION [HttpGet] — TRANG ĐẶT BÀN, LỊCH SỬ & KIỂM TRA BÀN TRỐNG
    // =========================================================================

    // GET: /Customer (Trang Đặt bàn trực tuyến riêng biệt)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Đặt bàn trực tuyến";

        var nowVn = BookingPolicy.NowVietnam();
        var defaultStartVn = new DateTime(nowVn.Year, nowVn.Month, nowVn.Day, nowVn.Hour, 0, 0).AddHours(1);
        var minStartVn = new DateTime(nowVn.Year, nowVn.Month, nowVn.Day, nowVn.Hour, nowVn.Minute, 0).AddMinutes(10);
        var maxStartVn = nowVn.AddDays(BookingPolicy.MaxAdvanceDays);

        var defaultStartUtc = BookingPolicy.ToUtcFromVietnamTime(defaultStartVn);
        var availableTables = await _bookingService.GetBookableTablesAsync(defaultStartUtc);

        var model = new CustomerBookingDashboardViewModel
        {
            DefaultStartLocal = defaultStartVn,
            MinStartLocal = minStartVn,
            MaxStartLocal = maxStartVn,
            AvailableTables = availableTables
        };

        return View(model);
    }

    // GET: /Customer/History (Trang Lịch đặt bàn của tôi riêng biệt)
    [HttpGet]
    public async Task<IActionResult> History()
    {
        ViewData["Title"] = "Lịch đặt bàn của tôi";

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(customerId))
        {
            return Challenge();
        }

        var myBookings = await _bookingService.GetCustomerBookingsAsync(customerId);
        return View(myBookings);
    }

    // GET: /Customer/GetBookableTables?startTimeLocal=2026-10-26T19:30
    [HttpGet]
    public async Task<IActionResult> GetBookableTables([FromQuery] DateTime startTimeLocal)
    {
        var startUtc = BookingPolicy.ToUtcFromVietnamTime(startTimeLocal);
        var tables = await _bookingService.GetBookableTablesAsync(startUtc);
        return Json(new { success = true, tables });
    }

    // =========================================================================
    // PHẦN 3: CÁC ACTION [HttpPost] — TẠO ĐẶT BÀN MỚI & HỦY ĐẶT BÀN CỦA KHÁCH
    // =========================================================================

    // POST: /Customer/CreateBooking
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBooking([FromForm] CustomerCreateBookingRequest request)
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(customerId))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid)
        {
            TempData["BookingError"] = "Vui lòng chọn bàn và thời gian đến hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var startUtc = BookingPolicy.ToUtcFromVietnamTime(request.StartTimeLocal);
        var result = await _bookingService.CreateBookingAsync(customerId, request.TableId, startUtc, request.Notes);

        if (result.Success)
        {
            TempData["BookingSuccess"] = result.Message ?? "Đặt bàn thành công!";
            return RedirectToAction(nameof(History));
        }

        TempData["BookingError"] = result.Message ?? "Không thể đặt bàn. Vui lòng thử lại.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Customer/CancelBooking
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking([FromForm] CancelBookingRequest request)
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(customerId))
        {
            return Unauthorized();
        }

        var result = await _bookingService.CancelBookingAsync(request.BookingId, customerId, request.Reason);
        if (result.Success)
        {
            TempData["BookingSuccess"] = result.Message ?? "Đã hủy lịch đặt bàn.";
        }
        else
        {
            TempData["BookingError"] = result.Message ?? "Không thể hủy lịch đặt bàn.";
        }

        return RedirectToAction(nameof(History));
    }
}
