using System.Text.Json;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

static class WalletEarningChecks
{
    public static async Task RunAsync(Func<ApplicationDbContext> open, Func<ApplicationDbContext, CheckoutService> checkout,
        Func<ApplicationDbContext, string?, LoyaltyTestController> controller, string actor, Action<bool, string> check)
    {
        await using var db = open();
        var key = Guid.NewGuid().ToString("N");
        var silver = await db.MembershipTiers.SingleAsync(t => t.TierName == "Silver");
        var gold = await db.MembershipTiers.SingleAsync(t => t.TierName == "Gold");
        var customer = new ApplicationUser { Id = "we-" + key, UserName = "we-" + key, FullName = "Wallet earning verification",
            TierId = silver.Id, RewardPoints = 100, TotalSpend = 2_000_000, WalletBalance = 2_000_000, SecurityStamp = key };
        db.Users.Add(customer);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = customer.Id,
            RoleId = await db.Roles.Where(r => r.Name == "Customer").Select(r => r.Id).SingleAsync() });
        db.PointTransactions.Add(new() { CustomerId = customer.Id, Amount = 100, BalanceAfter = 100,
            SpendAmount = 2_000_000, ReferenceCode = key, CreatedById = actor });
        await db.SaveChangesAsync();
        async Task<CheckoutDraft> Draft(int points = 0, bool choose = false)
        {
            var redirect = (RedirectToActionResult)await controller(db, actor).Index(customer.Id, playtimeAmount: 190000,
                pointsToRedeem: points, paymentMethod: "Wallet", useRewardPoints: choose);
            return await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == (string)redirect.RouteValues!["referenceCode"]!);
        }
        CheckoutCommand Command(CheckoutDraft draft) => new(customer.Id, draft.ReferenceCode, draft.SessionId,
            190000, 0, draft.PointsToRedeem, null, actor);
        async Task<ApplicationUser> Snapshot() => await db.Users.AsNoTracking().SingleAsync(u => u.Id == customer.Id);
        async Task<LoyaltyTestViewModel> Receipt(string reference) => (LoyaltyTestViewModel)((ViewResult)await controller(db, actor).Index(referenceCode: reference)).Model!;
        async Task<JsonElement> Preview(int points, bool choose) => JsonSerializer.SerializeToElement(((JsonResult)await controller(db, actor)
            .Preview(customer.Id, 190000, 0, pointsToRedeem: points, paymentMethod: "Wallet", useRewardPoints: choose)).Value);

        var noChoice = await Draft(50);
        var preview = await Preview(50, false);
        check(noChoice.PointsToRedeem == 0 && preview.GetProperty("pointsDiscountAmount").GetDecimal() == 0
            && preview.GetProperty("pointsToEarn").GetInt32() == 9 && preview.GetProperty("spend12MonthsAfter").GetDecimal() == 2_180_500,
            "Wallet preview defaults to zero redemption and earns nine points on 180,500 actual payment");
        await controller(db, actor).CommitCheckout(customer.Id, noChoice.ReferenceCode, 190000, 0, pointsToRedeem: 50, paymentMethod: "Wallet");
        var paid = await Receipt(noChoice.ReferenceCode);
        var state = await Snapshot();
        check(paid.IsPaid && paid.FinalAmount == 180500 && paid.EarnedPoints == 9 && paid.RedeemedPoints == 0
            && state.WalletBalance == 1_819_500 && state.TotalSpend == 2_180_500 && state.RewardPoints == 109,
            "Wallet POST without explicit choice ignores submitted points and persists the preview result");
        var redeem = await Draft(10, true);
        preview = await Preview(10, true);
        check(preview.GetProperty("finalAmount").GetDecimal() == 170500 && preview.GetProperty("pointsToEarn").GetInt32() == 8,
            "Wallet preview deducts requested ten points before calculating new earned points");
        async Task<CheckoutPaymentResult> Pay(CheckoutDraft draft)
        {
            await using var scope = open();
            return await checkout(scope).SubmitDraftAsync(Command(draft), "Wallet");
        }
        var concurrent = await Task.WhenAll(Pay(redeem), Pay(redeem));
        state = await Snapshot();
        paid = await Receipt(redeem.ReferenceCode);
        check(concurrent.All(r => r.Success) && concurrent.Count(r => r.Duplicate) == 1
            && state.WalletBalance == 1_649_000 && state.TotalSpend == 2_351_000 && state.RewardPoints == 107
            && paid.RedeemedPoints == 10 && paid.EarnedPoints == 8, "Concurrent wallet confirmations redeem ten, earn eight and debit 170,500 exactly once");
        check(await db.PointTransactions.CountAsync(t => t.ReferenceCode == redeem.ReferenceCode && t.TransactionType == PointTransaction.Earn && t.SpendAmount == 170500) == 1
            && await db.PointTransactions.CountAsync(t => t.ReferenceCode == redeem.ReferenceCode && t.TransactionType == PointTransaction.Redeem && t.Amount == -10) == 1
            && await db.WalletTransactions.CountAsync(t => t.ReferenceCode == redeem.ReferenceCode && t.Amount == -170500) == 1,
            "Wallet receipt links one earning, one requested redemption and one wallet debit");

        var invalid = await Draft(91, true); // 50% of 180,500 permits at most 90 points.
        check(!(await Pay(invalid)).Success, "Wallet rejects redemption above the 50-percent limit");
        var insufficient = await Draft(10, true);
        await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 1));
        check(!(await Pay(insufficient)).Success && !await db.PointTransactions.AnyAsync(t => t.ReferenceCode == insufficient.ReferenceCode)
            && !await db.WalletTransactions.AnyAsync(t => t.ReferenceCode == insufficient.ReferenceCode)
            && (await Snapshot()).RewardPoints == state.RewardPoints, "Insufficient wallet funds never consume points or award new points");
        await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, state.WalletBalance));

        // Failure after the invoice and loyalty writes must roll back the entire wallet payment.
        var rollback = await Draft(10, true);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.VerifyWalletEarningRollback ON dbo.WalletTransactions AFTER INSERT AS BEGIN THROW 51000, 'Verification wallet rollback', 1; END;");
        CheckoutPaymentResult failed;
        try { failed = await Pay(rollback); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.VerifyWalletEarningRollback;"); }
        var afterFailure = await Snapshot();
        check(!failed.Success && afterFailure.WalletBalance == state.WalletBalance && afterFailure.RewardPoints == state.RewardPoints
            && afterFailure.TotalSpend == state.TotalSpend && !await db.PointTransactions.AnyAsync(t => t.ReferenceCode == rollback.ReferenceCode)
            && await db.CheckoutDrafts.AnyAsync(d => d.ReferenceCode == rollback.ReferenceCode && d.Status == "Draft"),
            "Wallet debit failure rolls back earned/redeemed points, spend, invoice and draft atomically");
        check((await Pay(rollback)).Success, "Rolled-back wallet payment can safely retry");

        // An actual table always supplies both monetary amounts, regardless of forged posted values.
        var session = new PlaySession { TableId = await db.BilliardTables.Select(t => t.Id).FirstAsync(), CustomerId = customer.Id,
            OpenedById = actor, ClosedById = actor, StartAtUtc = DateTime.UtcNow.AddHours(-1), EndAtUtc = DateTime.UtcNow,
            HourlyRateSnapshot = 100000, PlaytimeAmount = 100000, Status = "Closed" };
        db.PlaySessions.Add(session); await db.SaveChangesAsync();
        var order = new Order { SessionId = session.Id, Status = "Served" };
        order.OrderDetails.Add(new OrderDetail { ProductId = await db.Products.Select(p => p.Id).FirstAsync(), Quantity = 2, UnitPrice = 30000 });
        db.Orders.Add(order); await db.SaveChangesAsync();
        var redirect = (RedirectToActionResult)await controller(db, actor).Index(customer.Id, sessionId: session.Id, paymentMethod: "Wallet");
        var tableDraft = await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == (string)redirect.RouteValues!["referenceCode"]!);
        var tablePayment = await checkout(db).SubmitDraftAsync(Command(tableDraft) with { SubmittedPlaytimeAmount = 1, SubmittedFandBAmount = 999999, ManualAmounts = true }, "Wallet");
        check(tablePayment.Success && tablePayment.FinalAmount == 155000 && await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.Invoices WHERE Id={tablePayment.InvoiceId} AND TotalPlaytime=100000 AND TotalFandB=60000").SingleAsync() == 1,
            "Selected-table payment ignores forged hour/F&B amounts and manual override flag");

        var beforeUpgrade = await Snapshot();
        var spendAdjustment = 4_950_000 - beforeUpgrade.TotalSpend;
        db.PointTransactions.Add(new() { CustomerId = customer.Id, Amount = 0, SpendAmount = spendAdjustment, ReferenceCode = "ADJUST-" + key });
        await db.SaveChangesAsync();
        await db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TotalSpend, 4_950_000));
        var upgrade = await Draft();
        check((await Pay(upgrade)).Success && (await Snapshot()).TierId == gold.Id && (await Receipt(upgrade.ReferenceCode)).EarnedPoints == 9
            && await new TierEvaluationService(db).GetSpendLast12MonthsAsync(customer.Id) == 5_130_500,
            "Wallet spending can upgrade Silver to Gold, using Silver multiplier on the upgrading payment");
        // Simulate an old paid invoice with a zero-earning ledger; a retry must not retroactively credit it.
        await db.PointTransactions.Where(t => t.ReferenceCode == noChoice.ReferenceCode && t.TransactionType == PointTransaction.Earn)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Amount, 0).SetProperty(t => t.SpendAmount, 0));
        var beforeRetry = await Snapshot();
        check((await Pay(noChoice)).Duplicate && (await Snapshot()).RewardPoints == beforeRetry.RewardPoints
            && (await Snapshot()).TotalSpend == beforeRetry.TotalSpend && (await Receipt(noChoice.ReferenceCode)).EarnedPoints == 0,
            "Historical paid wallet invoices remain unchanged when retried under the new earning policy");

        // Exercise the wallet form's choice through the controller, including a stale saved amount.
        foreach (var (choose, submittedPoints, expectedPoints) in new[] { (false, 10, 0), (true, 0, 0), (true, 5, 5) })
        {
            var choiceDraft = await Draft(10, true);
            var beforeChoice = await Snapshot();
            await controller(db, actor).CommitCheckout(customer.Id, choiceDraft.ReferenceCode, 190000, 0,
                pointsToRedeem: submittedPoints, paymentMethod: "Wallet", useRewardPoints: choose);
            var receipt = await Receipt(choiceDraft.ReferenceCode);
            var afterChoice = await Snapshot();
            var redemptions = await db.PointTransactions.AsNoTracking()
                .Where(t => t.ReferenceCode == choiceDraft.ReferenceCode && t.TransactionType == PointTransaction.Redeem).ToListAsync();
            check(receipt.IsPaid && receipt.RedeemedPoints == expectedPoints
                && redemptions.Count == (expectedPoints > 0 ? 1 : 0)
                && redemptions.Sum(t => t.Amount) == -expectedPoints
                && afterChoice.RewardPoints == beforeChoice.RewardPoints - expectedPoints + receipt.EarnedPoints
                && beforeChoice.WalletBalance - afterChoice.WalletBalance == receipt.FinalAmount,
                $"Wallet form choice={choose}, points={submittedPoints} overrides saved ten points and redeems exactly {expectedPoints}");
        }
    }
}
