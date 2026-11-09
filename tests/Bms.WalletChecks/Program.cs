using Microsoft.Extensions.Configuration;
using Bms.Web.Data;
using Bms.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

// Run only on a disposable copy, never a real customer database.
var connection = Environment.GetEnvironmentVariable("BMS_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set BMS_TEST_CONNECTION to a disposable Verification database.");
var parsed = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
if (!parsed.InitialCatalog.Contains("Verification", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Database name must contain Verification.");
ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
WalletService Wallet(ApplicationDbContext context) => new(context, new PointLedgerService(context),
    new TierEvaluationService(context), new CustomerSegmentService(context, new MemoryCache(new MemoryCacheOptions())));
// Preserve regression coverage for historical requests explicitly configured for automatic bank confirmation.
async Task<WalletResult> LegacyAutoQr(ApplicationDbContext context, string customer, decimal amount, string requestId, string actor)
{
    var result = await Wallet(context).CreateTransferIntentAsync(customer, amount, requestId, actor);
    await context.WalletTopUpIntents.Where(i => i.ReferenceCode == result.ReferenceCode).ExecuteUpdateAsync(s => s.SetProperty(i => i.RequiresStaffConfirmation, false));
    context.ChangeTracker.Clear();
    return result;
}
CheckoutService Checkout(ApplicationDbContext context) => new(context,
    new LoyaltyService(context, new PointLedgerService(context), new TierEvaluationService(context), new VoucherService(context, new PointLedgerService(context))),
    Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<CheckoutService>(Microsoft.Extensions.Logging.LoggerFactory.Create(b => Microsoft.Extensions.Logging.ConsoleLoggerExtensions.AddConsole(b))));
int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); passed++;
}
await using var db = Open();
await CheckoutWorkflowSchema.ApplyAsync(db);
await CheckoutWorkflowSchema.ApplyAsync(db);
await PaymentReviewSchema.ApplyAsync(db);
await WalletSchema.ApplyAsync(db);
await WalletSchema.ApplyAsync(db);
await ManualQrSchema.ApplyAsync(db);
var tiers = await db.MembershipTiers.AsNoTracking().ToListAsync();
var member = tiers.Single(t => t.TierName == "Member");
var silver = tiers.Single(t => t.TierName == "Silver");
var gold = tiers.Single(t => t.TierName == "Gold");
Check(silver.SpendThreshold == 2_000_000 && gold.SpendThreshold == 5_000_000, "DB thresholds 2m / 5m");
Check(TierHelper.Resolve(tiers, 0, 100000)?.Id == member.Id, "Points cannot bypass tier thresholds");
var key = Guid.NewGuid().ToString("N");
var user = new ApplicationUser { Id = "wc-" + key, UserName = "wc-" + key, NormalizedUserName = "WC-" + key,
    FullName = "Wallet verification", TierId = member.Id, SecurityStamp = key };
var actor = new ApplicationUser { Id = "wa-" + key, UserName = "wa-" + key, NormalizedUserName = "WA-" + key,
    FullName = "Wallet verification cashier", SecurityStamp = key };
db.Users.AddRange(user, actor);
var customerRole = await db.Roles.SingleAsync(r => r.Name == "Customer");
var staffRole = await db.Roles.SingleAsync(r => r.Name == "Staff");
db.UserRoles.Add(new IdentityUserRole<string> { UserId = actor.Id, RoleId = staffRole.Id });
db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = customerRole.Id });
await db.SaveChangesAsync();
var shift = await new ShiftService(db).OpenShiftAsync(actor.Id, 100000m, "Verification");
var wallet = Wallet(db);
var firstRef = Guid.NewGuid().ToString("N");
Check((await wallet.TopUpAsync(user.Id, 2_000_000, firstRef, actor.Id)).Success, "Top up 2m");
await db.Entry(user).ReloadAsync();
Check(user.WalletBalance == 2_000_000 && user.RewardPoints == 100 && user.TierId == silver.Id && user.TotalSpend == 2_000_000, "2m yields Silver, 100 points, full wallet credit");
Check((await wallet.TopUpAsync(user.Id, 2_000_000, firstRef, actor.Id)).Duplicate, "Repeated top up is idempotent");
Check(!(await wallet.TopUpAsync(user.Id, 3_000_000, firstRef, actor.Id)).Success, "Reused reference with different amount rejected");
Check(!(await wallet.TopUpAsync(user.Id, -1, Guid.NewGuid().ToString("N"), actor.Id)).Success, "Negative top up rejected");
Check(!(await wallet.TopUpAsync(user.Id, 1000.5m, Guid.NewGuid().ToString("N"), actor.Id)).Success, "Fractional top up rejected");
Check(!(await wallet.TopUpAsync(user.Id, 20000, Guid.NewGuid().ToString("N"), user.Id)).Success, "Top up without active shift rejected");
var checkout = Checkout(db);
var invoiceRef = "WC-" + Guid.NewGuid().ToString("N");
var cmd = new CheckoutCommand(user.Id, invoiceRef, null, 100000, 0, 0, null, actor.Id);
var paid = await checkout.CompleteWalletAsync(cmd);
Check(paid.Success && paid.FinalAmount == 95000, "Wallet pays invoice after Silver 5% discount: " + paid.Message);
await db.Entry(user).ReloadAsync();
Check(user.WalletBalance == 1_905_000 && user.TotalSpend == 2_095_000 && user.RewardPoints == 104, "Wallet payment earns four points and 95,000 qualifying spend after discounts");
Check((await checkout.CompleteWalletAsync(cmd)).Duplicate, "Retry wallet payment cannot debit twice");
Check(await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == paid.InvoiceId && p.PaymentMethod == "Wallet" && p.AmountPaid == 95000), "Invoice payment persisted with Wallet method");
Check(await db.PointTransactions.AnyAsync(p => p.ReferenceCode == invoiceRef && p.SpendAmount == 95000 && p.Amount == 4), "Wallet invoice persists earned points and actual eligible spending");
var beforeInvoices = await db.PaymentTransactions.CountAsync();
var insufficient = await checkout.CompleteWalletAsync(cmd with { ReferenceCode = "WC-" + Guid.NewGuid().ToString("N"), SubmittedPlaytimeAmount = 4_000_000 });
Check(!insufficient.Success && await db.PaymentTransactions.CountAsync() == beforeInvoices, "Insufficient funds cannot settle invoice");
db.ChangeTracker.Clear();
Check((await Wallet(db).TopUpAsync(user.Id, 3_000_000, Guid.NewGuid().ToString("N"), actor.Id)).Success, "Top up another 3m");
var snapshot = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
Check(snapshot.TierId == gold.Id && snapshot.RewardPoints == 254, "5m total yields Gold; top-up uses pre-transaction Silver multiplier");
Check((await Wallet(db).TopUpAsync(user.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id)).Success, "Gold top up");
snapshot = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
Check(snapshot.RewardPoints == 256, "Gold multiplier doubles newly earned points");
var cash = await Checkout(db).CompleteCashAsync(new CheckoutCommand(user.Id, "WC-" + Guid.NewGuid().ToString("N"), null, 0, 20000, 0, null, actor.Id, 20000));
Check(cash.Success, "Cash invoice still works: " + cash.Message);
snapshot = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
Check(snapshot.RewardPoints == 258 && snapshot.TotalSpend == 5_135_000, "Cash, wallet payment and top up share earning policy");
var summary = await new ShiftService(db).GetSummaryAsync(shift.Id);
Check(summary?.CashCollected == 5_040_000 && summary.ExpectedCash == 5_140_000, "Cash reconciliation includes top ups exactly once");
var duplicateKey = Guid.NewGuid().ToString("N");
async Task<WalletResult> ParallelTopUp(string reference)
{
    await using var context = Open();
    return await Wallet(context).TopUpAsync(user.Id, 20000, reference, actor.Id);
}
var repeats = await Task.WhenAll(ParallelTopUp(duplicateKey), ParallelTopUp(duplicateKey));
Check(repeats.All(x => x.Success) && repeats.Count(x => x.Duplicate) == 1, "Concurrent identical requests credit exactly once");
var independent = await Task.WhenAll(ParallelTopUp(Guid.NewGuid().ToString("N")), ParallelTopUp(Guid.NewGuid().ToString("N")));
Check(independent.All(x => x.Success && !x.Duplicate), "Concurrent independent top ups both persist");
snapshot = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
Check(snapshot.WalletBalance == 4_985_000 && snapshot.RewardPoints == 264, "Concurrent updates preserve balance and points");
async Task<CheckoutPaymentResult> ParallelPayment()
{
    await using var context = Open();
    return await Checkout(context).CompleteWalletAsync(new CheckoutCommand(user.Id, "WC-" + Guid.NewGuid().ToString("N"), null, 0, 3_000_000, 0, null, actor.Id));
}
var payments = await Task.WhenAll(ParallelPayment(), ParallelPayment());
Check(payments.Count(p => p.Success) == 1, "Concurrent wallet payments cannot overspend");
snapshot = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
Check(snapshot.WalletBalance == 1_985_000 && snapshot.RewardPoints == 564 && snapshot.TotalSpend == 8_195_000, "Only the successful concurrent wallet payment earns points and qualifying spend");
var ledgerSum = await db.WalletTransactions.Where(w => w.CustomerId == user.Id).SumAsync(w => w.Amount);
Check(ledgerSum == snapshot.WalletBalance, "Wallet ledger reconciles with stored balance");
db.PointTransactions.Add(new PointTransaction { CustomerId = user.Id, Amount = 0, TransactionType = PointTransaction.Earn,
    SpendAmount = 9_000_000, CreatedAt = DateTime.UtcNow.AddDays(-366), ReferenceCode = "OLD-" + key });
await db.SaveChangesAsync();
Check(await new TierEvaluationService(db).GetSpendLast12MonthsAsync(user.Id) == 8_195_000, "Rolling 365-day accumulation includes wallet payments and excludes old entries");

// Transfer top-ups: pending never credits funds; confirmation uses exactly the same earning pipeline.
await using var transfersDb = Open();
var transferCustomer = new ApplicationUser { Id = "wt-" + key, UserName = "wt-" + key,
    NormalizedUserName = "WT-" + key, FullName = "Transfer verification", TierId = member.Id, SecurityStamp = key };
transfersDb.Users.Add(transferCustomer);
transfersDb.UserRoles.Add(new IdentityUserRole<string> { UserId = transferCustomer.Id, RoleId = customerRole.Id });
await transfersDb.SaveChangesAsync();
var transferWallet = Wallet(transfersDb);
var requestKey = Guid.NewGuid().ToString("N");
var pending = await LegacyAutoQr(transfersDb, transferCustomer.Id, 2_000_000, requestKey, actor.Id);
Check(pending.Success && pending.Pending && pending.ReferenceCode != null, "Transfer intent created");
Check((await LegacyAutoQr(transfersDb, transferCustomer.Id, 2_000_000, requestKey, actor.Id)).Duplicate, "Creating same transfer twice is idempotent");
Check(await transfersDb.Users.Where(u => u.Id == transferCustomer.Id).Select(u => u.WalletBalance).SingleAsync() == 0, "Pending transfer does not credit wallet");
var bankId = "SEPAY-TEST-" + Guid.NewGuid().ToString("N");
Check(!(await transferWallet.ConfirmTransferAsync(pending.ReferenceCode!, bankId, 1_000_000)).Success, "Wrong transfer amount rejected");
Check(!await transfersDb.WalletTransactions.AnyAsync(w => w.CustomerId == transferCustomer.Id), "Wrong amount has no ledger effects");
Check((await transferWallet.ConfirmTransferAsync(pending.ReferenceCode!, bankId, 2_000_000)).Success, "Bank confirmation credits wallet");
var transferSnap = await transfersDb.Users.AsNoTracking().SingleAsync(u => u.Id == transferCustomer.Id);
Check(transferSnap.WalletBalance == 2_000_000 && transferSnap.RewardPoints == 100 && transferSnap.TierId == silver.Id, "Transfer 2m gives 100 points and Silver");
Check((await transferWallet.ConfirmTransferAsync(pending.ReferenceCode!, bankId, 2_000_000)).Duplicate, "Repeated bank webhook cannot double credit");
Check(!(await transferWallet.ConfirmTransferAsync(pending.ReferenceCode!, bankId + "OTHER", 2_000_000)).Success, "Second bank transaction for same intent rejected");
var transferLedger = await transfersDb.WalletTransactions.SingleAsync(w => w.ReferenceCode == pending.ReferenceCode);
Check(transferLedger.PaymentMethod == "Transfer" && transferLedger.WorkShiftId == null && transferLedger.ProviderTransactionId == bankId, "Transfer ledger preserves bank id and does not enter cash drawer");
var cashAfterTransfer = await new ShiftService(transfersDb).GetSummaryAsync(shift.Id);
Check(cashAfterTransfer?.CashCollected == 5_100_000, "Transfer top up does not inflate cash shift total");
var another = await LegacyAutoQr(transfersDb, transferCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
Check(!(await transferWallet.ConfirmTransferAsync(another.ReferenceCode!, bankId, 20000)).Success, "Bank id cannot be reused across wallet intents");
var expired = await LegacyAutoQr(transfersDb, transferCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
await transfersDb.WalletTopUpIntents.Where(i => i.ReferenceCode == expired.ReferenceCode).ExecuteUpdateAsync(x => x.SetProperty(i => i.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
transfersDb.ChangeTracker.Clear();
Check((await transferWallet.ConfirmTransferAsync(expired.ReferenceCode!, bankId + "EXP", 20000)).Success, "Historical date does not expire a pending QR");
var concurrent = await LegacyAutoQr(transfersDb, transferCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
async Task<WalletResult> ConcurrentConfirm()
{
    await using var context = Open();
    return await Wallet(context).ConfirmTransferAsync(concurrent.ReferenceCode!, bankId + "RACE", 20000);
}
var confirms = await Task.WhenAll(ConcurrentConfirm(), ConcurrentConfirm());
Check(confirms.All(r => r.Success) && confirms.Count(r => r.Duplicate) == 1, "Concurrent webhooks credit exactly once");
// An existing wallet bank transaction cannot settle an invoice.
var invoiceIntent = await Checkout(transfersDb).CreateTransferIntentAsync(new CheckoutCommand(transferCustomer.Id,
    "INVTEST-" + Guid.NewGuid().ToString("N"), null, 10000, 10500, 0, null, actor.Id));
Check(invoiceIntent.Success && invoiceIntent.Pending, "Invoice transfer intent still works");
await transfersDb.PaymentIntents.Where(i => i.ReferenceCode == invoiceIntent.ReferenceCode).ExecuteUpdateAsync(s => s.SetProperty(i => i.RequiresStaffConfirmation, false));
Check(!(await Checkout(transfersDb).ConfirmTransferAsync(invoiceIntent.ReferenceCode!, bankId, invoiceIntent.FinalAmount)).Success,
    "Wallet bank transaction cannot pay invoice again");
var invoiceBankId = "SEPAY-INV-" + Guid.NewGuid().ToString("N");
Check((await Checkout(transfersDb).ConfirmTransferAsync(invoiceIntent.ReferenceCode!, invoiceBankId, invoiceIntent.FinalAmount)).Success,
    "Invoice transfer confirms with unused bank transaction");
Check(!(await Wallet(transfersDb).ConfirmTransferAsync(another.ReferenceCode!, invoiceBankId, 20000)).Success,
    "Invoice bank transaction cannot credit wallet again");


// Exercise authenticated webhook routing and customer ownership checks with actual controllers.
var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{
    ["Payments:SePay:ApiKey"] = "verification-only-key",
    ["Payments:SePay:BankCode"] = "MB",
    ["Payments:SePay:AccountNumber"] = "0000000000",
    ["Payments:SePay:AccountName"] = "VERIFICATION ONLY"
}).Build();
var webhookIntent = await LegacyAutoQr(transfersDb, transferCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
var webhook = new Bms.Web.Controllers.PaymentWebhookController(Checkout(transfersDb), config,
    NullLogger<Bms.Web.Controllers.PaymentWebhookController>.Instance, Wallet(transfersDb));
webhook.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
var payload = new Bms.Web.Controllers.SePayWebhookRequest { Id = DateTime.UtcNow.Ticks, Code = webhookIntent.ReferenceCode, TransferAmount = 20000, TransferType = "in" };
Check(await webhook.Receive(payload, default) is Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult, "Webhook without API key rejected");
webhook.Request.Headers.Authorization = "Apikey verification-only-key";
payload.TransferType = "out";
Check(await webhook.Receive(payload, default) is Microsoft.AspNetCore.Mvc.BadRequestObjectResult, "Outgoing bank transfer cannot credit wallet");
payload.TransferType = "in";
Check(await webhook.Receive(payload, default) is Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: 201 }, "Authenticated webhook routes to wallet credit");
Check(await webhook.Receive(payload, default) is Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: 201 }, "Repeated webhook acknowledges safely");
var contentIntent = await LegacyAutoQr(transfersDb, transferCustomer.Id, 40000, Guid.NewGuid().ToString("N"), actor.Id);
var beforeContentBalance = await transfersDb.Users.AsNoTracking().Where(u => u.Id == transferCustomer.Id).Select(u => u.WalletBalance).SingleAsync();
var contentPayload = new Bms.Web.Controllers.SePayWebhookRequest { Id = DateTime.UtcNow.Ticks,
    Code = null, Content = "MB: " + contentIntent.ReferenceCode + " chuyen tien", TransferAmount = 40000, TransferType = "in" };
Check(await webhook.Receive(contentPayload, default) is Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: 201 }, "Existing webhook accepts wallet reference in content when SePay code is null");
Check(await webhook.Receive(contentPayload, default) is Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: 201 }, "Content webhook retry is idempotent");
Check(await transfersDb.Users.AsNoTracking().Where(u => u.Id == transferCustomer.Id).Select(u => u.WalletBalance).SingleAsync() == beforeContentBalance + 40000,
    "Content webhook credits exact wallet amount once");
contentPayload.Code = "HD-CONFLICT";
Check(await webhook.Receive(contentPayload, default) is Microsoft.AspNetCore.Mvc.BadRequestObjectResult, "Conflicting code and content rejected");
config["Payments:SePay:AccountName"] = null;
var walletController = new Bms.Web.Controllers.WalletController(transfersDb, Wallet(transfersDb), NullLogger<Bms.Web.Controllers.WalletController>.Instance, config);
var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Customer") }, "Test"));
walletController.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };
Check(await walletController.TransferStatus(webhookIntent.ReferenceCode!) is Microsoft.AspNetCore.Mvc.NotFoundResult, "Customer cannot inspect another wallet transfer");
http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, actor.Id),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Staff") }, "Test"));
Check(await walletController.TransferStatus(webhookIntent.ReferenceCode!) is Microsoft.AspNetCore.Mvc.JsonResult, "Staff can inspect transfer status");
var walletPage = (Microsoft.AspNetCore.Mvc.ViewResult)await walletController.Index(transferCustomer.Id);
Check(walletPage.Model is Bms.Web.Models.WalletViewModel { TransferEnabled: true, AccountName: "", AccountNumber: "0000000000" },
    "Wallet reuses configured bank connection without requiring account display name");
// QR generation needs only a receiving account; it must not silently settle payment.
config["Payments:SePay:ApiKey"] = null;
walletPage = (Microsoft.AspNetCore.Mvc.ViewResult)await walletController.Index(transferCustomer.Id);
Check(walletPage.Model is Bms.Web.Models.WalletViewModel { TransferEnabled: true, AutomaticConfirmationConfigured: false },
    "QR generation stays enabled without webhook configuration");
walletController.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, new VerificationTempData());
var beforeQr = await transfersDb.Users.AsNoTracking().SingleAsync(u => u.Id == transferCustomer.Id);
var qrResult = (Microsoft.AspNetCore.Mvc.RedirectToActionResult)await walletController.TopUp(transferCustomer.Id, 2_000_000, Guid.NewGuid().ToString("N"), false, "Transfer");
var qrReference = qrResult.RouteValues?["referenceCode"]?.ToString();
Check(qrReference != null && await transfersDb.WalletTopUpIntents.AnyAsync(i => i.ReferenceCode == qrReference && i.Status == "Pending" && i.ExpectedAmount == 2_000_000),
    "Transfer form creates pending QR without API key or cash confirmation");
var afterQr = await transfersDb.Users.AsNoTracking().SingleAsync(u => u.Id == transferCustomer.Id);
Check(afterQr.WalletBalance == beforeQr.WalletBalance && afterQr.RewardPoints == beforeQr.RewardPoints && afterQr.TierId == beforeQr.TierId
    && !await transfersDb.WalletTransactions.AnyAsync(w => w.ReferenceCode == qrReference),
    "Creating QR alone does not credit wallet, points, tier or payment ledger");
// Manual bank confirmation uses the same credit transaction, with the real confirming staff audited.
await using var manualDb = Open();
var manualCustomer = new ApplicationUser { Id = "wm-" + key, UserName = "wm-" + key, NormalizedUserName = "WM-" + key,
    FullName = "Manual verification", TierId = member.Id, SecurityStamp = key };
var confirmer = new ApplicationUser { Id = "wma-" + key, UserName = "wma-" + key, NormalizedUserName = "WMA-" + key,
    FullName = "Manual confirming staff", SecurityStamp = key };
manualDb.Users.AddRange(manualCustomer, confirmer);
manualDb.UserRoles.AddRange(new IdentityUserRole<string> { UserId = manualCustomer.Id, RoleId = customerRole.Id },
    new IdentityUserRole<string> { UserId = confirmer.Id, RoleId = staffRole.Id });
await manualDb.SaveChangesAsync();
var manualWallet = Wallet(manualDb);
var manualIntent = await manualWallet.CreateTransferIntentAsync(manualCustomer.Id, 2_000_000, Guid.NewGuid().ToString("N"), actor.Id);
Check(!(await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2_000_000, manualCustomer.Id)).Success,
    "Customer cannot confirm their own bank deposit");
Check(!(await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 1_000_000, confirmer.Id)).Success,
    "Manual amount mismatch cannot credit wallet");
Check(!(await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2000.5m, confirmer.Id)).Success,
    "Manual confirmation rejects fractional VND");
Check(!await manualDb.WalletTransactions.AnyAsync(w => w.CustomerId == manualCustomer.Id), "Rejected manual confirmation has no credit effects");
await manualDb.Users.Where(u => u.Id == confirmer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
Check(!(await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2_000_000, confirmer.Id)).Success,
    "Inactive staff cannot confirm bank deposits");
await manualDb.Users.Where(u => u.Id == confirmer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
Check((await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2_000_000, confirmer.Id)).Success,
    "Staff confirms transfer without bank transaction input, webhook configuration or a cash shift");
var manualSnapshot = await manualDb.Users.AsNoTracking().SingleAsync(u => u.Id == manualCustomer.Id);
Check(manualSnapshot.WalletBalance == 2_000_000 && manualSnapshot.RewardPoints == 100 && manualSnapshot.TierId == silver.Id,
    "Manual 2m confirmation credits 2m, awards 100 points and upgrades to Silver");
var manualAudit = await manualDb.WalletTopUpIntents.AsNoTracking().SingleAsync(i => i.ReferenceCode == manualIntent.ReferenceCode);
var manualEntry = await manualDb.WalletTransactions.AsNoTracking().SingleAsync(i => i.ReferenceCode == manualIntent.ReferenceCode);
Check(manualAudit.ConfirmationMethod == "Manual" && manualAudit.ConfirmedById == confirmer.Id && manualAudit.ConfirmedAtUtc != null
    && manualAudit.CreatedById == actor.Id && manualEntry.CreatedById == confirmer.Id,
    "Manual audit preserves creator and records actual confirming staff and time");
Check(manualEntry.PaymentMethod == "Transfer" && manualEntry.WorkShiftId == null
    && manualEntry.ProviderTransactionId == "MANUAL-" + manualIntent.ReferenceCode,
    "Manual bank deposit is transfer revenue, never cash drawer money");
Check(await manualDb.PointTransactions.AnyAsync(p => p.ReferenceCode == manualIntent.ReferenceCode && p.CreatedById == confirmer.Id),
    "Point ledger attributes manual confirmation to the confirming staff");
Check((await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2_000_000, actor.Id)).Duplicate,
    "Repeated confirmation of same request by another staff cannot credit twice");
var legacyBankReference = "BANK-LEGACY-" + key;
await manualDb.WalletTopUpIntents.Where(i => i.ReferenceCode == manualIntent.ReferenceCode)
    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProviderTransactionId, legacyBankReference));
await manualDb.WalletTransactions.Where(i => i.ReferenceCode == manualIntent.ReferenceCode)
    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProviderTransactionId, legacyBankReference));
manualDb.ChangeTracker.Clear();
Check((await manualWallet.ConfirmTransferManuallyAsync(manualIntent.ReferenceCode!, 2_000_000, actor.Id)).Duplicate
    && await manualDb.WalletTopUpIntents.AsNoTracking().AnyAsync(i => i.ReferenceCode == manualIntent.ReferenceCode
        && i.ProviderTransactionId == legacyBankReference && i.ConfirmedById == confirmer.Id),
    "Old confirmations with bank IDs remain idempotent and retain their original audit");
var otherManualIntent = await manualWallet.CreateTransferIntentAsync(manualCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
Check(!(await manualWallet.ConfirmTransferAsync(manualIntent.ReferenceCode!, "SEPAY-AFTER-MANUAL-" + key, 2_000_000)).Success,
    "Later webhook cannot credit a manually confirmed request again");
Check(await manualDb.WalletTransactions.CountAsync(w => w.ReferenceCode == manualIntent.ReferenceCode) == 1,
    "Manual confirmation, retries and webhook preserve one ledger entry");

async Task<WalletResult> ManualRace()
{
    await using var raceDb = Open();
    return await Wallet(raceDb).ConfirmTransferManuallyAsync(otherManualIntent.ReferenceCode!, 20000, confirmer.Id);
}
var manualRace = await Task.WhenAll(ManualRace(), ManualRace());
Check(manualRace.All(r => r.Success) && manualRace.Count(r => r.Duplicate) == 1, "Concurrent staff confirmations credit exactly once");

var expiredManual = await manualWallet.CreateTransferIntentAsync(manualCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
await manualDb.WalletTopUpIntents.Where(i => i.ReferenceCode == expiredManual.ReferenceCode)
    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "Expired").SetProperty(i => i.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
manualDb.ChangeTracker.Clear();
Check((await manualWallet.ConfirmTransferManuallyAsync(expiredManual.ReferenceCode!, 20000, confirmer.Id)).Success,
    "Staff can reconcile money received for an expired QR");

// Actual controller path must require the acknowledgement checkbox and ignore posted actor identities.
var manualController = new Bms.Web.Controllers.WalletController(manualDb, Wallet(manualDb), NullLogger<Bms.Web.Controllers.WalletController>.Instance, config);
manualController.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };
manualController.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, new VerificationTempData());
var controllerIntent = await manualWallet.CreateTransferIntentAsync(manualCustomer.Id, 20000, Guid.NewGuid().ToString("N"), confirmer.Id);
await manualController.ConfirmTransfer(controllerIntent.ReferenceCode!, false);
Check(!await manualDb.WalletTransactions.AnyAsync(w => w.ReferenceCode == controllerIntent.ReferenceCode),
    "Controller blocks manual confirmation without explicit acknowledgement");
// Extra posted amounts cannot override the authoritative amount of the selected intent.
http.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
{
    ["receivedAmount"] = "99999999",
    ["amount"] = "1"
});
await manualController.ConfirmTransfer(controllerIntent.ReferenceCode!, true);
Check(await manualDb.WalletTopUpIntents.AsNoTracking().AnyAsync(i => i.ReferenceCode == controllerIntent.ReferenceCode && i.ConfirmedById == actor.Id),
    "Controller credits deposit using authenticated staff identity");
Check(await manualDb.WalletTransactions.AsNoTracking().AnyAsync(w => w.ReferenceCode == controllerIntent.ReferenceCode && w.Amount == 20000),
    "Confirmation uses stored QR amount and ignores forged posted amounts");
var confirmedPage = (Microsoft.AspNetCore.Mvc.ViewResult)await manualController.Index(manualCustomer.Id, referenceCode: controllerIntent.ReferenceCode);
Check(confirmedPage.Model is Bms.Web.Models.WalletViewModel { SelectedTransfer.ConfirmationMethod: "Manual" } auditModel
    && auditModel.ActorNames.ContainsKey(actor.Id), "Wallet view exposes confirmation audit for staff");
Check((await new ShiftService(manualDb).GetSummaryAsync(shift.Id))?.CashCollected == 5_100_000,
    "Manual bank confirmations do not change expected cash shift totals");
var mixedIntent = await manualWallet.CreateTransferIntentAsync(manualCustomer.Id, 20000, Guid.NewGuid().ToString("N"), actor.Id);
async Task<WalletResult> MixedConfirm(bool byStaff)
{
    await using var context = Open();
    return byStaff
        ? await Wallet(context).ConfirmTransferManuallyAsync(mixedIntent.ReferenceCode!, 20000, confirmer.Id)
        : await Wallet(context).ConfirmTransferAsync(mixedIntent.ReferenceCode!, "SEPAY-MIXED-" + key, 20000);
}
var mixedResults = await Task.WhenAll(MixedConfirm(true), MixedConfirm(false));
Check(mixedResults.Count(r => r.Success && !r.Duplicate) == 1 && await manualDb.WalletTransactions.CountAsync(w => w.ReferenceCode == mixedIntent.ReferenceCode) == 1,
    "Simultaneous staff and webhook confirmation credits only once");
Console.WriteLine($"ALL {passed} WALLET CHECKS PASSED");

sealed class VerificationTempData : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
}
