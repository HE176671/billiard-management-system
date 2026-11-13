using System.Reflection;
using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Real SQL Server integration checks. All fixtures and writes are rolled back.
var services = new ServiceCollection();
services.AddLogging();
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var appSettingsFile = Path.Combine(repoRoot, "src", "Bms.Web", "appsettings.json");
var configBuilder = new ConfigurationBuilder();
if (File.Exists(appSettingsFile))
{
    configBuilder.AddJsonFile(appSettingsFile, optional: true);
}
var configuration = configBuilder.Build();
services.AddSingleton<IConfiguration>(configuration);
services.AddDataProtection().UseEphemeralDataProtectionProvider();
var connString = Environment.GetEnvironmentVariable("BMS_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(connString))
{
    var devSettings = Path.Combine(repoRoot, "src", "Bms.Web", "appsettings.Development.json");
    if (File.Exists(devSettings))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(devSettings));
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                cs.TryGetProperty("DefaultConnection", out var dc))
            {
                connString = dc.GetString();
            }
        }
        catch { }
    }
}
connString ??= "Server=localhost\\SQLEXPRESS;Database=BilliardDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";

services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connString));
services.AddIdentity<ApplicationUser, IdentityRole>(o => o.Stores.MaxLengthForKeys = 128)
    .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
services.AddMemoryCache();
services.AddScoped<IPointLedgerService, PointLedgerService>();
services.AddScoped<ITierEvaluationService, TierEvaluationService>();
services.AddScoped<IVoucherService, VoucherService>();
services.AddScoped<ILoyaltyService, LoyaltyService>();
services.AddScoped<ICheckoutService, CheckoutService>();
services.AddScoped<ICustomerSegmentService, CustomerSegmentService>();
services.AddScoped<ICampaignService, CampaignService>();
services.AddScoped<ITableSessionService, TableSessionService>();
services.AddScoped<IInventoryService, InventoryService>();
services.AddScoped<IProductService, ProductService>();
services.AddScoped<IComboService, ComboService>();
services.AddScoped<IOrderService, OrderService>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
await SchemaUpgrader.ApplyAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
await Wf5SchemaUpgrader.ApplyAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
await using var tx = await db.Database.BeginTransactionAsync();
var passed = 0;
void Check(bool condition, string label) {
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); passed++;
}
T Prepare<T>(T controller, ClaimsPrincipal? principal = null) where T : Controller {
    var httpCtx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    if (principal != null) httpCtx.User = principal;
    controller.ControllerContext = new ControllerContext {
        HttpContext = httpCtx,
        RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
        ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
    };
    controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(controller.ControllerContext);
    controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempData());
    return controller;
}
try
{
    var key = Guid.NewGuid().ToString("N");
    var legacyTiers = Prepare(new MemberTierController(db));
    Check(await legacyTiers.Index() is RedirectToActionResult { ControllerName: "TierConfig" }, "Legacy tiers redirect to monetary configuration");
    var tierController = Prepare(new TierConfigController(db, new TierEvaluationService(db)));
    decimal threshold = 10_000_000_000m;
    while (await db.MembershipTiers.AnyAsync(t => t.SpendThreshold == threshold)) threshold++;
    Check(await tierController.AddTier("Smoke " + key[..8], threshold, 5) is RedirectToActionResult, "Tier create by accumulation threshold");
    var tier = await db.MembershipTiers.SingleAsync(t => t.SpendThreshold == threshold);
    await tierController.AddTier("Duplicate " + key[..8], threshold, 5);
    Check(await db.MembershipTiers.CountAsync(t => t.SpendThreshold == threshold) == 1, "Duplicate monetary threshold rejected");

    var type = await db.TableTypes.FirstAsync();
    // Use a temporary type so existing configs never interfere with the test.
    var testType = new TableType { Name = "Smoke " + key[..8], HourlyRate = 50000 };
    db.TableTypes.Add(testType); await db.SaveChangesAsync();
    var pricing = Prepare(new PricingController(db, new AuditLogService(db, new HttpContextAccessor())));
    Check(await pricing.Create(new() { TableTypeId = testType.Id, StartHour = "08:00", EndHour = "12:00", HourlyRate = 50000 }) is RedirectToActionResult, "Pricing create");
    var config = await db.PricingConfigs.SingleAsync(p => p.TableTypeId == testType.Id);
    var overlap = Prepare(new PricingController(db, new AuditLogService(db, new HttpContextAccessor())));
    Check(await overlap.Create(new() { Id = config.Id, TableTypeId = testType.Id, StartHour = "09:00", EndHour = "10:00", HourlyRate = 50000 }) is ViewResult && !overlap.ModelState.IsValid, "Overlap rejected even with forged create ID");
    Check(await pricing.Create(new() { TableTypeId = testType.Id, StartHour = "12:00", EndHour = "14:00", HourlyRate = 60000 }) is RedirectToActionResult, "Adjacent pricing intervals accepted");
    var invalid = Prepare(new PricingController(db, new AuditLogService(db, new HttpContextAccessor())));
    Check(await invalid.Create(new() { TableTypeId = testType.Id, StartHour = "25:00", EndHour = "26:00", HourlyRate = 50000 }) is ViewResult && !invalid.ModelState.IsValid, "Out-of-day hours rejected");
    var unknown = Prepare(new PricingController(db, new AuditLogService(db, new HttpContextAccessor())));
    Check(await unknown.Create(new() { TableTypeId = int.MaxValue, StartHour = "08:00", EndHour = "10:00", HourlyRate = 50000 }) is ViewResult && !unknown.ModelState.IsValid, "Unknown table type rejected");
    Check(await pricing.Edit(new CreateEditPricingViewModel() { Id = config.Id, TableTypeId = testType.Id, StartHour = "08:00", EndHour = "11:00", HourlyRate = 70000 }) is RedirectToActionResult && config.HourlyRate == 70000, "Pricing update");
    Check(await pricing.Delete(config.Id) is RedirectToActionResult && !await db.PricingConfigs.AnyAsync(p => p.Id == config.Id), "Pricing delete");

    var staff = new ApplicationUser { UserName = "smoke." + key, Email = key + "@example.invalid", FullName = "Smoke staff", TierId = tier.Id };
    Check((await users.CreateAsync(staff, "SmokeOriginal!123")).Succeeded && (await users.AddToRoleAsync(staff, "Staff")).Succeeded, "Identity fixture creation");
    await tierController.DeleteTier(tier.Id);
    Check(await db.MembershipTiers.AnyAsync(t => t.Id == tier.Id), "Referenced tier deletion blocked");
    var employees = Prepare(new EmployeeController(users, db));
    await employees.ResetPassword(new ResetStaffPasswordViewModel() { UserId = staff.Id, NewPassword = "12345", ConfirmPassword = "12345" });
    Check(await users.CheckPasswordAsync(staff, "SmokeOriginal!123"), "Rejected reset preserves old password");
    employees = Prepare(new EmployeeController(users, db));
    Check(await employees.ResetPassword(new ResetStaffPasswordViewModel() { UserId = staff.Id, NewPassword = "SmokeReplacement!123", ConfirmPassword = "SmokeReplacement!123" }) is RedirectToActionResult && await users.CheckPasswordAsync(staff, "SmokeReplacement!123"), "Valid reset replaces password");

    var customer = new ApplicationUser { UserName = "customer." + key, Email = "c" + key + "@example.invalid", FullName = "Smoke customer", RewardPoints = 42, TierId = tier.Id };
    Check((await users.CreateAsync(customer, "SmokeCustomer!123")).Succeeded && (await users.AddToRoleAsync(customer, "Customer")).Succeeded, "Customer fixture creation");
    var customers = Prepare(new CustomerController(users, db));
    var list = (CustomerListViewModel)((ViewResult)await customers.Index(key, "active", tier.Id)).Model!;
    Check(list.TotalCount == 1 && list.Customers[0].RewardPoints == 42 && list.Customers[0].TierName == tier.TierName, "Customer search, status, tier filters and projection");
    Check(await customers.ToggleLock(staff.Id) is NotFoundResult, "Customer action rejects staff ID");
    var stamp = customer.SecurityStamp;
    await customers.ToggleLock(customer.Id);
    Check(!customer.IsActive && customer.SecurityStamp != stamp, "Customer locking changes security stamp");
    await customers.ToggleLock(customer.Id);
    Check(customer.IsActive, "Customer unlocking");

    // Payment fixtures at UTC+7 day boundaries. Invoice/session remain within the transaction.
    await db.Database.ExecuteSqlInterpolatedAsync($@"
        DECLARE @table int = (SELECT TOP(1) Id FROM dbo.BilliardTables);
        INSERT dbo.PlaySessions(TableId,OpenedById,ClosedById,StartAtUtc,EndAtUtc,HourlyRateSnapshot,PlaytimeAmount,Status)
        VALUES(@table,{staff.Id},{staff.Id},'2035-01-01','2035-01-01T01:00:00',50000,50000,'Closed');
        DECLARE @session int = SCOPE_IDENTITY();
        INSERT dbo.Invoices(SessionId,TotalPlaytime,Status) VALUES(@session,50000,'Paid');
        DECLARE @invoice int = SCOPE_IDENTITY();
        INSERT dbo.PaymentTransactions(InvoiceId,PaymentMethod,AmountPaid,TransactionTimeUtc) VALUES
        (@invoice,'Cash',10000,'2035-01-01T16:59:59'),
        (@invoice,'Cash',20000,'2035-01-01T17:00:00'),
        (@invoice,'Transfer',30000,'2035-01-02T16:59:59'),
        (@invoice,'Cash',40000,'2035-01-02T17:00:00');");
    var report = Prepare(new RevenueController(db));
    var reportModel = (RevenueReportViewModel)((ViewResult)await report.Index(new(2035,1,2), new(2035,1,2), "day")).Model!;
    Check(reportModel.TotalRevenue == 50000 && reportModel.PaymentCount == 2 && reportModel.InvoiceCount == 1 && reportModel.Methods.Count == 2, "Revenue UTC+7 inclusive final day and distinct invoice count");
    var month = (RevenueReportViewModel)((ViewResult)await report.Index(new(2035,1,1), new(2035,1,3), "month")).Model!;
    Check(month.TotalRevenue == 100000 && month.Periods.Count == 1, "Monthly revenue grouping");
    var empty = (RevenueReportViewModel)((ViewResult)await report.Index(new(2035,2,1), new(2035,2,2), "day")).Model!;
    Check(empty.TotalRevenue == 0 && empty.Periods.Count == 2, "Empty report zero-filled periods");
    Check(await report.Export(new(2035,1,2),new(2035,1,2)) is FileContentResult csv && csv.FileContents.Length > 0, "CSV export");
    Check(await report.Export(new(2035,1,3),new(2035,1,1)) is BadRequestObjectResult, "Reversed report range rejected");
    var dashboard = Prepare(new DashboardController(db, new RevenueTargetService(db)));
    Check(((ViewResult)await dashboard.Index()).Model is DashboardViewModel { TotalTables: > 0 }, "Dashboard live SQL queries");

    // TV5 — Báo cáo vận hành (OperationsReportController)
    var opsCtrl = Prepare(new OperationsReportController(db));
    var todayVnOps = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    Check(OperationsReportController.CalculateOccupancyPercent(480m, 1, 960, 1) == 50.0m
        && OperationsReportController.CalculateOccupancyPercent(0m, 5, 960, 7) == 0m
        && OperationsReportController.CalculateOccupancyPercent(2000m, 1, 960, 1) == 100.0m,
        "OperationsReport occupancy formula (exact %, zero safe, 100% cap)");
    var opsBadRange = Prepare(new OperationsReportController(db));
    await opsBadRange.Index(todayVnOps, todayVnOps.AddDays(-2));
    var opsTooLong = Prepare(new OperationsReportController(db));
    await opsTooLong.Index(todayVnOps.AddDays(-100), todayVnOps);
    var opsFuture = Prepare(new OperationsReportController(db));
    await opsFuture.Index(todayVnOps, todayVnOps.AddDays(2));
    var opsBadHours = Prepare(new OperationsReportController(db));
    await opsBadHours.Index(todayVnOps.AddDays(-3), todayVnOps, openHour: 20, closeHour: 10);
    var opsBadType = Prepare(new OperationsReportController(db));
    await opsBadType.Index(todayVnOps.AddDays(-3), todayVnOps, 8, 24, tableTypeId: int.MaxValue);
    Check(!opsBadRange.ModelState.IsValid && !opsTooLong.ModelState.IsValid && !opsFuture.ModelState.IsValid && !opsBadHours.ModelState.IsValid && !opsBadType.ModelState.IsValid,
        "OperationsReport server-side filter validation (date range, 92-day limit, future date, hours, tableTypeId)");
    var opsValidRes = (ViewResult)await opsCtrl.Index(todayVnOps.AddDays(-6), todayVnOps, 8, 24, null);
    var opsVm = (OperationsReportViewModel)opsValidRes.Model!;
    var opsCsv = await opsCtrl.Export(todayVnOps.AddDays(-6), todayVnOps, 8, 24, null) as FileContentResult;
    Check(opsCtrl.ModelState.IsValid && opsVm.TotalDays == 7 && opsVm.HeatmapRows.Count == 7 && opsVm.HeatmapHours.Count == 16
        && opsCsv != null && opsCsv.FileContents.Length > 3 && opsCsv.FileContents[0] == 0xEF && opsCsv.FileContents[1] == 0xBB && opsCsv.FileContents[2] == 0xBF,
        "OperationsReport populates heatmap, table occupancy metrics and UTF-8 BOM CSV export");

    // TV5 ca làm (cần 08_AddWorkShiftReconciliation.sql). Ngoại tx đang mở nên service dùng chung tx.
    // BR-23: cả quán một ca. Đóng tạm các ca đang mở trong DB thật (transaction sẽ rollback cuối test).
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE dbo.WorkShifts SET Status = 'Closed', EndTimeUtc = SYSUTCDATETIME(), EndingCash = StartingCash WHERE Status = 'Active'");
    var shiftService = new ShiftService(db, new AuditLogService(db, new HttpContextAccessor()));
    var opened = await shiftService.OpenShiftAsync(staff.Id, 500000, "smoke");
    Check(opened.Status == WorkShift.StatusActive && opened.StartingCash == 500000, "Shift open");
    var secondRejected = false;
    try { await shiftService.OpenShiftAsync(staff.Id, 0, null); } catch (ShiftException e) { secondRejected = e.Code == "ALREADY_OPEN"; }
    Check(secondRejected, "Second active shift in store rejected (BR-23)");
    var negativeRejected = false;
    try { await shiftService.OpenShiftAsync(customer.Id, -1, null); } catch (ShiftException) { negativeRejected = true; }
    Check(negativeRejected, "Negative starting cash rejected");
    Check(await shiftService.RequireActiveShiftIdForCashAsync(staff.Id) == opened.Id, "Cash payment locks the active shift");
    await db.Database.ExecuteSqlInterpolatedAsync($@"
        DECLARE @table int = (SELECT TOP(1) Id FROM dbo.BilliardTables);
        INSERT dbo.PlaySessions(TableId,OpenedById,ClosedById,StartAtUtc,EndAtUtc,HourlyRateSnapshot,PlaytimeAmount,Status)
        VALUES(@table,{staff.Id},{staff.Id},'2035-03-01','2035-03-01T01:00:00',50000,50000,'Closed');
        INSERT dbo.Invoices(SessionId,TotalPlaytime,Status) VALUES(SCOPE_IDENTITY(),200000,'Paid');
        DECLARE @invoice int = SCOPE_IDENTITY();
        INSERT dbo.PaymentTransactions(InvoiceId,PaymentMethod,AmountPaid,WorkShiftId,ReceivedById) VALUES
        (@invoice,'Cash',120000,{opened.Id},{staff.Id}),
        (@invoice,'VNPay',80000,{opened.Id},NULL);");
    var live = (await shiftService.GetSummaryAsync(opened.Id))!;
    Check(live.CashCollected == 120000 && live.NonCashCollected == 80000 && live.ExpectedCash == 620000, "Shift expected cash excludes VNPay");
    var noteRequired = false;
    try { await shiftService.CloseShiftAsync(opened.Id, staff.Id, 600000, null); } catch (ShiftException e) { noteRequired = e.Code == "NOTE_REQUIRED"; }
    Check(noteRequired, "Cash difference requires a note");
    var closed = await shiftService.CloseShiftAsync(opened.Id, staff.Id, 600000, "Thiếu tiền lẻ");
    Check(!closed.IsActive && closed.Difference == -20000 && closed.Shift.ExpectedCash == 620000, "Shift close snapshots expected cash and difference");
    var openedKey = opened.Id.ToString();
    Check(await db.AuditLogs.AnyAsync(log => log.EntityId == openedKey && log.ActionType == AuditActions.ShiftOpen)
        && await db.AuditLogs.AnyAsync(log => log.EntityId == openedKey && log.ActionType == AuditActions.ShiftCashDiff && log.Reason == "Thiếu tiền lẻ"),
        "Shift open and cash difference are audit-logged");
    var closeTwice = false;
    try { await shiftService.CloseShiftAsync(opened.Id, staff.Id, 620000, null); } catch (ShiftException e) { closeTwice = e.Code == "ALREADY_CLOSED"; }
    Check(closeTwice, "Closing twice rejected");
    var noShift = false;
    try { await shiftService.RequireActiveShiftIdForCashAsync(staff.Id); } catch (ShiftException e) { noShift = e.Code == "NO_ACTIVE_SHIFT"; }
    Check(noShift, "Cash payment after close rejected");
    // Bàn giao két: ca trước đóng với 600.000 -> mở ca mới khác số này phải ghi lý do, khớp thì không cần.
    var handoverRejected = false;
    try { await shiftService.OpenShiftAsync(staff.Id, 500000, null); } catch (ShiftException e) { handoverRejected = e.Code == "HANDOVER_NOTE_REQUIRED"; }
    var fractionRejected = false;
    try { await shiftService.OpenShiftAsync(staff.Id, 600000.5m, "x"); } catch (ShiftException e) { fractionRejected = e.Code == "INVALID_CASH"; }
    var handoverOk = await shiftService.OpenShiftAsync(staff.Id, 600000, null);
    Check(handoverRejected && fractionRejected && handoverOk.Status == WorkShift.StatusActive,
        "Shift handover: opening cash must match previous counted cash or carry a reason; whole VND only");
    await shiftService.CloseShiftAsync(handoverOk.Id, staff.Id, 600000, null);
    var dashboardWithShift = (DashboardViewModel)((ViewResult)await Prepare(new DashboardController(db, new RevenueTargetService(db))).Index()).Model!;
    Check(dashboardWithShift.TotalTables == dashboardWithShift.AvailableTables + dashboardWithShift.InUseTables
        + dashboardWithShift.AwaitingPaymentTables + dashboardWithShift.UnavailableTables, "Dashboard table statuses add up");

    staff.TierId = null; customer.TierId = null; await db.SaveChangesAsync();
    await tierController.DeleteTier(tier.Id);
    Check(!await db.MembershipTiers.AnyAsync(t => t.Id == tier.Id), "Unreferenced tier deletion");

    // =========================================================================
    // TV2 CRM & Loyalty V2 Acceptance Tests (T1 - T16)
    // =========================================================================
    var ledger = scope.ServiceProvider.GetRequiredService<IPointLedgerService>();
    var tierEval = scope.ServiceProvider.GetRequiredService<ITierEvaluationService>();
    var voucherSvc = scope.ServiceProvider.GetRequiredService<IVoucherService>();
    var loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
    var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
    var segmentSvc = scope.ServiceProvider.GetRequiredService<ICustomerSegmentService>();
    var campaignSvc = scope.ServiceProvider.GetRequiredService<ICampaignService>();

    var allTiers = await db.MembershipTiers.OrderBy(t => t.MinPoints).ToListAsync();
    var memberTier = allTiers.FirstOrDefault(t => t.MinPoints == 0) ?? allTiers.First();
    var silverTier = allTiers.FirstOrDefault(t => t.TierName == "Silver" || t.TierName == "Bạc" || t.MinPoints == 200 || t.MinPoints == 500)
                     ?? (allTiers.Count > 1 ? allTiers[1] : allTiers[0]);
    var goldTier = allTiers.FirstOrDefault(t => t.TierName == "Gold" || t.TierName == "Vàng" || t.MinPoints == 1000 || t.MinPoints == 1500 || t.MinPoints == 2000)
                   ?? (allTiers.Count > 2 ? allTiers[2] : allTiers[^1]);

    var adminUser = new ApplicationUser { UserName = "admin.tv2." + key, Email = "admin." + key + "@example.invalid", FullName = "TV2 Admin Checker" };
    Check((await users.CreateAsync(adminUser, "SmokeAdmin!123")).Succeeded && (await users.AddToRoleAsync(adminUser, "Admin")).Succeeded, "TV2 Admin fixture creation");

    var tv2Customer = new ApplicationUser
    {
        UserName = "tv2.cust." + key,
        Email = "tv2." + key + "@example.invalid",
        PhoneNumber = "09" + Random.Shared.Next(10000000, 99999999),
        FullName = "TV2 Smoke Customer",
        RewardPoints = 0,
        TierId = memberTier.Id,
        IsActive = true,
        MarketingOptIn = true
    };
    Check((await users.CreateAsync(tv2Customer, "SmokeCustomer!123")).Succeeded && (await users.AddToRoleAsync(tv2Customer, "Customer")).Succeeded, "TV2 Customer fixture creation");

    // T1: Commit checkout 2 lần cùng ReferenceCode -> Lần 2 trả Duplicate, điểm chỉ cộng 1 lần, chỉ 1 bút toán Earn
    var refT1 = $"INV-T1-{key[..12]}";
    var reqT1 = new CheckoutLoyaltyRequest(tv2Customer.Id, refT1, null, 200000m, 40000m, 0, null, staff.Id);
    var commit1 = await loyalty.CommitCheckoutAsync(reqT1);
    var commit2 = await loyalty.CommitCheckoutAsync(reqT1);
    var ptsAfterT1 = await db.Users.AsNoTracking().Where(u => u.Id == tv2Customer.Id).Select(u => u.RewardPoints).SingleAsync();
    var earnCountT1 = await db.PointTransactions.CountAsync(t => t.CustomerId == tv2Customer.Id && t.TransactionType == PointTransaction.Earn && t.ReferenceCode == refT1);
    Check(commit1.Success && !commit1.Duplicate && commit1.PointsEarned == 12 && commit2.Success && commit2.Duplicate && ptsAfterT1 == 12 && earnCountT1 == 1,
        "T1: Commit checkout twice with same ReferenceCode is idempotent (Duplicate=true, 1 Earn row)");

    // T2: Ledger trừ quá số dư -> Thất bại, RewardPoints không đổi, không có bút toán
    var overRef = $"OVER-{key[..12]}";
    var overRes = await ledger.PostAsync(tv2Customer.Id, -50, PointTransaction.Redeem, overRef, null, "Test over deduction", staff.Id);
    var ptsAfterT2 = await db.Users.AsNoTracking().Where(u => u.Id == tv2Customer.Id).Select(u => u.RewardPoints).SingleAsync();
    Check(!overRes.Success && ptsAfterT2 == 12 && !await db.PointTransactions.AnyAsync(t => t.ReferenceCode == overRef),
        "T2: Ledger rejects deduction exceeding available RewardPoints");

    // T3: BalanceAfter -> Sau chuỗi Earn/Redeem/Adjust, BalanceAfter của bút toán cuối = RewardPoints
    await ledger.PostAsync(tv2Customer.Id, 100, PointTransaction.Earn, $"T3-E-{key[..10]}", null, "T3 Earn", staff.Id, 2000000m);
    await ledger.PostAsync(tv2Customer.Id, -30, PointTransaction.Redeem, $"T3-R-{key[..10]}", null, "T3 Redeem", staff.Id);
    await ledger.PostAsync(tv2Customer.Id, 18, PointTransaction.Adjust, $"T3-A-{key[..10]}", null, "T3 Adjust", staff.Id);
    var ptsAfterT3 = await db.Users.AsNoTracking().Where(u => u.Id == tv2Customer.Id).Select(u => u.RewardPoints).SingleAsync();
    var lastTxT3 = await db.PointTransactions.AsNoTracking().Where(t => t.CustomerId == tv2Customer.Id).OrderByDescending(t => t.Id).FirstAsync();
    Check(ptsAfterT3 == 100 && lastTxT3.BalanceAfter == ptsAfterT3,
        "T3: BalanceAfter matches actual RewardPoints after Earn/Redeem/Adjust sequence");

    // T4: Preview: đổi điểm vượt 50% -> IsValid=false, MaxRedeemablePoints đúng công thức
    await ledger.PostAsync(tv2Customer.Id, 400, PointTransaction.Adjust, $"T4-SEED-{key[..10]}", null, "Seed T4", staff.Id);
    var previewT4 = await loyalty.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(tv2Customer.Id, "PREV-T4", null, 100000m, 0m, 80, null, staff.Id));
    Check(!previewT4.IsValid && previewT4.MaxRedeemablePoints == 50,
        "T4: PreviewCheckout rejects points exceeding 50% of remaining bill and returns exact MaxRedeemablePoints");

    // T5: Thứ tự giảm giá -> Hạng 10% chỉ áp tiền giờ; voucher; điểm <= 50% phần còn lại; FinalAmount đúng
    await db.Users.Where(u => u.Id == tv2Customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TierId, goldTier.Id));
    db.ChangeTracker.Clear();
    var rewardT5 = new RewardItemEntity
    {
        Code = $"T5{key[..6].ToUpperInvariant()}",
        Name = "Quà T5 30k",
        PointCost = 0,
        RewardType = RewardItemEntity.TypeBillDiscount,
        DiscountValue = 30000m,
        ValidDays = 7,
        IsActive = true
    };
    db.RewardItems.Add(rewardT5);
    await db.SaveChangesAsync();
    var voucherT5 = new CustomerVoucher
    {
        Code = $"BMS-T5-{key[..6].ToUpperInvariant()}",
        CustomerId = tv2Customer.Id,
        RewardItemId = rewardT5.Id,
        Title = "Voucher T5 30k",
        VoucherType = RewardItemEntity.TypeBillDiscount,
        DiscountValue = 30000m,
        Status = CustomerVoucher.StatusActive,
        IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
    };
    db.CustomerVouchers.Add(voucherT5);
    await db.SaveChangesAsync();
    var previewT5 = await loyalty.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(tv2Customer.Id, "PREV-T5", null, 200000m, 100000m, 100, rewardT5.Code, staff.Id));
    Check(previewT5.IsValid
        && previewT5.TierDiscountAmount == 20000m
        && previewT5.VoucherDiscountAmount == 30000m
        && previewT5.MaxRedeemablePoints == 125
        && previewT5.PointsDiscountAmount == 100000m
        && previewT5.FinalAmount == 150000m,
        "T5: Discount order applies 10% tier discount ONLY to PlaytimeAmount, then Voucher, then Points <= 50% remaining");

    // T6: Earn tính trên FinalAmount × multiplier -> Đúng floor(Final/20000)*mult
    Check(previewT5.PointsToEarn == 7 * goldTier.PointMultiplier,
        "T6: PointsToEarn calculated on FinalAmount * PointMultiplier");

    // T7: Tự động nâng hạng tức thì ngay sau thanh toán khi đạt ngưỡng -> TierId đổi, TierReviewAtUtc = +1 tháng, có Notification
    var upCustomer = new ApplicationUser
    {
        UserName = "tv2.up." + key,
        Email = "up." + key + "@example.invalid",
        FullName = "TV2 Upgrade Customer",
        RewardPoints = 0,
        TierId = memberTier.Id,
        IsActive = true,
        MarketingOptIn = true
    };
    await users.CreateAsync(upCustomer, "SmokeCustomer!123");
    await users.AddToRoleAsync(upCustomer, "Customer");
    var silverSpendThreshold = silverTier.SpendThreshold;
    var commitT7 = await loyalty.CommitCheckoutAsync(new CheckoutLoyaltyRequest(upCustomer.Id, $"INV-T7-{key[..10]}", null, silverSpendThreshold + 200000m, 0m, 0, null, staff.Id));
    var upUserAfter = await db.Users.AsNoTracking().SingleAsync(u => u.Id == upCustomer.Id);
    var hasUpgradeNotif = await db.Notifications.AnyAsync(n => n.UserId == upCustomer.Id && n.Title.Contains(silverTier.TierName));
    Check(commitT7.Success && commitT7.UpgradedTier && upUserAfter.TierId == silverTier.Id
        && upUserAfter.TierReviewAtUtc.HasValue && upUserAfter.TierReviewAtUtc.Value > DateTime.UtcNow.AddDays(360)
        && upUserAfter.TierReviewAtUtc.Value < DateTime.UtcNow.AddDays(370)
        && hasUpgradeNotif,
        "T7: Immediate tier upgrade on threshold updates TierId, sets 12-month TierReviewAtUtc (BR-17d), and creates Notification");

    // T8: Hết review, chưa đủ chi tiêu -> Vào ân hạn 30 ngày, chưa hạ
    var graceCustomer = new ApplicationUser
    {
        UserName = "tv2.grace." + key,
        Email = "grace." + key + "@example.invalid",
        FullName = "TV2 Grace Customer",
        RewardPoints = 0,
        TierId = goldTier.Id,
        TierAchievedAtUtc = DateTime.UtcNow.AddMonths(-12),
        TierReviewAtUtc = DateTime.UtcNow.AddDays(-1),
        TierGraceUntilUtc = null,
        IsActive = true
    };
    await users.CreateAsync(graceCustomer, "SmokeCustomer!123");
    await users.AddToRoleAsync(graceCustomer, "Customer");
    await tierEval.ProcessDailyTierReviewsAsync(DateTime.UtcNow);
    var graceAfterT8 = await db.Users.AsNoTracking().SingleAsync(u => u.Id == graceCustomer.Id);
    Check(graceAfterT8.TierId == goldTier.Id && graceAfterT8.TierGraceUntilUtc.HasValue && graceAfterT8.TierGraceUntilUtc.Value > DateTime.UtcNow.AddDays(28),
        "T8: Expired tier review with insufficient 12M spend enters 30-day grace period without immediate downgrade");

    // T9: Hết ân hạn -> Hạ đúng 1 bậc (Gold -> Silver, không tụt thẳng về Member dù chi tiêu = 0)
    await db.Users.Where(u => u.Id == graceCustomer.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.TierGraceUntilUtc, DateTime.UtcNow.AddDays(-1)));
    db.ChangeTracker.Clear();
    await tierEval.ProcessDailyTierReviewsAsync(DateTime.UtcNow);
    var graceAfterT9 = await db.Users.AsNoTracking().SingleAsync(u => u.Id == graceCustomer.Id);
    Check(graceAfterT9.TierId == silverTier.Id && graceAfterT9.TierGraceUntilUtc == null,
        "T9: Expired grace period downgrades tier by at most 1 step (Gold -> Silver)");

    // T10: Đổi quà -> voucher Active; xác nhận -> Used; xác nhận lần 2 bị từ chối
    var rewardItem = await db.RewardItems.FirstAsync(r => r.IsActive && r.RewardType == RewardItemEntity.TypeGift && r.PointCost > 0);
    var redeem10 = await voucherSvc.RedeemRewardForVoucherAsync(tv2Customer.Id, rewardItem.Code);
    var confirm1 = await voucherSvc.ConfirmGiftDeliveryAsync(redeem10.Voucher!.Code, staff.Id);
    var confirm2 = await voucherSvc.ConfirmGiftDeliveryAsync(redeem10.Voucher!.Code, staff.Id);
    var v10Reloaded = await db.CustomerVouchers.AsNoTracking().SingleAsync(v => v.Id == redeem10.Voucher.Id);
    Check(redeem10.Success && confirm1.Success && !confirm2.Success && v10Reloaded.Status == CustomerVoucher.StatusUsed,
        "T10: Redeem creates Active voucher; ConfirmGiftDelivery marks Used; second confirmation is rejected");

    // T11: Admin hủy voucher -> Hoàn điểm bằng bút toán Reverse
    var redeem11 = await voucherSvc.RedeemRewardForVoucherAsync(tv2Customer.Id, rewardItem.Code);
    var ptsBeforeCancel = await db.Users.AsNoTracking().Where(u => u.Id == tv2Customer.Id).Select(u => u.RewardPoints).SingleAsync();
    var cancel11 = await voucherSvc.CancelVoucherAsync(redeem11.Voucher!.Id, adminUser.Id, "Khách đổi ý T11");
    var ptsAfterCancel = await db.Users.AsNoTracking().Where(u => u.Id == tv2Customer.Id).Select(u => u.RewardPoints).SingleAsync();
    var hasReverseTx = await db.PointTransactions.AnyAsync(t => t.CustomerId == tv2Customer.Id && t.TransactionType == PointTransaction.Reverse && t.ReferenceCode == $"REV-VOUCHER-{redeem11.Voucher.Code}");
    Check(cancel11.Success && ptsAfterCancel == ptsBeforeCancel + rewardItem.PointCost && hasReverseTx,
        "T11: Admin CancelVoucher refunds points via Reverse ledger transaction");

    // T12: Điều chỉnh 200 điểm bởi Staff -> Pending; maker không duyệt được; Admin duyệt -> có bút toán Adjust
    var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, staff.Id),
        new Claim(ClaimTypes.Role, "Staff")
    }, "TestAuth"));
    var staffAdjCtrl = Prepare(new PointAdjustmentController(db, ledger), staffPrincipal);
    await staffAdjCtrl.Create(tv2Customer.Id, 200, "Bù sự cố bàn/điện", "Test T12", null);
    var adjReq = await db.PointAdjustmentRequests.OrderByDescending(r => r.Id).FirstAsync(r => r.CustomerId == tv2Customer.Id && r.Points == 200);
    var isPendingInitially = adjReq.Status == PointAdjustmentRequest.StatusPending;

    // Maker tries to approve own request -> rejected
    var makerAsAdminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, staff.Id),
        new Claim(ClaimTypes.Role, "Admin")
    }, "TestAuth"));
    var makerAdjCtrl = Prepare(new PointAdjustmentController(db, ledger), makerAsAdminPrincipal);
    await makerAdjCtrl.Approve(adjReq.Id, "Tự duyệt");
    var stillPendingAfterSelfApprove = (await db.PointAdjustmentRequests.AsNoTracking().SingleAsync(r => r.Id == adjReq.Id)).Status == PointAdjustmentRequest.StatusPending;

    // Separate Admin checker approves -> Approved + Adjust ledger entry
    var checkerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, adminUser.Id),
        new Claim(ClaimTypes.Role, "Admin")
    }, "TestAuth"));
    var checkerAdjCtrl = Prepare(new PointAdjustmentController(db, ledger), checkerPrincipal);
    await checkerAdjCtrl.Approve(adjReq.Id, "Admin checker duyệt");
    var adjAfterApprove = await db.PointAdjustmentRequests.AsNoTracking().SingleAsync(r => r.Id == adjReq.Id);
    var hasAdjustLedger = adjAfterApprove.PointTransactionId.HasValue
        && await db.PointTransactions.AnyAsync(t => t.Id == adjAfterApprove.PointTransactionId.Value && t.TransactionType == PointTransaction.Adjust && t.Amount == 200);
    Check(isPendingInitially && stillPendingAfterSelfApprove && adjAfterApprove.Status == PointAdjustmentRequest.StatusApproved && hasAdjustLedger,
        "T12: Staff 200pt adjustment creates Pending request, blocks self-approval (Maker != Checker), and posts Adjust on Admin approval");

    // T13: Phân khúc RFM -> Mỗi fixture rơi đúng 1 trong 4 phân khúc (VIP, Thân thiết, Khách mới, Lâu rồi chưa ghé)
    var nowRef = DateTime.UtcNow;
    var segNew = segmentSvc.ClassifySegment(nowRef.AddDays(-10), nowRef.AddDays(-2), 1, nowRef);
    var segChampion = segmentSvc.ClassifySegment(nowRef.AddDays(-100), nowRef.AddDays(-5), 8, nowRef);
    var segLoyal = segmentSvc.ClassifySegment(nowRef.AddDays(-100), nowRef.AddDays(-15), 3, nowRef);
    var segAtRisk = segmentSvc.ClassifySegment(nowRef.AddDays(-120), nowRef.AddDays(-45), 3, nowRef);
    var segNever = segmentSvc.ClassifySegment(nowRef.AddDays(-120), null, 0, nowRef);
    Check(segNever == CustomerSegmentService.SegNever, "T13b: hội viên chưa thanh toán lần nào không bị xếp vào 'Lâu rồi chưa ghé'");
    Check(segNew == CustomerSegmentService.SegNew
        && segChampion == CustomerSegmentService.SegChampion
        && segLoyal == CustomerSegmentService.SegLoyal
        && segAtRisk == CustomerSegmentService.SegAtRisk,
        "T13: RFM ClassifySegment places each fixture into its exact 4-group segment (VIP, Loyal, New, AtRisk)");

    // T15: Quản lý voucher tập trung tại Quà đổi điểm, loại bỏ voucher sinh nhật tự động; hiển thị thông tin sinh nhật trên hồ sơ khách hàng
    var vnToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    var bdayDate = new DateOnly(1998, vnToday.Month, vnToday.Day);
    await db.Users.Where(u => u.Id == upCustomer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DateOfBirth, bdayDate));
    db.ChangeTracker.Clear();
    var custProfileRes = await Prepare(new CustomerManagementController(users, db, ledger, segmentSvc, voucherSvc), staffPrincipal).Profile(upCustomer.Id);
    var profileVm = (custProfileRes as ViewResult)?.Model as Customer360ViewModel;
    Check(profileVm != null && profileVm.IsBirthdaySoon && profileVm.DaysUntilBirthday == 0,
        "T15: Birthday display active on customer profile, birthday vouchers removed in favor of centralized voucher catalog");

    // T16: Staff truy cập /CrmDashboard, /RewardItem, duyệt điều chỉnh -> Bị chặn bởi [Authorize(Roles = "Manager,Admin")]
    var crmAuth = typeof(CrmDashboardController).GetCustomAttribute<AuthorizeAttribute>();
    var campAuth = typeof(CampaignController).GetCustomAttribute<AuthorizeAttribute>();
    var approveAuth = typeof(PointAdjustmentController).GetMethod(nameof(PointAdjustmentController.Approve))?.GetCustomAttribute<AuthorizeAttribute>();
    var pendingAuth = typeof(PointAdjustmentController).GetMethod(nameof(PointAdjustmentController.Pending))?.GetCustomAttribute<AuthorizeAttribute>();
    var rewardAuth = typeof(RewardItemController).GetCustomAttribute<AuthorizeAttribute>();
    Check(crmAuth?.Roles == "Manager"
        && campAuth?.Roles == "Manager"
        && approveAuth?.Roles == "Manager"
        && pendingAuth?.Roles == "Manager"
        && rewardAuth?.Roles == "Manager"
        && typeof(EmployeeController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin",
        "T16: CRM/Campaign/RewardItem/PointAdjustment are Manager-only; account management (Employee) is Admin-only (SRS I.4.4)");

    // T17: Admin đổi vai trò tài khoản nội bộ trên màn Tài khoản & phân quyền (EmployeeController)
    var roleCtrl = Prepare(new EmployeeController(users, db), checkerPrincipal);
    var roleVm = (EmployeeListViewModel)((ViewResult)await roleCtrl.Index(null, null, null)).Model!;
    await roleCtrl.ChangeRole(staff.Id, "Manager");
    var isStaffNowManager = await users.IsInRoleAsync(staff, "Manager") && !await users.IsInRoleAsync(staff, "Staff");
    await roleCtrl.ChangeRole(staff.Id, "Staff");
    var isBackToStaff = await users.IsInRoleAsync(staff, "Staff") && !await users.IsInRoleAsync(staff, "Manager");
    Check(roleVm.TotalCount >= 1 && isStaffNowManager && isBackToStaff,
        "T17: Admin switches internal roles (Staff <-> Manager) from the single account management screen");

    // T18: Luồng Staff lập phiếu điều chỉnh điểm -> Manager vào màn riêng (/PointAdjustment/Pending - View 'Manage') phê duyệt
    var managerUser = new ApplicationUser { UserName = "manager.tv2." + key, Email = "mgr." + key + "@example.invalid", FullName = "TV2 Manager Checker" };
    Check((await users.CreateAsync(managerUser, "SmokeManager!123")).Succeeded && (await users.AddToRoleAsync(managerUser, "Manager")).Succeeded, "TV2 Manager fixture creation");
    var managerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, managerUser.Id),
        new Claim(ClaimTypes.Role, "Manager")
    }, "TestAuth"));
    await staffAdjCtrl.Create(tv2Customer.Id, 75, "Khiếu nại dịch vụ", "Staff lập phiếu chờ Manager duyệt T18", null);
    var reqT18 = await db.PointAdjustmentRequests.OrderByDescending(r => r.Id).FirstAsync(r => r.CustomerId == tv2Customer.Id && r.Points == 75);
    var managerAdjCtrl = Prepare(new PointAdjustmentController(db, ledger), managerPrincipal);
    var pendingView = (ViewResult)await managerAdjCtrl.Pending();
    var pendingVm = (PointAdjustmentIndexViewModel)pendingView.Model!;
    await managerAdjCtrl.Approve(reqT18.Id, "Manager đã duyệt phiếu T18");
    var reqT18After = await db.PointAdjustmentRequests.AsNoTracking().SingleAsync(r => r.Id == reqT18.Id);
    Check(pendingView.ViewName == "Manage"
        && pendingVm.PendingRequests.Any(x => x.Id == reqT18.Id)
        && pendingVm.RequesterRoleLabels.GetValueOrDefault(reqT18.RequestedById) == "Nhân viên yêu cầu"
        && reqT18After.Status == PointAdjustmentRequest.StatusApproved
        && reqT18After.ReviewedById == managerUser.Id,
        "T18: Staff creates pending point adjustment (with requester role label) and Manager approves it on dedicated Manage screen");

    // T19: Manager cấu hình Giá Bàn chuẩn, Giá Gậy cho thuê theo lượt & Khuyến mãi Combo Giờ chơi tặng nước
    var mgrPricingCtrl = Prepare(new PricingController(db, new AuditLogService(db, new HttpContextAccessor())), managerPrincipal);
    await mgrPricingCtrl.UpdateTableBaseRate(testType.Id, 65000m);
    var updatedTableRate = (await db.TableTypes.AsNoTracking().SingleAsync(t => t.Id == testType.Id)).HourlyRate;
    var cueName = "Gậy Carbon Smoke " + key[..6];
    var comboName = "Combo 2H Tặng Nước " + key[..6];
    await mgrPricingCtrl.SaveRentalCue(0, cueName, 45000m, 12, true);
    var savedCue = await db.Products.AsNoTracking().SingleAsync(p => p.Name == cueName);
    var drinkProd = await db.Products.AsNoTracking().FirstOrDefaultAsync();
    await mgrPricingCtrl.SaveCombo(0, comboName, 2.0m, 185000m, drinkProd?.Id, 2, true);
    var savedCombo = await db.Combos.Include(c => c.ComboItems).AsNoTracking().SingleAsync(c => c.Name == comboName);
    Check(updatedTableRate == 65000m
        && savedCue.Price == 45000m && savedCue.StockQuantity == 12
        && savedCombo.PlaytimeHours == 2.0m && savedCombo.Price == 185000m
        && (drinkProd == null || savedCombo.ComboItems.Any(i => i.ProductId == drinkProd.Id && i.Quantity == 2)),
        "T19: Manager configures table base hourly rate, rental cue per-turn pricing, and 2-hour playtime combo with free drinks");






    // T20: Đăng ký hội viên tại quầy không dùng thẻ thành viên (chặn sai định dạng Họ tên có số/ký tự đặc biệt, SĐT, Email, chưa đủ 18 tuổi)
    var custMgmtCtrl = Prepare(new CustomerManagementController(users, db, ledger, segmentSvc, voucherSvc), staffPrincipal);
    var minorPhone = "09" + Random.Shared.Next(10000000, 99999999);
    await custMgmtCtrl.QuickEnroll(new QuickEnrollInputModel
    {
        FullName = "Nguyễn Văn Nhỏ Tuổi",
        PhoneNumber = minorPhone,
        Email = $"minor.{key[..6]}@bms.local",
        DateOfBirth = VnTime.Today.AddYears(-16),
        IsCccdVerified = true,
        ReturnTo = "checkout"
    });
    var minorCreated = await db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == minorPhone);

    // Kiểm thử chặn Họ tên chứa số/ký tự đặc biệt, SĐT sai format, Email sai format -> không lưu DB
    var badSyntaxPhone = "09" + Random.Shared.Next(10000000, 99999999);
    await custMgmtCtrl.QuickEnroll(new QuickEnrollInputModel
    {
        FullName = "Nguyễn Văn A 123@",
        PhoneNumber = badSyntaxPhone,
        Email = $"badsyntax.{key[..6]}@bms.local",
        DateOfBirth = VnTime.Today.AddYears(-22),
        IsCccdVerified = true,
        ReturnTo = "checkout"
    });
    var badNameCreated = await db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == badSyntaxPhone);

    await custMgmtCtrl.QuickEnroll(new QuickEnrollInputModel
    {
        FullName = "Nguyễn Văn Hợp Lệ",
        PhoneNumber = "1901234567",
        Email = $"badphone.{key[..6]}@bms.local",
        DateOfBirth = VnTime.Today.AddYears(-22),
        IsCccdVerified = true,
        ReturnTo = "checkout"
    });
    var badPhoneCreated = await db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == "1901234567");

    await custMgmtCtrl.QuickEnroll(new QuickEnrollInputModel
    {
        FullName = "Nguyễn Văn Hợp Lệ",
        PhoneNumber = badSyntaxPhone,
        Email = "saicuphap@gmail",
        DateOfBirth = VnTime.Today.AddYears(-22),
        IsCccdVerified = true,
        ReturnTo = "checkout"
    });
    var badEmailCreated = await db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == badSyntaxPhone);

    var adultPhone = "09" + Random.Shared.Next(10000000, 99999999);
    var adultEmail = $"adult.{key[..6]}@bms.local";
    var adultDob = VnTime.Today.AddYears(-22);
    var enrollRes = await custMgmtCtrl.QuickEnroll(new QuickEnrollInputModel
    {
        FullName = "Nguyễn Văn Trưởng Thành",
        PhoneNumber = adultPhone,
        Email = adultEmail,
        DateOfBirth = adultDob,
        IsCccdVerified = true,
        ReturnTo = "checkout"
    }) as RedirectToActionResult;
    var adultUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PhoneNumber == adultPhone);
    Check(!minorCreated
        && !badNameCreated
        && !badPhoneCreated
        && !badEmailCreated
        && adultUser != null
        && adultUser.EmployeeCode == null
        && adultUser.Email == adultEmail
        && adultUser.DateOfBirth == adultDob
        && adultUser.RewardPoints == 10
        && enrollRes?.ControllerName == "LoyaltyTest",
        "T20: Counter QuickEnroll rejects invalid FullName (numbers/symbols), invalid PhoneNumber, invalid Email, or under-18 CCCD, and registers valid 18+ member (+10 welcome points)");

    // T21: Thanh toán tại quầy (POS Checkout) hỗ trợ Khách lẻ & Hội viên qua 2 hình thức VietQR / Tiền mặt + tích hợp bàn chơi & F&B
    var posCtrl = Prepare(new LoyaltyTestController(db, loyalty, ledger, tierEval, checkout), staffPrincipal);
    await posCtrl.CommitCheckout(
        customerId: null,
        referenceCode: $"GUEST-SHORT-{key[..6]}",
        playtimeAmount: 180000m,
        fandBAmount: 70000m,
        pointsToRedeem: 0,
        voucherCode: null,
        customerMode: "guest",
        paymentMethod: "Cash",
        cashReceived: 200000m);
    var shortErr = posCtrl.TempData["Error"]?.ToString() ?? "";

    posCtrl.TempData.Clear();
    await posCtrl.CommitCheckout(
        customerId: null,
        referenceCode: $"GUEST-OK-{key[..6]}",
        playtimeAmount: 180000m,
        fandBAmount: 70000m,
        pointsToRedeem: 0,
        voucherCode: null,
        customerMode: "guest",
        paymentMethod: "Cash",
        cashReceived: 500000m);
    var guestCashSuccess = posCtrl.TempData["Success"]?.ToString() ?? "";

    await posCtrl.CommitCheckout(
        customerId: adultUser!.Id,
        referenceCode: $"MEM-QR-{key[..6]}",
        playtimeAmount: 200000m,
        fandBAmount: 40000m,
        pointsToRedeem: 0,
        voucherCode: null,
        customerMode: "member",
        paymentMethod: "VietQR",
        cashReceived: null);
    var ptsWhileQrPending = await db.Users.AsNoTracking().Where(u => u.Id == adultUser.Id).Select(u => u.RewardPoints).SingleAsync();
    var qrReference = $"MEM-QR-{key[..6]}".ToUpperInvariant();
    var qrIntent = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.ReferenceCode == qrReference);
    if (qrIntent == null)
        throw new Exception($"FAIL: VietQR intent was not created. Error={posCtrl.TempData["Error"]}; Warning={posCtrl.TempData["Warning"]}");
    var wrongAmount = await checkout.ConfirmTransferAsync(qrIntent.ReferenceCode, $"SEPAY-WRONG-{key[..6]}", qrIntent.ExpectedAmount - 1000m);
    var ptsAfterWrongQr = await db.Users.AsNoTracking().Where(u => u.Id == adultUser.Id).Select(u => u.RewardPoints).SingleAsync();
    var qrConfirmed = await checkout.ConfirmTransferAsync(qrIntent.ReferenceCode, $"SEPAY-OK-{key[..6]}", qrIntent.ExpectedAmount);
    if (qrIntent.RequiresStaffConfirmation)
    {
        await checkout.ConfirmTransferManuallyAsync(qrIntent.ReferenceCode, staff.Id);
    }
    var ptsAfterQr = await db.Users.AsNoTracking().Where(u => u.Id == adultUser.Id).Select(u => u.RewardPoints).SingleAsync();

    await posCtrl.CommitCheckout(
        customerId: adultUser.Id,
        referenceCode: $"MEM-CASH-{key[..6]}",
        playtimeAmount: 160000m,
        fandBAmount: 40000m,
        pointsToRedeem: 0,
        voucherCode: null,
        customerMode: "member",
        paymentMethod: "Cash",
        cashReceived: 500000m);
    var ptsAfterCash = await db.Users.AsNoTracking().Where(u => u.Id == adultUser.Id).Select(u => u.RewardPoints).SingleAsync();

    Check(shortErr.Contains("nhỏ hơn")
        && guestCashSuccess.Contains("250.000")
        && ptsWhileQrPending == 10
        && !wrongAmount.Success && ptsAfterWrongQr == 10
        && qrConfirmed.Success
        && ptsAfterQr == 22
        && ptsAfterCash == 32,
        "T21: POS Checkout records cash atomically and only awards VietQR points after an exact, confirmed bank transaction");

    // T22: Voucher BMS-XXXX-XXXX hạn dùng 7 ngày; LoyaltyDailyJob quét hết hạn voucher (Expired) & thu hồi điểm thưởng hồ sơ >100 ngày không giao dịch
    await ledger.PostAsync(adultUser.Id, rewardItem.PointCost + 50, PointTransaction.Bonus, $"T22-BONUS-{key[..6]}", null, "Cấp điểm kiểm thử T22", staff.Id);
    var redeemT22 = await voucherSvc.RedeemRewardForVoucherAsync(adultUser.Id, rewardItem.Code);
    var vT22 = redeemT22.Voucher!;
    bool isBmsFormat = System.Text.RegularExpressions.Regex.IsMatch(vT22.Code, @"^BMS-[A-Z0-9]{4}-[A-Z0-9]{4}$");
    int validDaysT22 = (int)Math.Round((vT22.ExpiresAtUtc - vT22.IssuedAtUtc).TotalDays);

    await db.CustomerVouchers.Where(v => v.Id == vT22.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(v => v.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-5)));
    await db.Users.Where(u => u.Id == adultUser.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastPaidDate, DateTime.UtcNow.AddDays(-105)));
    db.ChangeTracker.Clear();

    var dailySummary = await campaignSvc.RunAllDailyTasksAsync(DateTime.UtcNow);
    var vT22After = await db.CustomerVouchers.AsNoTracking().SingleAsync(v => v.Id == vT22.Id);
    var adultAfterDaily = await db.Users.AsNoTracking().SingleAsync(u => u.Id == adultUser.Id);
    var hasExpire100Tx = await db.PointTransactions.AnyAsync(t => t.CustomerId == adultUser.Id && t.TransactionType == PointTransaction.Expire && t.Reason!.Contains("100 ngày"));

    Check(redeemT22.Success
        && isBmsFormat
        && validDaysT22 == 7
        && dailySummary.VouchersExpired >= 1
        && vT22After.Status == CustomerVoucher.StatusExpired
        && dailySummary.PointsExpiredUsers == 0
        && !hasExpire100Tx,
        "T22: Redeem issues BMS-XXXX-XXXX voucher with 7-day validity; daily cycle expires overdue vouchers and does NOT reclaim points (no point expiry in scope v1)");

    // T23: Manager tạo thêm bàn & điều chỉnh trạng thái bàn; Staff mở bàn chọn 1 trong 2 chế độ (Combo hoặc Tính giờ, không chạy song song)
    var newTableCode = "VIP-SM-" + key[..4];
    await mgrPricingCtrl.SaveTable(0, newTableCode, testType.Id, 2, "Available");
    var createdTable = await db.BilliardTables.AsNoTracking().SingleAsync(t => t.TableCode == newTableCode);
    await mgrPricingCtrl.UpdateTableStatus(createdTable.Id, "Maintenance");
    var statusMaint = (await db.BilliardTables.AsNoTracking().SingleAsync(t => t.Id == createdTable.Id)).Status;
    await mgrPricingCtrl.UpdateTableStatus(createdTable.Id, "Available");
    var statusAvail = (await db.BilliardTables.AsNoTracking().SingleAsync(t => t.Id == createdTable.Id)).Status;

    var tableSessionSvc = scope.ServiceProvider.GetRequiredService<ITableSessionService>();
    var tableCards = await tableSessionSvc.GetTableCardsAsync();
    var activeCombos = await tableSessionSvc.GetActiveCombosAsync();
    Check(createdTable.FloorNumber == 2
        && statusMaint == "Maintenance"
        && statusAvail == "Available"
        && tableCards.Any(c => c.Id == createdTable.Id)
        && activeCombos.Any(c => c.Id == savedCombo.Id),
        "T23: Manager creates new tables & updates table status (Available <-> Maintenance); TableSessionService loads table grid and active combos");

    // T24: Tính tiền giờ chơi theo đúng số phút thực tế (bỏ tối thiểu 15 phút đầu) & mỗi phiên Combo chỉ áp dụng duy nhất 1 gói Combo
    int mins1 = TableSessionService.CalculateBilledMinutes(60, false);
    int mins3 = TableSessionService.CalculateBilledMinutes(125, false);
    var rejectMultiQty = await tableSessionSvc.AddSessionComboAsync(1, savedCombo.Id, 2, staff.Id);
    Check(mins1 == 1
        && mins3 == 3
        && !rejectMultiQty.Success
        && rejectMultiQty.ErrorCode == 51712,
        "T24: Hourly playtime bills exact elapsed minutes without 15-minute floor; Combo mode enforces max 1 package per session");

    // T25: Chuẩn hóa chỉ sử dụng 4 trạng thái bàn (Còn trống, Đang sử dụng, Đã hết giờ Combo (Tính vượt giờ), Bảo trì) xuyên suốt BE/FE/DB
    var allowedDisplayStatuses = new HashSet<string>
    {
        "Còn trống",
        "Đang sử dụng",
        "Đã hết giờ Combo (Tính vượt giờ)",
        "Bảo trì"
    };
    await mgrPricingCtrl.UpdateTableStatus(createdTable.Id, "AwaitingPayment");
    var rejectLegacyStatusErr = mgrPricingCtrl.TempData["Error"]?.ToString() ?? "";
    var cardsAfterStd = await tableSessionSvc.GetTableCardsAsync();
    var b01Detail = await tableSessionSvc.GetTableDetailAsync(1);
    var loyaltyAuth = typeof(LoyaltyTestController).GetCustomAttribute<AuthorizeAttribute>();
    var quickEnrollAuth = typeof(CustomerManagementController).GetMethod(nameof(CustomerManagementController.QuickEnroll))?.GetCustomAttribute<AuthorizeAttribute>();
    Check(TableSessionService.MapDisplayStatus("Available") == "Còn trống"
        && TableSessionService.MapDisplayStatus("InUse", false) == "Đang sử dụng"
        && TableSessionService.MapDisplayStatus("InUse", true) == "Đã hết giờ Combo (Tính vượt giờ)"
        && TableSessionService.MapDisplayStatus("Maintenance") == "Bảo trì"
        && !string.IsNullOrWhiteSpace(rejectLegacyStatusErr)
        && cardsAfterStd.All(c => allowedDisplayStatuses.Contains(c.DisplayStatus))
        && b01Detail != null && allowedDisplayStatuses.Contains(b01Detail.DisplayStatus)
        && loyaltyAuth?.Roles == "Staff"
        && quickEnrollAuth?.Roles == "Staff",
        "T25: Table statuses strictly standardized to 4 states; Manager excluded from POS Checkout & QuickEnroll");

    // T26: Kiểm tra điểm/tiền từ 0 (chặn số âm), đồng bộ RewardItem -> Voucher và cảnh báo Hạng tối thiểu (MinTier) ở bước Thanh toán
    var rewardCtrl = Prepare(new RewardItemController(db), managerPrincipal);
    await rewardCtrl.Save(0, "NEG_TEST", "Quà âm tiền", null, 0, "Gift", -1000m, 7, silverTier.Id, true);
    var negDiscountErr = rewardCtrl.TempData["Error"]?.ToString() ?? "";
    var negExists = await db.RewardItems.AnyAsync(r => r.Code == "NEG_TEST");

    rewardCtrl.TempData.Clear();
    await rewardCtrl.Save(0, "COMBO_SILVER", "Nước ngọt free Silver", "Tặng nước cho hạng Silver", 0, "BillDiscount", 20000m, 7, silverTier.Id, true);
    var zeroPointItem = await db.RewardItems.FirstOrDefaultAsync(r => r.Code == "COMBO_SILVER");

    // Cho khách Silver đổi quà 0 điểm -> thành công
    await db.Users.Where(u => u.Id == tv2Customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TierId, silverTier.Id));
    var zeroRedeemRes = await voucherSvc.RedeemRewardForVoucherAsync(tv2Customer.Id, "COMBO_SILVER");
    var issuedVoucherCode = zeroRedeemRes.Voucher?.Code ?? "";

    // Manager cập nhật giá trị quy đổi của COMBO_SILVER từ 20.000 -> 35.000 VND -> Voucher đang Active tự động đồng bộ
    await rewardCtrl.Save(zeroPointItem!.Id, "COMBO_SILVER", "Nước ngọt free Silver", "Tặng nước cho hạng Silver", 0, "BillDiscount", 35000m, 7, silverTier.Id, true);
    var syncedVoucher = await db.CustomerVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == issuedVoucherCode);

    // Điều chỉnh hạ hạng hội viên xuống Member -> Khi áp mã COMBO_SILVER ở bước Thanh toán (PreviewCheckoutAsync) phải cảnh báo lỗi không đủ hạng!
    await db.Users.Where(u => u.Id == tv2Customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TierId, memberTier.Id));
    var ineligibleTierPreview = await loyalty.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(
        CustomerId: tv2Customer.Id,
        ReferenceCode: "REF-TIER-WARN",
        InvoiceId: null,
        PlaytimeAmount: 100000m,
        FandBAmount: 50000m,
        PointsToRedeem: 0,
        VoucherCode: "COMBO_SILVER",
        CashierId: staff.Id));

    Check(!negExists
        && !string.IsNullOrWhiteSpace(negDiscountErr)
        && zeroPointItem != null && zeroPointItem.PointCost == 0
        && zeroRedeemRes.Success && !string.IsNullOrWhiteSpace(issuedVoucherCode)
        && syncedVoucher != null && syncedVoucher.DiscountValue == 35000m
        && !ineligibleTierPreview.IsValid
        && ineligibleTierPreview.VoucherDiscountAmount == 0m
        && ineligibleTierPreview.Errors.Any(e => e.Contains("Silver", StringComparison.OrdinalIgnoreCase)),
        "T26: RewardItem allows 0 points/money, blocks negative numbers, syncs active vouchers on edit, and warns/blocks ineligible tier voucher at POS checkout");

    // T27: TierConfig cho phép thêm/xóa hạng thành viên mới; Pricing.SaveTable tích hợp tạo theo số lượng bàn (quantity >= 1) ngay trong 1 form
    var tierCfgCtrl = Prepare(new TierConfigController(db, tierEval), managerPrincipal);
    var newTierName = "Platinum-" + key[..4];
    await tierCfgCtrl.AddTier(newTierName, 12000000m, 15m, 3);
    var addedTier = await db.MembershipTiers.AsNoTracking().FirstOrDefaultAsync(t => t.TierName == newTierName);

    var multiPrefix = "B-MULTI-" + key[..3] + "-";
    await mgrPricingCtrl.SaveTable(0, multiPrefix + "01", testType.Id, 3, "Available", quantity: 3);
    var createdMultiCount = await db.BilliardTables.CountAsync(t => t.TableCode.StartsWith(multiPrefix));

    if (addedTier != null)
    {
        await tierCfgCtrl.DeleteTier(addedTier.Id);
    }
    var deletedTierGone = addedTier != null && !await db.MembershipTiers.AnyAsync(t => t.Id == addedTier.Id);

    Check(addedTier != null
        && addedTier.SpendThreshold == 12000000m
        && addedTier.DiscountPercent == 15m
        && addedTier.PointMultiplier == 3
        && createdMultiCount == 3
        && deletedTierGone,
        "T27: TierConfig supports adding & deleting membership tiers; Pricing.SaveTable creates single or multiple tables via quantity field");

    // T28: QR không cần đăng nhập -> Pending; chỉ Staff duyệt mới Served, trừ kho và cộng F&B; chặn thanh toán khi còn đơn chờ.
    var orderProduct = await db.Products.FirstAsync(p => p.IsActive && p.StockQuantity >= 2);
    int stockBeforeOrder = orderProduct.StockQuantity;
    var orderSession = new PlaySession
    {
        TableId = createdTable.Id,
        CustomerId = null,
        OpenedById = staff.Id,
        StartAtUtc = DateTime.UtcNow.AddMinutes(-30),
        HourlyRateSnapshot = 60000m,
        Status = "Active"
    };
    db.PlaySessions.Add(orderSession);
    await db.SaveChangesAsync();

    var pendingOrder = new Order
    {
        SessionId = orderSession.Id,
        OrderTimeUtc = DateTime.UtcNow,
        Status = OrderConstants.StatusPending,
        OrderDetails = new List<OrderDetail>
        {
            new() { ProductId = orderProduct.Id, Quantity = 2, UnitPrice = orderProduct.Price }
        }
    };
    db.Orders.Add(pendingOrder);
    await db.SaveChangesAsync();

    decimal billableBeforeApproval = await db.OrderDetails
        .Where(d => d.Order.SessionId == orderSession.Id && d.Order.Status == OrderConstants.StatusServed)
        .SumAsync(d => (decimal?)d.Quantity * d.UnitPrice) ?? 0m;
    var blockedByPending = await checkout.CreateTransferIntentAsync(new CheckoutCommand(
        null, $"ORDER-PENDING-{key[..6]}", orderSession.Id, 0, 0, 0, null, staff.Id));

    var orderController = Prepare(new OrderController(
        db, users, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderController>.Instance), staffPrincipal);
    await orderController.ApproveOrder(pendingOrder.Id);
    db.ChangeTracker.Clear();

    var approvedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == pendingOrder.Id);
    int stockAfterApproval = await db.Products.AsNoTracking()
        .Where(p => p.Id == orderProduct.Id).Select(p => p.StockQuantity).SingleAsync();
    decimal billableAfterApproval = await db.OrderDetails
        .Where(d => d.Order.SessionId == orderSession.Id && d.Order.Status == OrderConstants.StatusServed)
        .SumAsync(d => (decimal?)d.Quantity * d.UnitPrice) ?? 0m;

    await orderController.ApproveOrder(pendingOrder.Id);
    int stockAfterSecondApproval = await db.Products.AsNoTracking()
        .Where(p => p.Id == orderProduct.Id).Select(p => p.StockQuantity).SingleAsync();

    var transferReady = await checkout.CreateTransferIntentAsync(new CheckoutCommand(
        null, $"ORDER-READY-{key[..6]}", orderSession.Id, 0, 0, 0, null, staff.Id));
    int orderCountBeforeLockedSubmit = await db.Orders.CountAsync(o => o.SessionId == orderSession.Id);
    var anonymousOrderController = Prepare(new OrderController(
        db, users, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderController>.Instance));
    await anonymousOrderController.SubmitTable(createdTable.TableCode, new SubmitOrderInputModel
    {
        Items = new List<OrderItemInputModel>
        {
            new() { ProductId = orderProduct.Id, Quantity = 1 }
        }
    });
    int orderCountAfterLockedSubmit = await db.Orders.CountAsync(o => o.SessionId == orderSession.Id);
    var orderApproveAuth = typeof(OrderController).GetMethod(nameof(OrderController.ApproveOrder))?.GetCustomAttribute<AuthorizeAttribute>();

    Check(billableBeforeApproval == 0m
        && !blockedByPending.Success && blockedByPending.Message.Contains("chờ Staff")
        && approvedOrder.Status == OrderConstants.StatusServed
        && stockAfterApproval == stockBeforeOrder - 2
        && billableAfterApproval == orderProduct.Price * 2
        && stockAfterSecondApproval == stockAfterApproval
        && transferReady.Pending
        && orderCountAfterLockedSubmit == orderCountBeforeLockedSubmit
        && orderApproveAuth?.Roles == "Staff",
        "T28: Anonymous table QR creates Pending flow; only Staff approval makes order billable, decrements stock once, blocks checkout while pending, and locks ordering during pending transfer");

    // ══════════════════════════════════════════════════════════════
    // WF2 PHASE 1 TEST: Category, Product, Price History (BR-15), Combo
    // ══════════════════════════════════════════════════════════════
    var productSvc = scope.ServiceProvider.GetRequiredService<IProductService>();
    var comboSvc = scope.ServiceProvider.GetRequiredService<IComboService>();

    // 1. UC-08: Tao danh muc
    var catRes = await productSvc.SaveCategoryAsync(null, "Nuoc uong test " + key[..6]);
    Check(catRes.Success, "WF2 P1: Category creation");
    var createdCat = (await productSvc.GetAllCategoriesAsync()).First(c => c.Name.Contains(key[..6]));

    // 2. UC-09: Tao san pham
    var prodInput = new ProductInputModel
    {
        CategoryId = createdCat.Id,
        Name = "Tra chanh test " + key[..6],
        Price = 15000m,
        StockQuantity = 20,
        IsActive = true
    };
    var prodRes = await productSvc.CreateProductAsync(prodInput, adminUser.Id);
    Check(prodRes.Success, "WF2 P1: Product creation");
    var createdProd = (await productSvc.GetAllProductsAsync()).First(p => p.Name == prodInput.Name);
    Check(createdProd.Price == 15000m && createdProd.StockQuantity == 20, "WF2 P1: Initial product price and stock");

    // 3. BR-15: Doi gia ban phai luu ProductPriceHistory
    var editInput = new ProductInputModel
    {
        Id = createdProd.Id,
        CategoryId = createdCat.Id,
        Name = createdProd.Name,
        Price = 20000m, // Tang tu 15k -> 20k
        StockQuantity = createdProd.StockQuantity,
        IsActive = true,
        PriceChangeReason = "Tang gia theo khao sat thi truong"
    };
    var updateRes = await productSvc.UpdateProductAsync(editInput, adminUser.Id);
    Check(updateRes.Success, "WF2 P1: Product price update");

    var history = await productSvc.GetPriceHistoryAsync(createdProd.Id);
    Check(history.Count == 1 && history[0].OldPrice == 15000m && history[0].NewPrice == 20000m,
        "WF2 P1: BR-15 ProductPriceHistory logged correctly on price modification");

    // 4. UC-06: Tao Combo gom gio choi + mon kem + loai ban
    var poolType = await db.TableTypes.FirstAsync();
    var comboInput = new ComboInputModel
    {
        Name = "Combo 2H + 2 Nuoc " + key[..6],
        Price = 180000m,
        PlaytimeHours = 2.0m,
        TableTypeId = poolType.Id,
        IsActive = true,
        Items = new List<ComboItemInputModel>
        {
            new() { ProductId = createdProd.Id, Quantity = 2 }
        }
    };
    var comboRes = await comboSvc.SaveComboAsync(comboInput, adminUser.Id);
    Check(comboRes.Success, "WF2 P1: UC-06 Combo creation with playtime hours, bundled items, and table type");

    var createdCombo = (await comboSvc.GetAllCombosAsync()).First(c => c.Name.Contains(key[..6]));
    Check(createdCombo.Items.Count == 1 && createdCombo.Items[0].Quantity == 2 && createdCombo.PlaytimeHours == 2.0m,
        "WF2 P1: Combo items and hours verified");

    // ══════════════════════════════════════════════════════════════
    // WF2 PHASE 2 TEST: Stock Receipt (UC-11), Ledger & Audit (UC-13), Demo Step 1
    // ══════════════════════════════════════════════════════════════
    var inventorySvc = scope.ServiceProvider.GetRequiredService<IInventoryService>();

    // 1. Tạo 1 sản phẩm mới với tồn kho = 0 (Lon Coca Demo)
    var demoDrinkName = "Coca Cola Demo " + key[..6];
    var demoDrinkInput = new ProductInputModel
    {
        CategoryId = createdCat.Id,
        Name = demoDrinkName,
        Price = 12000m, // Giá bán ban đầu 12k
        StockQuantity = 0,
        IsActive = true
    };
    var createDrinkRes = await productSvc.CreateProductAsync(demoDrinkInput, adminUser.Id);
    Check(createDrinkRes.Success, "WF2 P2: Demo product created with 0 stock");
    var demoDrink = (await productSvc.GetAllProductsAsync()).First(p => p.Name == demoDrinkName);

    // 2. Demo Bước 1: Nhập 24 lon nước có giá nhập (CostPrice)
    var receiptCode = "PN-TEST-" + key[..6];
    var receiptInput = new StockReceiptInputModel
    {
        ReceiptCode = receiptCode,
        SupplierName = "Nha phan phoi Coca Horeca",
        Note = "Nhap 1 thung 24 lon theo kich ban Demo",
        Items = new List<StockReceiptItemInputModel>
        {
            new() { ProductId = demoDrink.Id, Quantity = 24, CostPrice = 8500m }
        }
    };
    var receiptRes = await inventorySvc.CreateReceiptAsync(receiptInput, adminUser.Id);
    Check(receiptRes.Success, "WF2 P2: UC-11 Stock receipt creation (24 cans with CostPrice=8,500)");

    // Kiểm tra tồn kho tăng lên 24 và giá nhập được lưu
    var demoDrinkAfterReceipt = await productSvc.GetProductByIdAsync(demoDrink.Id);
    var receipts = await inventorySvc.GetReceiptsAsync();
    var createdReceipt = receipts.First(r => r.ReceiptCode == receiptCode);
    var receiptDetail = await inventorySvc.GetReceiptDetailAsync(createdReceipt.Id);
    Check(demoDrinkAfterReceipt?.StockQuantity == 24
        && createdReceipt.TotalAmount == 24 * 8500m
        && receiptDetail?.Details.Count == 1
        && receiptDetail.Details[0].CostPrice == 8500m,
        "WF2 P2: Stock increased to 24 and cost price recorded");

    // 3. Demo Bước 1 (tiếp): Đổi giá bán sau khi nhập kho -> kiểm tra lịch sử giá BR-15
    var updatePriceInput = new ProductInputModel
    {
        Id = demoDrink.Id,
        CategoryId = demoDrink.CategoryId,
        Name = demoDrink.Name,
        Price = 15000m, // Đổi từ 12k -> 15k
        StockQuantity = demoDrinkAfterReceipt!.StockQuantity,
        IsActive = true,
        PriceChangeReason = "Cap nhat gia ban theo khao sat"
    };
    var updateDemoPriceRes = await productSvc.UpdateProductAsync(updatePriceInput, adminUser.Id);
    var demoDrinkAfterPriceChange = await productSvc.GetProductByIdAsync(demoDrink.Id);
    var demoPriceHistory = await productSvc.GetPriceHistoryAsync(demoDrink.Id);
    Check(updateDemoPriceRes.Success
        && demoDrinkAfterPriceChange?.Price == 15000m
        && demoPriceHistory.Count == 1
        && demoPriceHistory[0].OldPrice == 12000m
        && demoPriceHistory[0].NewPrice == 15000m,
        "WF2 P2: Demo Step 1 completed (Receipt 24 cans -> Update selling price with BR-15 history)");

    // 4. UC-13: Thẻ kho / Sổ cái ghi nhận dòng biến động Import với BalanceAfter = 24
    var demoMovements = await inventorySvc.GetStockMovementsAsync(demoDrink.Id);
    Check(demoMovements.Any(m => m.MovementType == StockMovement.TypeImport
        && m.QuantityChange == 24
        && m.BalanceAfter == 24
        && m.ReferenceType == "StockReceipt"),
        "WF2 P2: StockMovement ledger recorded Import with BalanceAfter=24");

    // 5. UC-13: Kiểm kê điều chỉnh tồn (cho WF5) & Test tồn không âm
    var adjustRes = await inventorySvc.AdjustStockAsync(demoDrink.Id, 25, "Kiem ke thuc te thua 1 lon", adminUser.Id);
    var demoDrinkAfterAdjust = await productSvc.GetProductByIdAsync(demoDrink.Id);
    var movementsAfterAdjust = await inventorySvc.GetStockMovementsAsync(demoDrink.Id);
    Check(adjustRes.Success
        && demoDrinkAfterAdjust?.StockQuantity == 25
        && movementsAfterAdjust.First().MovementType == StockMovement.TypeAuditAdjust
        && movementsAfterAdjust.First().BalanceAfter == 25,
        "WF2 P2: UC-13 Audit stock adjustment (from 24 to 25) with ledger record");

    // Test: Tồn không âm khi điều chỉnh số âm bị chặn
    var negativeAdjustRes = await inventorySvc.AdjustStockAsync(demoDrink.Id, -5, "Thu dieu chinh so am", adminUser.Id);
    Check(!negativeAdjustRes.Success, "WF2 P2: Test - Negative stock adjustment rejected (Ton khong am)");

    // Test: Tồn không âm khi xuất kho vượt quá số lượng tồn
    var overDeductRes = await inventorySvc.ChangeStockAsync(demoDrink.Id, -100, StockMovement.TypeOrderDeduct, "Test", null, "Xuat vuot ton", adminUser.Id);
    Check(!overDeductRes.Success, "WF2 P2: Test - Over-deduct exceeding current stock rejected (Ton khong am)");

    // ══════════════════════════════════════════════════════════════
    // WF2 PHASE 3 TEST: Gọi món tại quầy (UC-23), Hủy hoàn kho (UC-25),
    //                   Mở bàn Combo trừ kho món kèm & Chặn hết hàng (UC-06 / BR-07a)
    // ══════════════════════════════════════════════════════════════
    var orderSvc = scope.ServiceProvider.GetRequiredService<IOrderService>();

    // 1. Mở phiên chơi trên bàn để chuẩn bị gọi món
    var counterTableCode = "CNT-" + key[..4];
    await mgrPricingCtrl.SaveTable(0, counterTableCode, testType.Id, 1, "Available");
    var counterTable = await db.BilliardTables.AsNoTracking().SingleAsync(t => t.TableCode == counterTableCode);

    var openSessionRes = await tableSessionSvc.OpenSessionAsync(counterTable.Id, staff.Id);
    Check(openSessionRes.Success, "WF2 P3: Open table session for counter ordering");
    var activeSession = await db.PlaySessions.Include(s => s.BilliardTable).FirstAsync(s => s.TableId == counterTable.Id && s.Status == "Active");

    // 2. UC-23: Thêm món tại quầy (Gọi 2 lon Coca Demo đang có giá 15,000 đ)
    var counterOrderItems = new List<OrderItemInputModel>
    {
        new() { ProductId = demoDrink.Id, Quantity = 2 }
    };
    var counterOrderRes = await orderSvc.CreateCounterOrderAsync(activeSession.Id, counterOrderItems, staff.Id, "Gọi 2 lon Coca tại quầy");
    Console.WriteLine($"counterOrderRes Success={counterOrderRes.Success}, Message={counterOrderRes.Message}");
    Check(counterOrderRes.Success, "WF2 P3: UC-23 Staff adds F&B items at counter");

    // Kiểm tra tồn kho giảm từ 25 -> 23 và thẻ kho ghi nhận OrderDeduct
    var demoDrinkAfterCounterOrder = await productSvc.GetProductByIdAsync(demoDrink.Id);
    var movementsAfterOrder = await inventorySvc.GetStockMovementsAsync(demoDrink.Id);
    var sessionOrders = await orderSvc.GetSessionOrdersAsync(activeSession.Id);
    var tableDetailAfterOrder = await tableSessionSvc.GetTableDetailAsync(counterTable.Id);

    Check(demoDrinkAfterCounterOrder?.StockQuantity == 23
        && movementsAfterOrder.First().MovementType == StockMovement.TypeOrderDeduct
        && movementsAfterOrder.First().QuantityChange == -2
        && movementsAfterOrder.First().BalanceAfter == 23
        && sessionOrders.Count == 1
        && sessionOrders[0].Details.Count == 1
        && sessionOrders[0].Details[0].Quantity == 2
        && sessionOrders[0].TotalAmount == 30000m
        && tableDetailAfterOrder.FandBAmount == 30000m,
        "WF2 P3: Stock deducted by 2 (25->23), ledger logged OrderDeduct, and F&B amount added to bill (30k)");

    // 3. UC-25: Hủy món có lý do (Hủy 1 lon -> Hoàn kho về 24, bill giảm còn 15k)
    var orderDetailToCancel = sessionOrders[0].Details[0];
    var cancelRes = await orderSvc.CancelOrderDetailAsync(orderDetailToCancel.Id, 1, "Khách đổi ý không uống lon thứ 2", staff.Id);
    Check(cancelRes.Success, "WF2 P3: UC-25 Cancel 1 item with reason and return stock");

    // Kiểm tra tồn kho hoàn về 24, thẻ kho ghi OrderCancelReturn, bill còn 15k
    var demoDrinkAfterCancel = await productSvc.GetProductByIdAsync(demoDrink.Id);
    var movementsAfterCancel = await inventorySvc.GetStockMovementsAsync(demoDrink.Id);
    var sessionOrdersAfterCancel = await orderSvc.GetSessionOrdersAsync(activeSession.Id);
    var tableDetailAfterCancel = await tableSessionSvc.GetTableDetailAsync(counterTable.Id);

    Check(demoDrinkAfterCancel?.StockQuantity == 24
        && movementsAfterCancel.First().MovementType == StockMovement.TypeOrderCancelReturn
        && movementsAfterCancel.First().QuantityChange == 1
        && movementsAfterCancel.First().BalanceAfter == 24
        && sessionOrdersAfterCancel[0].Details[0].CancelledQuantity == 1
        && sessionOrdersAfterCancel[0].Details[0].CancelReason == "Khách đổi ý không uống lon thứ 2"
        && sessionOrdersAfterCancel[0].TotalAmount == 15000m
        && tableDetailAfterCancel.FandBAmount == 15000m,
        "WF2 P3: Stock returned by 1 (23->24), ledger logged OrderCancelReturn, and bill reduced to 15k");

    // Test hủy vượt số lượng còn lại -> bị từ chối
    var invalidCancelRes = await orderSvc.CancelOrderDetailAsync(orderDetailToCancel.Id, 5, "Hủy vượt số lượng", staff.Id);
    Check(!invalidCancelRes.Success, "WF2 P3: Test - Cancel exceeding available quantity rejected");

    // 4. Test Concurrency UC-23: Hai người cùng gọi món chỉ còn 1 cái thì chỉ một đơn được xác nhận
    var rareItemInput = new ProductInputModel
    {
        CategoryId = createdCat.Id,
        Name = "Mon Cuoi Cung " + key[..6],
        Price = 25000m,
        StockQuantity = 1, // Chỉ còn đúng 1 cái
        IsActive = true
    };
    await productSvc.CreateProductAsync(rareItemInput, adminUser.Id);
    var rareItem = (await productSvc.GetAllProductsAsync()).First(p => p.Name == rareItemInput.Name);

    // Đơn 1 gọi 1 cái -> Thành công
    var order1Res = await orderSvc.CreateCounterOrderAsync(activeSession.Id, new List<OrderItemInputModel> { new() { ProductId = rareItem.Id, Quantity = 1 } }, staff.Id);
    Check(order1Res.Success, "WF2 P3: Concurrency test - First order for last item succeeds");

    // Đơn 2 gọi 1 cái -> Thất bại do hết hàng
    var order2Res = await orderSvc.CreateCounterOrderAsync(activeSession.Id, new List<OrderItemInputModel> { new() { ProductId = rareItem.Id, Quantity = 1 } }, staff.Id);
    Check(!order2Res.Success, "WF2 P3: Concurrency test - Second concurrent order rejected due to insufficient stock");

    // 5. UC-06 & BR-07a: Mở phiên với Combo có món kèm -> Kho tự động trừ món kèm
    // Sử dụng createdCombo từ Phase 1 (kèm 2x createdProd)
    var comboTableCode = "CB-TBL-" + key[..4];
    await mgrPricingCtrl.SaveTable(0, comboTableCode, testType.Id, 1, "Available");
    var comboTable = await db.BilliardTables.AsNoTracking().SingleAsync(t => t.TableCode == comboTableCode);

    var createdProdBeforeCombo = await productSvc.GetProductByIdAsync(createdProd.Id);
    int stockBeforeCombo = createdProdBeforeCombo!.StockQuantity; // 20

    var openComboRes = await tableSessionSvc.OpenSessionAsync(comboTable.Id, staff.Id, null, createdCombo.Id);
    Check(openComboRes.Success, "WF2 P3: UC-06 Open session with Combo package");

    var createdProdAfterCombo = await productSvc.GetProductByIdAsync(createdProd.Id);
    var movementsAfterCombo = await inventorySvc.GetStockMovementsAsync(createdProd.Id);
    Check(createdProdAfterCombo?.StockQuantity == stockBeforeCombo - 2
        && movementsAfterCombo.First().MovementType == StockMovement.TypeComboDeduct
        && movementsAfterCombo.First().QuantityChange == -2
        && movementsAfterCombo.First().BalanceAfter == stockBeforeCombo - 2,
        "WF2 P3: BR-07a Opening session with Combo automatically deducted bundled items from stock (20->18) with ComboDeduct ledger");

    // 6. Test: Món kèm hết hàng thì không chọn được combo
    // Chỉnh tồn kho của createdProd về 0 bằng AdjustStockAsync
    var adjRes = await inventorySvc.AdjustStockAsync(createdProd.Id, 0, "Chỉnh hết hàng món kèm để test", adminUser.Id);
    Console.WriteLine($"ADJUST RES: Success={adjRes.Success}, Bal={adjRes.BalanceAfter}, Msg={adjRes.Message}");

    var itemsInCombo = await db.ComboItems.Where(ci => ci.ComboId == createdCombo.Id).ToListAsync();
    foreach (var ci in itemsInCombo)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ci.ProductId);
        Console.WriteLine($"COMBO CHECK: ComboId={ci.ComboId}, ProdId={ci.ProductId}, ReqQty={ci.Quantity}, ActualStock={p?.StockQuantity}");
    }

    var failComboTable = await db.BilliardTables.AsNoTracking().FirstAsync(t => t.Status == "Available" && t.Id != comboTable.Id);

    var openOutOfStockComboRes = await tableSessionSvc.OpenSessionAsync(failComboTable.Id, staff.Id, null, createdCombo.Id);
    Console.WriteLine($"DEBUG OUT OF STOCK COMBO: Success={openOutOfStockComboRes.Success}, ErrorCode={openOutOfStockComboRes.ErrorCode}, Message={openOutOfStockComboRes.Message}");
    Check(!openOutOfStockComboRes.Success
        && openOutOfStockComboRes.ErrorCode == 51716,
        "WF2 P3: Test - Combo rejected when bundled item is out of stock (51716 BR-07a)");

    // =========================================================================
    // WF2 PHASE 4: KHÁCH QR & REALTIME NHÂN VIÊN (UC-31, UC-24)
    // =========================================================================

    // 1. Chuẩn bị bàn T01 và sản phẩm test với 24 lon nước
    var qrTableCode = "T01-" + key[..4];
    var qrTable = new BilliardTable
    {
        TableCode = qrTableCode,
        TableTypeId = testType.Id,
        FloorNumber = 1,
        Status = "Available"
    };
    db.BilliardTables.Add(qrTable);
    await db.SaveChangesAsync();

    var qrDrink = new Product
    {
        Name = "Nuoc tang luc QR " + key[..6],
        CategoryId = createdCat.Id,
        Price = 20000m,
        StockQuantity = 0,
        IsActive = true
    };
    db.Products.Add(qrDrink);
    await db.SaveChangesAsync();

    // Nhập kho 24 lon
    var qrReceipt = await inventorySvc.CreateReceiptAsync(new StockReceiptInputModel
    {
        ReceiptCode = "PN-QR-" + key[..6],
        SupplierName = "Nha cung cap RedBull",
        Note = "Nhap 24 lon cho test QR",
        Items = new List<StockReceiptItemInputModel>
        {
            new() { ProductId = qrDrink.Id, Quantity = 24, CostPrice = 12000m }
        }
    }, staff.Id);
    Check(qrReceipt.Success, "WF2 P4: Initial stock of 24 cans imported for QR demo");

    // Mở phiên chơi qrSession1 cho bàn T01
    var openQrSession1Res = await tableSessionSvc.OpenSessionAsync(qrTable.Id, staff.Id);
    Check(openQrSession1Res.Success, "WF2 P4: Open session qrSession1 for Table T01");
    var qrSession1 = await db.PlaySessions.AsNoTracking().FirstAsync(s => s.TableId == qrTable.Id && s.Status == "Active");

    // Khách gọi 2 lon qua QR (mã phiên gửi kèm: qrSession1.Id)
    var qrOrderReq = new QrOrderRequest
    {
        TableCode = qrTable.TableCode,
        ClientSessionId = qrSession1.Id,
        Note = "Mang cho em 2 ly da lanh",
        Items = new List<OrderItemInputModel>
        {
            new() { ProductId = qrDrink.Id, Quantity = 2 }
        }
    };
    var qrOrderRes = await orderSvc.CreateQrOrderAsync(qrOrderReq);
    Check(qrOrderRes.Success && qrOrderRes.OrderId.HasValue, "WF2 P4: UC-31 Customer places QR order for 2 cans");

    // Trước khi nhân viên xác nhận: Tồn kho VẪN LÀ 24 (chưa trừ), đơn ở trạng thái Pending
    var drinkBeforeConfirm = await productSvc.GetProductByIdAsync(qrDrink.Id);
    var pendingOrders = await orderSvc.GetPendingOrdersAsync();
    var ourPendingOrder = pendingOrders.FirstOrDefault(o => o.Id == qrOrderRes.OrderId!.Value);

    Check(drinkBeforeConfirm?.StockQuantity == 24
        && ourPendingOrder != null
        && ourPendingOrder.Status == "Pending"
        && ourPendingOrder.OrderSource == "QR"
        && ourPendingOrder.Details.First().Quantity == 2
        && ourPendingOrder.Details.First().CurrentStock == 24,
        "WF2 P4: UC-24 Order is Pending: stock remains 24, order visible in staff pending queue");

    // 3. UC-24: Nhân viên xác nhận đơn realtime -> Tồn còn 22, món vào bill
    var confirmQrRes = await orderSvc.ConfirmQrOrderAsync(ourPendingOrder!.Id, staff.Id);
    Check(confirmQrRes.Success, "WF2 P4: UC-24 Staff confirms QR order");

    var drinkAfterConfirm = await productSvc.GetProductByIdAsync(qrDrink.Id);
    var movementsAfterConfirm = await inventorySvc.GetStockMovementsAsync(qrDrink.Id);
    var session1Orders = await orderSvc.GetSessionOrdersAsync(qrSession1.Id);
    var tableDetailAfterConfirm = await tableSessionSvc.GetTableDetailAsync(qrTable.Id);

    Check(drinkAfterConfirm?.StockQuantity == 22
        && movementsAfterConfirm.First().MovementType == StockMovement.TypeOrderDeduct
        && movementsAfterConfirm.First().QuantityChange == -2
        && movementsAfterConfirm.First().BalanceAfter == 22
        && session1Orders.Any(o => o.Id == ourPendingOrder.Id && o.Status == "Completed")
        && tableDetailAfterConfirm?.FandBAmount == 40000m,
        "WF2 P4: Demo Step 2: Confirmed QR order reduced stock (24->22), logged OrderDeduct, and added 40k to bill");

    // 4. UC-24: Nhân viên từ chối đơn QR kèm lý do -> Kho không bị trừ
    var qrOrderReq2 = new QrOrderRequest
    {
        TableCode = qrTable.TableCode,
        ClientSessionId = qrSession1.Id,
        Items = new List<OrderItemInputModel>
        {
            new() { ProductId = qrDrink.Id, Quantity = 1 }
        }
    };
    var qrOrderRes2 = await orderSvc.CreateQrOrderAsync(qrOrderReq2);
    Check(qrOrderRes2.Success && qrOrderRes2.OrderId.HasValue, "WF2 P4: Customer places second QR order");

    var rejectRes = await orderSvc.RejectQrOrderAsync(qrOrderRes2.OrderId!.Value, "Hết đá lạnh", staff.Id);
    Check(rejectRes.Success, "WF2 P4: UC-24 Staff rejects QR order with reason");

    var drinkAfterReject = await productSvc.GetProductByIdAsync(qrDrink.Id);
    var order2Entity = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == qrOrderRes2.OrderId!.Value);
    Check(drinkAfterReject?.StockQuantity == 22
        && order2Entity.Status == "Rejected"
        && order2Entity.RejectReason == "Hết đá lạnh",
        "WF2 P4: Rejected order preserved stock at 22 and recorded RejectReason");

    // 5. UC-24 Test rule: Bàn đã Chờ thanh toán thì không xác nhận được đơn
    var qrOrderReq3 = new QrOrderRequest
    {
        TableCode = qrTable.TableCode,
        ClientSessionId = qrSession1.Id,
        Items = new List<OrderItemInputModel>
        {
            new() { ProductId = qrDrink.Id, Quantity = 1 }
        }
    };
    var qrOrderRes3 = await orderSvc.CreateQrOrderAsync(qrOrderReq3);
    Check(qrOrderRes3.Success, "WF2 P4: Create QR order before payment status");

    // Đổi trạng thái bàn thành AwaitingPayment
    await db.Database.ExecuteSqlRawAsync($"UPDATE dbo.BilliardTables SET Status = 'AwaitingPayment' WHERE Id = {qrTable.Id}");

    var confirmWhileAwaitingRes = await orderSvc.ConfirmQrOrderAsync(qrOrderRes3.OrderId!.Value, staff.Id);
    Check(!confirmWhileAwaitingRes.Success
        && confirmWhileAwaitingRes.Message.Contains("Chờ thanh toán"),
        "WF2 P4: Test - Confirmation rejected when table is AwaitingPayment");

    // Đổi lại trạng thái bàn thành InUse để tiếp tục
    await db.Database.ExecuteSqlRawAsync($"UPDATE dbo.BilliardTables SET Status = 'InUse' WHERE Id = {qrTable.Id}");

    // 6. UC-31 Test rule: Chống hack phiên - Trang QR của phiên cũ gửi đơn vào phiên mới thì bị từ chối
    // Kết thúc phiên qrSession1
    var closeRes = await tableSessionSvc.CloseSessionAsync(qrSession1.Id, staff.Id);
    Check(closeRes.Success, "WF2 P4: Closed session qrSession1");

    // Đảm bảo bàn Available trước khi mở phiên mới
    await db.Database.ExecuteSqlRawAsync($"UPDATE dbo.BilliardTables SET Status = 'Available' WHERE Id = {qrTable.Id}");

    // Mở phiên mới qrSession2 cho bàn T01
    var openQrSession2Res = await tableSessionSvc.OpenSessionAsync(qrTable.Id, staff.Id);
    Check(openQrSession2Res.Success, "WF2 P4: Opened new session qrSession2 for table T01");
    var qrSession2 = await db.PlaySessions.AsNoTracking().FirstAsync(s => s.TableId == qrTable.Id && s.Status == "Active");
    Check(qrSession2.Id != qrSession1.Id, "WF2 P4: qrSession2 has a distinct ID from qrSession1");

    // Khách cũ vẫn mở trang QR của phiên qrSession1 (ClientSessionId = qrSession1.Id), cố gửi đơn vào bàn
    var staleSessionOrderReq = new QrOrderRequest
    {
        TableCode = qrTable.TableCode,
        ClientSessionId = qrSession1.Id, // Mã phiên cũ!
        Items = new List<OrderItemInputModel>
        {
            new() { ProductId = qrDrink.Id, Quantity = 2 }
        }
    };
    var staleOrderRes = await orderSvc.CreateQrOrderAsync(staleSessionOrderReq);
    Check(!staleOrderRes.Success
        && staleOrderRes.Message.Contains("Phiên chơi lúc bạn quét mã QR đã kết thúc"),
        "WF2 P4: Test - Stale QR session order rejected when table moved to new session (UC-31 anti-hack)");

    // Kiểm tra phiên qrSession2 không có đơn hàng nào bị gán nhầm
    var session2Orders = await orderSvc.GetSessionOrdersAsync(qrSession2.Id);
    Check(session2Orders.Count == 0, "WF2 P4: New session qrSession2 remains completely clean without rogue orders");

    // TV5 — UC40 mục tiêu doanh thu: công thức thuần, không phụ thuộc dữ liệu
    {
        var t = new RevenueTarget { Id = 1, Year = 2026, Month = 11, TargetAmount = 300_000_000 };
        var mid = RevenueTargetService.Calculate(t, 120_000_000, new DateOnly(2026, 11, 10));
        Check(mid.State == TargetMonthState.Current && mid.PercentAchieved == 40m, "UC40 % đạt = thực thu / mục tiêu");
        Check(mid.ForecastRevenue == 360_000_000m && mid.StatusText == "Đúng tiến độ", "UC40 dự báo = 120tr / 10 ngày × 30 ngày");
        Check(mid.RequiredPerDay == 8_571_429m, "UC40 cần mỗi ngày = 180tr / 21 ngày còn lại (làm tròn lên)");
        var ended = RevenueTargetService.Calculate(t, 250_000_000, new DateOnly(2026, 12, 1));
        Check(ended.State == TargetMonthState.Ended && ended.StatusText == "Không đạt" && ended.RequiredPerDay == 0, "UC40 tháng đã qua thiếu mục tiêu = Không đạt");
        var future = RevenueTargetService.Calculate(t, 0, new DateOnly(2026, 10, 31));
        Check(future.State == TargetMonthState.Future && future.StatusText == "Chưa bắt đầu", "UC40 tháng chưa tới = Chưa bắt đầu");
        Check(RevenueTargetService.GetState(2026, 12, new DateOnly(2027, 1, 5)) == TargetMonthState.Ended, "UC40 so tháng qua năm (12/2026 < 01/2027)");
    }

    Console.WriteLine($"{passed} integration checks passed; fixtures rolled back.");
}
finally { await tx.RollbackAsync(); }

class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
