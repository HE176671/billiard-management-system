using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Real SQL Server integration checks. All fixtures and writes are rolled back.
var services = new ServiceCollection();
services.AddLogging();
services.AddDataProtection().UseEphemeralDataProtectionProvider();
services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(
    Environment.GetEnvironmentVariable("BMS_TEST_CONNECTION") ??
    "Server=localhost;Database=BilliardDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;"));
services.AddIdentity<ApplicationUser, IdentityRole>(o => o.Stores.MaxLengthForKeys = 128)
    .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
await using var tx = await db.Database.BeginTransactionAsync();
var passed = 0;
void Check(bool condition, string label) {
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); passed++;
}
T Prepare<T>(T controller) where T : Controller {
    controller.ControllerContext = new ControllerContext {
        HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
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
    var tierController = Prepare(new MemberTierController(db));
    var threshold = 1000000000;
    while (await db.MembershipTiers.AnyAsync(t => t.MinPoints == threshold)) threshold++;
    Check(await tierController.Create(new() { TierName = "Smoke " + key[..8], MinPoints = threshold, DiscountPercent = 5 }) is RedirectToActionResult, "Tier create");
    var tier = await db.MembershipTiers.SingleAsync(t => t.MinPoints == threshold);
    var duplicateTier = Prepare(new MemberTierController(db));
    Check(await duplicateTier.Create(new() { TierName = "Duplicate " + key[..8], MinPoints = threshold }) is ViewResult && !duplicateTier.ModelState.IsValid, "Duplicate threshold rejected");
    Check(await tierController.Edit(new CreateEditTierViewModel() { Id = tier.Id, TierName = tier.TierName, MinPoints = threshold, DiscountPercent = 10 }) is RedirectToActionResult && tier.DiscountPercent == 10, "Tier update");

    var type = await db.TableTypes.FirstAsync();
    // Use a temporary type so existing configs never interfere with the test.
    var testType = new TableType { Name = "Smoke " + key[..8], HourlyRate = 50000 };
    db.TableTypes.Add(testType); await db.SaveChangesAsync();
    var pricing = Prepare(new PricingController(db));
    Check(await pricing.Create(new() { TableTypeId = testType.Id, StartHour = "08:00", EndHour = "12:00", HourlyRate = 50000 }) is RedirectToActionResult, "Pricing create");
    var config = await db.PricingConfigs.SingleAsync(p => p.TableTypeId == testType.Id);
    var overlap = Prepare(new PricingController(db));
    Check(await overlap.Create(new() { Id = config.Id, TableTypeId = testType.Id, StartHour = "09:00", EndHour = "10:00", HourlyRate = 50000 }) is ViewResult && !overlap.ModelState.IsValid, "Overlap rejected even with forged create ID");
    Check(await pricing.Create(new() { TableTypeId = testType.Id, StartHour = "12:00", EndHour = "14:00", HourlyRate = 60000 }) is RedirectToActionResult, "Adjacent pricing intervals accepted");
    var invalid = Prepare(new PricingController(db));
    Check(await invalid.Create(new() { TableTypeId = testType.Id, StartHour = "25:00", EndHour = "26:00", HourlyRate = 50000 }) is ViewResult && !invalid.ModelState.IsValid, "Out-of-day hours rejected");
    var unknown = Prepare(new PricingController(db));
    Check(await unknown.Create(new() { TableTypeId = int.MaxValue, StartHour = "08:00", EndHour = "10:00", HourlyRate = 50000 }) is ViewResult && !unknown.ModelState.IsValid, "Unknown table type rejected");
    Check(await pricing.Edit(new CreateEditPricingViewModel() { Id = config.Id, TableTypeId = testType.Id, StartHour = "08:00", EndHour = "11:00", HourlyRate = 70000 }) is RedirectToActionResult && config.HourlyRate == 70000, "Pricing update");
    Check(await pricing.Delete(config.Id) is RedirectToActionResult && !await db.PricingConfigs.AnyAsync(p => p.Id == config.Id), "Pricing delete");

    var staff = new ApplicationUser { UserName = "smoke." + key, Email = key + "@example.invalid", FullName = "Smoke staff", TierId = tier.Id };
    Check((await users.CreateAsync(staff, "SmokeOriginal!123")).Succeeded && (await users.AddToRoleAsync(staff, "Staff")).Succeeded, "Identity fixture creation");
    await tierController.Delete(tier.Id);
    Check(await db.MembershipTiers.AnyAsync(t => t.Id == tier.Id), "Referenced tier deletion blocked");
    var employees = Prepare(new EmployeeController(users, db));
    await employees.ResetPassword(new ResetStaffPasswordViewModel() { UserId = staff.Id, NewPassword = "abcdefgh", ConfirmPassword = "abcdefgh" });
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
    var dashboard = Prepare(new DashboardController(db));
    Check(((ViewResult)await dashboard.Index()).Model is DashboardViewModel { TotalTables: > 0 }, "Dashboard live SQL queries");
    staff.TierId = null; customer.TierId = null; await db.SaveChangesAsync();
    await tierController.Delete(tier.Id);
    Check(!await db.MembershipTiers.AnyAsync(t => t.Id == tier.Id), "Unreferenced tier deletion");
    Console.WriteLine($"{passed} integration checks passed; fixtures rolled back.");
}
finally { await tx.RollbackAsync(); }

class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
