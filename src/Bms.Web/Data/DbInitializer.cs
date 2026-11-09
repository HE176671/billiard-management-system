using System.Text.RegularExpressions;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// Chỉ chạy trong môi trường Development.
/// - Mở khóa và đặt mật khẩu demo (mặc định 123456) cho các tài khoản demo mỗi lần chạy.
/// - Đảm bảo có dữ liệu phiên chơi, đơn F&B và giao dịch thanh toán thật trong 7 ngày gần nhất
///   để Tổng quan hoạt động, Báo cáo doanh thu và Báo cáo vận hành luôn khớp số 100%.
/// </summary>
public static class DbInitializer
{
    private static readonly string[] DemoAccounts = { "admin.demo", "manager.demo", "staff.demo01", "staff.demo02" };

    public static async Task SeedDevelopmentPasswordsAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        var password = config["DevSeed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password)) password = "123456";

        foreach (var userName in DemoAccounts)
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                logger.LogWarning("Không tìm thấy {UserName} — bỏ qua.", userName);
                continue;
            }

            user.IsActive = true;
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);

            if (await userManager.HasPasswordAsync(user))
                await userManager.RemovePasswordAsync(user);
            var result = await userManager.AddPasswordAsync(user, password);
            if (result.Succeeded)
                logger.LogInformation("Đã mở khóa {UserName} và đặt mật khẩu demo.", userName);
            else
                logger.LogError("Đặt mật khẩu {UserName} thất bại: {Errors}", userName,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// TV5: bảo đảm có đủ 3 role nội bộ và tài khoản demo manager.demo (Manager).
    /// </summary>
    public static async Task EnsureInternalRolesAndDemoManagerAsync(
        UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        foreach (var role in new[] { "Admin", "Manager", "Staff" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        var manager = await userManager.FindByNameAsync("manager.demo");
        if (manager == null)
        {
            manager = new ApplicationUser
            {
                UserName = "manager.demo",
                Email = "manager@example.test",
                FullName = "Quản lý CLB Mẫu",
                EmployeeCode = await userManager.Users.AnyAsync(u => u.EmployeeCode == "MG001") ? null : "MG001",
                HireDate = new DateOnly(2026, 1, 1),
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var created = await userManager.CreateAsync(manager);
            if (!created.Succeeded)
            {
                logger.LogWarning("Không tạo được manager.demo: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogInformation("Đã tạo tài khoản demo manager.demo.");
        }
        if (!await userManager.IsInRoleAsync(manager, "Manager"))
            await userManager.AddToRoleAsync(manager, "Manager");
    }

    /// <summary>
    /// Nạp dữ liệu mẫu thật (PlaySessions, PlaySessionTableSegments, Orders, Invoices, PaymentTransactions)
    /// trải đều trong 7 ngày gần nhất (bao gồm cả hôm nay) nếu 7 ngày qua chưa có giao dịch thanh toán nào.
    /// Nhờ đó Tổng quan hoạt động, Báo cáo doanh thu và Báo cáo vận hành luôn đồng bộ 100% từ DB thật.
    /// </summary>
    public static async Task SeedShiftDemoDataAsync(ApplicationDbContext db, string contentRoot, ILogger logger)
    {
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        var sevenDaysAgoStartUtc = todayVn.AddDays(-6).AddHours(-7);
        var todayEndUtc = todayVn.AddDays(1).AddHours(-7);

        // Nếu trong 7 ngày gần nhất đã có ít nhất 5 giao dịch thanh toán thì giữ nguyên
        var recentTxCount = await db.PaymentTransactions
            .CountAsync(p => p.TransactionTimeUtc >= sevenDaysAgoStartUtc && p.TransactionTimeUtc < todayEndUtc);
        if (recentTxCount >= 5) return;

        var tables = await db.BilliardTables.Include(t => t.TableType).OrderBy(t => t.Id).ToListAsync();
        if (tables.Count == 0) return;

        var staffUser = await db.Users.FirstOrDefaultAsync(u => u.UserName == "staff.demo01")
                        ?? await db.Users.FirstOrDefaultAsync();
        if (staffUser == null) return;

        var products = await db.Products.Where(p => p.IsActive && p.Price > 0).OrderBy(p => p.Id).Take(4).ToListAsync();

        // Kịch bản phiên chơi cho 7 ngày (từ -6 ngày trước đến hôm nay = 0):
        // Mỗi phần tử: (daysAgo, tableIndex, startHourVn, durationMinutes, paymentMethod, fnbQty)
        var specs = new (int DaysAgo, int TableIdx, int StartHourVn, int DurationMins, string Method, int FnbQty)[]
        {
            (6, 0, 10, 120, "Cash",     2),
            (6, 1, 19, 150, "Transfer", 3),
            (5, 0, 14,  90, "Cash",     1),
            (5, 2, 20, 180, "Transfer", 2),
            (4, 1,  9, 120, "Cash",     2),
            (4, 3, 18, 120, "Cash",     2),
            (3, 0, 15, 120, "Transfer", 2),
            (3, 2, 19, 150, "Cash",     3),
            (2, 1, 10,  90, "Cash",     1),
            (2, 3, 20, 180, "Transfer", 4),
            (1, 0, 13, 120, "Cash",     2),
            (1, 1, 18, 150, "Transfer", 2),
            (1, 2, 20, 120, "Cash",     3),
            (0, 0,  9, 120, "Cash",     2),
            (0, 1, 11,  90, "Transfer", 2)
        };

        int seededCount = 0;
        foreach (var sp in specs)
        {
            var table = tables[sp.TableIdx % tables.Count];
            decimal hourlyRate = table.TableType?.HourlyRate > 0 ? table.TableType.HourlyRate : 60000m;

            var dayVn = todayVn.AddDays(-sp.DaysAgo);
            var startUtc = dayVn.AddHours(sp.StartHourVn - 7);
            var endUtc = startUtc.AddMinutes(sp.DurationMins);

            // Không tạo phiên kết thúc ở tương lai so với thời điểm hiện tại
            if (endUtc > DateTime.UtcNow.AddMinutes(-5))
            {
                endUtc = DateTime.UtcNow.AddMinutes(-5);
                startUtc = endUtc.AddMinutes(-sp.DurationMins);
            }

            decimal playtimeAmount = Math.Round(hourlyRate * sp.DurationMins / 60m, 0);

            var session = new PlaySession
            {
                TableId = table.Id,
                OpenedById = staffUser.Id,
                ClosedById = staffUser.Id,
                StartAtUtc = startUtc,
                EndAtUtc = endUtc,
                HourlyRateSnapshot = hourlyRate,
                PlaytimeAmount = playtimeAmount,
                Status = "Closed"
            };
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();

            var segment = new PlaySessionTableSegment
            {
                SessionId = session.Id,
                TableId = table.Id,
                HourlyRateSnapshot = hourlyRate,
                StartAtUtc = startUtc,
                EndAtUtc = endUtc
            };
            db.PlaySessionTableSegments.Add(segment);

            decimal fandBAmount = 0m;
            if (products.Count > 0 && sp.FnbQty > 0)
            {
                var prod = products[(sp.DaysAgo + sp.TableIdx) % products.Count];
                var order = new Order
                {
                    SessionId = session.Id,
                    OrderTimeUtc = startUtc.AddMinutes(15),
                    Status = Order.StatusCompleted,
                    OrderSource = Order.SourceStaff,
                    CreatedById = staffUser.Id,
                    Note = "Đơn F&B tại quầy"
                };
                db.Orders.Add(order);
                await db.SaveChangesAsync();

                var detail = new OrderDetail
                {
                    OrderId = order.Id,
                    ProductId = prod.Id,
                    Quantity = sp.FnbQty,
                    UnitPrice = prod.Price,
                    Status = OrderDetail.StatusActive,
                    CancelledQuantity = 0
                };
                db.OrderDetails.Add(detail);
                fandBAmount = prod.Price * sp.FnbQty;
            }

            await db.SaveChangesAsync();

            decimal totalBill = playtimeAmount + fandBAmount;
            string refCode = $"BMS-OPSDEMO-{dayVn:yyyyMMdd}-{session.Id}";

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
                VALUES ({session.Id}, NULL, {playtimeAmount}, {fandBAmount}, 0, 'Paid', {endUtc});
                DECLARE @invId int = CONVERT(int, SCOPE_IDENTITY());
                INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode, TransactionTimeUtc, ReceivedById)
                VALUES (@invId, {sp.Method}, {totalBill}, {refCode}, {endUtc}, {staffUser.Id});
            ");

            seededCount++;
        }

        logger.LogInformation("Đã khởi tạo {Count} phiên chơi, đơn F&B và hóa đơn thanh toán thực tế cho 7 ngày gần nhất.", seededCount);
    }
}
