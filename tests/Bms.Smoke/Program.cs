using System.Reflection;
using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
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
var connString = Environment.GetEnvironmentVariable("BMS_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(connString))
{
    var devSettings = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Bms.Web", "appsettings.Development.json");
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
services.AddScoped<ICustomerSegmentService, CustomerSegmentService>();
services.AddScoped<ICampaignService, CampaignService>();
services.AddScoped<IFinancialAuditService, FinancialAuditService>();
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

    // TV5 ca làm (cần 08_AddWorkShiftReconciliation.sql). Ngoại tx đang mở nên service dùng chung tx.
    var shiftService = new ShiftService(db);
    var opened = await shiftService.OpenShiftAsync(staff.Id, 500000, "smoke");
    Check(opened.Status == WorkShift.StatusActive && opened.StartingCash == 500000, "Shift open");
    var secondRejected = false;
    try { await shiftService.OpenShiftAsync(staff.Id, 0, null); } catch (ShiftException e) { secondRejected = e.Code == "ALREADY_OPEN"; }
    Check(secondRejected, "Second active shift for same staff rejected");
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
    var closeTwice = false;
    try { await shiftService.CloseShiftAsync(opened.Id, staff.Id, 620000, null); } catch (ShiftException e) { closeTwice = e.Code == "ALREADY_CLOSED"; }
    Check(closeTwice, "Closing twice rejected");
    var noShift = false;
    try { await shiftService.RequireActiveShiftIdForCashAsync(staff.Id); } catch (ShiftException e) { noShift = e.Code == "NO_ACTIVE_SHIFT"; }
    Check(noShift, "Cash payment after close rejected");
    var dashboardWithShift = (DashboardViewModel)((ViewResult)await Prepare(new DashboardController(db)).Index()).Model!;
    Check(dashboardWithShift.TotalTables == dashboardWithShift.AvailableTables + dashboardWithShift.InUseTables
        + dashboardWithShift.AwaitingPaymentTables + dashboardWithShift.UnavailableTables, "Dashboard table statuses add up");

    staff.TierId = null; customer.TierId = null; await db.SaveChangesAsync();
    await tierController.Delete(tier.Id);
    Check(!await db.MembershipTiers.AnyAsync(t => t.Id == tier.Id), "Unreferenced tier deletion");

    // =========================================================================
    // TV2 CRM & Loyalty V2 Acceptance Tests (T1 - T16)
    // =========================================================================
    var ledger = scope.ServiceProvider.GetRequiredService<IPointLedgerService>();
    var tierEval = scope.ServiceProvider.GetRequiredService<ITierEvaluationService>();
    var voucherSvc = scope.ServiceProvider.GetRequiredService<IVoucherService>();
    var loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
    var segmentSvc = scope.ServiceProvider.GetRequiredService<ICustomerSegmentService>();
    var campaignSvc = scope.ServiceProvider.GetRequiredService<ICampaignService>();

    var allTiers = await db.MembershipTiers.OrderBy(t => t.MinPoints).ToListAsync();
    var memberTier = allTiers.First(t => t.MinPoints == 0);
    var silverTier = allTiers.First(t => t.TierName == "Silver" || t.MinPoints == 200);
    var goldTier = allTiers.First(t => t.TierName == "Gold" || t.MinPoints == 1000);

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
    var voucherT5 = new CustomerVoucher
    {
        Code = $"BMS-T5-{key[..6].ToUpperInvariant()}",
        CustomerId = tv2Customer.Id,
        Title = "Voucher T5 30k",
        VoucherType = RewardItemEntity.TypeBillDiscount,
        DiscountValue = 30000m,
        Status = CustomerVoucher.StatusActive,
        IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
    };
    db.CustomerVouchers.Add(voucherT5);
    await db.SaveChangesAsync();
    var previewT5 = await loyalty.PreviewCheckoutAsync(new CheckoutLoyaltyRequest(tv2Customer.Id, "PREV-T5", null, 200000m, 100000m, 100, voucherT5.Code, staff.Id));
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

    // T7: Lên hạng khi vượt ngưỡng 12 tháng -> TierId đổi, TierReviewAtUtc = +12 tháng, có Notification
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
        && upUserAfter.TierReviewAtUtc.HasValue && upUserAfter.TierReviewAtUtc.Value > DateTime.UtcNow.AddMonths(11)
        && hasUpgradeNotif,
        "T7: Immediate tier upgrade on 12M spend threshold updates TierId, sets TierReviewAtUtc +12M, and creates Notification");

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
    Check(segNew == CustomerSegmentService.SegNew
        && segChampion == CustomerSegmentService.SegChampion
        && segLoyal == CustomerSegmentService.SegLoyal
        && segAtRisk == CustomerSegmentService.SegAtRisk,
        "T13: RFM ClassifySegment places each fixture into its exact 4-group segment (VIP, Loyal, New, AtRisk)");

    // T14: Chiến dịch gửi 2 lần cho cùng người -> Không phát trùng voucher/điểm
    var campT14 = new Campaign
    {
        Name = "Smoke Campaign T14 " + key[..6],
        CampaignType = Campaign.TypeManual,
        TargetSegment = "All",
        TargetTierId = silverTier.Id,
        BonusPoints = 25,
        MessageTitle = "Ưu đãi T14 {FullName}",
        MessageContent = "Tặng 25 điểm thưởng T14",
        Status = Campaign.StatusDraft,
        CreatedById = adminUser.Id,
        CreatedAtUtc = DateTime.UtcNow
    };
    db.Campaigns.Add(campT14);
    await db.SaveChangesAsync();
    var send1 = await campaignSvc.SendCampaignAsync(campT14.Id, adminUser.Id);
    var send2 = await campaignSvc.SendCampaignAsync(campT14.Id, adminUser.Id);
    var cmpTxCount = await db.PointTransactions.CountAsync(t => t.CustomerId == upCustomer.Id && t.ReferenceCode == $"CMP-{campT14.Id}-{upCustomer.Id}");
    Check(send1.Success && send1.SentCount >= 1 && send2.SentCount == 0 && cmpTxCount == 1,
        "T14: Sending campaign twice does not duplicate bonus points or vouchers for the same customer");

    // T15: Voucher sinh nhật -> Phát 1 lần/năm, chỉ hạng có BirthdayVoucherValue > 0
    var vnToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    var bdayDate = new DateOnly(1998, vnToday.Month, vnToday.Day);
    await db.Users.Where(u => u.Id == upCustomer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DateOfBirth, bdayDate));
    await db.Users.Where(u => u.Id == tv2Customer.Id).ExecuteUpdateAsync(s => s
        .SetProperty(u => u.TierId, memberTier.Id)
        .SetProperty(u => u.DateOfBirth, bdayDate));
    db.ChangeTracker.Clear();
    var bdayRun1 = await campaignSvc.IssueBirthdayVouchersAsync(vnToday);
    var bdayRun2 = await campaignSvc.IssueBirthdayVouchersAsync(vnToday);
    var bdayRefSilver = $"BDAY-{vnToday.Year}-{upCustomer.Id}";
    if (bdayRefSilver.Length > 50) bdayRefSilver = bdayRefSilver[..50];
    var bdayRefMember = $"BDAY-{vnToday.Year}-{tv2Customer.Id}";
    if (bdayRefMember.Length > 50) bdayRefMember = bdayRefMember[..50];
    var silverBdayCount = await db.CustomerVouchers.CountAsync(v => v.CustomerId == upCustomer.Id && v.UsedReferenceCode == bdayRefSilver);
    var memberBdayCount = await db.CustomerVouchers.CountAsync(v => v.CustomerId == tv2Customer.Id && v.UsedReferenceCode == bdayRefMember);
    Check(bdayRun1 >= 1 && bdayRun2 == 0 && silverBdayCount == 1 && memberBdayCount == 0,
        "T15: Birthday voucher issued once per year only for tiers with BirthdayVoucherValue > 0");

    // T16: Staff truy cập /CrmDashboard, /Campaign, duyệt điều chỉnh -> Bị chặn bởi [Authorize(Roles = "Admin")]
    var crmAuth = typeof(CrmDashboardController).GetCustomAttribute<AuthorizeAttribute>();
    var campAuth = typeof(CampaignController).GetCustomAttribute<AuthorizeAttribute>();
    var approveAuth = typeof(PointAdjustmentController).GetMethod(nameof(PointAdjustmentController.Approve))?.GetCustomAttribute<AuthorizeAttribute>();
    var pendingAuth = typeof(PointAdjustmentController).GetMethod(nameof(PointAdjustmentController.Pending))?.GetCustomAttribute<AuthorizeAttribute>();
    var rewardAuth = typeof(RewardItemController).GetCustomAttribute<AuthorizeAttribute>();
    Check(crmAuth?.Roles == "Admin"
        && campAuth?.Roles == "Admin"
        && approveAuth?.Roles == "Admin"
        && pendingAuth?.Roles == "Admin"
        && rewardAuth?.Roles == "Admin",
        "T16: CrmDashboard, Campaign, RewardItem, and PointAdjustment.Approve/Pending enforce [Authorize(Roles = Admin)]");

    // =========================================================================
    // Financial P&L (12 Months), Operating Expenses & Revenue Leakage Audit (T17 - T20)
    // =========================================================================
    var finService = scope.ServiceProvider.GetRequiredService<IFinancialAuditService>();
    await finService.EnsureYearFinancialDataSeededAsync(adminUser.Id);
    var finController = Prepare(new RevenueController(db, finService));

    // T17: Báo cáo P&L 12 tháng năm 2026 (cả Lãi, Lỗ, đỉnh T3-T5, đỉnh nước T4-T6, Ca Đêm > Ca Ngày)
    var yearReport = (RevenueReportViewModel)((ViewResult)await finController.Index(new(2026, 1, 1), new(2026, 12, 31), "month")).Model!;
    var marPnl = yearReport.PnlPeriods.Single(p => p.Period == "2026-03");
    var aprPnl = yearReport.PnlPeriods.Single(p => p.Period == "2026-04");
    var mayPnl = yearReport.PnlPeriods.Single(p => p.Period == "2026-05");
    var junPnl = yearReport.PnlPeriods.Single(p => p.Period == "2026-06");
    var augPnl = yearReport.PnlPeriods.Single(p => p.Period == "2026-08");
    Check(yearReport.PnlPeriods.Count == 12
        && yearReport.ProfitablePeriodsCount >= 8
        && yearReport.LossPeriodsCount >= 3
        && marPnl.IsPeakRevenue && aprPnl.IsPeakRevenue && mayPnl.IsPeakRevenue
        && aprPnl.IsPeakBeverage && mayPnl.IsPeakBeverage && junPnl.IsPeakBeverage
        && augPnl.NetProfit < 0
        && yearReport.DayNight.NightRevenue > yearReport.DayNight.DayRevenue,
        "T17: 12-Month P&L accurately computes profit/loss months, Mar-May revenue peaks, Apr-Jun beverage peaks, and Night > Day shift revenue");

    // T18: Quản lý Sổ cái 4 luồng chi phí mất tiền (UC-FIN-02: Validate, AddExpense, DeleteExpense)
    var invalidExp = await finService.AddExpenseAsync(new CreateExpenseInputModel
    {
        ExpenseDate = new DateOnly(2026, 11, 1),
        Category = OperatingExpense.CatMaintenance,
        Title = "Chi phí âm không hợp lệ",
        Amount = -50000m
    }, adminUser.Id);
    var validExp = await finService.AddExpenseAsync(new CreateExpenseInputModel
    {
        ExpenseDate = new DateOnly(2026, 11, 5),
        Category = OperatingExpense.CatMaintenance,
        Title = "Thay vải nỉ bàn VIP Smoke Test",
        Amount = 3500000m,
        ShiftPeriod = OperatingExpense.ShiftAll
    }, adminUser.Id);
    var delExp = await finService.DeleteExpenseAsync(validExp.Expense!.Id);
    Check(!invalidExp.Success
        && invalidExp.Message == FinancialMessages.MsgFin04InvalidAmount
        && validExp.Success
        && delExp.Success,
        "T18: OperatingExpenses validates amount/category (BR-FIN-05), records new maintenance cost, and deletes cleanly");

    // T19: Kiểm toán & Truy vết 5 kịch bản thất thoát doanh thu (UC-FIN-03: LEAK-01 -> LEAK-05)
    var leakRules = yearReport.LeakageItems.Select(x => x.RuleCode).Distinct().ToHashSet();
    Check(leakRules.Contains("LEAK-01")
        && leakRules.Contains("LEAK-02")
        && leakRules.Contains("LEAK-03")
        && leakRules.Contains("LEAK-04")
        && leakRules.Contains("LEAK-05")
        && yearReport.TotalLeakageAmount > 0,
        "T19: Revenue Leakage Audit Engine detects all 5 anomaly categories (LEAK-01..LEAK-05) with exact loss amounts");

    // T20: Xuất CSV Báo cáo P&L 12 tháng & Biên bản Kiểm toán thất thoát (UC-FIN-04)
    var finCsvResult = await finController.Export(new(2026, 1, 1), new(2026, 12, 31), "month") as FileContentResult;
    var finCsvText = finCsvResult != null ? System.Text.Encoding.UTF8.GetString(finCsvResult.FileContents) : "";
    Check(finCsvResult != null
        && finCsvText.Contains("Loi nhuan rong P&L VND")
        && finCsvText.Contains("LEAK-01")
        && finCsvText.Contains("LEAK-03"),
        "T20: Financial P&L and Leakage Audit CSV export includes P&L summary, expense breakdown, and audit findings");

    Console.WriteLine($"{passed} integration checks passed; fixtures rolled back.");
}
finally { await tx.RollbackAsync(); }

class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
