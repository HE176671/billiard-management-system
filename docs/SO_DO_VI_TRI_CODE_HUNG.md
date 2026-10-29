# Thầy hỏi chức năng nào — mở code ở đâu?

Đối chiếu source ngày **16/10/2026**. Trong Visual Studio: **Ctrl+T** nhập tên file, Enter mở; **Ctrl+G** nhập số dòng. Các đoạn dưới đây là vị trí hiện tại; có thể thay đổi nếu source được sửa tiếp.

## Sơ đồ phần code của Hùng

```text
PHẦN HÙNG
├── Tài khoản                 → AccountController
│   ├── Đăng nhập             → Login POST, dòng 42–72
│   ├── Đăng ký               → Register POST, dòng 92–134
│   └── Đăng xuất             → Logout, dòng 144–148
├── Đổi mật khẩu              → ProfileController, dòng 39–64
├── Nhân viên                 → EmployeeController
│   ├── Danh sách             → Index, dòng 26–82
│   ├── Thêm                  → Create POST, dòng 99–151
│   ├── Sửa                   → Edit POST, dòng 179–205
│   ├── Khóa/mở khóa          → ToggleLock, dòng 214–231
│   ├── Xóa                   → Delete, dòng 240–267
│   └── Reset mật khẩu        → ResetPassword POST, dòng 289–308
├── Khách hàng                → CustomerController
│   ├── Danh sách/tìm/lọc      → Index, dòng 28–89
│   ├── Khóa/mở khóa          → ToggleLock, dòng 98–118
│   └── Tài khoản riêng       → Portal, dòng 125–135
├── Hạng thành viên           → MemberTierController
│   ├── Danh sách             → Index, dòng 20–43
│   ├── Thêm                  → Create POST, dòng 54–81
│   ├── Sửa                   → Edit POST, dòng 104–132
│   └── Xóa                   → Delete, dòng 140–162
├── Bảng giá                  → PricingController
│   ├── Danh sách             → Index, dòng 29–52
│   ├── Thêm                  → Create POST, dòng 67–92
│   ├── Sửa                   → Edit POST, dòng 118–142
│   ├── Xóa                   → Delete, dòng 150–160
│   └── Kiểm tra giờ trùng    → ValidateConfigAsync, dòng 166–174
├── Dashboard                 → DashboardController.Index, dòng 16–33
└── Doanh thu                 → RevenueController
    ├── Hiển thị              → Index, dòng 19–24
    ├── Kiểm tra khoảng ngày  → CreateRange, dòng 46–58
    ├── Lấy/tính số liệu      → PopulateAsync, dòng 63–85
    └── Xuất CSV              → Export, dòng 30–40
```

## Mở đúng file

| Nhóm | Liên kết mở file |
|---|---|
| Đăng nhập/đăng ký/đăng xuất | [AccountController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:42) |
| Đổi mật khẩu | [ProfileController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/ProfileController.cs:39) |
| Nhân viên | [EmployeeController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:26) |
| Khách hàng | [CustomerController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:28) |
| Hạng thành viên | [MemberTierController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:54) |
| Bảng giá | [PricingController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:67) |
| Dashboard | [DashboardController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/DashboardController.cs:16) |
| Doanh thu | [RevenueController.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:63) |

## Hỏi sâu vào một dòng xử lý cụ thể

| Thầy hỏi | Mở file | Đoạn cần chỉ và giải thích |
|---|---|---|
| Đoạn nào lấy danh sách nhân viên? | EmployeeController | 29–33 lấy role Staff; 54–70 truy vấn một trang và tạo rows |
| Tìm nhân viên theo tên/mã ở đâu? | EmployeeController | 36–44: Where theo FullName/EmployeeCode/PhoneNumber/UserName |
| Lọc nhân viên hoạt động/bị khóa ở đâu? | EmployeeController | 47–48: Where theo IsActive |
| Phân trang nhân viên ở đâu? | EmployeeController | 50–52 đếm trang, 56–57 Skip/Take; PageSize=10 tại dòng 15 |
| Danh sách nhân viên trả sang View ở đâu? | EmployeeController | 72–81: Employees=rows, return View(vm) |
| Ghi nhân viên mới xuống SQL ở đâu? | EmployeeController | 123 CreateAsync, 132 AddToRoleAsync; transaction 120–140 |
| Cập nhật nhân viên xuống SQL ở đâu? | EmployeeController | 193: UserManager.UpdateAsync |
| Kiểm tra đúng Staff trước sửa/xóa ở đâu? | EmployeeController | 317–322: GetStaffOrNull, FindByIdAsync và IsInRoleAsync |
| Reset mật khẩu thực hiện ở đâu? | EmployeeController | 297–298: tạo token rồi ResetPasswordAsync |
| Xác thực mật khẩu đăng nhập ở đâu? | AccountController | 47 tìm user, 54–55 PasswordSignInAsync |
| Gán quyền Customer khi đăng ký ở đâu? | AccountController | 123: AddToRoleAsync(user, "Customer") |
| Đổi mật khẩu đang đăng nhập ở đâu? | ProfileController | 44 lấy user từ phiên, 47 ChangePasswordAsync, 61 refresh cookie |
| Lấy danh sách khách hàng ở đâu? | CustomerController | 30–38 giới hạn role Customer; 58–74 lấy dữ liệu hiển thị |
| Lọc khách theo hạng ở đâu? | CustomerController | 51: Where theo TierId |
| Lấy tên khách để hiển thị ở đâu? | CustomerController | 65: FullName=u.FullName; dữ liệu từ AspNetUsers.FullName |
| Lấy danh sách hạng ở đâu? | MemberTierController | 22: MembershipTiers.OrderBy(...).ToListAsync() |
| Kiểm tra hạng trùng tên/ngưỡng ở đâu? | MemberTierController | 59 kiểm tra MinPoints; 65 kiểm tra TierName |
| Lưu hạng mới xuống SQL ở đâu? | MemberTierController | 71–77: Add MembershipTier, SaveChangesAsync |
| Chặn xóa hạng đang dùng ở đâu? | MemberTierController | 145–149: Users.AnyAsync(u=>u.TierId==id), báo lỗi và redirect |
| Lấy tên loại bàn cho form giá ở đâu? | PricingController | 19–22: TableTypes → TableTypeOptionViewModel |
| Lấy bảng giá kèm tên loại bàn ở đâu? | PricingController | 31–35: PricingConfigs.Include(p=>p.TableType) |
| Kiểm tra giờ bắt đầu/kết thúc ở đâu? | PricingController | 73–78 khi thêm; 123–128 khi sửa |
| Kiểm tra giá chồng giờ ở đâu? | PricingController | 170–172: cùng loại bàn, p.StartHour<end && start<p.EndHour |
| Lưu bảng giá ở đâu? | PricingController | 81–88 thêm; 134–138 sửa; 155–156 xóa |
| Dashboard lấy số bàn ở đâu? | DashboardController | 24–26: CountAsync trên BilliardTables |
| Dashboard lấy số nhân viên/khách ở đâu? | DashboardController | 23 gọi CountRoleAsync; 38–40 join Roles/UserRoles |
| Báo cáo lấy doanh thu ở đâu? | RevenueController | 68–70: đọc PaymentTransactions, Sum AmountPaid |
| Một hóa đơn trả hai lần có đếm trùng không? | RevenueController | 71 đếm giao dịch, 72 Distinct InvoiceId để đếm hóa đơn |
| Nhóm doanh thu theo ngày/tháng ở đâu? | RevenueController | 73–81: chọn format, GroupBy và thêm kỳ không có tiền |
| Tổng hợp theo phương thức thanh toán ở đâu? | RevenueController | 83–84: GroupBy PaymentMethod |
| Xuất file CSV ở đâu? | RevenueController | 35–39: tạo chuỗi CSV, UTF-8 BOM, return File |

## View hiển thị nằm ở đâu?

| Màn hình | File và dòng quan trọng |
|---|---|
| Đăng nhập | [Login.cshtml:96](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Login.cshtml:96): form POST |
| Đăng ký | [Register.cshtml:43](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Register.cshtml:43): form POST |
| Danh sách nhân viên | [Employee/Index.cshtml:94](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Index.cshtml:94): foreach Model.Employees |
| Thêm nhân viên | [Employee/Create.cshtml:13](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Create.cshtml:13): form POST Create |
| Sửa nhân viên | [Employee/Edit.cshtml:28](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Edit.cshtml:28): form POST Edit |
| Reset mật khẩu | [Employee/ResetPassword.cshtml:16](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/ResetPassword.cshtml:16): form POST |
| Đổi mật khẩu | [Profile/ChangePassword.cshtml:18](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Profile/ChangePassword.cshtml:18): form POST |
| Danh sách khách | [Customer/Index.cshtml:65](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Index.cshtml:65): foreach Customers; dòng 99 form khóa |
| Hạng thành viên | [MemberTier/Index.cshtml:41](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:41): foreach Tiers |
| Thêm/sửa hạng | [MemberTier/CreateEdit.cshtml:17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/CreateEdit.cshtml:17): form chung Create/Edit |
| Bảng giá | [Pricing/Index.cshtml:34](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/Index.cshtml:34): foreach Configs |
| Thêm/sửa giá | [Pricing/CreateEdit.cshtml:17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/CreateEdit.cshtml:17): form chung Create/Edit |
| Doanh thu | [Revenue/Index.cshtml:4](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Revenue/Index.cshtml:4): form GET; 18/21 bảng ngày/phương thức |
| Dashboard | [Dashboard/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Dashboard/Index.cshtml:1): giao diện các chỉ số |

## Nếu hỏi SQL, Model hoặc quyền

- Kết nối SQL: [Program.cs:11](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Program.cs:11), UseSqlServer tại dòng 13. Route dòng 77–79; kiểm tra cookie 37–55; authentication/authorization 74–75.
- Mapping bảng: [ApplicationDbContext.cs:13](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/ApplicationDbContext.cs:13), DbSet và OnModelCreating. Identity dùng chung AspNetUsers/Roles/UserRoles.
- Validation nhân viên: [EmployeeViewModels.cs:30](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/EmployeeViewModels.cs:30), Create; dòng 73 Edit.
- Validation hạng: [MemberTierViewModels.cs:19](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/MemberTierViewModels.cs:19).
- Validation giá: [PricingViewModels.cs:20](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/PricingViewModels.cs:20).
- Validation mật khẩu: [PasswordViewModels.cs:6](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/PasswordViewModels.cs:6), reset; dòng 26 đổi mật khẩu.

## Ví dụ trả lời ngay khi thầy chỉ vào màn hình

“Lấy danh sách nhân viên ở đâu?” → mở EmployeeController.Index tại dòng 26 → chỉ 29–33 lọc Staff → 54–70 lấy trang dữ liệu → 72–81 đưa sang View → mở View dòng 94 để chỉ foreach.

“Thêm hạng ở đâu?” → mở MemberTierController.Create POST tại dòng 54 → 57–68 kiểm tra dữ liệu/trùng → 71–77 lưu MembershipTiers → 80 redirect → Index dòng 20 đọc lại danh sách.

“Tính doanh thu ở đâu?” → mở RevenueController.PopulateAsync tại dòng 63 → 68–69 lấy giao dịch từ SQL → 70 cộng tiền → 71–72 đếm giao dịch/hóa đơn → Index dòng 23 trả View.

Muốn xem luồng GET/POST đầy đủ của từng chức năng: [Luồng code dễ hiểu](LUONG_CODE_DE_HIEU_HUNG.md). CRUD hạng/giá đã có; tự tích điểm/nâng hạng và áp PricingConfigs khi mở phiên chưa có luồng tích hợp hoàn chỉnh.
