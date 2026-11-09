using System.Text.Json;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

static class ManualQrChecks
{
    public static async Task RunAsync(Func<ApplicationDbContext> open, Func<ApplicationDbContext, CheckoutService> checkout,
        Func<ApplicationDbContext, string?, LoyaltyTestController> controller, string actor, Action<bool, string> check)
    {
        await using var db = open();
        var key = Guid.NewGuid().ToString("N");
        var customer = new ApplicationUser { Id = "choice-" + key, UserName = "choice-" + key, FullName = "Điểm tự chọn", RewardPoints = 100,
            TierId = await db.MembershipTiers.Where(t => t.TierName == "Silver").Select(t => t.Id).SingleAsync(), TotalSpend = 2000000, SecurityStamp = key };
        db.Users.Add(customer);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = customer.Id, RoleId = await db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).SingleAsync() });
        db.PointTransactions.Add(new() { CustomerId = customer.Id, Amount = 100, BalanceAfter = 100, SpendAmount = 2000000, ReferenceCode = key, CreatedById = actor });
        await db.SaveChangesAsync();
        async Task<string> Draft(int points = 0, bool choose = false)
        {
            var redirect = (RedirectToActionResult)await controller(db, actor).Index(customer.Id, playtimeAmount: 190000, fandBAmount: 90000, pointsToRedeem: points, useRewardPoints: choose);
            return (string)redirect.RouteValues!["referenceCode"]!;
        }
        async Task<LoyaltyTestViewModel> Page(string reference) => (LoyaltyTestViewModel)((ViewResult)await controller(db, actor).Index(referenceCode: reference)).Model!;
        var initial = await Draft(40);
        check((await Page(initial)).PointsToRedeem == 0, "New checkout ignores points without explicit customer choice");
        var preview = (JsonResult)await controller(db, actor).Preview(customer.Id, 190000, 90000, pointsToRedeem: 40);
        var json = JsonSerializer.SerializeToElement(preview.Value);
        check(json.GetProperty("pointsDiscountAmount").GetDecimal() == 0 && json.GetProperty("tierDiscountAmount").GetDecimal() == 9500
            && json.GetProperty("finalAmount").GetDecimal() == 270500, "Screenshot amount 9,500 is Silver discount, not automatic point redemption");
        preview = (JsonResult)await controller(db, actor).Preview(customer.Id, 190000, 90000, pointsToRedeem: 5, useRewardPoints: true);
        check(JsonSerializer.SerializeToElement(preview.Value).GetProperty("pointsDiscountAmount").GetDecimal() == 5000, "Preview redeems only the explicitly selected five points");
        await controller(db, actor).CommitCheckout(customer.Id, initial, 190000, 90000, pointsToRedeem: 40, paymentMethod: "Cash", cashReceived: 300000);
        var noPointsReceipt = await Page(initial);
        check(noPointsReceipt.IsPaid && noPointsReceipt.FinalAmount == 270500 && noPointsReceipt.RedeemedPoints == 0
            && !await db.PointTransactions.AnyAsync(p => p.ReferenceCode == initial && p.TransactionType == PointTransaction.Redeem),
            "POST cannot spend points when the customer-choice checkbox is absent");
        var chosen = await Draft(5, true);
        check((await Page(chosen)).PointsToRedeem == 5, "Explicit choice is restored only for that saved checkout");
        await controller(db, actor).CommitCheckout(customer.Id, chosen, 190000, 90000, pointsToRedeem: 5, paymentMethod: "Cash", cashReceived: 300000, useRewardPoints: true);
        var chosenReceipt = await Page(chosen);
        check(chosenReceipt.IsPaid && chosenReceipt.FinalAmount == 265500 && chosenReceipt.RedeemedPoints == 5
            && await db.PointTransactions.CountAsync(p => p.ReferenceCode == chosen && p.TransactionType == PointTransaction.Redeem) == 1,
            "Confirmed customer choice spends exactly five points and receipt reads actual ledger");
        var uncheckedDraft = await Draft(5, true);
        await controller(db, actor).CommitCheckout(customer.Id, uncheckedDraft, 190000, 90000, pointsToRedeem: 5, paymentMethod: "Cash", cashReceived: 300000, useRewardPoints: false);
        check((await Page(uncheckedDraft)).RedeemedPoints == 0 && (await Page(uncheckedDraft)).FinalAmount == 270500,
            "Unchecking point use clears a previously saved choice at final payment");

        var qrRef = await Draft();
        await controller(db, actor).CommitCheckout(customer.Id, qrRef, 190000, 90000);
        var intent = await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.ReferenceCode == qrRef);
        check(intent.ExpiresAtUtc == null && intent.RequiresStaffConfirmation && (await Page(qrRef)).HasPendingTransfer, "New invoice QR has no expiry and waits for staff confirmation");
        var reviewCount = await db.PaymentReconciliations.CountAsync();
        var noteCount = await db.Notifications.CountAsync(n => n.Type == "PaymentReview");
        var observed = await checkout(db).ConfirmTransferAsync(qrRef, "SEPAY-CHOICE-" + key, intent.ExpectedAmount);
        check(observed.Success && observed.Pending && !await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == intent.InvoiceId)
            && !await db.PointTransactions.AnyAsync(p => p.ReferenceCode == qrRef), "Bank notification cannot finish a new invoice or award points before staff confirms");
        await db.PaymentIntents.Where(i => i.Id == intent.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAtUtc, DateTime.UtcNow.AddYears(-1)).SetProperty(i => i.Status, "Expired"));
        check((await Page(qrRef)).HasPendingTransfer
            && JsonSerializer.SerializeToElement(((JsonResult)await controller(db, actor).CheckoutStatus(qrRef)).Value).GetProperty("status").GetString() == "Pending",
            "Old timestamps and old Expired status do not hide an unpaid QR");
        await ManualQrSchema.ApplyAsync(db);
        check(await db.PaymentIntents.AsNoTracking().AnyAsync(i => i.Id == intent.Id && i.ExpiresAtUtc == null && i.Status == "Pending"), "Upgrade restores old unpaid QR without changing the reference or amount");
        await controller(db, actor).ConfirmTransfer(qrRef, true);
        check((await Page(qrRef)).IsPaid && !(await Page(qrRef)).HasPendingTransfer && (await Page(qrRef)).RedeemedPoints == 0,
            "Only the staff confirmation closes the invoice QR and awards points");

        // Verify pending QR reserves points and locks expected amount across tier changes:
        var lockQr = await Draft(50, true);
        await controller(db, actor).CommitCheckout(customer.Id, lockQr, 190000, 90000, pointsToRedeem: 50, useRewardPoints: true);
        var currentPoints = await db.Users.AsNoTracking().Where(u => u.Id == customer.Id).Select(u => u.RewardPoints).SingleAsync();
        var secondPreview = JsonSerializer.SerializeToElement(((JsonResult)await controller(db, actor)
            .Preview(customer.Id, 400000, 0, pointsToRedeem: currentPoints, useRewardPoints: true)).Value);
        check(!secondPreview.GetProperty("isValid").GetBoolean(),
            "Another checkout cannot double-spend points already reserved in a pending QR");
        var silverTierId = await db.MembershipTiers.Where(t => t.TierName == "Silver").Select(t => t.Id).SingleAsync();
        var goldTierId = await db.MembershipTiers.Where(t => t.TierName == "Gold").Select(t => t.Id).SingleAsync();
        await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TierId, goldTierId));
        check((await checkout(db).ConfirmTransferManuallyAsync(lockQr, actor)).Success && (await Page(lockQr)).IsPaid,
            "Confirming a pending QR succeeds with its locked expected amount even if customer tier changed meanwhile");
        await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TierId, silverTierId));
        var cashQr = await Draft();
        await controller(db, actor).CommitCheckout(customer.Id, cashQr, 190000, 90000);
        check((await checkout(db).SwitchTransferToCashAsync(cashQr, 300000, "Khách đổi sang tiền mặt", actor)).Success, "Persistent QR still supports explicit cash switch");
        check(!(await checkout(db).ConfirmTransferAsync(cashQr, "SEPAY-CANCEL-" + key, 270500)).Success
            && await db.PaymentReconciliations.CountAsync() == reviewCount && await db.Notifications.CountAsync(n => n.Type == "PaymentReview") == noteCount,
            "Late money never settles a cancelled QR and does not create review reports or notifications");

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var wallet = new WalletService(db, new PointLedgerService(db), new TierEvaluationService(db), new CustomerSegmentService(db, cache));
        var walletQr = await wallet.CreateTransferIntentAsync(customer.Id, 20000, Guid.NewGuid().ToString("N"), actor);
        var walletBefore = await db.Users.AsNoTracking().Where(u => u.Id == customer.Id).Select(u => u.WalletBalance).SingleAsync();
        check(await db.WalletTopUpIntents.AnyAsync(i => i.ReferenceCode == walletQr.ReferenceCode && i.ExpiresAtUtc == null && i.RequiresStaffConfirmation),
            "New wallet QR also has no expiry and requires staff");
        var walletObserved = await wallet.ConfirmTransferAsync(walletQr.ReferenceCode!, "SEPAY-WALLET-" + key, 20000);
        check(walletObserved.Pending && !await db.WalletTransactions.AnyAsync(t => t.ReferenceCode == walletQr.ReferenceCode), "Wallet webhook acknowledges pending without crediting money or points");
        await db.WalletTopUpIntents.Where(i => i.ReferenceCode == walletQr.ReferenceCode).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAtUtc, DateTime.UtcNow.AddYears(-1)).SetProperty(i => i.Status, "Expired"));
        check((await wallet.ConfirmTransferManuallyAsync(walletQr.ReferenceCode!, 20000, actor)).Success
            && (await db.Users.AsNoTracking().Where(u => u.Id == customer.Id).Select(u => u.WalletBalance).SingleAsync()) == walletBefore + 20000,
            "Staff can confirm old wallet QR once without time-based expiry");
        check((await wallet.ConfirmTransferManuallyAsync(walletQr.ReferenceCode!, 20000, actor)).Duplicate, "Repeated staff confirmation cannot credit a wallet twice");
    }
}
