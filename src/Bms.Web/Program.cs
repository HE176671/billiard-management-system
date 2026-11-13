using Bms.Web.Data;
using Bms.Web.Hubs;
using Bms.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

#region Cau hinh MVC, SQL Server, Identity va cookie
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITableSessionService, TableSessionService>();

// DbContext trỏ vào BilliardDB (connection string trong appsettings)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Loyalty & CRM V2 (TV2)
builder.Services.AddScoped<IPointLedgerService, PointLedgerService>();
builder.Services.AddScoped<ITierEvaluationService, TierEvaluationService>();
builder.Services.AddScoped<IVoucherService, VoucherService>();
builder.Services.AddScoped<ICustomerSegmentService, CustomerSegmentService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddHostedService<LoyaltyDailyJob>();

// Identity — khớp schema SQL V1, khóa login/token dài 128
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
    options.Stores.MaxLengthForKeys = 128;
    options.User.RequireUniqueEmail = true;
    // Yêu cầu của nhóm: mật khẩu chỉ cần tối thiểu 6 ký tự (vd: 123456).
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    // Demo local chưa có email sender
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Revalidate SecurityStamp va IsActive moi request: khoa tai khoan la mat quyen ngay.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.GetUserAsync(context.Principal);
            if (user == null || !user.IsActive)
            {
                context.RejectPrincipal();
                await context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
            }
        }
    };
});

// TV5: dich vu ca lam dung chung (TV3 goi khi ghi nhan tien mat)
builder.Services.AddScoped<IShiftService, ShiftService>();
// TV5: nhat ky thao tac nhay cam dung chung
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IRevenueTargetService, RevenueTargetService>();
// TV5: email SMTP (API-02) và chu kỳ cuối ngày (JOB-01, JOB-02)
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IDailyCycleService, DailyCycleService>();

// WF2: Goi mon & Kho (Tung)
builder.Services.AddSignalR();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IComboService, ComboService>();
builder.Services.AddScoped<IOrderService, OrderService>();

#endregion

#region Thu tu middleware va dinh tuyen MVC
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();  // phải trước UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "change_password",
    pattern: "Profile/ChangePassword",
    defaults: new { controller = "Profile", action = "ChangePassword" });

app.MapControllerRoute(
    name: "order_table",
    pattern: "Order/Table/{tableCode}",
    defaults: new { controller = "Order", action = "Table" });

app.MapControllerRoute(
    name: "order",
    pattern: "Order/{action=Index}/{id?}",
    defaults: new { controller = "Order" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<OrderHub>("/hubs/order");
app.MapHub<TableHub>(TableHub.Path); // TV5: sơ đồ bàn realtime

#endregion

#region Tu nang cap schema con thieu (script 08 & script 09)
using (var upgradeScope = app.Services.CreateScope())
{
    var upgradeLogger = upgradeScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await SchemaUpgrader.ApplyAsync(
            upgradeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), upgradeLogger);
    }
    catch (Exception ex)
    {
        upgradeLogger.LogError(ex,
            "Không tự nâng cấp được schema. Hãy chạy tay BMS_Database_Starter/08_AddWorkShiftReconciliation.sql và 09_TV2_CrmLoyaltyV2.sql.");
    }

    // TV5: bảng AuditLogs + ràng buộc một ca đang mở (script 18). Tự bắt lỗi bên trong.
    await Wf5SchemaUpgrader.ApplyAsync(
        upgradeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), upgradeLogger);
}

#endregion

#region Mat khau admin demo chi trong Development
// Development: đặt mật khẩu tài khoản mẫu và nạp dữ liệu vận hành mẫu
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    // TV5: database cũ có thể thiếu role/tài khoản demo (Manager) -> tạo bổ sung trước khi đặt mật khẩu demo.
    await DbInitializer.EnsureInternalRolesAndDemoManagerAsync(
        userManager, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), logger);
    await DbInitializer.SeedDevelopmentPasswordsAsync(userManager, config, logger);
    try
    {
        await DbInitializer.SeedRecentOperationsDemoDataAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), app.Environment.ContentRootPath, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Không nạp được dữ liệu vận hành mẫu 7 ngày gần nhất.");
    }
}

#endregion

app.Run();
