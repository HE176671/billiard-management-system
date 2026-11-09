using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;

static class CashSwitchChecks
{
    public static async Task RunAsync(Func<ApplicationDbContext> open, Func<ApplicationDbContext, CheckoutService> checkout,
        Func<ApplicationDbContext, string?, LoyaltyTestController> controller, string actorId, string confirmerId, string customerId, Action<bool, string> check)
    {
        await using var db = open();
        var service = checkout(db);
        async Task<PaymentIntent> Qr(string? customer = null)
        {
            var draft = new CheckoutDraft { CreatedById = actorId, CustomerId = customer, IsManualEntry = true,
                PlaytimeAmount = 300000, FandBAmount = 80000 };
            db.CheckoutDrafts.Add(draft); await db.SaveChangesAsync();
            var result = await service.SubmitDraftAsync(new(customer, draft.ReferenceCode, null, 300000, 80000, 0, null, actorId), "VietQR");
            if (!result.Success) throw new Exception("Fixture QR: " + result.Message);
            return await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.ReferenceCode == draft.ReferenceCode);
        }
        async Task<CheckoutPaymentResult> Cash(PaymentIntent intent, string? actor = null, decimal received = 500000)
        {
            await using var scope = open();
            return await checkout(scope).SwitchTransferToCashAsync(intent.ReferenceCode, received, "Ngân hàng khách gặp lỗi", actor ?? actorId);
        }
        async Task<bool> Unpaid(PaymentIntent intent) => !await db.PaymentTransactions.AnyAsync(p => p.InvoiceId == intent.InvoiceId)
            && !await db.PaymentMethodChanges.AnyAsync(c => c.ReferenceCode == intent.ReferenceCode)
            && await db.PaymentIntents.AsNoTracking().AnyAsync(i => i.Id == intent.Id && i.Status == "Pending");

        var cashQr = await Qr(customerId);
        var cashPage = (LoyaltyTestViewModel)((ViewResult)await controller(db, actorId).Index(referenceCode: cashQr.ReferenceCode, cashMode: true)).Model!;
        check(cashPage.CashSwitchMode && cashPage.FinalAmount == cashQr.ExpectedAmount && await Unpaid(cashQr), "Opening cash form preserves QR, invoice and all ledgers");
        await controller(db, actorId).SwitchToCash(cashQr.ReferenceCode, 500000, "Bank unavailable", false);
        check(await Unpaid(cashQr), "Cash-switch POST requires checked cash-and-bank acknowledgement");
        check(!(await Cash(cashQr, customerId)).Success && await Unpaid(cashQr), "Customer cannot switch QR to cash");
        await db.Users.Where(u => u.Id == confirmerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        check(!(await Cash(cashQr, confirmerId)).Success && await Unpaid(cashQr), "Inactive cashier cannot switch QR to cash");
        await db.Users.Where(u => u.Id == confirmerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
        check(!(await Cash(cashQr, confirmerId)).Success && await Unpaid(cashQr), "Cash switch requires an active shift of the receiving cashier");
        check(!(await Cash(cashQr, received: 1)).Success && !(await Cash(cashQr, received: 500000.5m)).Success && await Unpaid(cashQr), "Short or fractional cash never cancels QR");
        var beforeCustomer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customerId);
        // The live session amounts can change after QR creation; this switch must use its saved receipt.
        await db.PlaySessions.Where(s => s.Id == cashQr.SessionId).ExecuteUpdateAsync(s => s.SetProperty(p => p.PlaytimeAmount, 900000));
        var cashResults = await Task.WhenAll(Cash(cashQr), Cash(cashQr));
        var audit = await db.PaymentMethodChanges.AsNoTracking().SingleAsync(c => c.ReferenceCode == cashQr.ReferenceCode);
        var payment = await db.PaymentTransactions.AsNoTracking().SingleAsync(p => p.InvoiceId == cashQr.InvoiceId);
        check(cashResults.All(r => r.Success) && cashResults.Count(r => r.Duplicate) == 1 && payment.PaymentMethod == "Cash"
            && payment.AmountPaid == cashQr.ExpectedAmount && cashResults.All(r => r.ChangeAmount == 500000 - cashQr.ExpectedAmount), "Concurrent cash switches settle the original frozen amount once, with correct change");
        check(audit.ChangedById == actorId && audit.Reason == "Ngân hàng khách gặp lỗi" && audit.Amount == payment.AmountPaid
            && audit.CashReceived == 500000 && payment.WorkShiftId != null && payment.ReceivedById == actorId,
            "Cash payment and method-change audit identify the receiver, shift, reason and amounts");
        var afterCustomer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customerId);
        check(afterCustomer.RewardPoints - beforeCustomer.RewardPoints == 19 && afterCustomer.TotalSpend - beforeCustomer.TotalSpend == cashQr.ExpectedAmount
            && await db.PaymentIntents.AsNoTracking().AnyAsync(i => i.Id == cashQr.Id && i.Status == "Cancelled")
            && await db.CheckoutDrafts.AsNoTracking().AnyAsync(d => d.ReferenceCode == cashQr.ReferenceCode && d.Status == "Paid" && d.PaymentMethod == "Cash"),
            "Cash switch earns once, cancels QR and completes the same checkout draft");
        var receipt = (LoyaltyTestViewModel)((ViewResult)await controller(db, actorId).Index(referenceCode: cashQr.ReferenceCode)).Model!;
        check(receipt.IsPaid && !receipt.CanConfirmTransfer && receipt.MethodChange?.Id == audit.Id && receipt.PaymentMethod == "Cash", "Reload after switch displays the cash receipt and audit, with no confirmation form");
        check(!(await service.ConfirmTransferManuallyAsync(cashQr.ReferenceCode, actorId)).Success, "Cancelled QR cannot be confirmed manually after cash settlement");

        // Force failure after the financial writes, inside the final audit insert.
        var rollbackQr = await Qr(customerId);
        var beforeRollback = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customerId);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.VerifyCashSwitchRollback ON dbo.PaymentMethodChanges AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Reason=N'INJECT-ROLLBACK-TEST') THROW 51000, 'Verification rollback', 1; END;");
        CheckoutPaymentResult rollback;
        try { rollback = await service.SwitchTransferToCashAsync(rollbackQr.ReferenceCode, 500000, "INJECT-ROLLBACK-TEST", actorId); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.VerifyCashSwitchRollback;"); }
        var afterRollback = await db.Users.AsNoTracking().SingleAsync(u => u.Id == customerId);
        check(!rollback.Success && await Unpaid(rollbackQr) && beforeRollback.RewardPoints == afterRollback.RewardPoints
            && beforeRollback.TotalSpend == afterRollback.TotalSpend && !await db.PointTransactions.AnyAsync(p => p.ReferenceCode == rollbackQr.ReferenceCode)
            && await db.CheckoutDrafts.AsNoTracking().AnyAsync(d => d.ReferenceCode == rollbackQr.ReferenceCode && d.Status == "Pending"),
            "Failure while saving method audit rolls back invoice, payment, QR, draft and loyalty atomically");
        check((await Cash(rollbackQr)).Success, "Rolled-back switch can safely retry the same QR");

    }
}
