# Rebuild the presentation guide from the actual #region markers and line numbers.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'docs/HUONG_DAN_TIM_CODE.md'
$guide = [Text.StringBuilder]::new()
$header = @'
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

'@
[void]$guide.AppendLine($header)
$controllers = @('Account','Profile','Employee','Customer','MemberTier','Pricing','Dashboard','Revenue')
foreach ($name in $controllers) {
    $relative = "src/Bms.Web/Controllers/${name}Controller.cs"
    $path = Join-Path $repoRoot $relative
    $lines = [IO.File]::ReadAllLines($path)
    $linkPath = $path.Replace('\','/')
    [void]$guide.AppendLine("### ${name}Controller.cs")
    [void]$guide.AppendLine('')
    [void]$guide.AppendLine('| Khối xử lý | Dòng bắt đầu–kết thúc | Mở code |')
    [void]$guide.AppendLine('|---|---|---|')
    $regionStart = 0
    $label = ''
    for ($i=0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s*#region\s+(.+)$') {
            if ($regionStart -ne 0) { throw "Nested region unsupported: $relative" }
            $regionStart = $i+1; $label = $Matches[1]
        } elseif ($lines[$i] -match '^\s*#endregion') {
            if ($regionStart -eq 0) { throw "Unmatched region: $relative" }
            [void]$guide.AppendLine("| $label | $regionStart–$($i+1) | [Đến dòng $regionStart](${linkPath}:$regionStart) |")
            $regionStart = 0
        }
    }
    if ($regionStart -ne 0) { throw "Unclosed region: $relative" }
    [void]$guide.AppendLine('')
}
[void]$guide.AppendLine('## 4. Model, SQL mapping và giao diện tương ứng')
[void]$guide.AppendLine('')
[void]$guide.AppendLine('| Chức năng | File model / entity / view | Dòng |')
[void]$guide.AppendLine('|---|---|---|')
$groups = [ordered]@{
    'Cấu hình chung' = @('Program.cs','Data/ApplicationDbContext.cs','Data/ApplicationUser.cs','Data/DbInitializer.cs','Views/Shared/_AdminLayout.cshtml')
    'Đăng nhập / đăng ký' = @('Models/LoginViewModel.cs','Models/RegisterViewModel.cs','Views/Account/Login.cshtml','Views/Account/Register.cshtml')
    'Đổi / reset mật khẩu' = @('Models/PasswordViewModels.cs','Views/Profile/ChangePassword.cshtml','Views/Employee/ResetPassword.cshtml')
    'Nhân viên' = @('Models/EmployeeViewModels.cs','Views/Employee/Index.cshtml','Views/Employee/Create.cshtml','Views/Employee/Edit.cshtml')
    'Khách hàng' = @('Models/CustomerViewModels.cs','Views/Customer/Index.cshtml','Views/Customer/Portal.cshtml')
    'Hạng thành viên' = @('Models/MemberTierViewModels.cs','Data/Entities/MembershipTier.cs','Views/MemberTier/Index.cshtml','Views/MemberTier/CreateEdit.cshtml')
    'Bảng giá' = @('Models/PricingViewModels.cs','Data/Entities/PricingConfig.cs','Data/Entities/TableType.cs','Views/Pricing/Index.cshtml','Views/Pricing/CreateEdit.cshtml')
    'Dashboard' = @('Models/DashboardViewModel.cs','Data/Entities/BilliardTable.cs','Views/Dashboard/Index.cshtml')
    'Doanh thu' = @('Models/ReportViewModels.cs','Data/Entities/PaymentTransaction.cs','Views/Revenue/Index.cshtml')
}
foreach ($group in $groups.GetEnumerator()) {
    foreach ($file in $group.Value) {
        $path = Join-Path $repoRoot ('src/Bms.Web/'+$file)
        $count = [IO.File]::ReadAllLines($path).Length
        $linkPath = $path.Replace('\','/')
        [void]$guide.AppendLine("| $($group.Key) | [$file](${linkPath}:1) | 1–$count |")
    }
}
$footer = @'

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
'@
[void]$guide.AppendLine($footer)
[IO.File]::WriteAllText($output,$guide.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output "Written: $output"
