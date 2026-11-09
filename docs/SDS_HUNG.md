# Billiard Management System

## Software Design Specification — Phần của Hùng

**Ngày cập nhật:** 01/10/2026. **Phiên bản:** 1.1. **Người phụ trách theo phân công:** Hùng.

Tài liệu cung cấp phần thiết kế kiến trúc, package, Database Design đầy đủ 24 bảng và thiết kế chi tiết cho đăng nhập, tạo nhân viên, đổi mật khẩu. Phần database được mở rộng theo yêu cầu để ghép vào báo cáo chung; phân công chi tiết chức năng giữa các thành viên vẫn giữ nguyên. Cấu trúc đánh số tương ứng mẫu SDS của nhóm. Nội dung giải thích bằng tiếng Việt; tên lớp, bảng và phương thức giữ nguyên theo C# và SQL Server.

**Cách đọc trạng thái:** “Hiện có” nghĩa là đã thấy trong mã nguồn hoặc script, không đồng nghĩa đã kiểm thử chạy thực tế. “Thiết kế cần bổ sung” là hành vi phải triển khai để đáp ứng SDS. “Dự kiến” là thành phần thuộc phạm vi mở rộng mới được giao. Bản SDS không khẳng định các module VNPay, điểm thưởng và gọi món đã hoạt động.

Nguồn đối chiếu: `BMS_Database_Starter/BilliardDB_Full.sql`, `src/Bms.Web/Program.cs`, `Data/ApplicationUser.cs`, `Data/ApplicationDbContext.cs`, ba controller Account/Employee/Profile và các ViewModel tương ứng. Repo hiện nhắm **.NET 8**, dùng **ASP.NET Core MVC, Razor, Bootstrap, Identity và EF Core SQL Server 8.x**; tên database trong phần tạo schema là **BilliardDB**. Các thông tin cũ về chưa có project hoặc chỉ có 13 bảng không còn đại diện cho source hiện tại.

### Change Log

| Date | A / M / D | In charge | Change Description |
|---|---|---|---|
| 01/10/2026 | A | Hùng | Soạn phần kiến trúc, package, bốn bảng, thiết kế đăng nhập/tạo Staff/đổi mật khẩu và bảo mật; đối chiếu với source hiện tại |
| 01/10/2026 | M | Hùng | Mở rộng Database Design thành 24 bảng, 150 cột, 33 FK theo SQL mới; cập nhật trạng thái verify và seed |

# I. High Level Design

## 1. Software Architecture

### 1.1 Kiến trúc tổng thể

BMS sử dụng kiến trúc ứng dụng web MVC triển khai trong một ứng dụng ASP.NET Core. Trình duyệt hiển thị HTML do Razor tạo và gửi yêu cầu HTTP đến controller. Controller tiếp nhận ViewModel, kiểm tra dữ liệu và điều phối xử lý. ASP.NET Core Identity chịu trách nhiệm tài khoản, mật khẩu, vai trò và cookie đăng nhập. EF Core ánh xạ dữ liệu tài khoản vào SQL Server; các nghiệp vụ đặt bàn/phiên chơi dùng procedure có tham số theo thiết kế database.

Các tầng dưới đây là phân chia trách nhiệm logic; không có nghĩa mỗi tầng là một server hoặc microservice riêng. Các module thanh toán, F&B, ca làm và thành viên được đưa vào kiến trúc đích theo phân công mới, chưa coi là chức năng đã triển khai. Đối với nghiệp vụ phức tạp, nhóm có thể bổ sung service; source của Hùng hiện gọi Identity trực tiếp từ controller.

```mermaid
flowchart TB
    Actor["Admin / Staff / Customer"] --> Browser["Trình duyệt<br/>HTML, CSS, Bootstrap, JavaScript"]
    subgraph App["Bms.Web — ASP.NET Core MVC"]
        Gate["Routing + Authentication + Authorization<br/>Anti-forgery cho POST"]
        Controllers["MVC Controllers<br/>Account, Employee, Profile và các module nghiệp vụ"]
        Views["Razor Views + ViewModels"]
        Identity["ASP.NET Core Identity<br/>UserManager + SignInManager"]
        Biz["Xử lý nghiệp vụ<br/>Bàn, Booking, F&B, Ca làm, Thanh toán, Thành viên"]
        Data["ApplicationDbContext / EF Core<br/>SQL có tham số và procedure"]
        Payment["VNPay adapter<br/>Dự kiến — TV3 phụ trách"]
        Gate --> Controllers
        Controllers --> Views
        Controllers --> Identity
        Controllers --> Biz
        Identity --> Data
        Biz --> Data
        Biz -.-> Payment
    end
    Browser -->|"HTTPS request + cookie + form"| Gate
    Views -->|"HTML response"| Browser
    Data -->|"SQL qua kết nối server"| DB[("SQL Server<br/>BilliardDB")]
    Payment -.->|"Tạo URL thanh toán có chữ ký"| Browser
    Browser -.->|"Chuyển hướng thanh toán"| VNPay["VNPay — hệ thống ngoài, dự kiến"]
    VNPay -.->|"IPN server-to-server"| Payment
    VNPay -.->|"Return URL qua trình duyệt"| Browser
```

**Hình I.1 — Kiến trúc logic toàn hệ thống.** Nét đứt biểu thị tích hợp dự kiến. Luồng IPN và Return URL thuộc thiết kế thanh toán của TV3, cần thống nhất trước khi triển khai; không tự công nhận thanh toán chỉ từ thông tin trình duyệt gửi về.

### 1.2 Architecture component descriptions

| No | Component | Description |
|---|---|---|
| C01 | Client tier — Web browser | Hiển thị giao diện, nhập dữ liệu và gửi yêu cầu. Không kết nối trực tiếp database, không tự quyết định quyền hoặc kết quả giao dịch |
| A01 | Routing và security pipeline | Chọn action, xác thực cookie, kiểm tra quyền; kiểm tra anti-forgery ở các POST có thay đổi dữ liệu |
| A02 | Controllers | Điều phối yêu cầu và trả View/Redirect. Account xử lý đăng nhập; Employee quản lý Staff; Profile xử lý đổi mật khẩu của người đang đăng nhập |
| A03 | Razor Views và ViewModels | Views dựng HTML; ViewModels giới hạn dữ liệu đầu vào/đầu ra và khai báo validation. Không đưa PasswordHash ra giao diện |
| A04 | ASP.NET Core Identity | Quản lý user, role, kiểm tra và băm mật khẩu, khóa tạm và cookie. Tất cả vai trò dùng chung nguồn tài khoản |
| A05 | Các module nghiệp vụ | Bàn/phiên, booking/ca làm, F&B, thanh toán, điểm và hạng. Các nhóm cung cấp thiết kế chi tiết trong phần II của mình |
| A06 | Data access | ApplicationDbContext và Identity EF stores truy cập bảng tài khoản. Procedure có tham số xử lý nghiệp vụ cần kiểm soát transaction/đặt trùng |
| D01 | SQL Server | Lưu dữ liệu và thực thi PK, FK, UNIQUE, CHECK, transaction. Schema do SQL scripts quản lý theo SQL-first |
| E01 | VNPay adapter — dự kiến | Biên tích hợp bên ngoài, tách khỏi quản lý nhân viên. Xác thực thông báo thanh toán, xử lý lặp và cập nhật trạng thái do TV3 thiết kế |

### 1.3 Ranh giới trách nhiệm

Hùng phụ trách tài khoản và quyền dùng chung cho các module. Các controller nghiệp vụ nhận định danh người gọi từ `HttpContext.User`, không tin `StaffId`, `CustomerId` hay role do form tùy ý gửi. Membership tier chỉ biểu thị ưu đãi; vai trò Admin/Staff/Customer quyết định quyền. Nâng hạng khách hàng không cấp quyền quản trị.

Ứng dụng dùng cookie Identity cho website MVC. Kiến trúc này không có JWT Security Filter như ví dụ trong template. SQL scripts là nguồn quản lý schema; ứng dụng không gọi `EnsureCreated`, `Migrate` hoặc áp dụng migration khởi tạo lên BilliardDB đã có bảng.

## 2. Package Diagram

### 2.1 Sơ đồ package

Trong C#, package được biểu diễn bằng namespace/thư mục và các dependency framework. Sơ đồ sau thể hiện phụ thuộc, không phải thứ tự thực thi. `Views` và `wwwroot` là nhóm tài nguyên, không phải namespace C#.

```mermaid
flowchart LR
    Root["Bms.Web<br/>Program.cs — composition root"]
    C["«namespace» Bms.Web.Controllers<br/>Account, Employee, Profile<br/>Home, Dashboard, Staff, Customer"]
    M["«namespace» Bms.Web.Models<br/>LoginViewModel<br/>CreateEmployeeViewModel<br/>ChangePasswordViewModel"]
    V["«folder» Views<br/>Account / Employee / Profile / Shared"]
    W["«folder» wwwroot<br/>CSS / JS / Bootstrap"]
    D["«namespace» Bms.Web.Data<br/>ApplicationUser<br/>ApplicationDbContext<br/>DbInitializer"]
    I["«framework» Microsoft.AspNetCore.Identity"]
    EF["«framework» Identity.EntityFrameworkCore<br/>Microsoft.EntityFrameworkCore.SqlServer"]
    Future["«proposed namespace» Bms.Web.Integrations.VNPay<br/>TV3 thiết kế, chưa có trong source"]
    Root -.->|"đăng ký DI"| C
    Root -.-> D
    Root -.-> I
    C -.->|"nhận/trả model"| M
    C -.->|"sử dụng entity/context"| D
    C -.->|"gọi manager"| I
    C -.->|"chọn view"| V
    V -.->|"strongly typed model"| M
    V -.->|"tham chiếu tài nguyên"| W
    D -.->|"kế thừa IdentityUser"| I
    D -.->|"kế thừa IdentityDbContext"| EF
    C -.->|"tích hợp dự kiến"| Future
```

**Hình I.2 — Package Diagram.** Mũi tên nét đứt biểu thị dependency; riêng namespace VNPay được gắn nhãn dự kiến. Không có dependency từ Data ngược về Controllers/Views.

### 2.2 Package descriptions và quy ước đặt tên

| Package / thư mục | Trách nhiệm | Quy ước và ví dụ |
|---|---|---|
| `Bms.Web.Controllers` | Tiếp nhận HTTP, kiểm tra quyền, điều phối xử lý | PascalCase và hậu tố `Controller`: `EmployeeController`; action `Login`, `Create`, `ChangePassword` |
| `Bms.Web.Models` | Dữ liệu form và hiển thị, validation | PascalCase và hậu tố `ViewModel`: `LoginViewModel`, `CreateEmployeeViewModel`; không bind trực tiếp toàn bộ entity từ form |
| `Bms.Web.Data` | Entity, mapping và khởi tạo dữ liệu Development | `ApplicationUser`, `ApplicationDbContext`, `DbInitializer`; không viết HTML tại đây |
| `Views/{Controller}` | Giao diện Razor theo action | `Views/Account/Login.cshtml`, `Views/Employee/Create.cshtml`, `Views/Profile/ChangePassword.cshtml` |
| `Views/Shared` | Layout và thành phần dùng chung | `_Layout.cshtml`; tên partial có tiền tố `_` |
| `wwwroot` | Tài nguyên trình duyệt | Tên CSS/JS rõ mục đích; không lưu connection string hoặc khóa VNPay |
| `Bms.Web.Integrations.VNPay` — dự kiến | Adapter giao tiếp VNPay | Namespace đề xuất cho TV3; tên lớp cần chốt khi triển khai |

Kiểu và public member dùng PascalCase; tham số/biến cục bộ dùng camelCase; dependency private dùng `_userManager`, `_signInManager`. Phương thức bất đồng bộ mới nên có hậu tố Async; action hiện hữu giữ nguyên tên route đang dùng. Tên bảng và cột giữ đúng PascalCase của SQL script. Các module bổ sung phải dùng cùng `ApplicationUser` và cùng cấu hình Identity.

## 3. Database Design — Toàn bộ BilliardDB

**Ngày cập nhật:** 01/10/2026. **Nguồn:** `BMS_Database_Starter/BilliardDB_Full.sql` tại commit database `4448bb2`. **Phạm vi:** 24 bảng nghiệp vụ, 150 cột và 33 khóa ngoại.

Phần này thay thế mục Database Design bốn bảng trước đây để dùng trong báo cáo SDS chung của nhóm. Nội dung phản ánh DDL hiện có; không tự bổ sung các cột/ràng buộc chưa có. Bộ schema và seed đã được thực thi trên SQL Server; script nâng cấp combo đã áp dụng trên localhost / BilliardDB. Có bảng không có nghĩa mọi luồng nghiệp vụ đã được code.

### 3.0 Sơ đồ database tổng thể

Sơ đồ có đủ 24 bảng và toàn bộ FK. Mỗi hộp rút gọn chỉ hiện các cột khóa để dễ đọc; các bảng bên dưới mô tả đầy đủ mọi cột. `dbo.sysdiagrams` do SSMS tạo để lưu sơ đồ, không thuộc 24 bảng nghiệp vụ và không đưa vào SDS.

```mermaid
erDiagram
    direction LR
    AspNetUsers {
        nvarchar Id PK
        int TierId FK
    }
    AspNetRoles {
        nvarchar Id PK
    }
    AspNetUserRoles {
        nvarchar UserId PK,FK
        nvarchar RoleId PK,FK
    }
    AspNetUserClaims {
        int Id PK
        nvarchar UserId FK
    }
    AspNetRoleClaims {
        int Id PK
        nvarchar RoleId FK
    }
    AspNetUserLogins {
        nvarchar LoginProvider PK
        nvarchar ProviderKey PK
        nvarchar UserId FK
    }
    AspNetUserTokens {
        nvarchar UserId PK,FK
        nvarchar LoginProvider PK
        nvarchar Name PK
    }
    MembershipTiers {
        int Id PK
    }
    TableTypes {
        int Id PK
    }
    BilliardTables {
        int Id PK
        int TableTypeId FK
    }
    PricingConfigs {
        int Id PK
        int TableTypeId FK
    }
    Bookings {
        int Id PK
        nvarchar CustomerId FK
        int TableId FK
        nvarchar CheckedInById FK
        nvarchar CancelledById FK
    }
    PlaySessions {
        int Id PK
        int TableId FK
        int BookingId FK,UK
        nvarchar CustomerId FK
        nvarchar OpenedById FK
        nvarchar ClosedById FK
    }
    ProductCategories {
        int Id PK
    }
    Products {
        int Id PK
        int CategoryId FK
    }
    Orders {
        int Id PK
        int SessionId FK
    }
    OrderDetails {
        int Id PK
        int OrderId FK
        int ProductId FK
    }
    Combos {
        int Id PK
    }
    ComboItems {
        int ComboId PK,FK
        int ProductId PK,FK
    }
    SessionCombos {
        int Id PK
        int SessionId FK
        int ComboId FK
        nvarchar CreatedById FK
    }
    SessionComboItems {
        int SessionComboId PK,FK
        int ProductId PK,FK
    }
    Invoices {
        int Id PK
        int SessionId FK,UK
        nvarchar CustomerId FK
    }
    PaymentTransactions {
        int Id PK
        int InvoiceId FK
    }
    WorkShifts {
        int Id PK
        nvarchar StaffId FK
    }
    MembershipTiers o|..o{ AspNetUsers : "TierId"
    AspNetUsers ||--o{ AspNetUserRoles : "UserId"
    AspNetRoles ||--o{ AspNetUserRoles : "RoleId"
    AspNetUsers ||..o{ AspNetUserClaims : "UserId"
    AspNetRoles ||..o{ AspNetRoleClaims : "RoleId"
    AspNetUsers ||..o{ AspNetUserLogins : "UserId"
    AspNetUsers ||--o{ AspNetUserTokens : "UserId"
    TableTypes ||..o{ BilliardTables : "TableTypeId"
    TableTypes ||..o{ PricingConfigs : "TableTypeId"
    AspNetUsers ||..o{ Bookings : "CustomerId"
    BilliardTables ||..o{ Bookings : "TableId"
    AspNetUsers o|..o{ Bookings : "CheckedInById"
    AspNetUsers o|..o{ Bookings : "CancelledById"
    BilliardTables ||..o{ PlaySessions : "TableId"
    Bookings o|..o| PlaySessions : "BookingId"
    AspNetUsers o|..o{ PlaySessions : "CustomerId"
    AspNetUsers ||..o{ PlaySessions : "OpenedById"
    AspNetUsers o|..o{ PlaySessions : "ClosedById"
    ProductCategories ||..o{ Products : "CategoryId"
    PlaySessions ||..o{ Orders : "SessionId"
    Orders ||..o{ OrderDetails : "OrderId"
    Products ||..o{ OrderDetails : "ProductId"
    Combos ||--o{ ComboItems : "ComboId"
    Products ||--o{ ComboItems : "ProductId"
    PlaySessions ||..o{ SessionCombos : "SessionId"
    Combos ||..o{ SessionCombos : "ComboId"
    AspNetUsers ||..o{ SessionCombos : "CreatedById"
    SessionCombos ||--o{ SessionComboItems : "SessionComboId"
    Products ||--o{ SessionComboItems : "ProductId"
    PlaySessions ||..o| Invoices : "SessionId"
    AspNetUsers o|..o{ Invoices : "CustomerId"
    Invoices ||..o{ PaymentTransactions : "InvoiceId"
    AspNetUsers ||..o{ WorkShifts : "StaffId"
```

**Hình I.3 — Database Diagram toàn hệ thống BMS (24 bảng).** PK là khóa chính; FK là khóa ngoại; UK là tính duy nhất ở cột. Đầu quan hệ 0..1 thể hiện FK có thể NULL hoặc phía con có FK duy nhất; 0..n thể hiện nhiều bản ghi. Nét liền là quan hệ định danh (FK thuộc PK), nét đứt là quan hệ không định danh. Điều kiện chỉ một phiên Active/bàn là unique index có lọc, không biến toàn bộ lịch sử phiên thành quan hệ một–một.

**Danh mục bảng:**

| No | Table | Description |
|---|---|---|
| 1 | [AspNetUsers](#3-1-aspnetusers) | Nguồn tài khoản chung cho Admin, Staff, Customer; thông tin nhân viên, bảo mật và hạng thành viên. |
| 2 | [AspNetRoles](#3-2-aspnetroles) | Danh mục vai trò và quyền ở mức role: Admin, Staff, Customer. |
| 3 | [AspNetUserRoles](#3-3-aspnetuserroles) | Bảng nối nhiều–nhiều giữa tài khoản và vai trò. |
| 4 | [AspNetUserClaims](#3-4-aspnetuserclaims) | Các claim bổ sung gắn với từng tài khoản theo cơ chế Identity. |
| 5 | [AspNetRoleClaims](#3-5-aspnetroleclaims) | Các claim được cấp thông qua vai trò theo cơ chế Identity. |
| 6 | [AspNetUserLogins](#3-6-aspnetuserlogins) | Liên kết tài khoản BMS với định danh của nhà cung cấp đăng nhập ngoài. |
| 7 | [AspNetUserTokens](#3-7-aspnetusertokens) | Lưu token theo tài khoản, nhà cung cấp và tên token; không phải bảng phiên chơi. |
| 8 | [MembershipTiers](#3-8-membershiptiers) | Danh mục hạng thành viên và phần trăm ưu đãi. |
| 9 | [TableTypes](#3-9-tabletypes) | Danh mục loại bàn và đơn giá giờ cơ sở. |
| 10 | [BilliardTables](#3-10-billiardtables) | Thông tin bàn bida vật lý, vị trí và trạng thái hiện tại. |
| 11 | [PricingConfigs](#3-11-pricingconfigs) | Cấu hình đơn giá theo khoảng giờ cho từng loại bàn. |
| 12 | [Bookings](#3-12-bookings) | Lượt đặt bàn, khách đặt, khoảng thời gian, check-in và hủy đặt. |
| 13 | [PlaySessions](#3-13-playsessions) | Phiên chơi thực tế, người mở/đóng và giá giờ được ghi nhận lúc mở. |
| 14 | [ProductCategories](#3-14-productcategories) | Danh mục nhóm đồ ăn và nước uống. |
| 15 | [Products](#3-15-products) | Món đang bán, giá, tồn kho và trạng thái kinh doanh. |
| 16 | [Orders](#3-16-orders) | Lượt gọi món F&B gắn với phiên chơi. |
| 17 | [OrderDetails](#3-17-orderdetails) | Chi tiết món, số lượng và giá tại thời điểm gọi trong một đơn F&B. |
| 18 | [Combos](#3-18-combos) | Danh mục gói gồm giờ chơi và các món đi kèm. |
| 19 | [ComboItems](#3-19-comboitems) | Các món và số lượng cho mỗi đơn vị combo trong danh mục hiện tại. |
| 20 | [SessionCombos](#3-20-sessioncombos) | Lượt mua combo của phiên chơi, số gói, tên/giá/giờ tại thời điểm mua. |
| 21 | [SessionComboItems](#3-21-sessioncomboitems) | Thành phần món được lưu tại thời điểm mua combo để giữ lịch sử. |
| 22 | [Invoices](#3-22-invoices) | Hóa đơn của phiên chơi: tiền giờ, F&B, giảm giá và trạng thái thanh toán. |
| 23 | [PaymentTransactions](#3-23-paymenttransactions) | Các giao dịch thanh toán của hóa đơn, phương thức và mã tham chiếu. |
| 24 | [WorkShifts](#3-24-workshifts) | Ca làm của nhân viên, thời điểm và số tiền đầu/cuối ca. |

Sơ đồ lớn: kéo thanh cuộn để xem từng vùng, hoặc mở [ảnh SVG riêng](sds-hung/03-database.svg) để phóng to. Bấm tên bảng trong danh sách để đến bảng mô tả.

**Quy ước đọc các bảng:**

- `NULL`/`NOT NULL`, `DEFAULT`, `CHECK`, PK, FK và UNIQUE dưới đây lấy từ SQL. Một cột NULL ở SQL vẫn có thể bắt buộc ở form theo nghiệp vụ.
- PK ghép yêu cầu duy nhất cả nhóm cột, không phải từng cột riêng. Unique filtered index chỉ áp dụng các dòng thỏa WHERE.
- Cột `rowversion` do SQL Server sinh, phục vụ kiểm soát đồng thời; không lưu ngày giờ. Các cột có hậu tố Utc lưu UTC; khi hiển thị đổi sang giờ Việt Nam. `HireDate` là ngày, không đổi múi giờ.
- Các số tiền dùng decimal; đơn vị nghiệp vụ hiện là VND. `DiscountPercent` là phần trăm; `DiscountAmount` là số tiền.
- Cột `AS (...) PERSISTED` do SQL Server tính và lưu; DDL không khai báo riêng kiểu dữ liệu hoặc NOT NULL cho các cột tính toán này.
- FK không ghi ON DELETE CASCADE có hành vi NO ACTION. FK không tự xác minh role, phiên đang mở, tồn kho hoặc chống xử lý yêu cầu lặp.

### 3.1 AspNetUsers

**Mục đích:** Nguồn tài khoản chung cho Admin, Staff, Customer; thông tin nhân viên, bảo mật và hạng thành viên. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `nvarchar(450)`; `NOT NULL`; `PK` | Khóa chuỗi Identity; không phải số tự tăng. |
| `UserName` | `nvarchar(256)`; `NULL` | Tên đăng nhập gốc. |
| `NormalizedUserName` | `nvarchar(256)`; `NULL`; `UNIQUE INDEX UserNameIndex(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL` | Tên đăng nhập chuẩn hóa do Identity quản lý. |
| `Email` | `nvarchar(256)`; `NULL` | Địa chỉ email gốc. |
| `NormalizedEmail` | `nvarchar(256)`; `NULL`; `UNIQUE INDEX EmailIndex(NormalizedEmail) WHERE NormalizedEmail IS NOT NULL` | Email chuẩn hóa; schema BMS yêu cầu không trùng khi có giá trị. |
| `EmailConfirmed` | `bit`; `NOT NULL`; `DEFAULT 0` | Cờ xác nhận email. |
| `PasswordHash` | `nvarchar(max)`; `NULL` | Hash mật khẩu do Identity tạo; seed NULL, chưa có mật khẩu để đăng nhập. |
| `SecurityStamp` | `nvarchar(max)`; `NULL` | Dấu thay đổi bảo mật phục vụ kiểm tra phiên đăng nhập. |
| `ConcurrencyStamp` | `nvarchar(max)`; `NULL` | Token kiểm soát cập nhật đồng thời của Identity; không phải rowversion. |
| `PhoneNumber` | `nvarchar(20)`; `NULL`; CHECK `PhoneNumber IS NULL OR LEN(LTRIM(RTRIM(PhoneNumber))) > 0`; `UNIQUE INDEX UX_User_Phone(PhoneNumber) WHERE PhoneNumber IS NOT NULL` | Số điện thoại; cần chuẩn hóa nhất quán ở ứng dụng. |
| `PhoneNumberConfirmed` | `bit`; `NOT NULL`; `DEFAULT 0` | Cờ xác nhận số điện thoại. |
| `TwoFactorEnabled` | `bit`; `NOT NULL`; `DEFAULT 0` | Cờ sử dụng xác thực hai yếu tố; không tự chứng minh đã có luồng 2FA. |
| `LockoutEnd` | `datetimeoffset(7)`; `NULL` | Thời điểm kết thúc khóa tạm; khác khóa quản trị IsActive. |
| `LockoutEnabled` | `bit`; `NOT NULL`; `DEFAULT 1` | Cho phép áp dụng khóa tạm. |
| `AccessFailedCount` | `int`; `NOT NULL`; `DEFAULT 0`; CHECK `AccessFailedCount >= 0` | Số lần đăng nhập thất bại. |
| `FullName` | `nvarchar(100)`; `NOT NULL`; CHECK `LEN(LTRIM(RTRIM(FullName))) > 0` | Họ tên hiển thị. |
| `IsActive` | `bit`; `NOT NULL`; `DEFAULT 1` | Trạng thái cho phép sử dụng tài khoản; backend/cookie phải kiểm tra. |
| `CreatedAtUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm tạo tài khoản, lưu UTC. |
| `EmployeeCode` | `nvarchar(20)`; `NULL`; CHECK `EmployeeCode IS NULL OR LEN(LTRIM(RTRIM(EmployeeCode))) > 0`; `UNIQUE INDEX UX_User_EmployeeCode(EmployeeCode) WHERE EmployeeCode IS NOT NULL` | Mã nhân viên; tính duy nhất áp dụng toàn bảng. |
| `RewardPoints` | `int`; `NOT NULL`; `DEFAULT 0` | Điểm tích lũy; chưa có CHECK >= 0 hoặc lịch sử điểm. |
| `TierId` | `int`; `NULL`; FK → `MembershipTiers.Id`; ON DELETE NO ACTION | Hạng thành viên; NULL nghĩa chưa gán hạng. |
| `HireDate` | `date`; `NULL` | Ngày vào làm; là date, không đổi múi giờ. |

**Indexes khai báo riêng:**

- `UserNameIndex`: `UNIQUE (NormalizedUserName) WHERE NormalizedUserName IS NOT NULL`.
- `EmailIndex`: `UNIQUE (NormalizedEmail) WHERE NormalizedEmail IS NOT NULL`.
- `UX_User_Phone`: `UNIQUE (PhoneNumber) WHERE PhoneNumber IS NOT NULL`.
- `UX_User_EmployeeCode`: `UNIQUE (EmployeeCode) WHERE EmployeeCode IS NOT NULL`.

**Lưu ý nghiệp vụ:** Không có bảng Employee tách riêng. Staff được xác định qua role Staff. UserManager quản lý password/normalized fields/security stamp. SQL-first: không áp dụng migration khởi tạo lên bảng đã có. FK đến tier không cấp quyền quản trị.

### 3.2 AspNetRoles

**Mục đích:** Danh mục vai trò và quyền ở mức role: Admin, Staff, Customer. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `nvarchar(450)`; `NOT NULL`; `PK` | Khóa chuỗi vai trò. |
| `Name` | `nvarchar(256)`; `NULL` | Tên vai trò, ví dụ Admin, Staff, Customer. |
| `NormalizedName` | `nvarchar(256)`; `NULL`; `UNIQUE INDEX RoleNameIndex(NormalizedName) WHERE NormalizedName IS NOT NULL` | Tên vai trò chuẩn hóa, ví dụ ADMIN. |
| `ConcurrencyStamp` | `nvarchar(max)`; `NULL` | Token kiểm soát sửa role đồng thời. |

**Indexes khai báo riêng:**

- `RoleNameIndex`: `UNIQUE (NormalizedName) WHERE NormalizedName IS NOT NULL`.

**Lưu ý nghiệp vụ:** Vai trò là phân quyền, khác với MembershipTiers. NULL ở DDL không có nghĩa ứng dụng nên tạo role không tên.

### 3.3 AspNetUserRoles

**Mục đích:** Bảng nối nhiều–nhiều giữa tài khoản và vai trò. **Primary key:** `(UserId, RoleId)`.

| Column | Constraints | Notes |
|---|---|---|
| `UserId` | `nvarchar(450)`; `NOT NULL`; `thuộc PK ghép`; FK → `AspNetUsers.Id`; ON DELETE CASCADE | Tài khoản được gán vai trò. |
| `RoleId` | `nvarchar(450)`; `NOT NULL`; `thuộc PK ghép`; FK → `AspNetRoles.Id`; ON DELETE CASCADE | Vai trò được cấp; PK ghép ngăn trùng một cặp user–role. |

**Indexes khai báo riêng:**

- `IX_AspNetUserRoles_RoleId`: `(RoleId)`.

**Lưu ý nghiệp vụ:** SQL hỗ trợ nhiều role/user. Quy tắc một role khi tạo tài khoản là quy tắc ứng dụng, không phải UNIQUE(UserId).

### 3.4 AspNetUserClaims

**Mục đích:** Các claim bổ sung gắn với từng tài khoản theo cơ chế Identity. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa của claim. |
| `UserId` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE CASCADE | Tài khoản sở hữu claim. |
| `ClaimType` | `nvarchar(max)`; `NULL` | Loại claim. |
| `ClaimValue` | `nvarchar(max)`; `NULL` | Giá trị claim; Identity quản lý. |

**Indexes khai báo riêng:**

- `IX_AspNetUserClaims_UserId`: `(UserId)`.

**Lưu ý nghiệp vụ:** Claim không thay thế xác thực danh tính. Cascade chỉ mô tả DDL; không cho phép xóa tài khoản có lịch sử nghiệp vụ.

### 3.5 AspNetRoleClaims

**Mục đích:** Các claim được cấp thông qua vai trò theo cơ chế Identity. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa của claim vai trò. |
| `RoleId` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetRoles.Id`; ON DELETE CASCADE | Vai trò sở hữu claim. |
| `ClaimType` | `nvarchar(max)`; `NULL` | Loại claim của role. |
| `ClaimValue` | `nvarchar(max)`; `NULL` | Giá trị claim được cấp qua role. |

**Indexes khai báo riêng:**

- `IX_AspNetRoleClaims_RoleId`: `(RoleId)`.

**Lưu ý nghiệp vụ:** Bảng này có trong schema nhưng chưa được giao rõ trong phân công 20 bảng ban đầu; cần nhóm chốt người phụ trách.

### 3.6 AspNetUserLogins

**Mục đích:** Liên kết tài khoản BMS với định danh của nhà cung cấp đăng nhập ngoài. **Primary key:** `(LoginProvider, ProviderKey)`.

| Column | Constraints | Notes |
|---|---|---|
| `LoginProvider` | `nvarchar(128)`; `NOT NULL`; `thuộc PK ghép` | Tên nhà cung cấp đăng nhập ngoài. |
| `ProviderKey` | `nvarchar(128)`; `NOT NULL`; `thuộc PK ghép` | Định danh user tại nhà cung cấp; cùng LoginProvider tạo PK. |
| `ProviderDisplayName` | `nvarchar(max)`; `NULL` | Tên nhà cung cấp để hiển thị. |
| `UserId` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE CASCADE | Tài khoản BMS được liên kết; có bảng không có nghĩa đã tích hợp Google/Facebook. |

**Indexes khai báo riêng:**

- `IX_AspNetUserLogins_UserId`: `(UserId)`.

**Lưu ý nghiệp vụ:** BMS hiện dùng đăng nhập Identity; cần triển khai cấu hình provider trước khi hỗ trợ đăng nhập ngoài.

### 3.7 AspNetUserTokens

**Mục đích:** Lưu token theo tài khoản, nhà cung cấp và tên token; không phải bảng phiên chơi. **Primary key:** `(UserId, LoginProvider, Name)`.

| Column | Constraints | Notes |
|---|---|---|
| `UserId` | `nvarchar(450)`; `NOT NULL`; `thuộc PK ghép`; FK → `AspNetUsers.Id`; ON DELETE CASCADE | Tài khoản sở hữu token. |
| `LoginProvider` | `nvarchar(128)`; `NOT NULL`; `thuộc PK ghép` | Nhà cung cấp của token. |
| `Name` | `nvarchar(128)`; `NOT NULL`; `thuộc PK ghép` | Tên/loại token; cùng UserId và LoginProvider tạo PK. |
| `Value` | `nvarchar(max)`; `NULL` | Giá trị token; không đưa vào giao diện hoặc log công khai. |

**Lưu ý nghiệp vụ:** Không lưu mật khẩu rõ vào Value. Quy trình phát hành/thu hồi token thuộc Identity và ứng dụng.

### 3.8 MembershipTiers

**Mục đích:** Danh mục hạng thành viên và phần trăm ưu đãi. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa hạng thành viên tự tăng. |
| `TierName` | `nvarchar(50)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(TierName))) > 0` | Tên hạng; seed hiện chưa định nghĩa các hạng cụ thể. |
| `DiscountPercent` | `decimal(5,2)`; `NOT NULL`; `DEFAULT 0`; CHECK `DiscountPercent >= 0 AND DiscountPercent <= 100` | Phần trăm ưu đãi; 10.00 nghĩa là 10%, không phải hệ số 0.10. |

**Lưu ý nghiệp vụ:** Chưa có MinPoints, thứ tự hạng hoặc lịch sử điểm. Ngưỡng nâng hạng và phạm vi ưu đãi cần TV3/TV4 thống nhất; không tự ghi các cột chưa có vào SDS.

### 3.9 TableTypes

**Mục đích:** Danh mục loại bàn và đơn giá giờ cơ sở. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa loại bàn. |
| `Name` | `nvarchar(50)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(Name))) > 0` | Tên loại bàn, ví dụ Pool hoặc Carom. |
| `HourlyRate` | `decimal(18,2)`; `NOT NULL`; CHECK `HourlyRate > 0` | Đơn giá cơ sở cho một giờ; số tiền theo VND. |

### 3.10 BilliardTables

**Mục đích:** Thông tin bàn bida vật lý, vị trí và trạng thái hiện tại. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa bàn. |
| `TableCode` | `nvarchar(20)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(TableCode))) > 0` | Mã bàn duy nhất, ví dụ B01. |
| `TableTypeId` | `int`; `NOT NULL`; FK → `TableTypes.Id`; ON DELETE NO ACTION | Loại bàn tương ứng. |
| `FloorNumber` | `int`; `NOT NULL`; `DEFAULT 1`; CHECK `FloorNumber >= 0` | Tầng/khu vực bàn; schema cho phép 0. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Available'`; CHECK `Status IN ('Available','InUse','AwaitingPayment','Maintenance','Inactive')` | Trạng thái vật lý; đặt lịch tương lai không tự biến bàn thành Reserved. |
| `RowVersion` | `rowversion`; `NOT NULL` | Token SQL Server tự sinh để phát hiện cập nhật đồng thời; không phải ngày giờ. |

**Indexes khai báo riêng:**

- `IX_BilliardTables_Type_Status`: `(TableTypeId, Status)`.

**Lưu ý nghiệp vụ:** Chỉ cập nhật trạng thái theo vòng đời nghiệp vụ. Đặt bàn theo lịch và trạng thái vật lý là hai thông tin khác nhau.

### 3.11 PricingConfigs

**Mục đích:** Cấu hình đơn giá theo khoảng giờ cho từng loại bàn. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa cấu hình giá. |
| `TableTypeId` | `int`; `NOT NULL`; FK → `TableTypes.Id`; ON DELETE NO ACTION | Loại bàn áp dụng. |
| `HourlyRate` | `decimal(18,2)`; `NOT NULL`; CHECK `HourlyRate > 0` | Giá giờ cho khoảng thời gian này. |
| `StartHour` | `time`; `NOT NULL`; tham gia `CK_PricingConfig_Hours` (xem dưới) | Giờ bắt đầu trong ngày; chưa có quy tắc ngày trong tuần. |
| `EndHour` | `time`; `NOT NULL`; tham gia `CK_PricingConfig_Hours` (xem dưới) | Giờ kết thúc; CHECK hiện không cho khoảng qua nửa đêm. |

**CHECK phối hợp nhiều cột:**

- `CK_PricingConfig_Hours`: `StartHour < EndHour`.

**Lưu ý nghiệp vụ:** Chưa có constraint chặn các khoảng cấu hình giá chồng nhau. usp_OpenSession hiện đọc TableTypes.HourlyRate, chưa áp dụng PricingConfigs; không mô tả giá theo khung giờ là đã chạy.

### 3.12 Bookings

**Mục đích:** Lượt đặt bàn, khách đặt, khoảng thời gian, check-in và hủy đặt. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa lượt đặt. |
| `CustomerId` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Khách đặt; FK chỉ kiểm tra tồn tại, role Customer do procedure/backend kiểm tra. |
| `TableId` | `int`; `NOT NULL`; FK → `BilliardTables.Id`; ON DELETE NO ACTION | Bàn được đặt. |
| `StartAtUtc` | `datetime2(0)`; `NOT NULL`; tham gia `CK_Booking_Times` (xem dưới) | Thời điểm bắt đầu dự kiến, UTC. |
| `EndAtUtc` | `datetime2(0)`; `NOT NULL`; tham gia `CK_Booking_Times` (xem dưới) | Thời điểm kết thúc dự kiến, UTC; phải sau giờ bắt đầu. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Confirmed'`; CHECK `Status IN ('Confirmed','CheckedIn','Completed','Cancelled','NoShow')`; tham gia `CK_Booking_Cancel` (xem dưới) | Trạng thái lượt đặt theo danh sách CHECK. |
| `CreatedAtUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm tạo lượt đặt, UTC. |
| `CheckedInAtUtc` | `datetime2(0)`; `NULL`; tham gia `CK_Booking_Checkin` (xem dưới) | Thời điểm xác nhận khách đến; phải có cùng CheckedInById. |
| `CheckedInById` | `nvarchar(450)`; `NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION; tham gia `CK_Booking_Checkin` (xem dưới) | Người xác nhận check-in; quyền Staff/Admin do nghiệp vụ kiểm tra. |
| `CancelledAtUtc` | `datetime2(0)`; `NULL`; tham gia `CK_Booking_Cancel` (xem dưới) | Thời điểm hủy; phải có khi Status=Cancelled. |
| `CancelledById` | `nvarchar(450)`; `NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION; tham gia `CK_Booking_Cancel` (xem dưới) | Người hủy; phải có khi Status=Cancelled. |
| `CancellationReason` | `nvarchar(300)`; `NULL` | Lý do hủy, có thể NULL theo DDL. |
| `Notes` | `nvarchar(300)`; `NULL` | Ghi chú lượt đặt. |
| `RowVersion` | `rowversion`; `NOT NULL` | Token phát hiện cập nhật đồng thời. |

**CHECK phối hợp nhiều cột:**

- `CK_Booking_Times`: `EndAtUtc > StartAtUtc`.
- `CK_Booking_Checkin`: ` (CheckedInAtUtc IS NULL AND CheckedInById IS NULL) OR (CheckedInAtUtc IS NOT NULL AND CheckedInById IS NOT NULL)`.
- `CK_Booking_Cancel`: ` (Status = 'Cancelled' AND CancelledAtUtc IS NOT NULL AND CancelledById IS NOT NULL) OR (Status <> 'Cancelled' AND CancelledAtUtc IS NULL AND CancelledById IS NULL)`.

**Indexes khai báo riêng:**

- `IX_Bookings_Table_Time`: `(TableId, StartAtUtc, EndAtUtc) INCLUDE (Status)`.
- `IX_Bookings_Customer`: `(CustomerId, StartAtUtc)`.

**Lưu ý nghiệp vụ:** CHECK thời gian không chặn các booking trùng lịch. Procedure serialize theo bàn và kiểm tra khoảng [StartAtUtc, EndAtUtc); không bỏ qua bằng cách ghi EF trực tiếp mà thiếu kiểm tra tương đương. Check-in chưa tự mở phiên.

### 3.13 PlaySessions

**Mục đích:** Phiên chơi thực tế, người mở/đóng và giá giờ được ghi nhận lúc mở. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa phiên chơi. |
| `TableId` | `int`; `NOT NULL`; FK → `BilliardTables.Id`; ON DELETE NO ACTION; `UNIQUE INDEX UX_PlaySessions_ActiveTable(TableId) WHERE Status = 'Active'` | Bàn được sử dụng; mỗi bàn tối đa một phiên Active. |
| `BookingId` | `int`; `NULL`; FK → `Bookings.Id`; ON DELETE NO ACTION; `UNIQUE INDEX UX_PlaySessions_Booking(BookingId) WHERE BookingId IS NOT NULL` | Lượt đặt tương ứng, có thể NULL cho walk-in; một booking tối đa một phiên. |
| `CustomerId` | `nvarchar(450)`; `NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Khách chơi; schema cho phép khách vãng lai chưa gắn tài khoản. |
| `OpenedById` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Người mở phiên; lấy định danh đã xác thực. |
| `ClosedById` | `nvarchar(450)`; `NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION; tham gia `CK_Session_Lifecycle` (xem dưới) | Người đóng phiên; NULL khi Active, bắt buộc khi Closed. |
| `StartAtUtc` | `datetime2(0)`; `NOT NULL`; tham gia `CK_Session_Lifecycle` (xem dưới) | Thời điểm bắt đầu thực tế, UTC. |
| `EndAtUtc` | `datetime2(0)`; `NULL`; tham gia `CK_Session_Lifecycle` (xem dưới) | Thời điểm kết thúc thực tế, UTC; trạng thái Closed mới có. |
| `HourlyRateSnapshot` | `decimal(18,2)`; `NOT NULL`; CHECK `HourlyRateSnapshot > 0` | Giá giờ tại lúc mở; đổi giá danh mục không làm đổi phiên cũ. |
| `PlaytimeAmount` | `decimal(18,2)`; `NULL`; tham gia `CK_Session_Lifecycle` (xem dưới) | Tiền giờ khi đóng phiên; Active để NULL. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Active'`; CHECK `Status IN ('Active','Closed')`; tham gia `CK_Session_Lifecycle` (xem dưới) | Active hoặc Closed; phải khớp điều kiện vòng đời. |
| `RowVersion` | `rowversion`; `NOT NULL` | Token phát hiện cập nhật đồng thời. |

**CHECK phối hợp nhiều cột:**

- `CK_Session_Lifecycle`: ` (Status = 'Active' AND EndAtUtc IS NULL AND ClosedById IS NULL AND PlaytimeAmount IS NULL) OR (Status = 'Closed' AND EndAtUtc IS NOT NULL AND EndAtUtc >= StartAtUtc AND ClosedById IS NOT NULL AND PlaytimeAmount IS NOT NULL AND PlaytimeAmount >= 0)`.

**Indexes khai báo riêng:**

- `UX_PlaySessions_ActiveTable`: `UNIQUE (TableId) WHERE Status = 'Active'`.
- `UX_PlaySessions_Booking`: `UNIQUE (BookingId) WHERE BookingId IS NOT NULL`.
- `IX_PlaySessions_Table_Start`: `(TableId, StartAtUtc)`.

**Lưu ý nghiệp vụ:** usp_CloseSession hiện tính toàn bộ giờ theo giá snapshot và chuyển bàn sang AwaitingPayment. Chưa trừ giờ combo, thanh toán hoặc trả bàn về Available.

### 3.14 ProductCategories

**Mục đích:** Danh mục nhóm đồ ăn và nước uống. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa danh mục món. |
| `Name` | `nvarchar(60)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(Name))) > 0` | Tên nhóm, ví dụ Đồ uống hoặc Đồ ăn. |

### 3.15 Products

**Mục đích:** Món đang bán, giá, tồn kho và trạng thái kinh doanh. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa sản phẩm. |
| `CategoryId` | `int`; `NOT NULL`; FK → `ProductCategories.Id`; ON DELETE NO ACTION | Danh mục sản phẩm. |
| `Name` | `nvarchar(100)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(Name))) > 0` | Tên món duy nhất. |
| `Price` | `decimal(18,2)`; `NOT NULL`; CHECK `Price > 0` | Giá món lẻ theo VND; phải lớn hơn 0. |
| `StockQuantity` | `int`; `NOT NULL`; `DEFAULT 0`; CHECK `StockQuantity >= 0` | Tồn kho không âm; thao tác bán cần transaction để chống bán vượt tồn. |
| `IsActive` | `bit`; `NOT NULL`; `DEFAULT 1` | Còn kinh doanh hay đã ngừng bán; khác với còn/hết hàng. |
| `RowVersion` | `rowversion`; `NOT NULL` | Token phát hiện cập nhật đồng thời. |

**Indexes khai báo riêng:**

- `IX_Products_Category_Active`: `(CategoryId, IsActive)`.

**Lưu ý nghiệp vụ:** Món đã có lịch sử nên ngừng bán bằng IsActive. Phân quyền và đồng bộ trừ tồn không được FK/CHECK này tự thực hiện.

### 3.16 Orders

**Mục đích:** Lượt gọi món F&B gắn với phiên chơi. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa đơn gọi món. |
| `SessionId` | `int`; `NOT NULL`; FK → `PlaySessions.Id`; ON DELETE NO ACTION | Phiên chơi phát sinh đơn. |
| `OrderTimeUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm gọi món, UTC. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Pending'`; CHECK `Status IN ('Pending', 'Preparing', 'Served', 'Cancelled')` | Pending, Preparing, Served hoặc Cancelled. |

**Indexes khai báo riêng:**

- `IX_Orders_Session`: `(SessionId)`.

**Lưu ý nghiệp vụ:** Chưa có nhân viên ghi nhận hoặc liên kết nguồn combo trong bảng Orders hiện tại. Chưa có constraint bắt buộc phiên đang Active khi gọi món.

### 3.17 OrderDetails

**Mục đích:** Chi tiết món, số lượng và giá tại thời điểm gọi trong một đơn F&B. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa dòng món. |
| `OrderId` | `int`; `NOT NULL`; FK → `Orders.Id`; ON DELETE CASCADE | Đơn chứa dòng món; FK hiện có cascade khi xóa đơn. |
| `ProductId` | `int`; `NOT NULL`; FK → `Products.Id`; ON DELETE NO ACTION | Sản phẩm được gọi; không cascade xóa sản phẩm. |
| `Quantity` | `int`; `NOT NULL`; CHECK `Quantity > 0` | Số lượng món, phải lớn hơn 0. |
| `UnitPrice` | `decimal(18,2)`; `NOT NULL`; CHECK `UnitPrice >= 0` | Giá một món tại thời điểm gọi; DDL cho phép 0. |
| `Subtotal` | `AS (Quantity * UnitPrice) PERSISTED`; cột tính toán, không nhập trực tiếp | Thành tiền SQL tự tính bằng Quantity × UnitPrice; không nhập trực tiếp. |

**Lưu ý nghiệp vụ:** DDL chưa UNIQUE(OrderId, ProductId), nên có thể có nhiều dòng cùng món. Dòng món combo chưa có liên kết nguồn; không tính các quyền lợi combo như món lẻ để thu tiền/trừ tồn hai lần.

### 3.18 Combos

**Mục đích:** Danh mục gói gồm giờ chơi và các món đi kèm. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa combo. |
| `Name` | `nvarchar(100)`; `NOT NULL`; `UNIQUE`; CHECK `LEN(LTRIM(RTRIM(Name))) > 0` | Tên gói duy nhất. |
| `Price` | `decimal(18,2)`; `NOT NULL`; CHECK `Price >= 0` | Giá một gói; DDL cho phép 0. |
| `PlaytimeHours` | `decimal(5,2)`; `NOT NULL`; CHECK `PlaytimeHours > 0` | Số giờ chơi trong một gói. |
| `IsActive` | `bit`; `NOT NULL`; `DEFAULT 1` | Gói đang bán hay đã ngừng bán. |

**Lưu ý nghiệp vụ:** Một gói gồm giờ và các món tại ComboItems. Schema không tự bắt buộc gói có ít nhất một món; chưa có loại bàn/khung giờ áp dụng.

### 3.19 ComboItems

**Mục đích:** Các món và số lượng cho mỗi đơn vị combo trong danh mục hiện tại. **Primary key:** `(ComboId, ProductId)`.

| Column | Constraints | Notes |
|---|---|---|
| `ComboId` | `int`; `NOT NULL`; `thuộc PK ghép`; FK → `Combos.Id`; ON DELETE NO ACTION | Combo trong danh mục. |
| `ProductId` | `int`; `NOT NULL`; `thuộc PK ghép`; FK → `Products.Id`; ON DELETE NO ACTION | Món đi kèm; PK ghép ngăn lặp một món trong cùng combo. |
| `Quantity` | `int`; `NOT NULL`; CHECK `Quantity > 0` | Số lượng món cho MỘT gói; chưa phải tổng số lượng phiên mua. |

**Indexes khai báo riêng:**

- `IX_ComboItems_ProductId`: `(ProductId)`.

**Lưu ý nghiệp vụ:** Danh mục có thể thay đổi; thông tin đã mua phải đọc từ SessionCombos/SessionComboItems. FK đều NO ACTION khi xóa.

### 3.20 SessionCombos

**Mục đích:** Lượt mua combo của phiên chơi, số gói, tên/giá/giờ tại thời điểm mua. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa lượt mua combo. |
| `SessionId` | `int`; `NOT NULL`; FK → `PlaySessions.Id`; ON DELETE NO ACTION | Phiên mua gói; schema cho phép nhiều lượt mua trong một phiên. |
| `ComboId` | `int`; `NOT NULL`; FK → `Combos.Id`; ON DELETE NO ACTION | Combo nguồn; không tự cập nhật thông tin đã mua theo danh mục mới. |
| `Quantity` | `int`; `NOT NULL`; CHECK `Quantity > 0` | Số gói trong lượt mua. |
| `ComboNameSnapshot` | `nvarchar(100)`; `NOT NULL`; CHECK `LEN(LTRIM(RTRIM(ComboNameSnapshot))) > 0` | Tên combo tại lúc mua. |
| `UnitPriceSnapshot` | `decimal(18,2)`; `NOT NULL`; CHECK `UnitPriceSnapshot >= 0` | Giá MỘT gói tại lúc mua; tổng giá gói = Quantity × UnitPriceSnapshot. |
| `PlaytimeHoursSnapshot` | `decimal(5,2)`; `NOT NULL`; CHECK `PlaytimeHoursSnapshot > 0` | Giờ chơi MỘT gói tại lúc mua; tổng giờ = Quantity × PlaytimeHoursSnapshot. |
| `PurchasedAtUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm ghi nhận mua, UTC. |
| `CreatedById` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Người ghi nhận; backend kiểm tra quyền và lấy ID từ phiên đăng nhập. |

**Indexes khai báo riêng:**

- `IX_SessionCombos_SessionId`: `(SessionId)`.
- `IX_SessionCombos_ComboId`: `(ComboId)`.
- `IX_SessionCombos_CreatedById`: `(CreatedById)`.

**Lưu ý nghiệp vụ:** Backend sao chép giá/tên/giờ từ database trong cùng transaction với các món; không tin giá từ form. Schema chưa có idempotency key, hủy/hoàn hoặc trạng thái giao món.

### 3.21 SessionComboItems

**Mục đích:** Thành phần món được lưu tại thời điểm mua combo để giữ lịch sử. **Primary key:** `(SessionComboId, ProductId)`.

| Column | Constraints | Notes |
|---|---|---|
| `SessionComboId` | `int`; `NOT NULL`; `thuộc PK ghép`; FK → `SessionCombos.Id`; ON DELETE NO ACTION | Lượt mua combo chứa quyền lợi món. |
| `ProductId` | `int`; `NOT NULL`; `thuộc PK ghép`; FK → `Products.Id`; ON DELETE NO ACTION | Sản phẩm đi kèm. |
| `ProductNameSnapshot` | `nvarchar(100)`; `NOT NULL`; CHECK `LEN(LTRIM(RTRIM(ProductNameSnapshot))) > 0` | Tên món tại lúc mua, giữ nguyên khi đổi tên danh mục. |
| `QuantityPerCombo` | `int`; `NOT NULL`; CHECK `QuantityPerCombo > 0` | Số món cho MỘT gói tại lúc mua; tổng món = QuantityPerCombo × SessionCombos.Quantity. |

**Indexes khai báo riêng:**

- `IX_SessionComboItems_ProductId`: `(ProductId)`.

**Lưu ý nghiệp vụ:** Lưu quyền lợi đã mua, chưa mô tả món đã giao. Snapshot còn cần service bảo vệ khỏi UPDATE trực tiếp. FK đều NO ACTION khi xóa.

### 3.22 Invoices

**Mục đích:** Hóa đơn của phiên chơi: tiền giờ, F&B, giảm giá và trạng thái thanh toán. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa hóa đơn. |
| `SessionId` | `int`; `NOT NULL`; `UNIQUE`; FK → `PlaySessions.Id`; ON DELETE NO ACTION | Phiên được lập hóa đơn; UNIQUE quy định tối đa một hóa đơn/phiên. |
| `CustomerId` | `nvarchar(450)`; `NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Khách gắn với hóa đơn; có thể NULL. |
| `TotalPlaytime` | `decimal(18,2)`; `NOT NULL`; `DEFAULT 0` | Tổng tiền giờ; DDL chưa CHECK không âm. |
| `TotalFandB` | `decimal(18,2)`; `NOT NULL`; `DEFAULT 0` | Tổng tiền món; DDL chưa CHECK không âm. |
| `DiscountAmount` | `decimal(18,2)`; `NOT NULL`; `DEFAULT 0` | Số tiền giảm, không phải phần trăm; chưa có CHECK giới hạn mức giảm. |
| `FinalAmount` | `AS (TotalPlaytime + TotalFandB - DiscountAmount) PERSISTED`; cột tính toán, không nhập trực tiếp | SQL tự tính TotalPlaytime + TotalFandB − DiscountAmount; schema chưa chặn kết quả âm. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Unpaid'`; CHECK `Status IN ('Unpaid', 'Paid')` | Unpaid hoặc Paid; nghiệp vụ phải đối chiếu giao dịch để cập nhật. |
| `CreatedAtUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm tạo hóa đơn, UTC. |

**Lưu ý nghiệp vụ:** Chưa có thành phần tiền combo riêng, hóa đơn âm chưa bị CHECK chặn. Công thức kết hợp combo/giờ vượt/F&B cần thống nhất trước khi bán; không suy ra tổng đúng từ việc schema có bảng.

### 3.23 PaymentTransactions

**Mục đích:** Các giao dịch thanh toán của hóa đơn, phương thức và mã tham chiếu. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa giao dịch. |
| `InvoiceId` | `int`; `NOT NULL`; FK → `Invoices.Id`; ON DELETE CASCADE | Hóa đơn nhận thanh toán; FK hiện có cascade khi xóa hóa đơn. |
| `PaymentMethod` | `varchar(20)`; `NOT NULL`; CHECK `PaymentMethod IN ('Cash', 'VNPay', 'SePay', 'Transfer')` | Cash, VNPay, SePay hoặc Transfer theo CHECK; enum không có nghĩa đã tích hợp các cổng. |
| `AmountPaid` | `decimal(18,2)`; `NOT NULL`; CHECK `AmountPaid > 0` | Số tiền giao dịch theo VND, lớn hơn 0. |
| `GatewayRefCode` | `nvarchar(100)`; `NULL` | Mã tham chiếu cổng/ngân hàng; có thể NULL và chưa có UNIQUE. |
| `TransactionTimeUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()` | Thời điểm ghi nhận giao dịch, UTC. |

**Lưu ý nghiệp vụ:** Chưa có UNIQUE cho mã cổng hoặc xử lý IPN lặp trong schema. DDL có cascade nhưng nghiệp vụ cần giữ lịch sử thanh toán, không xóa hóa đơn đã thanh toán tùy ý.

### 3.24 WorkShifts

**Mục đích:** Ca làm của nhân viên, thời điểm và số tiền đầu/cuối ca. **Primary key:** `(Id)`.

| Column | Constraints | Notes |
|---|---|---|
| `Id` | `int`; `NOT NULL`; `IDENTITY`; `PK` | Khóa ca làm. |
| `StaffId` | `nvarchar(450)`; `NOT NULL`; FK → `AspNetUsers.Id`; ON DELETE NO ACTION | Tài khoản nhân viên; FK không tự kiểm tra role Staff. |
| `StartTimeUtc` | `datetime2(0)`; `NOT NULL`; `DEFAULT SYSUTCDATETIME()`; tham gia `CK_Shift_Time` (xem dưới) | Thời điểm mở ca, UTC. |
| `EndTimeUtc` | `datetime2(0)`; `NULL`; tham gia `CK_Shift_Time` (xem dưới) | Thời điểm đóng ca, UTC; có thể NULL. |
| `StartingCash` | `decimal(18,2)`; `NOT NULL`; `DEFAULT 0` | Tiền đầu ca; chưa có CHECK không âm. |
| `EndingCash` | `decimal(18,2)`; `NULL` | Tiền cuối ca; NULL nếu chưa ghi nhận, chưa có CHECK không âm. |
| `Status` | `varchar(20)`; `NOT NULL`; `DEFAULT 'Active'`; CHECK `Status IN ('Active', 'Closed')` | Active hoặc Closed; DDL chưa bắt buộc Closed có EndTimeUtc. |
| `Notes` | `nvarchar(500)`; `NULL` | Ghi chú ca làm. |

**CHECK phối hợp nhiều cột:**

- `CK_Shift_Time`: `EndTimeUtc IS NULL OR EndTimeUtc >= StartTimeUtc`.

**Lưu ý nghiệp vụ:** Chưa có unique filtered index giới hạn một ca Active/nhân viên, chưa có liên kết ca với Orders/Invoices/PaymentTransactions; chưa có CHECK ràng buộc trạng thái với thời điểm đóng.

### 3.25 Các quan hệ và giới hạn cần thống nhất

- Tài khoản → role là nhiều–nhiều qua AspNetUserRoles. MembershipTiers là hạng ưu đãi, không phải role bảo mật.
- Một bàn có nhiều lượt đặt và nhiều phiên trong lịch sử. Mỗi booking tối đa một phiên; mỗi phiên tối đa một hóa đơn. Phiên walk-in có thể không có booking/tài khoản khách.
- Một phiên có nhiều đơn F&B, nhiều lượt mua combo; một hóa đơn có nhiều giao dịch. Những quan hệ này lấy từ FK/UNIQUE, không tự xác nhận luồng thanh toán đã triển khai.
- ComboItems là danh mục hiện tại; SessionCombos và SessionComboItems là thông tin lúc mua. Tổng giá gói = số gói × giá một gói; tổng giờ/món nhân theo số gói. Chưa có liên kết nguồn giao món combo trong OrderDetails hoặc thành phần tiền combo riêng trong Invoices.
- Những điểm chưa có ràng buộc SQL: tiền hóa đơn/tiền ca không âm, GatewayRefCode duy nhất, một ca Active/nhân viên, khoảng cấu hình giá không trùng, ngưỡng nâng hạng và lịch sử điểm. Đây là phần cần thiết kế tiếp, không mô tả là đã tồn tại.
- Tổng hợp CASCADE hiện có: AspNetUserRoles từ User/Role; AspNetUserClaims, AspNetUserLogins, AspNetUserTokens từ User; AspNetRoleClaims từ Role; OrderDetails từ Orders; PaymentTransactions từ Invoices. Các FK còn lại dùng NO ACTION. Không dùng khả năng cascade để xóa lịch sử nhân viên/hóa đơn trái quy tắc ứng dụng.

**Cài đặt/kiểm tra:** xem `BMS_Database_Starter/README_VI.md`. Không chạy Full hoặc seed lại trên database đang có dữ liệu. Phần này là tài liệu thiết kế, không phải script thay đổi database.

# II. Detailed Code Design

## 1. Quản lý tài khoản và nhân viên

Phần này gồm ba chức năng được giao cho Hùng: Login, Tạo nhân viên và Đổi mật khẩu. GET trả form; POST nhận dữ liệu và thực hiện thao tác. Các sequence mô tả thiết kế đích; chỗ khác với code đã được đánh dấu ngay dưới sơ đồ và tổng hợp ở phụ lục.

### 1.1 Class Diagram

```mermaid
classDiagram
    class IdentityUser {
        +string Id
        +string UserName
        +string Email
        +string PasswordHash
        +string SecurityStamp
        +string ConcurrencyStamp
    }
    class ApplicationUser {
        +string FullName
        +bool IsActive
        +DateTime CreatedAtUtc
        +string EmployeeCode
        +DateOnly HireDate
    }
    class AccountController {
        -UserManager userManager
        -SignInManager signInManager
        +Login(LoginViewModel, returnUrl) Task~IActionResult~
        +Logout() Task~IActionResult~
    }
    class EmployeeController {
        -UserManager userManager
        +Create(CreateEmployeeViewModel) Task~IActionResult~
        +Index(search, status, page) Task~IActionResult~
        +ToggleLock(id) Task~IActionResult~
        -GenerateNextEmployeeCodeAsync() Task~string~
    }
    class ProfileController {
        -UserManager userManager
        -SignInManager signInManager
        +ChangePassword(ChangePasswordViewModel) Task~IActionResult~
    }
    class LoginViewModel {
        +string Username
        +string Password
        +bool RememberMe
    }
    class CreateEmployeeViewModel {
        +string FullName
        +string UserName
        +string Email
        +string PhoneNumber
        +string EmployeeCode
        +DateOnly HireDate
        +string Password
        +string ConfirmPassword
    }
    class ChangePasswordViewModel {
        +string CurrentPassword
        +string NewPassword
        +string ConfirmPassword
    }
    class UserManager {
        +FindByNameAsync(name)
        +CreateAsync(user, password)
        +AddToRoleAsync(user, role)
        +GetUserAsync(principal)
        +ChangePasswordAsync(user, oldPassword, newPassword)
    }
    class SignInManager {
        +PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure)
        +RefreshSignInAsync(user)
        +SignOutAsync()
    }
    class IdentityEFStores
    class IdentityDbContext
    class ApplicationDbContext {
        +OnModelCreating(builder)
    }
    IdentityUser <|-- ApplicationUser
    IdentityDbContext <|-- ApplicationDbContext
    AccountController --> UserManager : DI
    AccountController --> SignInManager : DI
    EmployeeController --> UserManager : DI
    EmployeeController ..> ApplicationDbContext : transaction cần bổ sung
    ProfileController --> UserManager : DI
    ProfileController --> SignInManager : DI
    AccountController ..> LoginViewModel : input
    EmployeeController ..> CreateEmployeeViewModel : input
    ProfileController ..> ChangePasswordViewModel : input
    SignInManager --> UserManager : sử dụng
    UserManager --> IdentityEFStores : sử dụng
    IdentityEFStores --> ApplicationDbContext : cùng scoped context
    ApplicationDbContext ..> ApplicationUser : mapping
```

**Hình II.1 — Class Diagram cho ba chức năng của Hùng.** Tên `UserManager`, `SignInManager`, `IdentityDbContext` được rút gọn trên sơ đồ; kiểu thực tế dùng generic với `ApplicationUser`. `IdentityEFStores` là thành phần framework gom UserStore/RoleStore cho dễ đọc, không phải lớp tự viết trong repo. Các thuộc tính có thể null được mô tả đầy đủ ở bảng database và source; không suy ra NOT NULL từ kiểu rút gọn trong hình.

**Class relationships:** ApplicationUser IS-A IdentityUser; ApplicationDbContext IS-A IdentityDbContext. Controller giữ tham chiếu manager do DI cấp, là association, không phải composition vì controller không sở hữu vòng đời manager. ViewModel là dependency của action. Không thêm quan hệ aggregation/composition nếu source không có ý nghĩa sở hữu tương ứng. MembershipTiers xuất hiện trong ERD; chưa tạo một entity C# giả để đưa vào ba luồng không sử dụng hạng thành viên.

| Class | Trách nhiệm |
|---|---|
| AccountController | Đọc tài khoản theo username, chặn inactive, gọi đăng nhập và điều hướng an toàn |
| EmployeeController | Chỉ Admin tạo Staff, gán role cố định; cần cùng DbContext để bao transaction |
| ProfileController | Lấy tài khoản từ principal, đổi mật khẩu của chính tài khoản đó, làm mới cookie |
| Các ViewModel | Chứa đúng trường form, Required/StringLength/Compare; không chứa role có thể tự nâng quyền |
| ApplicationUser | Mở rộng tài khoản Identity bằng thuộc tính BMS |
| ApplicationDbContext | Mapping Identity đến schema SQL, cung cấp transaction và EF tracking |
| UserManager / SignInManager | API framework cho vòng đời tài khoản, mật khẩu, role và đăng nhập |

### 1.2 Sequence Diagram — Login

**Actor:** Admin, Staff hoặc Customer. **Route:** GET/POST `/Account/Login`. **Đầu vào:** Username, Password, RememberMe, returnUrl tùy chọn. **Điều kiện:** tài khoản tồn tại, IsActive=true, có mật khẩu hợp lệ, không bị khóa tạm. **Đầu ra thành công:** cookie đăng nhập và điều hướng đến URL local hợp lệ hoặc trang theo role.

```mermaid
sequenceDiagram
    autonumber
    actor U as Người dùng
    participant B as Browser / Login View
    participant C as AccountController
    participant UM as UserManager
    participant SM as SignInManager
    participant DB as Identity EF / SQL Server
    U->>B: Nhập username, password, RememberMe
    B->>C: POST /Account/Login + anti-forgery token
    Note over B,C: Framework từ chối token không hợp lệ trước action
    alt ModelState không hợp lệ
        C-->>B: Trả form và lỗi validation
    else ModelState hợp lệ
        C->>UM: FindByNameAsync(Username)
        UM->>DB: Tra NormalizedUserName
        DB-->>UM: User hoặc null
        UM-->>C: User hoặc null
        alt Không có user hoặc IsActive=false
            C-->>B: Thông báo đăng nhập không hợp lệ
        else Tài khoản hoạt động
            C->>SM: PasswordSignInAsync(user, password, RememberMe, true)
            SM->>UM: Kiểm tra khóa tạm và mật khẩu
            UM->>DB: Đọc / cập nhật trạng thái bảo mật khi cần
            DB-->>UM: Kết quả lưu / đọc
            UM-->>SM: Kết quả kiểm tra
            alt Mật khẩu hợp lệ và được phép đăng nhập
                SM-->>B: Ghi cookie qua HTTP response
                SM-->>C: Succeeded
                C->>UM: IsInRoleAsync khi không có returnUrl local
                UM-->>C: Vai trò
                C-->>B: Redirect URL local hoặc Employee / Staff / Customer
            else Đang bị khóa tạm
                SM-->>C: IsLockedOut
                C-->>B: Thông báo khóa tạm, không cấp cookie
            else Sai mật khẩu hoặc trạng thái chưa hỗ trợ
                SM-->>C: Không thành công
                C-->>B: Thông báo chung, không cấp cookie
            end
        end
    end
```

**Hình II.2 — Đăng nhập bằng Identity.** Luồng kiểm tra bên trong manager được rút gọn; Identity tự quản lý hash, failed count và lockout. RememberMe quyết định cookie có được lưu lâu dài hay chỉ trong phiên trình duyệt, không bỏ qua các kiểm tra bảo mật.

Quy tắc hiện có: tối đa 5 lần sai trước khóa tạm 5 phút với tài khoản có LockoutEnabled=true. AccountController kiểm tra `Url.IsLocalUrl(returnUrl)` trước khi redirect. Nếu không có returnUrl hợp lệ, ưu tiên Admin → Employee, Staff → Staff, Customer → Customer; không có role phù hợp → Home. Việc redirect không thay cho Authorize ở trang đích. Form chưa triển khai luồng 2FA, nên không bật TwoFactorEnabled cho user nếu chưa bổ sung luồng tương ứng.

### 1.3 Sequence Diagram — Tạo nhân viên

**Actor:** Admin. **Route:** GET/POST `/Employee/Create`. **Đầu vào:** FullName, UserName, Email, PhoneNumber tùy chọn, EmployeeCode tùy chọn, HireDate tùy chọn, Password, ConfirmPassword. **Điều kiện:** phiên Admin hợp lệ và role Staff đã tồn tại. **Hậu điều kiện:** có một user hoạt động kèm role Staff; hoặc không có thay đổi dữ liệu khi thất bại.

```mermaid
sequenceDiagram
    autonumber
    actor A as Admin
    participant C as EmployeeController
    participant CTX as ApplicationDbContext
    participant UM as UserManager
    participant DB as SQL Server
    A->>C: POST /Employee/Create + form + token
    Note over A,C: Authorize Admin và anti-forgery phải hợp lệ
    alt Dữ liệu form không hợp lệ
        C-->>A: Trả form với lỗi validation
    else Dữ liệu hợp lệ
        C->>C: Trim dữ liệu, chuẩn hóa mã, tạo ApplicationUser
        C->>CTX: BeginTransactionAsync()
        CTX->>DB: BEGIN TRANSACTION
        C->>UM: CreateAsync(user, password)
        UM->>DB: Qua EF store cùng context: INSERT user có hash
        DB-->>UM: Thành công hoặc lỗi
        UM-->>C: IdentityResult hoặc exception
        alt Tạo user thất bại
            C->>CTX: RollbackAsync()
            CTX->>DB: ROLLBACK
            C-->>A: Báo lỗi hợp lệ / lỗi hệ thống đã được che chi tiết
        else Tạo user thành công
            C->>UM: AddToRoleAsync(user, Staff)
            UM->>DB: INSERT AspNetUserRoles
            DB-->>UM: Thành công hoặc lỗi
            UM-->>C: IdentityResult hoặc exception
            alt Gán role thất bại
                C->>CTX: RollbackAsync()
                CTX->>DB: ROLLBACK cả user và role link
                C-->>A: Báo không tạo được nhân viên
            else Gán role thành công
                C->>CTX: CommitAsync()
                CTX->>DB: COMMIT
                C-->>A: Thông báo thành công và redirect /Employee
            end
        end
    end
```

**Hình II.3 — Thiết kế đích có transaction cho tạo Staff.** Cần inject cùng scoped ApplicationDbContext mà UserManager đang sử dụng. `SaveChanges` riêng lẻ không tự làm hai lời gọi CreateAsync/AddToRoleAsync trở thành một transaction chung. [Tham khảo transaction EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

**Khác biệt hiện tại:** EmployeeController mới chỉ nhận UserManager, tạo user rồi gán Staff; nếu gán role lỗi thì gọi DeleteAsync để bù. Đây không phải transaction nguyên tử, dù comment trong code nói “cùng transaction”. Sơ đồ trên là yêu cầu cần bổ sung, không phải mô tả tính năng đã kiểm chứng.

Validation: FullName tối đa 100 ký tự và không toàn khoảng trắng; UserName 3–50 ký tự theo ViewModel; email đúng định dạng và không trùng sau chuẩn hóa; mật khẩu và xác nhận phải khớp. Số điện thoại/mã nhân viên rỗng chuyển thành NULL hoặc sinh mã theo quy tắc, không lưu chuỗi trắng. Password tối thiểu 8 ký tự, theo chính sách Identity của ứng dụng.

Mã tự sinh hiện dựa trên mã NV lớn nhất cộng một, có thể trùng khi hai Admin tạo đồng thời. Unique index là lớp bảo vệ cuối; thiết kế cần bắt lỗi trùng và yêu cầu thử lại hoặc bổ sung cơ chế sinh mã an toàn. Không đổi Role từ input; luôn gán Staff tại backend. Việc tạo Staff không cấp lại cookie để tránh thay phiên Admin bằng tài khoản vừa tạo.

### 1.4 Sequence Diagram — Đổi mật khẩu

**Actor:** tài khoản đã đăng nhập, gồm Admin, Staff, Customer. **Route:** GET/POST `/Profile/ChangePassword`. **Đầu vào:** CurrentPassword, NewPassword, ConfirmPassword. **Điều kiện:** phiên đăng nhập hợp lệ, tài khoản hoạt động và biết mật khẩu hiện tại. **Hậu điều kiện:** hash mật khẩu được cập nhật, phiên hiện tại được làm mới; thất bại thì giữ mật khẩu cũ.

```mermaid
sequenceDiagram
    autonumber
    actor U as Người dùng đã đăng nhập
    participant C as ProfileController
    participant UM as UserManager
    participant DB as Identity EF / SQL Server
    participant SM as SignInManager
    U->>C: POST ChangePassword + current/new/confirm + token
    Note over U,C: Authorize và anti-forgery, kiểm tra active theo thiết kế bảo mật
    alt ModelState không hợp lệ
        C-->>U: Trả lỗi required / độ dài / xác nhận không khớp
    else Form hợp lệ
        C->>UM: GetUserAsync(HttpContext.User)
        UM->>DB: Tìm tài khoản theo user ID trong principal
        DB-->>UM: User hoặc null
        UM-->>C: User hoặc null
        alt User không còn tồn tại
            C-->>U: Challenge đăng nhập lại
        else Có user
            C->>UM: ChangePasswordAsync(user, CurrentPassword, NewPassword)
            UM->>UM: Xác minh mật khẩu cũ và chính sách mật khẩu mới
            alt Mật khẩu cũ sai / mới không hợp lệ
                UM-->>C: IdentityResult thất bại
                C-->>U: Trả lỗi, mật khẩu cũ không đổi
            else Kiểm tra hợp lệ
                UM->>DB: Lưu hash mới và dấu bảo mật, kiểm tra concurrency
                DB-->>UM: Kết quả cập nhật
                UM-->>C: IdentityResult
                alt Cập nhật thất bại
                    C-->>U: Báo lỗi, không thông báo thành công
                else Cập nhật thành công
                    C->>SM: RefreshSignInAsync(user)
                    SM-->>U: Phát lại cookie cho phiên hiện tại
                    C-->>U: Redirect ChangePassword với thông báo thành công
                end
            end
        end
    end
```

**Hình II.4 — Người dùng tự đổi mật khẩu.** Lấy user từ principal, không nhận UserId để chọn tài khoản khác. Đây là luồng đổi mật khẩu có kiểm tra mật khẩu cũ, khác chức năng Admin đặt lại mật khẩu Staff. Các phiên khác bị từ chối khi security stamp được kiểm tra; không khẳng định chúng bị đăng xuất tức thì với cấu hình hiện tại.

Code ProfileController hiện đã dùng ChangePasswordAsync và RefreshSignInAsync. Kiểm tra active cho cookie và cách hiển thị lỗi database không dự kiến vẫn cần hoàn thiện theo phần III.

# III. Other Design Specifications

## 1. Cơ chế phân quyền và bảo mật

### 1.1 Xác thực và quản lý phiên

BMS dùng ASP.NET Core Identity với cookie authentication. Mật khẩu được xử lý bởi password hasher của Identity, không tự lưu plaintext, không tự viết hash MD5/SHA256 và không giả định ứng dụng dùng BCrypt như mẫu SDS. Cookie chứa ticket được framework bảo vệ; request được chuyển thành ClaimsPrincipal trước khi kiểm tra Authorize. Chính sách hiện có trong Program.cs đặt độ dài tối thiểu 8, khóa tạm sau 5 lần sai trong 5 phút; các yêu cầu chữ hoa/chữ thường/chữ số/ký tự đặc biệt theo cấu hình mặc định chưa bị ghi đè. [Cấu hình Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0).

Logout chỉ qua POST có token và gọi SignOutAsync. Các POST Login/Create/ChangePassword cũng yêu cầu anti-forgery. Mật khẩu, hash, cookie và token không được ghi vào log. Cookie triển khai thật cần HttpOnly và Secure qua HTTPS; thời hạn phiên và SameSite phải chốt trước triển khai, tránh tự ghi một thời hạn chưa được cấu hình vào SDS. Demo hiện không yêu cầu xác nhận email; không tuyên bố có email verification hoặc 2FA đã hoàn thiện.

### 1.2 Ma trận quyền cho phạm vi Hùng

| Chức năng | Chưa đăng nhập | Admin | Staff | Customer |
|---|---|---|---|---|
| Gửi đăng nhập | Có | Có endpoint; UI thường điều hướng khi đã đăng nhập | Tương tự Admin | Tương tự Admin |
| Danh sách/tìm nhân viên | Không | Có | Không | Không |
| Tạo tài khoản Staff | Không | Có | Không | Không |
| Sửa/khóa/mở khóa Staff | Không | Có, phải xác minh đối tượng là Staff | Không | Không |
| Tự đổi mật khẩu | Không | Chỉ của mình | Chỉ của mình | Chỉ của mình |
| Đăng xuất | Không | Phiên của mình | Phiên của mình | Phiên của mình |
| Xem trang Staff hiện có | Không | Có | Có | Không |
| Xem trang Customer hiện có | Không | Không theo attribute hiện tại | Không | Có |

`EmployeeController` dùng `[Authorize(Roles = "Admin")]`; `ProfileController` dùng `[Authorize]`. Admin không tự động được vào mọi endpoint Customer: quyền cụ thể phụ thuộc attribute/policy, không suy ra từ tên vai trò. Khi mở rộng quyền, nhóm cập nhật code và ma trận đồng thời.

Ẩn nút/menu chỉ giúp giao diện dễ dùng. Server vẫn phải từ chối truy cập trực tiếp bằng URL hoặc POST giả. Hàm lấy Staff phải kiểm tra role thật trước sửa/khóa; backend không dùng role trong hidden input để quyết định quyền. Với booking/lịch sử chơi, Customer chỉ được truy cập bản ghi của mình; TV2/TV3 triển khai kiểm tra ownership ở phía server.

### 1.3 Phân biệt hai loại khóa tài khoản

| Cơ chế | Dữ liệu | Ai quyết định | Điều kiện mở lại |
|---|---|---|---|
| Khóa quản trị | IsActive=false | Admin quản lý nhân viên | Admin đặt lại IsActive=true |
| Khóa tạm do đăng nhập sai | LockoutEnd, LockoutEnabled, AccessFailedCount | Identity theo chính sách đăng nhập | Hết thời gian khóa hoặc thao tác quản trị được thiết kế riêng |

Điều kiện đăng nhập phải đồng thời thỏa active và chính sách lockout. Mở khóa quản trị không tự xóa khóa tạm. Đổi SecurityStamp chỉ có hiệu lực khi cookie được kiểm tra, không tự đẩy thông báo logout ngay vào mọi trình duyệt.

**Thiết kế cần bổ sung:** kiểm tra user còn tồn tại và IsActive tại mỗi request được bảo vệ. Nếu có custom OnValidatePrincipal, vẫn gọi bộ kiểm tra SecurityStamp mặc định; nếu principal bị từ chối thì dừng xử lý. Với user bị khóa hoặc đã xóa, RejectPrincipal và SignOut. Tần suất kiểm tra stamp quyết định độ trễ thu hồi phiên khác sau đổi mật khẩu; phải mô tả đúng cấu hình thực tế. [Cơ chế xác thực SecurityStamp](https://source.dot.net/Microsoft.AspNetCore.Identity/SecurityStampValidator.cs.html).

Code hiện chưa đăng ký kiểm tra IsActive trong cookie event. ToggleLock cập nhật stamp và user nhưng chưa kiểm tra đầy đủ IdentityResult; cần chỉ báo thành công sau khi lưu thành công. Không coi comment “mất hiệu lực ngay” là bằng chứng đã đáp ứng yêu cầu.

### 1.4 Bảo vệ dữ liệu và transaction

Các form nhận ViewModel có tập trường cho phép, tránh người gọi gửi thêm IsActive/role/RewardPoints/TierId để tự tăng quyền hoặc điểm. Razor hiển thị dữ liệu người dùng dưới dạng đã encode; không dùng Html.Raw với tên hoặc nội dung do user nhập. Truy vấn database dùng EF hoặc SQL có tham số; không nối chuỗi SQL với input.

Tạo Staff phải nguyên tử: tạo user và liên kết role cùng commit hoặc cùng rollback. Bắt lỗi unique constraint cho username/email/phone/mã nhân viên và trả thông báo phù hợp; không đưa SQL stack trace ra UI. Khi sửa dữ liệu, kiểm tra concurrency bằng IdentityResult và giá trị phiên bản phù hợp với thời điểm tải form, tránh ghi đè thay đổi của người khác mà không báo.

Tài khoản có lịch sử nghiệp vụ được giữ lại bằng khóa IsActive. Chính sách xóa vĩnh viễn tài khoản chưa có lịch sử cần nhóm chốt riêng; source hiện có action Delete nên không mô tả rằng mọi thao tác xóa đều đã bị cấm. Connection string và mật khẩu khởi tạo lấy từ cấu hình local/user-secrets/biến môi trường. Initializer mật khẩu chỉ chạy Development, không reset mật khẩu đã có.

### 1.5 Ranh giới với điểm thưởng và VNPay

MembershipTiers không có quyền cấp role; RewardPoints và TierId phải do backend có thẩm quyền cập nhật. Hùng bàn giao tên bảng/cột và mapping; không tự chốt tỷ lệ điểm, ngưỡng hạng hoặc mức ưu đãi thay TV3/TV4. Theo phân công mới, TV3 phụ trách sự kiện điểm sau thanh toán và TV4 phụ trách công thức/nâng hạng; nhóm phải chọn một luồng cập nhật để một giao dịch không cộng điểm hai lần.

Endpoint VNPay IPN dự kiến không sử dụng phiên cookie của khách làm bằng chứng giao dịch; TV3 phải thiết kế xác thực thông báo riêng. Hùng cung cấp quyền truy cập trang thanh toán/quản trị, không tự nhận rằng chỉ có Authorize là đủ bảo vệ callback bên ngoài.

## 2. Tiêu chí kiểm tra phần Hùng

Các dòng dưới đây là tiêu chí nghiệm thu, chưa phải kết quả kiểm thử đã chạy trong lần soạn tài liệu này.

| ID | Tình huống | Kết quả mong đợi |
|---|---|---|
| H01 | Login đúng với tài khoản active | Có cookie; vào đúng trang theo role hoặc returnUrl local |
| H02 | Không tồn tại / inactive / sai mật khẩu | Không đăng nhập; không tiết lộ thông tin không cần thiết |
| H03 | Sai mật khẩu tới ngưỡng với lockout bật | Khóa tạm đúng chính sách, không cấp cookie trong thời gian khóa |
| H04 | returnUrl trỏ website ngoài | Không redirect ra URL đó |
| H05 | Staff/Customer gửi POST tạo nhân viên | Bị từ chối; database không thay đổi |
| H06 | Admin tạo Staff hợp lệ | Có user và đúng liên kết Staff; phiên Admin không bị thay |
| H07 | Lỗi gán role / trùng mã khi tạo | Rollback; không để user dở dang; lỗi dễ hiểu |
| H08 | Hai Admin đồng thời tạo cùng mã | Không tạo hai user cùng mã; thao tác thất bại được xử lý |
| H09 | Đổi mật khẩu cũ sai / xác nhận không khớp | Giữ nguyên mật khẩu cũ |
| H10 | Đổi mật khẩu thành công | Mật khẩu mới dùng được; cookie hiện tại làm mới; phiên khác bị thu hồi theo chính sách stamp |
| H11 | POST thiếu hoặc sai anti-forgery token | Framework từ chối trước khi ghi dữ liệu |
| H12 | Khóa Staff đang đăng nhập | Request được bảo vệ tiếp theo bị từ chối theo thiết kế active check |
| H13 | Đổi ID nhân viên thành ID Admin/Customer | Không được sửa/khóa đối tượng ngoài Staff |
| H14 | Gửi role Admin hoặc điểm/hạng trong form tạo Staff | Dữ liệu ngoài ViewModel không tạo thay đổi quyền/điểm trái phép |

# Phụ lục bàn giao cho nhóm

## A. Chênh lệch cần xử lý trước khi coi SDS khớp hoàn toàn với code

| Vấn đề | Bằng chứng hiện tại | Việc cần làm |
|---|---|---|
| Tạo Staff chưa nguyên tử | EmployeeController chỉ dùng UserManager và DeleteAsync bù khi gán role lỗi | Thêm transaction cùng context theo Hình II.3 |
| Thu hồi phiên sau khóa chưa tức thời | Program.cs chưa kiểm tra IsActive trong cookie validation | Bổ sung active check và giữ stamp validation |
| Thiếu mapping thành viên | SQL có RewardPoints/TierId/MembershipTiers; ApplicationUser chưa khai báo | Bổ sung khi triển khai thành viên; không khởi tạo lại database |
| Chưa có ngưỡng nâng hạng | MembershipTiers chỉ có Id/TierName/DiscountPercent | TV4 chốt thuật toán và cấu hình; Hùng phối hợp schema |
| Phân công bảng cần chốt bổ sung | Schema mới có 24 bảng; phân công ban đầu liệt kê 20 bảng | Còn AspNetRoleClaims (đề xuất TV2); 3 bảng liên kết combo đề xuất TV4 phối hợp TV1/TV3 |
| Dữ liệu live cũ có tên tiếng Việt lỗi mã hóa | Nguồn seed đã sửa ở commit 4448bb2; các dòng cũ không được reseed | Sửa dữ liệu live bằng tác vụ riêng khi cần; không chạy seed lại lên database có dữ liệu |
| Chưa có nghiệp vụ combo hoàn chỉnh | Có 3 bảng liên kết, nhưng đóng phiên vẫn tính giờ thường, hóa đơn chưa tách tiền combo | TV1/TV3/TV4 chốt giờ vượt, trừ tồn, giao món và tổng tiền trước khi bán combo |

Đã xử lý: phần verify dùng đúng BilliardDB; schema 24 bảng/5 procedure được thực thi và kiểm tra, kể cả trên database thử tạo mới; nguồn seed tiếng Việt đã sửa. Xem `docs/PROJECT_CONTEXT.md` và `BMS_Database_Starter/VALIDATION.txt` để phân biệt kết quả kiểm tra schema với các chức năng C# còn phải làm.

## B. Cách ghép vào báo cáo nhóm

Đưa mục I.1 và I.2 vào phần High Level Design chung. Mục I.3 hiện có đầy đủ sơ đồ và mô tả 24 bảng, dùng làm Database Design chung; không ghép thêm bản mô tả cũ trùng bảng. Bản riêng để sao chép nằm trong `docs/DATABASE_DESIGN.md` và `docs/DATABASE_DESIGN.html`. Đưa mục II.1 và bốn tiểu mục vào Section 1 của Detailed Code Design. Đưa phần III.1 vào Other Design Specifications. Giữ ma trận quyền và tiêu chí nghiệm thu nếu mẫu báo cáo cho phép. Phụ lục A phục vụ rà soát nội bộ; xử lý từng điểm rồi cập nhật SDS cho đúng trước bản nộp cuối.

Các khối Mermaid là file nguồn sơ đồ có thể sửa. Khi ghép Word/Google Docs, xuất chúng thành SVG/PNG bằng công cụ Mermaid, chèn ảnh kèm caption; không dán nguyên mã Mermaid vào báo cáo nộp. Giữ đồng nhất tên lớp trong diagram, bảng mô tả và code.

## C. Nguồn tham khảo

- Mẫu SDS của nhóm: https://docs.google.com/document/d/1ejRL6IrQYX4LTo6PDh2O4T5BMTfdGo2p/edit
- Schema: `BMS_Database_Starter/BilliardDB_Full.sql`.
- Source C#: `src/Bms.Web/Program.cs`, `Data/ApplicationUser.cs`, `Data/ApplicationDbContext.cs`, `Controllers/AccountController.cs`, `Controllers/EmployeeController.cs`, `Controllers/ProfileController.cs`, `Models/LoginViewModel.cs`, `Models/EmployeeViewModels.cs`, `Models/PasswordViewModels.cs`.
- Microsoft Learn — Configure ASP.NET Core Identity: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0
- Microsoft Learn — EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- .NET source — SecurityStampValidator: https://source.dot.net/Microsoft.AspNetCore.Identity/SecurityStampValidator.cs.html
