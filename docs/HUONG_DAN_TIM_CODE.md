# Bản đồ code để trình bày với thầy

Cập nhật cho buổi báo cáo 13/10/2026. Số dòng bên dưới được lấy từ file thật, không ước lượng. Trong Visual Studio dùng **Ctrl+G** để tới dòng; dùng **Ctrl+M, Ctrl+O** để thu gọn các khối. Trong VS Code dùng **Ctrl+G** và **Ctrl+Shift+O** để tìm hàm. Tìm `#region` để thấy điểm bắt đầu/kết thúc từng phần. Sau khi sửa code, chạy `pwsh -File scripts/Write-CodeGuide.ps1` để cập nhật số dòng.

## 1. Thư mục nào làm việc gì?

```text
src/Bms.Web/
├── Program.cs                  Cấu hình MVC, SQL Server, Identity, cookie, middleware
├── Controllers/                Xử lý request và quy tắc nghiệp vụ, chia theo chức năng
│   ├── AccountController.cs     Đăng nhập / đăng ký / đăng xuất
│   ├── ProfileController.cs     Đổi mật khẩu của người đang đăng nhập
│   ├── EmployeeController.cs    Quản lý nhân viên + reset mật khẩu
│   ├── CustomerController.cs    Quản lý khách và trang tài khoản riêng
│   ├── MemberTierController.cs  Hạng thành viên + ngưỡng điểm
│   ├── PricingController.cs     Bảng giá theo loại bàn / khung giờ
│   ├── DashboardController.cs   Tổng quan
│   └── RevenueController.cs     Báo cáo doanh thu + CSV
├── Data/
│   ├── ApplicationDbContext.cs  Mapping C# ↔ bảng/cột SQL
│   ├── ApplicationUser.cs       Tài khoản Identity + trường nghiệp vụ
│   ├── DbInitializer.cs         Đặt mật khẩu admin Development từ cấu hình bí mật
│   └── Entities/               Một entity / một file: MembershipTier, TableType,
│                               PricingConfig, PaymentTransaction, BilliardTable
├── Models/                     Dữ liệu cho form/trang, chia riêng theo chức năng
│   ├── LoginViewModel.cs / RegisterViewModel.cs
│   ├── PasswordViewModels.cs / EmployeeViewModels.cs
│   ├── CustomerViewModels.cs / MemberTierViewModels.cs / PricingViewModels.cs
│   └── DashboardViewModel.cs / ReportViewModels.cs
└── Views/                      Giao diện Razor, mỗi chức năng một thư mục
    ├── Account/ / Profile/ / Employee/ / Customer/
    ├── MemberTier/ / Pricing/ / Dashboard/ / Revenue/
    └── Shared/_AdminLayout.cshtml   Menu Admin và khung giao diện chung

BMS_Database_Starter/            SQL schema / nâng cấp / dữ liệu demo
tests/Bms.Smoke/                 Kiểm thử SQL thật, rollback fixture
tests/http-smoke.ps1             Kiểm thử HTTP, cookie, phân quyền và CSRF
```

Giữ cấu trúc MVC để tên controller và thư mục view khớp với routing. Các model từng gom trong `AdminViewModels.cs` đã tách thành file Khách hàng/Hạng/Bảng giá; Dashboard có file riêng. Entity đã đưa ra `Data/Entities`, còn DbContext chỉ giữ cấu hình mapping. Namespace được giữ nguyên nên luồng chạy không thay đổi.

## 2. Luồng xử lý dễ giải thích

Ví dụ Admin thêm hạng: trình duyệt GET `/MemberTier/Create` → Controller trả form `Views/MemberTier/CreateEdit.cshtml` → người dùng nhập và POST → `CreateEditTierViewModel` kiểm tra Required/Range → Controller kiểm tra trùng tên/ngưỡng → `ApplicationDbContext.MembershipTiers.Add` và `SaveChangesAsync` ghi SQL → redirect về Index → đọc lại SQL và hiển thị danh sách.

**Entity** mô tả dữ liệu trong bảng SQL. **ViewModel** mô tả dữ liệu form/trang và validation. **Controller** xử lý request, phân quyền và thao tác dữ liệu. **View** hiển thị HTML và gửi form. **DbContext** kết nối/mapping SQL. `TempData` mang thông báo qua redirect.

## 3. Code từ dòng nào đến dòng nào?

Các khoảng bên dưới bắt đầu tại `#region` và kết thúc tại `#endregion`. Constructor/khai báo dependency nằm đầu mỗi controller, phía trên khối đầu tiên.

### AccountController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC01 - Dang nhap | 25–74 | [Đến dòng 25](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:25) |
| UC19 - Dang ky khach hang | 76–136 | [Đến dòng 76](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:76) |
| UC02 - Dang xuat | 138–150 | [Đến dòng 138](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:138) |
| Thong bao tu choi truy cap | 152–159 | [Đến dòng 152](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:152) |
| Dieu huong theo role va dich loi Identity | 161–185 | [Đến dòng 161](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:161) |

### ProfileController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC03 - Hien thi form doi mat khau | 22–32 | [Đến dòng 22](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/ProfileController.cs:22) |
| UC03 - Kiem tra mat khau cu va luu mat khau moi | 34–66 | [Đến dòng 34](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/ProfileController.cs:34) |
| Dich loi Identity | 68–78 | [Đến dòng 68](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/ProfileController.cs:68) |

### EmployeeController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC43 - Danh sach, tim kiem, loc va phan trang | 23–84 | [Đến dòng 23](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:23) |
| UC43 - Tao nhan vien va gan role Staff | 86–153 | [Đến dòng 86](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:86) |
| UC43 - Sua thong tin nhan vien | 155–207 | [Đến dòng 155](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:155) |
| UC43 - Khoa va mo khoa nhan vien | 209–233 | [Đến dòng 209](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:209) |
| UC43 - Xoa nhan vien | 235–269 | [Đến dòng 235](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:235) |
| UC44 - Reset mat khau nhan vien | 271–310 | [Đến dòng 271](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:271) |
| Ham ho tro: kiem tra Staff, dich loi, sinh ma | 312–354 | [Đến dòng 312](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:312) |

### CustomerController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC35 - Danh sach, tim kiem, loc va phan trang | 24–91 | [Đến dòng 24](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:24) |
| UC35 - Khoa va mo khoa khach hang | 93–120 | [Đến dòng 93](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:93) |
| Trang tai khoan rieng cua Customer | 122–136 | [Đến dòng 122](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:122) |

### MemberTierController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| Danh sach hang va so nguoi su dung | 17–45 | [Đến dòng 17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:17) |
| Them hang va kiem tra nguong diem | 47–83 | [Đến dòng 47](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:47) |
| Sua hang va nguong diem | 85–134 | [Đến dòng 85](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:85) |
| Xoa hang va bao ve khoa ngoai | 136–163 | [Đến dòng 136](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:136) |

### PricingController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| Lay danh sach loai ban cho form | 17–24 | [Đến dòng 17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:17) |
| Danh sach bang gia theo loai ban | 26–54 | [Đến dòng 26](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:26) |
| Them muc gia | 56–94 | [Đến dòng 56](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:56) |
| Sua muc gia | 96–144 | [Đến dòng 96](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:96) |
| Xoa muc gia | 146–162 | [Đến dòng 146](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:146) |
| Kiem tra loai ban va khung gio chong nhau | 164–175 | [Đến dòng 164](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:164) |

### DashboardController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC04 - So lieu tong quan va doanh thu ngay/thang | 14–34 | [Đến dòng 14](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/DashboardController.cs:14) |
| Dem tai khoan theo role | 36–41 | [Đến dòng 36](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/DashboardController.cs:36) |

### RevenueController.cs

| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |
|---|---|---|
| UC46 - Hien thi bao cao | 17–26 | [Đến dòng 17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:17) |
| UC46 - Xuat CSV | 28–42 | [Đến dòng 28](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:28) |
| Kiem tra khoang ngay va kieu nhom | 44–59 | [Đến dòng 44](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:44) |
| Truy van, tong hop doanh thu va UTC+7 | 61–86 | [Đến dòng 61](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:61) |

## 4. Model, SQL mapping và giao diện tương ứng

| Chức năng | File model / entity / view | Dòng |
|---|---|---|
| Cấu hình chung | [Program.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Program.cs:1) | 1–96 |
| Cấu hình chung | [Data/ApplicationDbContext.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/ApplicationDbContext.cs:1) | 1–81 |
| Cấu hình chung | [Data/ApplicationUser.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/ApplicationUser.cs:1) | 1–15 |
| Cấu hình chung | [Data/DbInitializer.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/DbInitializer.cs:1) | 1–48 |
| Cấu hình chung | [Views/Shared/_AdminLayout.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Shared/_AdminLayout.cshtml:1) | 1–287 |
| Đăng nhập / đăng ký | [Models/LoginViewModel.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/LoginViewModel.cs:1) | 1–18 |
| Đăng nhập / đăng ký | [Models/RegisterViewModel.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/RegisterViewModel.cs:1) | 1–38 |
| Đăng nhập / đăng ký | [Views/Account/Login.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Login.cshtml:1) | 1–123 |
| Đăng nhập / đăng ký | [Views/Account/Register.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Register.cshtml:1) | 1–89 |
| Đổi / reset mật khẩu | [Models/PasswordViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/PasswordViewModels.cs:1) | 1–44 |
| Đổi / reset mật khẩu | [Views/Profile/ChangePassword.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Profile/ChangePassword.cshtml:1) | 1–53 |
| Đổi / reset mật khẩu | [Views/Employee/ResetPassword.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/ResetPassword.cshtml:1) | 1–50 |
| Nhân viên | [Models/EmployeeViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/EmployeeViewModels.cs:1) | 1–97 |
| Nhân viên | [Views/Employee/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Index.cshtml:1) | 1–164 |
| Nhân viên | [Views/Employee/Create.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Create.cshtml:1) | 1–76 |
| Nhân viên | [Views/Employee/Edit.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Edit.cshtml:1) | 1–72 |
| Khách hàng | [Models/CustomerViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/CustomerViewModels.cs:1) | 1–34 |
| Khách hàng | [Views/Customer/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Index.cshtml:1) | 1–132 |
| Khách hàng | [Views/Customer/Portal.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Portal.cshtml:1) | 1–13 |
| Hạng thành viên | [Models/MemberTierViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/MemberTierViewModels.cs:1) | 1–32 |
| Hạng thành viên | [Data/Entities/MembershipTier.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/MembershipTier.cs:1) | 1–9 |
| Hạng thành viên | [Views/MemberTier/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:1) | 1–74 |
| Hạng thành viên | [Views/MemberTier/CreateEdit.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/CreateEdit.cshtml:1) | 1–54 |
| Bảng giá | [Models/PricingViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/PricingViewModels.cs:1) | 1–43 |
| Bảng giá | [Data/Entities/PricingConfig.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/PricingConfig.cs:1) | 1–11 |
| Bảng giá | [Data/Entities/TableType.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/TableType.cs:1) | 1–8 |
| Bảng giá | [Views/Pricing/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/Index.cshtml:1) | 1–66 |
| Bảng giá | [Views/Pricing/CreateEdit.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/CreateEdit.cshtml:1) | 1–65 |
| Dashboard | [Models/DashboardViewModel.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/DashboardViewModel.cs:1) | 1–12 |
| Dashboard | [Data/Entities/BilliardTable.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/BilliardTable.cs:1) | 1–8 |
| Dashboard | [Views/Dashboard/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Dashboard/Index.cshtml:1) | 1–12 |
| Doanh thu | [Models/ReportViewModels.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/ReportViewModels.cs:1) | 1–15 |
| Doanh thu | [Data/Entities/PaymentTransaction.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/PaymentTransaction.cs:1) | 1–10 |
| Doanh thu | [Views/Revenue/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Revenue/Index.cshtml:1) | 1–22 |

## 5. Nếu thầy hỏi, mở đâu và trả lời thế nào?

| Câu hỏi | Chỗ mở | Ý chính để giải thích |
|---|---|---|
| Đăng nhập kiểm tra gì? | AccountController → UC01 | Tìm username bằng UserManager, chặn IsActive=false, PasswordSignInAsync kiểm tra mật khẩu/lockout, chỉ cho returnUrl nội bộ |
| Đăng ký có tự chọn role Admin được không? | AccountController → UC19 | Backend gán cứng Customer; CreateAsync + AddToRoleAsync cùng transaction, không lấy role từ form |
| Phân quyền ở đâu? | Đầu Controller và từng action; Program.cs | `[Authorize(Roles="Admin")]`; Customer Portal có quyền Customer riêng. Backend kiểm tra quyền, không chỉ ẩn menu |
| Khóa tài khoản thì phiên cũ còn dùng được không? | Customer/Employee → ToggleLock; Program.cs → cookie | Đổi IsActive và SecurityStamp; cookie được kiểm tra mỗi request, tài khoản bị khóa phải đăng nhập lại |
| Mật khẩu lưu ở đâu? | ApplicationUser/Identity; Profile → ChangePassword; Employee → ResetPassword | Identity tạo PasswordHash; dùng UserManager, không lưu mật khẩu thô. Reset bằng token thay vì xóa mật khẩu cũ trước |
| Anti-forgery để làm gì? | POST có ValidateAntiForgeryToken; View có form/token | Kiểm tra token để chặn POST giả mạo; không dùng GET để thay đổi dữ liệu |
| Tìm kiếm/phân trang làm thế nào? | Employee/Customer → Index | Xây IQueryable, Where theo bộ lọc, Count tổng, OrderBy, Skip/Take để lấy một trang |
| Ngưỡng điểm và giảm giá kiểm tra ở đâu? | MemberTierViewModels; MemberTier → Create/Edit | DataAnnotations kiểm tra miền giá trị; controller kiểm tra tên/ngưỡng trùng |
| Vì sao không xóa hạng đang dùng? | MemberTier → Delete; ApplicationDbContext | Kiểm tra User.TierId trước xóa; FK bảo vệ quan hệ. Catch DbUpdateException xử lý nếu phát sinh tham chiếu |
| Kiểm tra giờ trùng thế nào? | Pricing → ValidateConfigAsync | Cùng loại bàn: `existing.StartHour < new.EndHour && new.StartHour < existing.EndHour`; hai khoảng chỉ chạm đầu/cuối được phép |
| Doanh thu lấy từ đâu? | Revenue → PopulateAsync; PaymentTransaction entity | Tổng AmountPaid của PaymentTransactions theo thời điểm giao dịch; hóa đơn chưa trả không được cộng |
| Một hóa đơn trả hai lần thì đếm ra sao? | Revenue → PopulateAsync | PaymentCount đếm giao dịch; InvoiceCount dùng Distinct InvoiceId; tiền vẫn cộng đủ hai lần trả |
| Giờ Việt Nam xử lý ở đâu? | Revenue → PopulateAsync; Dashboard → Index | Đổi ngày lọc UTC+7 sang UTC; dùng khoảng `[đầu ngày, đầu ngày kế tiếp)` để gồm trọn ngày cuối |
| Xuất CSV ở đâu? | Revenue → Export | Tạo CSV từ cùng dữ liệu tổng hợp, UTF-8 có BOM để Excel đọc được, trả FileContentResult |
| EF có tự tạo lại database không? | Program.cs + ApplicationDbContext | SQL-first, kết nối UseSqlServer và mapping; không gọi EnsureCreated/Migrate |
| Đã tự động nâng hạng/áp giá mở phiên chưa? | MemberTier và Pricing; phần phạm vi trong báo cáo | Hiện có quản lý cấu hình. Tự động cộng điểm/nâng hạng và đọc bảng giá khi mở phiên còn cần tích hợp module liên quan |
| Dữ liệu doanh thu là thật không? | BMS_Database_Starter/07_AddPresentationDemoData.sql | Dữ liệu giả lập được yêu cầu để trình bày, có marker BMS-PRESENTATION-20261013; không phải thanh toán gateway thật |

## 6. Dữ liệu demo để đối chiếu khi trình bày

- Hạng: Đồng 0 điểm/0%; Bạc 500/5%; Vàng 1.500/10%; Kim Cương 3.000/15%.
- Tìm `demo.report` trong trang Khách hàng: 4 khách có điểm 120, 650, 1.800, 4.000; Kim Cương đang khóa để demo lọc trạng thái. Những tài khoản SQL này chưa đặt mật khẩu; dùng tài khoản tự đăng ký nếu cần demo đăng nhập Customer.
- Bảng giá: Pool và Carom, mỗi loại 3 khung giờ 08:00–12:00, 12:00–18:00, 18:00–23:59.
- 8 phiên đã đóng → 8 hóa đơn: 7 Paid, 1 Unpaid. Có 8 giao dịch vì một hóa đơn được trả hai phần bằng Cash/VNPay.
- Lọc **01/10/2026–13/10/2026**: **1.110.000đ, 7 giao dịch, 6 hóa đơn có thanh toán**.
- Lọc **12/10/2026**: **390.000đ, 3 giao dịch, 2 hóa đơn**.
- Lọc **13/10/2026**: **350.000đ, 2 giao dịch, 2 hóa đơn**; hóa đơn Unpaid 200.000đ không tính vào doanh thu.
- Lọc **28/09/2026–13/10/2026**: **1.310.000đ, 8 giao dịch, 7 hóa đơn có thanh toán**.
- Dữ liệu ngày 13/10 là số liệu giả lập cho buổi báo cáo. Dashboard chạy ngày 12/10 hiển thị hôm nay 390.000đ, tháng đến hết ngày 12/10 là 760.000đ. Nếu trình bày ngày 13/10 thì hôm nay 350.000đ, tháng đến hết ngày 13/10 là 1.110.000đ.
- Script 07 giữ dữ liệu cũ, transaction rollback nếu gặp xung đột và đã kiểm tra chạy lại không nhân đôi. Không chạy lại script Full/seed cũ.

Các số trên tính riêng dữ liệu mẫu vừa thêm. Nếu có giao dịch mới, báo cáo sẽ tăng theo dữ liệu thực tế trong database.
