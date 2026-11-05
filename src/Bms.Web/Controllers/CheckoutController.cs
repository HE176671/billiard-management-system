using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff,Admin")]
public class CheckoutController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IVnPayService _vnPayService;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IVnPayService vnPayService,
        ILogger<CheckoutController> logger)
    {
        _db = db;
        _userManager = userManager;
        _vnPayService = vnPayService;
        _logger = logger;
    }

    // GET /Checkout/Session/{sessionId}
    [HttpGet]
    public async Task<IActionResult> Session(int sessionId)
    {
        var session = await _db.PlaySessions
            .Include(s => s.Table).ThenInclude(t => t.TableType)
            .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return NotFound();

        var vm = await BuildCheckoutViewModelAsync(session);
        ViewData["Title"] = $"Thanh toán - Bàn {session.Table.TableCode}";
        return View(vm);
    }

    // POST /Checkout/PayCash (Thanh toán tiền mặt)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayCash(int sessionId)
    {
        var session = await _db.PlaySessions
            .Include(s => s.Table).ThenInclude(t => t.TableType)
            .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return NotFound();

        var staffId = _userManager.GetUserId(User) 
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
            ?? session.OpenedById;

        try
        {
            // 1. Đóng phiên nếu phiên vẫn đang Active (Stored proc tự quản lý transaction của nó)
            if (session.Status == "Active")
            {
                await CloseSessionViaStoredProcAsync(session.Id, staffId);
                
                // Re-query lại phiên sau khi stored proc cập nhật
                session = await _db.PlaySessions
                    .Include(s => s.Table).ThenInclude(t => t.TableType)
                    .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null) return NotFound();
            }

            // 2. Tính toán tiền giờ, F&B, ưu đãi
            var vm = await BuildCheckoutViewModelAsync(session);

            using var tx = await _db.Database.BeginTransactionAsync();

            // 3. Tạo hoặc lấy Invoice
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.SessionId == session.Id && i.Status != "Cancelled");

            if (invoice == null)
            {
                invoice = new Invoice
                {
                    SessionId = session.Id,
                    CustomerId = session.CustomerId,
                    TotalPlaytime = vm.TotalPlaytime,
                    TotalFandB = vm.TotalFandB,
                    DiscountAmount = vm.DiscountAmount,
                    Status = "Paid",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.Invoices.Add(invoice);
                await _db.SaveChangesAsync();
            }
            else
            {
                invoice.TotalPlaytime = vm.TotalPlaytime;
                invoice.TotalFandB = vm.TotalFandB;
                invoice.DiscountAmount = vm.DiscountAmount;
                invoice.Status = "Paid";
                await _db.SaveChangesAsync();
            }

            // 4. Tạo giao dịch PaymentTransaction
            var pt = new PaymentTransaction
            {
                InvoiceId = invoice.Id,
                PaymentMethod = "Cash",
                AmountPaid = vm.FinalAmount,
                GatewayRefCode = null,
                TransactionTimeUtc = DateTime.UtcNow
            };
            _db.PaymentTransactions.Add(pt);

            // 5. Giải phóng bàn -> Available
            var table = await _db.BilliardTables.FindAsync(session.TableId);
            if (table != null)
            {
                table.Status = "Available";
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = $"Thanh toán tiền mặt thành công {vm.FinalAmount:#,##0 ₫} cho bàn {session.Table.TableCode}.";
            return RedirectToAction(nameof(InvoiceReceipt), new { id = invoice.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi thanh toán tiền mặt cho phiên {SessionId}", sessionId);
            TempData["Error"] = "Có lỗi xảy ra khi xử lý thanh toán: " + ex.Message;
            return RedirectToAction(nameof(Session), new { sessionId });
        }
    }

    // POST /Checkout/PayVNPay (Bắt đầu thanh toán VNPay)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayVNPay(int sessionId)
    {
        var session = await _db.PlaySessions
            .Include(s => s.Table).ThenInclude(t => t.TableType)
            .Include(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return NotFound();

        try
        {
            // Tạm chốt thông tin hóa đơn
            var vm = await BuildCheckoutViewModelAsync(session);

            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.SessionId == session.Id && i.Status != "Cancelled");

            if (invoice == null)
            {
                invoice = new Invoice
                {
                    SessionId = session.Id,
                    CustomerId = session.CustomerId,
                    TotalPlaytime = vm.TotalPlaytime,
                    TotalFandB = vm.TotalFandB,
                    DiscountAmount = vm.DiscountAmount,
                    Status = "Unpaid",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.Invoices.Add(invoice);
                await _db.SaveChangesAsync();
            }
            else
            {
                invoice.TotalPlaytime = vm.TotalPlaytime;
                invoice.TotalFandB = vm.TotalFandB;
                invoice.DiscountAmount = vm.DiscountAmount;
                await _db.SaveChangesAsync();
            }

            var paymentUrl = _vnPayService.CreatePaymentUrl(
                HttpContext,
                invoice.Id,
                vm.FinalAmount,
                $"Thanh toan ban {session.Table.TableCode} - HD #{invoice.Id}"
            );

            return Redirect(paymentUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo URL VNPay cho phiên {SessionId}", sessionId);
            TempData["Error"] = "Có lỗi xảy ra khi kết nối VNPay: " + ex.Message;
            return RedirectToAction(nameof(Session), new { sessionId });
        }
    }

    // GET /Checkout/VNPayReturn (Callback từ VNPay sau khi khách quét mã / chuyển khoản)
    [HttpGet]
    public async Task<IActionResult> VNPayReturn()
    {
        var response = _vnPayService.ValidatePayment(Request.Query);

        if (!int.TryParse(response.OrderId, out var invoiceId))
        {
            TempData["Error"] = "Mã hóa đơn phản hồi từ VNPay không hợp lệ.";
            return RedirectToAction("Index", "Home");
        }

        var invoice = await _db.Invoices
            .Include(i => i.Session).ThenInclude(s => s.Table)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
        {
            TempData["Error"] = "Không tìm thấy hóa đơn.";
            return RedirectToAction("Index", "Home");
        }

        if (response.IsSuccess)
        {
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var staffId = _userManager.GetUserId(User) ?? invoice.Session.OpenedById ?? "demo-staff-01";

                // 1. Đóng phiên nếu còn Active
                if (invoice.Session.Status == "Active")
                {
                    await CloseSessionViaStoredProcAsync(invoice.SessionId, staffId);
                    await _db.Entry(invoice.Session).ReloadAsync();
                }

                // 2. Cập nhật hóa đơn
                invoice.Status = "Paid";

                // 3. Lưu giao dịch VNPay
                var pt = new PaymentTransaction
                {
                    InvoiceId = invoice.Id,
                    PaymentMethod = "VNPay",
                    AmountPaid = response.Amount,
                    GatewayRefCode = response.TransactionId,
                    TransactionTimeUtc = DateTime.UtcNow
                };
                _db.PaymentTransactions.Add(pt);

                // 4. Giải phóng bàn
                var table = await _db.BilliardTables.FindAsync(invoice.Session.TableId);
                if (table != null)
                {
                    table.Status = "Available";
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Success"] = $"Thanh toán VNPay thành công! Mã GD: {response.TransactionId}.";
                return RedirectToAction(nameof(InvoiceReceipt), new { id = invoice.Id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Lỗi cập nhật giao dịch VNPay cho hóa đơn #{InvoiceId}", invoiceId);
                TempData["Error"] = "Thanh toán thành công trên cổng nhưng lỗi khi ghi nhận DB: " + ex.Message;
                return RedirectToAction(nameof(Session), new { sessionId = invoice.SessionId });
            }
        }
        else
        {
            TempData["Error"] = "Thanh toán VNPay không thành công: " + response.Message;
            return RedirectToAction(nameof(Session), new { sessionId = invoice.SessionId });
        }
    }

    // GET /Checkout/Invoices (Tra cứu & Đối soát hóa đơn)
    [HttpGet]
    public async Task<IActionResult> Invoices(string? search, string? status, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        const int pageSize = 15;

        var query = _db.Invoices
            .Include(i => i.Session).ThenInclude(s => s.Table)
            .Include(i => i.PaymentTransactions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(i => i.Session.Table.TableCode.Contains(search) 
                                  || i.Id.ToString() == search
                                  || (i.CustomerId != null && i.CustomerId.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(i => i.Status == status);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(i => i.CreatedAtUtc >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(i => i.CreatedAtUtc <= endOfDay);
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InvoiceItemViewModel
            {
                Id = i.Id,
                SessionId = i.SessionId,
                TableCode = i.TotalPlaytime == 0 ? "Tại quầy" : i.Session.Table.TableCode,
                TotalPlaytime = i.TotalPlaytime,
                TotalFandB = i.TotalFandB,
                DiscountAmount = i.DiscountAmount,
                FinalAmount = (i.TotalPlaytime + i.TotalFandB) - i.DiscountAmount,
                Status = i.Status,
                CreatedAtUtc = i.CreatedAtUtc,
                PaymentMethod = i.PaymentTransactions.OrderByDescending(pt => pt.TransactionTimeUtc).Select(pt => pt.PaymentMethod).FirstOrDefault(),
                GatewayRefCode = i.PaymentTransactions.OrderByDescending(pt => pt.TransactionTimeUtc).Select(pt => pt.GatewayRefCode).FirstOrDefault()
            })
            .ToListAsync();

        var vm = new InvoiceListViewModel
        {
            Invoices = items,
            SearchKeyword = search,
            StatusFilter = status,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total
        };

        ViewData["Title"] = "Tra cứu & Đối soát hóa đơn";
        return View(vm);
    }

    // GET /Checkout/InvoiceReceipt/{id} (Biên lai chi tiết)
    [HttpGet]
    public async Task<IActionResult> InvoiceReceipt(int id)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Session).ThenInclude(s => s.Table)
            .Include(i => i.Session).ThenInclude(s => s.Orders).ThenInclude(o => o.OrderDetails).ThenInclude(d => d.Product)
            .Include(i => i.PaymentTransactions)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();

        ApplicationUser? customer = null;
        if (!string.IsNullOrEmpty(invoice.CustomerId))
        {
            customer = await _userManager.FindByIdAsync(invoice.CustomerId);
        }

        var duration = (invoice.Session.EndAtUtc ?? DateTime.UtcNow) - invoice.Session.StartAtUtc;

        var fandbItems = invoice.Session.Orders
            .Where(o => o.Status == OrderConstants.StatusServed)
            .SelectMany(o => o.OrderDetails)
            .Select(d => new CheckoutFandBItemViewModel
            {
                ProductId = d.ProductId,
                ProductName = d.Product.Name,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                OrderStatus = "Served"
            })
            .ToList();

        var transactions = invoice.PaymentTransactions
            .OrderByDescending(pt => pt.TransactionTimeUtc)
            .Select(pt => new PaymentTransactionViewModel
            {
                Id = pt.Id,
                PaymentMethod = pt.PaymentMethod,
                AmountPaid = pt.AmountPaid,
                GatewayRefCode = pt.GatewayRefCode,
                TransactionTimeUtc = pt.TransactionTimeUtc
            })
            .ToList();

        var vm = new InvoiceDetailViewModel
        {
            Id = invoice.Id,
            SessionId = invoice.SessionId,
            TableCode = invoice.TotalPlaytime == 0 ? "Tại quầy (Mang về)" : invoice.Session.Table.TableCode,
            CustomerName = customer?.FullName ?? "Khách vãng lai",
            CustomerPhone = customer?.PhoneNumber ?? "—",
            StartAtUtc = invoice.Session.StartAtUtc,
            EndAtUtc = invoice.Session.EndAtUtc,
            Duration = duration,
            HourlyRate = invoice.Session.HourlyRateSnapshot,
            TotalPlaytime = invoice.TotalPlaytime,
            TotalFandB = invoice.TotalFandB,
            DiscountAmount = invoice.DiscountAmount,
            FinalAmount = (invoice.TotalPlaytime + invoice.TotalFandB) - invoice.DiscountAmount,
            Status = invoice.Status,
            CreatedAtUtc = invoice.CreatedAtUtc,
            FandBItems = fandbItems,
            Transactions = transactions
        };

        ViewData["Title"] = $"Hóa đơn #{invoice.Id}";
        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private async Task<CheckoutSessionViewModel> BuildCheckoutViewModelAsync(PlaySession session)
    {
        var now = DateTime.UtcNow;
        var endAt = session.EndAtUtc ?? now;
        var duration = endAt > session.StartAtUtc ? (endAt - session.StartAtUtc) : TimeSpan.Zero;

        // Tiền giờ
        decimal totalPlaytime;
        if (session.Status == "Closed" && session.PlaytimeAmount.HasValue)
        {
            totalPlaytime = session.PlaytimeAmount.Value;
        }
        else
        {
            totalPlaytime = Math.Round((decimal)duration.TotalSeconds * session.HourlyRateSnapshot / 3600m, 0);
        }

        // Món F&B (lấy các món có status Served)
        var fandbItems = session.Orders
            .Where(o => o.Status == OrderConstants.StatusServed)
            .SelectMany(o => o.OrderDetails)
            .Select(d => new CheckoutFandBItemViewModel
            {
                ProductId = d.ProductId,
                ProductName = d.Product?.Name ?? $"Món #{d.ProductId}",
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                OrderStatus = "Served"
            })
            .ToList();

        var totalFandB = fandbItems.Sum(x => x.Subtotal);

        // Khách hàng & Hạng thành viên
        string tierName = "Đồng";
        decimal discountPercent = 0m;
        string? customerName = null;
        string? customerPhone = null;

        if (!string.IsNullOrEmpty(session.CustomerId))
        {
            var customer = await _userManager.FindByIdAsync(session.CustomerId);
            if (customer != null)
            {
                customerName = customer.FullName;
                customerPhone = customer.PhoneNumber;

                // Tính tổng điểm từ các phiên Closed trước đó để suy ra hạng
                var totalPoints = await _db.PlaySessions
                    .Where(s => s.CustomerId == session.CustomerId && s.Status == "Closed" && s.PlaytimeAmount.HasValue)
                    .SumAsync(s => (int)Math.Floor(s.PlaytimeAmount!.Value / 10000m));

                // Tra cứu bảng MembershipTiers
                var tier = await _db.MembershipTiers
                    .Where(t => t.MinPoints <= totalPoints)
                    .OrderByDescending(t => t.MinPoints)
                    .FirstOrDefaultAsync();

                if (tier != null)
                {
                    tierName = tier.TierName;
                    discountPercent = tier.DiscountPercent;
                }
                else
                {
                    // Fallback theo quy ước: Đồng (0%), Bạc (5%), Vàng (10%)
                    tierName = LoyaltyConstants.GetRankName(totalPoints);
                    discountPercent = tierName switch
                    {
                        "Bạc" => 5m,
                        "Vàng" => 10m,
                        "Kim cương" => 15m,
                        _ => 0m
                    };
                }
            }
        }

        // Ưu đãi: Giảm giá trên tiền giờ chơi
        var discountAmount = Math.Round(totalPlaytime * (discountPercent / 100m), 0);

        var existingInvoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.SessionId == session.Id && i.Status != "Cancelled");

        return new CheckoutSessionViewModel
        {
            SessionId = session.Id,
            TableCode = session.Table?.TableCode ?? string.Empty,
            TableTypeName = session.Table?.TableType?.Name ?? "Tiêu chuẩn",
            FloorNumber = session.Table?.FloorNumber ?? 1,
            StartAtUtc = session.StartAtUtc,
            EndAtUtc = session.EndAtUtc,
            Status = session.Status,
            HourlyRate = session.HourlyRateSnapshot,
            Duration = duration,
            TotalPlaytime = totalPlaytime,
            CustomerId = session.CustomerId,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            TierName = tierName,
            DiscountPercent = discountPercent,
            DiscountAmount = discountAmount,
            FandBItems = fandbItems,
            TotalFandB = totalFandB,
            ExistingInvoiceId = existingInvoice?.Id,
            ExistingInvoiceStatus = existingInvoice?.Status
        };
    }

    private async Task CloseSessionViaStoredProcAsync(int sessionId, string staffId)
    {
        var paramSessionId = new SqlParameter("@SessionId", sessionId);
        var paramStaffId = new SqlParameter("@StaffId", (object?)staffId ?? DBNull.Value);

        await _db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_CloseSession @SessionId, @StaffId",
            paramSessionId, paramStaffId
        );
    }
}

