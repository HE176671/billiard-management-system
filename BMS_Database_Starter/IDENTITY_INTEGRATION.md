# Nối ASP.NET Core Identity vào database này

Các đoạn C# dưới đây là hướng dẫn tham khảo của bộ starter. Repo hiện đã có MVC .NET 8 tại `src/Bms.Web`, dùng EF Core/Identity major 8; đọc code hiện tại và `docs/PROJECT_CONTEXT.md` trước khi áp dụng. Database hiện tên **BilliardDB**, schema mở rộng 24 bảng. Model minh họa bên dưới chỉ ánh xạ phần tài khoản ban đầu; chưa bao gồm TierId/RewardPoints hoặc combo. Không trộn package 8/9/10 tùy ý hoặc tạo lại nền dự án theo ví dụ cũ.

## Một nguồn quản lý schema

Bộ này chủ động dùng **SQL-first**: các file SQL tạo database trước; C# ánh xạ đến bảng đã tồn tại. Không gọi `Database.EnsureCreated()`, `Database.Migrate()` hay `Update-Database` để khởi tạo lại những bảng đó. Template Individual Accounts có thể chứa migration khởi tạo riêng: không áp dụng migration đó lên BilliardDB.

Nếu sau này nhóm muốn chuyển sang EF migrations, cần dựng đầy đủ model khớp schema (cả constraints/indexes), làm baseline và kiểm tra script migration trước khi áp dụng. Không chỉ tạo một migration Identity mới rồi chạy trên database này.

## Model tài khoản

Ví dụ namespace `Bms.Data` (thay đồng bộ nếu project dùng tên khác):

```csharp
using Microsoft.AspNetCore.Identity;

namespace Bms.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? EmployeeCode { get; set; }
    public DateOnly? HireDate { get; set; }
}
```

Username/email/phone/password không khai báo thêm lần nữa vì đã có trong IdentityUser. Không scaffold AspNetUsers thành một entity khác rồi dùng entity đó thay ApplicationUser để ghi mật khẩu.

## Identity DbContext (chỉ phần tài khoản)

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var user = builder.Entity<ApplicationUser>();
        user.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        user.Property(x => x.PhoneNumber).HasMaxLength(20);
        user.Property(x => x.EmployeeCode).HasMaxLength(20);
        user.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.HireDate).HasColumnType("date");
        user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex")
            .IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        user.HasIndex(x => x.PhoneNumber).HasDatabaseName("UX_User_Phone")
            .IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        user.HasIndex(x => x.EmployeeCode).HasDatabaseName("UX_User_EmployeeCode")
            .IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);
    }
}
```

Context này không có model cho bảng nghiệp vụ và không mô tả đầy đủ CHECK constraints của SQL. Nó là ánh xạ truy cập tài khoản, không phải migration model thay thế toàn bộ database. Bổ sung domain models bằng EF mapping, hoặc dùng truy vấn/procedure có tham số cho các màn nghiệp vụ. Không tự thêm domain model rồi chạy migration khởi tạo lên DB này.

## Cấu hình trong Program.cs

Ví dụ dưới đây dành cho ứng dụng MVC dùng Identity với role (không phải API bearer-only):

```csharp
using Bms.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Đặt trước builder.Build().
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Phải khớp SQL đã giao; v1 chưa dùng passkeys.
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
    options.Stores.MaxLengthForKeys = 128;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    // Demo local chưa có email sender. Bổ sung xác nhận email khi triển khai nếu cần.
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Trong pipeline, sau UseRouting(), trước MapControllerRoute():
app.UseAuthentication();
app.UseAuthorization();
```

Các route Account trong ví dụ chưa được tạo; người phụ trách tài khoản phải làm controller/view tương ứng. Không đăng ký thêm một AddDefaultIdentity<IdentityUser> song song bên cạnh cấu hình này.

Ví dụ `appsettings.Development.json` dùng Windows Authentication trên máy học:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=BilliardDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

Thay server bằng instance thực tế. TrustServerCertificate chỉ là cấu hình local ví dụ. Không commit connection string có password thật vào Git.

## Khóa tài khoản phải có xử lý C#

`IsActive` là trường riêng của BMS. **Identity không tự biết phải chặn user khi IsActive=false.**

1. Luồng đăng nhập tra user và từ chối khi user không tồn tại hoặc IsActive=false, dùng thông báo chung.
2. Dùng `PasswordSignInAsync(..., lockoutOnFailure: true)` cho kiểm tra mật khẩu và khóa tạm.
3. Kiểm tra IsActive cả khi xác thực cookie của phiên đã đăng nhập. Nếu dùng custom OnValidatePrincipal, giữ kiểm tra SecurityStamp mặc định qua `SecurityStampValidator.ValidatePrincipalAsync(context)` rồi mới thêm kiểm tra active; khi không hợp lệ, RejectPrincipal và SignOut.
4. Khi Admin khóa user, cập nhật bằng UserManager và đổi SecurityStamp. Nếu chỉ dùng security stamp validator theo chu kỳ, cần biết có độ trễ; để demo yêu cầu khóa có hiệu lực ngay, kiểm tra IsActive ở mỗi request được bảo vệ.
5. Không dùng riêng việc ẩn menu để phân quyền; controller/action phải có Authorize phù hợp.

## Tạo nhân viên và đăng ký khách hàng

- Admin: tạo ApplicationUser (FullName, PhoneNumber, EmployeeCode, HireDate…), gọi UserManager.CreateAsync(user, password), rồi AddToRoleAsync(user,"Staff").
- Public registration: CreateAsync rồi AddToRoleAsync(user,"Customer"); không nhận role từ input.
- Bao hai thao tác create + assign role trong transaction trên cùng scoped ApplicationDbContext của UserManager; kiểm tra IdentityResult của từng thao tác và rollback nếu lỗi.
- Role đã được seed sẵn. Không tạo lại RoleID ở một bảng Role riêng.
- Chuẩn hóa điện thoại thống nhất trước khi lưu; dữ liệu v1 dùng 10 chữ số dạng nội địa. UI/backend phải kiểm tra định dạng; unique index SQL chỉ chặn chuỗi trùng chính xác.
- Số điện thoại và EmployeeCode trùng có thể gây lỗi unique constraint từ database; chuyển lỗi thành thông báo dễ hiểu, không trả nguyên stack trace cho người dùng.
- Lọc danh sách Staff bằng quan hệ AspNetUserRoles/role Staff; khi sửa một user cụ thể cũng kiểm tra user đó có role Staff.

## Đặt mật khẩu cho dữ liệu mẫu

Chỉ làm trong initializer ở môi trường Development, lấy mật khẩu từ user-secrets hoặc biến môi trường. Với `demo-admin` đã có nhưng PasswordHash NULL:

```csharp
// userManager: UserManager<ApplicationUser> lấy từ DI scope.
// demoPassword: lấy từ cấu hình bí mật local, không viết cứng trong source.
var admin = await userManager.FindByIdAsync("demo-admin")
    ?? throw new InvalidOperationException("Run SQL demo seed first.");
if (!await userManager.HasPasswordAsync(admin))
{
    var result = await userManager.AddPasswordAsync(admin, demoPassword);
    if (!result.Succeeded)
        throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
}
```

Đoạn trên không reset mật khẩu nếu user đã có. Không mở endpoint công khai để chạy initializer. Tài khoản staff.demo02 vẫn IsActive=false dù được đặt mật khẩu.

Nguồn: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model
và https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration
