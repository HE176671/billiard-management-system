using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

var connection = Environment.GetEnvironmentVariable("BMS_TEST_CONNECTION") ?? throw new Exception("Use a disposable Verification database.");
if (!new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog.Contains("Verification", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Never run against a customer database.");
ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
LoyaltyService Loyalty(ApplicationDbContext d) => new(d, new PointLedgerService(d), new TierEvaluationService(d), new VoucherService(d, new PointLedgerService(d)));
CheckoutService Checkout(ApplicationDbContext d) => new(d, Loyalty(d), NullLogger<CheckoutService>.Instance);
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); passed++; }
await using var db = Open();
await WalletSchema.ApplyAsync(db);
await CheckoutWorkflowSchema.ApplyAsync(db);
await CheckoutWorkflowSchema.ApplyAsync(db);
await PaymentReviewSchema.ApplyAsync(db);
await PaymentReviewSchema.ApplyAsync(db);
await ManualQrSchema.ApplyAsync(db);
await ManualQrSchema.ApplyAsync(db);
var key = Guid.NewGuid().ToString("N");
var memberTier = await db.MembershipTiers.SingleAsync(t => t.TierName == "Member");
var customer = new ApplicationUser { Id = "cc-" + key, UserName = "cc-" + key, FullName = "Checkout customer", TierId = memberTier.Id, SecurityStamp = key };
var actor = new ApplicationUser { Id = "ca-" + key, UserName = "ca-" + key, FullName = "Checkout creator", SecurityStamp = key };
var confirmer = new ApplicationUser { Id = "cf-" + key, UserName = "cf-" + key, FullName = "Checkout confirmer", SecurityStamp = key };
db.Users.AddRange(customer, actor, confirmer);
var customerRole = await db.Roles.SingleAsync(r => r.Name == "Customer");
var staffRole = await db.Roles.SingleAsync(r => r.Name == "Staff");
db.UserRoles.AddRange(new IdentityUserRole<string> { UserId = customer.Id, RoleId = customerRole.Id },
    new IdentityUserRole<string> { UserId = actor.Id, RoleId = staffRole.Id },
    new IdentityUserRole<string> { UserId = confirmer.Id, RoleId = staffRole.Id });
await db.SaveChangesAsync();
var shift = await new ShiftService(db).OpenShiftAsync(actor.Id, 100000, "Checkout verification");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["Payments:SePay:BankCode"] = "MB", ["Payments:SePay:AccountNumber"] = "2107072003"
}).Build();
using var provider = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider();
LoyaltyTestController Controller(ApplicationDbContext d, string? actorId = null)
{
    var http = new DefaultHttpContext { RequestServices = provider, User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, actorId ?? actor.Id), new Claim(ClaimTypes.Role, "Staff") }, "Test")) };
    var controller = new LoyaltyTestController(d, Loyalty(d), new PointLedgerService(d), new TierEvaluationService(d), Checkout(d)) {
        ControllerContext = new ControllerContext { HttpContext = http, RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() },
        TempData = new TempDataDictionary(http, new MemoryTempData())
    };
    controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(controller.ControllerContext);
    return controller;
}
async Task<CheckoutDraft> Draft(string? customerId, decimal play = 300000, decimal fnb = 80000, string method = "VietQR", int? sessionId = null)
{
    var redirect = (RedirectToActionResult)await Controller(db).Index(customerId, customerId == null ? "guest" : "member", sessionId,
        play, fnb, paymentMethod: method);
    return await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == (string)redirect.RouteValues!["referenceCode"]!);
}
CheckoutCommand Command(CheckoutDraft d) => new(d.CustomerId, d.ReferenceCode, d.SessionId, d.PlaytimeAmount, d.FandBAmount,
    d.PointsToRedeem, d.VoucherCode, actor.Id, 10000000);
async Task<LoyaltyTestViewModel> Page(string reference) => (LoyaltyTestViewModel)((ViewResult)await Controller(db).Index(referenceCode: reference)).Model!;

var draft = await Draft(customer.Id);
var another = await Draft(customer.Id);
Check(draft.ReferenceCode != another.ReferenceCode && draft.ReferenceCode.Length == 34, "Server issues independent, compact references");
Check((await Page(draft.ReferenceCode)).ReferenceCode == draft.ReferenceCode, "Reload keeps the same persisted reference");
Check(await Controller(db).Index(customerId: "unknown-customer") is BadRequestObjectResult
    && await Controller(db).Index(customerMode: "guest", sessionId: int.MaxValue) is BadRequestObjectResult,
    "Invalid customer or session cannot create a broken draft");
var checkout = Checkout(db);
var qr = await checkout.SubmitDraftAsync(Command(draft), "VietQR");
Check(qr.Success && qr.Pending && qr.FinalAmount == 380000, "QR creates a pending invoice for the stored checkout: " + qr.Message);
Check(!await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == qr.InvoiceId)
    && !await db.PointTransactions.AnyAsync(p => p.ReferenceCode == draft.ReferenceCode), "Creating QR has no payment or point effects");
var pendingPage = (LoyaltyTestViewModel)((ViewResult)await Controller(db).Index(customerMode: "guest", playtimeAmount: 1, referenceCode: draft.ReferenceCode)).Model!;
Check(pendingPage.IsCheckoutLocked && pendingPage.CanConfirmTransfer && pendingPage.FinalAmount == 380000
    && pendingPage.SelectedCustomerId == customer.Id && pendingPage.SelectedSessionId != null, "Pending page restores authoritative customer, session and amount");
var altered = await checkout.SubmitDraftAsync(Command(draft) with { CustomerId = null, SubmittedPlaytimeAmount = 1 }, "Cash");
Check(!altered.Success && !await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == qr.InvoiceId), "Pending checkout cannot be changed by a forged cash POST");
await Controller(db).ConfirmTransfer(draft.ReferenceCode, false);
Check(!await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == qr.InvoiceId), "Manual controller requires acknowledgement");
Check(!(await checkout.ConfirmTransferManuallyAsync(draft.ReferenceCode, customer.Id)).Success, "Customer cannot confirm an invoice");
await db.Users.Where(u => u.Id == confirmer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
Check(!(await checkout.ConfirmTransferManuallyAsync(draft.ReferenceCode, confirmer.Id)).Success, "Inactive staff cannot confirm an invoice");
await db.Users.Where(u => u.Id == confirmer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
await Controller(db, confirmer.Id).ConfirmTransfer(draft.ReferenceCode, true);
var settled = await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.ReferenceCode == draft.ReferenceCode);
Check(settled.Status == "Confirmed" && settled.ConfirmationMethod == "Manual" && settled.ConfirmedById == confirmer.Id
    && settled.CashierId == actor.Id && settled.ConfirmedAtUtc != null, "Manual confirmation audits creator and actual confirmer separately");
Check(await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == qr.InvoiceId && p.AmountPaid == 380000 && p.ReceivedById == confirmer.Id
    && p.PaymentMethod == "Transfer" && p.WorkShiftId == null), "Manual transfer posts the QR amount and never enters the cash drawer");
Check(await db.PointTransactions.AnyAsync(p => p.ReferenceCode == draft.ReferenceCode && p.Amount == 19 && p.CreatedById == confirmer.Id), "Invoice awards points once and attributes them to confirmer");
Check((await checkout.ConfirmTransferManuallyAsync(draft.ReferenceCode, actor.Id)).Duplicate
    && (await checkout.ConfirmTransferAsync(draft.ReferenceCode, "SEPAY-LATE-" + key, 380000)).Duplicate
    && await db.PaymentTransactions.CountAsync(p => p.InvoiceId == qr.InvoiceId) == 1, "Manual retries and later webhook never settle twice");
var paidPage = await Page(draft.ReferenceCode);
Check(paidPage.IsPaid && paidPage.EarnedPoints == 19 && paidPage.FinalAmount == 380000 && paidPage.ConfirmedByName == confirmer.FullName,
    "Completed page shows persisted receipt and actual points");
Check(!(await checkout.ReopenTransferAsync(draft.ReferenceCode, actor.Id)).Success, "Paid invoice cannot be reopened");

var expired = await Draft(null);
Check((await checkout.SubmitDraftAsync(Command(expired), "VietQR")).Success, "Guest QR can be created");
await db.PaymentIntents.Where(i => i.ReferenceCode == expired.ReferenceCode).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
var expiredPage = await Page(expired.ReferenceCode);
Check(expiredPage.IsGuestMode && expiredPage.HasPendingTransfer && expiredPage.CanConfirmTransfer, "Past QR timestamps do not expire the QR; staff confirmation remains available");
Check((await checkout.ConfirmTransferManuallyAsync(expired.ReferenceCode, confirmer.Id)).Success, "Staff can reconcile money received after QR expiry");
Check((await Page(expired.ReferenceCode)).IsGuestMode && (await Page(expired.ReferenceCode)).EarnedPoints == 0, "Guest checkout remains guest after confirmation");

var edited = await Draft(null);
var editQr = await checkout.SubmitDraftAsync(Command(edited), "VietQR");
var reopened = await checkout.ReopenTransferAsync(edited.ReferenceCode, actor.Id);
Check(reopened.Success && reopened.ReferenceCode != edited.ReferenceCode, "Editing invalidates old intent and issues a new server reference");
Check((await checkout.ReopenTransferAsync(edited.ReferenceCode, actor.Id)).ReferenceCode == reopened.ReferenceCode, "Repeated edit reuses the same replacement");
Check(!(await checkout.ConfirmTransferManuallyAsync(edited.ReferenceCode, confirmer.Id)).Success, "Old QR cannot be manually confirmed after editing");
var replacement = await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == reopened.ReferenceCode);
Check(replacement.InvoiceId == editQr.InvoiceId && replacement.SessionId != null && replacement.IsManualEntry, "Replacement keeps invoice and manual-entry identity");
var replacementPage = await Page(replacement.ReferenceCode);
Check(!replacementPage.IsCheckoutLocked && replacementPage.FandBAmount == 80000 && replacementPage.SelectedSession == null, "Reopening manual receipt retains editable F&B amounts");
var editedCash = await checkout.SubmitDraftAsync(Command(replacement) with { SubmittedFandBAmount = 100000 }, "Cash");
Check(editedCash.Success && editedCash.InvoiceId == editQr.InvoiceId && editedCash.FinalAmount == 400000, "Switch to cash settles the original invoice with edited manual amounts: " + editedCash.Message);
Check((await checkout.SubmitDraftAsync(Command(replacement), "Cash")).Duplicate
    && await db.PaymentTransactions.CountAsync(p => p.InvoiceId == editQr.InvoiceId) == 1, "Repeated cash POST keeps one payment");
Check(!(await checkout.ConfirmTransferAsync(edited.ReferenceCode, "SEPAY-OLD-" + key, 380000)).Success,
    "Replaced QR cannot be settled and creates no review");

async Task<CheckoutPaymentResult> Confirm(string reference, bool manual)
{
    await using var d = Open();
    return manual ? await Checkout(d).ConfirmTransferManuallyAsync(reference, confirmer.Id)
        : await Checkout(d).ConfirmTransferAsync(reference, "SEPAY-RACE-" + reference, 380000);
}
var concurrent = await Draft(null);
var concurrentQr = await checkout.SubmitDraftAsync(Command(concurrent), "VietQR");
var confirmations = await Task.WhenAll(Confirm(concurrent.ReferenceCode, true), Confirm(concurrent.ReferenceCode, false));
Check(confirmations.Count(r => r.Success && !r.Duplicate && !r.Pending) == 1 && await db.PaymentTransactions.CountAsync(p => p.InvoiceId == concurrentQr.InvoiceId) == 1,
    "Concurrent staff and webhook settle exactly once");
var staffRace = await Draft(null);
await checkout.SubmitDraftAsync(Command(staffRace), "VietQR");
var staffResults = await Task.WhenAll(Confirm(staffRace.ReferenceCode, true), Confirm(staffRace.ReferenceCode, true));
Check(staffResults.All(r => r.Success) && staffResults.Count(r => r.Duplicate) == 1, "Two staff confirmations serialize correctly");
var cancelRace = await Draft(null);
var cancelQr = await checkout.SubmitDraftAsync(Command(cancelRace), "VietQR");
async Task<CheckoutPaymentResult> ReopenRace() { await using var d = Open(); return await Checkout(d).ReopenTransferAsync(cancelRace.ReferenceCode, actor.Id); }
var raceResults = await Task.WhenAll(Confirm(cancelRace.ReferenceCode, true), ReopenRace());
Check(raceResults.Count(r => r.Success) == 1, "Editing and confirming cannot both succeed");
var cashDraft = await Draft(null, method: "Cash");
async Task<CheckoutPaymentResult> CashRace() { await using var d = Open(); return await Checkout(d).SubmitDraftAsync(Command(cashDraft), "Cash"); }
var cashResults = await Task.WhenAll(CashRace(), CashRace());
Check(cashResults.All(r => r.Success) && cashResults.Count(r => r.Duplicate) == 1, "Repeated concurrent cash POST creates one invoice");
Check(!(await checkout.SubmitDraftAsync(Command(another) with { ReferenceCode = "FORGED" }, "Cash")).Success, "Unknown client-issued reference is rejected");
Check(!(await checkout.SubmitDraftAsync(Command(another) with { SessionId = replacement.SessionId }, "Cash")).Success, "Forged session cannot replace draft session");

var retryDraft = await Draft(null, method: "Cash");
var beforeRetryCount = await db.PaymentTransactions.CountAsync();
var shortCash = await checkout.SubmitDraftAsync(Command(retryDraft) with { SubmittedPlaytimeAmount = 500000, CashReceived = 1 }, "Cash");
var retryPage = await Page(retryDraft.ReferenceCode);
Check(!shortCash.Success && !retryPage.IsCheckoutLocked && retryPage.ReferenceCode == retryDraft.ReferenceCode
    && retryPage.PlaytimeAmount == 500000 && retryPage.CashReceived == 1 && await db.PaymentTransactions.CountAsync() == beforeRetryCount,
    "Short cash rolls back payment while retaining draft input and reference for retry");
Check((await checkout.SubmitDraftAsync(Command(retryDraft) with { SubmittedPlaytimeAmount = 500000, CashReceived = 580000 }, "Cash")).Success,
    "Corrected cash amount retries the same checkout successfully");

await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 100000));
var beforeWallet = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customer.Id);
var walletDraft = await Draft(customer.Id, 0, 20000, "Wallet");
var walletResult = await checkout.SubmitDraftAsync(Command(walletDraft), "Wallet");
var afterWallet = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customer.Id);
Check(walletResult.Success && afterWallet.WalletBalance == 80000 && afterWallet.RewardPoints == beforeWallet.RewardPoints + 1
    && afterWallet.TotalSpend == beforeWallet.TotalSpend + 20000 && (await Page(walletDraft.ReferenceCode)).IsPaid,
    "Wallet draft settles with points and qualifying spend on the final amount");
Check((await checkout.SubmitDraftAsync(Command(walletDraft), "Wallet")).Duplicate,
    "Wallet draft retry cannot debit twice");

var session = new Bms.Web.Data.Entities.PlaySession { TableId = await db.BilliardTables.Select(t => t.Id).FirstAsync(),
    OpenedById = actor.Id, ClosedById = actor.Id, CustomerId = null, StartAtUtc = DateTime.UtcNow.AddHours(-1), EndAtUtc = DateTime.UtcNow,
    HourlyRateSnapshot = 100000, PlaytimeAmount = 100000, Status = "Closed" };
db.PlaySessions.Add(session); await db.SaveChangesAsync();
var sessionQrDraft = await Draft(null, sessionId: session.Id);
var sessionCashDraft = await Draft(null, method: "Cash", sessionId: session.Id);
var sessionQr = await checkout.SubmitDraftAsync(Command(sessionQrDraft), "VietQR");
var blockedCash = await checkout.SubmitDraftAsync(Command(sessionCashDraft), "Cash");
Check(!blockedCash.Success && blockedCash.ReferenceCode == sessionQrDraft.ReferenceCode,
    "Another checkout cannot pay a session with an unresolved QR; response links existing request");
// Older data may contain multiple intents for one invoice. The invoice lock guards these too.
var legacyOther = new PaymentIntent { InvoiceId = sessionQr.InvoiceId!.Value, SessionId = session.Id, ReferenceCode = "OLD" + Guid.NewGuid().ToString("N"),
    ExpectedAmount = sessionQr.FinalAmount, PlaytimeAmount = sessionQr.FinalAmount, CashierId = actor.Id,
    CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15) };
db.PaymentIntents.Add(legacyOther); await db.SaveChangesAsync();
Check((await checkout.ConfirmTransferManuallyAsync(sessionQrDraft.ReferenceCode, actor.Id)).Success
    && !(await checkout.ConfirmTransferManuallyAsync(legacyOther.ReferenceCode, confirmer.Id)).Success
    && await db.PaymentTransactions.CountAsync(p => p.InvoiceId == sessionQr.InvoiceId) == 1,
    "Different historical references cannot settle one invoice twice");
var csrfActions = new[] { nameof(LoyaltyTestController.ConfirmTransfer), nameof(LoyaltyTestController.ReopenTransfer), nameof(LoyaltyTestController.SwitchToCash) };
Check(csrfActions.All(name => Attribute.IsDefined(typeof(LoyaltyTestController).GetMethod(name)!, typeof(ValidateAntiForgeryTokenAttribute))),
    "Both manual confirmation and editing require anti-forgery validation");
await CashSwitchChecks.RunAsync(Open, Checkout, Controller, actor.Id, confirmer.Id, customer.Id, Check);
await ManualQrChecks.RunAsync(Open, Checkout, Controller, actor.Id, Check);
await WalletEarningChecks.RunAsync(Open, Checkout, Controller, actor.Id, Check);
await SessionCheckoutChecks.RunAsync(Open, Checkout, Controller, actor.Id, Check);
await CheckoutRenderingChecks.RunAsync(connection, actor.Id, await Page(another.ReferenceCode), pendingPage, expiredPage, paidPage, Check);
Console.WriteLine($"ALL {passed} CHECKOUT CHECKS PASSED");

sealed class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
