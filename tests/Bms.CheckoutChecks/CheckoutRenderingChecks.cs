using System.Security.Claims;
using Bms.Web.Controllers;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class CheckoutRenderingChecks
{
    // Render the compiled Razor views in memory. No browser, web listener or real payment.
    public static async Task RunAsync(string connection, string actorId, LoyaltyTestViewModel draft,
        LoyaltyTestViewModel pending, LoyaltyTestViewModel expired, LoyaltyTestViewModel paid, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(LoyaltyTestController).Assembly.GetName().Name,
            ContentRootPath = Path.GetFullPath("src/Bms.Web"), EnvironmentName = "Production"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LoyaltyTestController).Assembly);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
        builder.Configuration["Payments:SePay:BankCode"] = "MB";
        builder.Configuration["Payments:SePay:AccountNumber"] = "2107072003";
        await using var app = builder.Build();
        app.UseRouting();
#pragma warning disable ASP0014 // Build routing for an in-memory view render; do not start a listener.
        app.UseEndpoints(routes => routes.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}"));
#pragma warning restore ASP0014
        async Task<string> Render<T>(T model, string controller = "LoyaltyTest", string action = "Index", string role = "Staff") where T : class
        {
            await using var scope = app.Services.CreateAsyncScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actorId), new Claim(ClaimTypes.Role, role) }, "Test")) };
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("localhost");
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Razor check"));
            var route = new RouteData();
            route.Values["controller"] = controller;
            route.Values["action"] = action;
            http.Request.RouteValues = route.Values;
            var context = new ActionContext(http, route, new ActionDescriptor());
            var engine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
            var view = engine.GetView(null, $"/Views/{controller}/{action}.cshtml", true);
            if (!view.Success) throw new Exception("Compiled checkout view not found");
            using var output = new StringWriter();
            var viewContext = new ViewContext(context, view.View,
                new ViewDataDictionary<T>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
                new TempDataDictionary(http, new MemoryTempData()), output, new HtmlHelperOptions());
            await view.View.RenderAsync(viewContext);
            return output.ToString();
        }
        var draftHtml = await Render(draft);
        check(System.Text.RegularExpressions.Regex.IsMatch(draftHtml, "id=\"useRewardPoints\"[^>]*")
            && !System.Text.RegularExpressions.Regex.IsMatch(draftHtml, "id=\"useRewardPoints\"[^>]*checked")
            && System.Text.RegularExpressions.Regex.IsMatch(draftHtml, "id=\"inpPoints\"[^>]*disabled")
            && draftHtml.Contains("checkout-points.js"), "Draft renders points as an unchecked customer choice with input disabled");
        check(draftHtml.Contains("type=\"hidden\" name=\"referenceCode\"") && !draftHtml.Contains("btnNewRef")
            && draftHtml.Contains("id=\"checkoutForm\""), "Draft Razor renders hidden server reference and no manual code generator");
        var originalMethod = draft.PaymentMethod;
        draft.PaymentMethod = "Wallet";
        var walletCheckoutHtml = await Render(draft);
        draft.PaymentMethod = originalMethod;
        check(System.Net.WebUtility.HtmlDecode(walletCheckoutHtml).Contains("Khách muốn dùng điểm")
            && !System.Text.RegularExpressions.Regex.IsMatch(walletCheckoutHtml, "id=\"useRewardPoints\"[^>]*checked")
            && System.Text.RegularExpressions.Regex.IsMatch(walletCheckoutHtml, "id=\"inpPoints\"[^>]*value=\"0\"[^>]*disabled")
            && System.Text.RegularExpressions.Regex.IsMatch(walletCheckoutHtml, "id=\"btnMaxPoints\"[^>]*disabled"),
            "Wallet checkout renders customer opt-in with zero points and disables automatic maximum redemption");
        var pendingHtml = await Render(pending);
        check(pendingHtml.Contains("id=\"checkoutConfirmTransfer\"") && pendingHtml.Contains("action=\"/CashierSimulator/ConfirmTransfer\"")
            && pendingHtml.Contains("__RequestVerificationToken") && pendingHtml.Contains("id=\"vietQrImg\"")
            && !pendingHtml.Contains("id=\"checkoutForm\""), "Pending Razor renders working confirmation route, CSRF and QR without editable checkout");
        var expiredHtml = await Render(expired);
        check(expiredHtml.Contains("id=\"vietQrImg\"") && expiredHtml.Contains("id=\"checkoutConfirmTransfer\"")
            && expiredHtml.Contains("action=\"/CashierSimulator/ReopenTransfer\""), "Old QR renders without time-based expiry and offers confirmation or editing");
        var paidHtml = await Render(paid);
        var decodedPaid = System.Net.WebUtility.HtmlDecode(paidHtml);
        check(decodedPaid.Contains("Không dùng điểm") && decodedPaid.Contains("Ưu đãi hạng / voucher")
            && !decodedPaid.Contains("Tổng giảm giá và đổi điểm") && !paidHtml.Contains("PaymentReconciliation"),
            "Receipt separates loyalty discounts from unused points and has no review links or menu");
        check(!paidHtml.Contains("id=\"checkoutConfirmTransfer\"") && !paidHtml.Contains("id=\"vietQrImg\"")
            && !paidHtml.Contains("id=\"checkoutForm\"") && paidHtml.Contains("checkout-state.js"), "Paid Razor has receipt state and no payment forms or QR");
        pending.CashSwitchMode = true;
        pending.CashSwitchReceived = 500000;
        var cashHtml = await Render(pending);
        check(cashHtml.Contains("action=\"/CashierSimulator/SwitchToCash\"") && cashHtml.Contains("name=\"checkedCashAndBank\"")
            && cashHtml.Contains("data-cash-mode=\"true\"") && System.Text.RegularExpressions.Regex.IsMatch(cashHtml, "id=\"vietQrBox\"[^>]*hidden=\"hidden\"")
            && System.Text.RegularExpressions.Regex.IsMatch(cashHtml, "id=\"checkoutTransferActions\"[^>]*hidden=\"hidden\"")
            && !System.Text.RegularExpressions.Regex.IsMatch(cashHtml, "id=\"checkoutCashPanel\"[^>]*hidden"),
            "Cash-mode Razor renders cash POST and acknowledgement, hiding QR and transfer controls");
        var wallet = new WalletViewModel { CustomerId = draft.SelectedCustomerId!, CustomerName = "QR test", CanTopUp = true, TransferEnabled = true,
            PaymentMethod = "Transfer", BankCode = "MB", AccountNumber = "0000000000", QrBaseUrl = "https://img.vietqr.io/image/MB-0000000000-compact2.png",
            SelectedTransfer = new WalletTopUpIntent { ReferenceCode = "NAPCKTEST", ExpectedAmount = 20000, Status = "Expired", ExpiresAtUtc = DateTime.UtcNow.AddYears(-1) } };
        var walletHtml = await Render(wallet, "Wallet");
        check(walletHtml.Contains("id=\"walletQrDetails\"") && !System.Net.WebUtility.HtmlDecode(walletHtml).Contains("15 phút")
            && walletHtml.Contains("data-pending=\"true\""), "Wallet renders old unpaid QR without expiry countdown");
    }
}
