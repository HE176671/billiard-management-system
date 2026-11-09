using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

static class SessionCheckoutChecks
{
    public static async Task RunAsync(Func<ApplicationDbContext> open, Func<ApplicationDbContext, CheckoutService> checkout,
        Func<ApplicationDbContext, string?, LoyaltyTestController> controller, string actor, Action<bool, string> check)
    {
        await using var db = open();
        var key = Guid.NewGuid().ToString("N")[..10];
        var type = await db.TableTypes.Select(t => t.Id).FirstAsync();
        var first = new BilliardTable { TableCode = "V1-" + key, TableTypeId = type };
        var second = new BilliardTable { TableCode = "V2-" + key, TableTypeId = type };
        var combo = new Combo { Name = "Verification 90 minutes " + key, Price = 100000, PlaytimeHours = 1.5m };
        db.AddRange(first, second, combo);
        await db.SaveChangesAsync();
        var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        async Task<CheckoutDraft> Draft(int? session = null)
        {
            var redirect = (RedirectToActionResult)await controller(db, actor).Index(customerMode: "guest",
                sessionId: session, playtimeAmount: 20000);
            return await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == (string)redirect.RouteValues!["referenceCode"]!);
        }
        CheckoutCommand Command(CheckoutDraft d) => new(null, d.ReferenceCode, d.SessionId,
            d.PlaytimeAmount, d.FandBAmount, 0, null, actor, 1000000);
        async Task<LoyaltyTestViewModel> Page(CheckoutDraft d) =>
            (LoyaltyTestViewModel)((ViewResult)await controller(db, actor).Index(referenceCode: d.ReferenceCode)).Model!;

        foreach (var withCombo in new[] { false, true })
        {
            // Two table rates, plus optional FIFO combo minutes. A single hourly rate is wrong for both.
            var session = new PlaySession { TableId = second.Id, OpenedById = actor, ClosedById = actor,
                StartAtUtc = start, EndAtUtc = start.AddHours(2), HourlyRateSnapshot = 120000, PlaytimeAmount = 240000, Status = "Closed" };
            session.Segments.Add(new() { TableId = first.Id, HourlyRateSnapshot = 60000, StartAtUtc = start, EndAtUtc = start.AddHours(1) });
            session.Segments.Add(new() { TableId = second.Id, HourlyRateSnapshot = 120000, StartAtUtc = start.AddHours(1), EndAtUtc = start.AddHours(2) });
            if (withCombo) session.SessionCombos.Add(new() { ComboId = combo.Id, Quantity = 1,
                ComboNameSnapshot = combo.Name, UnitPriceSnapshot = 100000, PlaytimeHoursSnapshot = 1.5m,
                PurchasedAtUtc = start, CreatedById = actor });
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();
            var draft = await Draft(session.Id);
            var expected = withCombo ? 160000m : 180000m;
            check((await Page(draft)).FinalAmount == expected, "Table preview accounts for transferred rates; combo=" + withCombo);
            var result = await checkout(db).SubmitDraftAsync(Command(draft) with { SubmittedPlaytimeAmount = 1 }, withCombo ? "VietQR" : "Cash");
            check(result.Success && result.FinalAmount == expected, "Actual checkout matches segment/combo preview and ignores posted price; combo=" + withCombo);
            if (withCombo)
            {
                var confirmed = await checkout(db).ConfirmTransferManuallyAsync(draft.ReferenceCode, actor);
                check(confirmed.Success && confirmed.FinalAmount == expected && (await Page(draft)).IsPaid,
                    "Combo QR confirms the frozen amount and persists the receipt");
            }
        }

        // Manual checkout currently uses the first table as an internal FK placeholder.
        // That table can simultaneously belong to a real playing customer.
        var occupiedTable = await db.BilliardTables.OrderBy(t => t.Id).FirstAsync();
        var originalStatus = occupiedTable.Status;
        var active = await db.PlaySessions.AsNoTracking().FirstOrDefaultAsync(s => s.TableId == occupiedTable.Id && s.Status == "Active");
        var createdActive = active == null;
        if (active == null)
        {
            active = new PlaySession { TableId = occupiedTable.Id, OpenedById = actor,
                StartAtUtc = DateTime.UtcNow, HourlyRateSnapshot = 60000, Status = "Active" };
            db.PlaySessions.Add(active);
        }
        await db.SaveChangesAsync();
        try
        {
            foreach (var method in new[] { "Cash", "VietQR", "SwitchCash", "Reopen", "ClosedSession" })
            {
                await db.BilliardTables.Where(t => t.Id == occupiedTable.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "InUse"));
                int? closedSessionId = null;
                if (method == "ClosedSession")
                {
                    var old = new PlaySession { TableId = occupiedTable.Id, OpenedById = actor, ClosedById = actor,
                        StartAtUtc = start, EndAtUtc = start.AddHours(1), HourlyRateSnapshot = 20000,
                        PlaytimeAmount = 20000, Status = "Closed" };
                    db.PlaySessions.Add(old);
                    await db.SaveChangesAsync();
                    closedSessionId = old.Id;
                }
                var draft = await Draft(closedSessionId);
                var result = await checkout(db).SubmitDraftAsync(Command(draft), method is "Cash" or "ClosedSession" ? "Cash" : "VietQR");
                check(result.Success, "Prepare manual checkout while a real table is occupied: " + method);
                if (method == "VietQR") result = await checkout(db).ConfirmTransferManuallyAsync(draft.ReferenceCode, actor);
                if (method == "SwitchCash") result = await checkout(db).SwitchTransferToCashAsync(draft.ReferenceCode, 1000000, "Verification cash switch", actor);
                if (method == "Reopen")
                {
                    var reopened = await checkout(db).ReopenTransferAsync(draft.ReferenceCode, actor);
                    var replacement = await db.CheckoutDrafts.AsNoTracking().SingleAsync(d => d.ReferenceCode == reopened.ReferenceCode);
                    result = await checkout(db).SubmitDraftAsync(Command(replacement), "Cash");
                }
                check(result.Success && await db.BilliardTables.AnyAsync(t => t.Id == occupiedTable.Id && t.Status == "InUse")
                    && await db.PlaySessions.AnyAsync(s => s.Id == active.Id && s.Status == "Active" && s.EndAtUtc == null),
                    "Manual settlement preserves the occupied table and its active session: " + method);
            }
        }
        finally
        {
            if (createdActive) await db.PlaySessions.Where(s => s.Id == active.Id).ExecuteDeleteAsync();
            await db.BilliardTables.Where(t => t.Id == occupiedTable.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, originalStatus));
        }

        var playing = new PlaySession { TableId = second.Id, OpenedById = actor,
            StartAtUtc = DateTime.UtcNow.AddHours(-1), HourlyRateSnapshot = 60000, Status = "Active" };
        db.PlaySessions.Add(playing);
        await db.SaveChangesAsync();
        await db.BilliardTables.Where(t => t.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "InUse"));
        var playingDraft = await Draft(playing.Id);
        var payment = await checkout(db).SubmitDraftAsync(Command(playingDraft), "Cash");
        check(payment.Success && await db.BilliardTables.AnyAsync(t => t.Id == second.Id && t.Status == "Available")
            && await db.PlaySessions.AnyAsync(s => s.Id == playing.Id && s.Status == "Closed" && s.EndAtUtc != null),
            "Paying the actual active session still closes it and releases its own table");
    }
}
