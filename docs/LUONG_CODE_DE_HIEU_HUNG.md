# Luồng code của Hùng — đọc theo từng bước

Đối chiếu source ngày 13/10/2026. Mỗi luồng ghi thao tác trên màn hình, action được gọi, dữ liệu SQL, kết quả trả về và vị trí code. Số dòng có thể đổi sau khi sửa source; dùng Ctrl+G để tới dòng và #region để tìm khối.

## Luồng chung

```text
Người dùng mở URL hoặc gửi form
    ↓
Routing chọn Controller/Action; xác thực, phân quyền và filter kiểm tra request
    ↓
MVC đưa dữ liệu URL/form vào tham số hoặc ViewModel của action
    ↓
Controller kiểm tra dữ liệu và xử lý nghiệp vụ
    ↓
DbContext / UserManager đọc hoặc ghi SQL Server
    ↓
return View(model): Razor dựng HTML và trả ngay trong request hiện tại
HOẶC RedirectToAction: trả redirect, trình duyệt gửi GET mới tới action đích
    ↓
Trình duyệt hiển thị HTML
```

Mở [Program.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Program.cs:11): kết nối SQL dòng 11–13; Identity dùng EF dòng 16–29; kiểm tra cookie dòng 37–55; middleware dòng 72–75; route dòng 77–79. Route `{controller}/{action}/{id?}`: `/MemberTier/Edit/5` gọi MemberTierController.Edit với id=5. Tên tham số form và thuộc tính ViewModel khớp nhau để MVC bind dữ liệu.

Mở [ApplicationDbContext.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/ApplicationDbContext.cs:13): DbSet dòng 13–17; mapping bảng nghiệp vụ dòng 44–80. Identity base class map các bảng AspNet*. Connection string local chọn `localhost / BilliardDB`; source không gọi EnsureCreated/Migrate để tạo lại database.

## 1. Đăng nhập

Form: [Views/Account/Login.cshtml dòng 96](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Login.cshtml:96). Xử lý: [AccountController.cs dòng 42](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:42).

```text
GET /Account/Login → Login() [dòng 30] → View form [dòng 35]
    ↓ người dùng nhập và bấm Đăng nhập
POST /Account/Login → Login(LoginViewModel model) [dòng 42]
    ↓
ModelState: dữ liệu form hợp lệ? [45]
    ↓
FindByNameAsync: tìm tài khoản từ AspNetUsers; kiểm tra IsActive [47–52]
    ↓
PasswordSignInAsync: Identity kiểm tra mật khẩu/lockout, đăng nhập nếu thành công [54–55]
    ↓
Sai: ModelState.AddModelError + return View(model) → HTML form có lỗi [64–71]
Đúng: returnUrl nội bộ hoặc RedirectByRoleAsync [57–61, 164–172]
    ↓
Admin → GET /Dashboard; Staff → GET /Staff; Customer → GET /Customer/Portal
    ↓
Controller trang đích đọc dữ liệu, trả ViewModel → HTML trang đích
```

`model` là dữ liệu nhập; `user` là tài khoản SQL; `result` là trạng thái đăng nhập. Cookie xác định người đang đăng nhập. Hàm POST Login không đọc số liệu Dashboard.

## 2. Đăng ký Customer

Form: [Views/Account/Register.cshtml dòng 43](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Account/Register.cshtml:43). Xử lý: [AccountController.cs dòng 92](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:92).

```text
GET Register() [81–85] → form đăng ký
    ↓ nhập thông tin và gửi POST /Account/Register
Register(RegisterViewModel model) [92]
    ↓
Kiểm tra ModelState và số điện thoại trùng [94–100]
    ↓
Mở transaction; tạo ApplicationUser; lấy hạng MinPoints=0 nếu có [102–113]
    ↓
UserManager.CreateAsync → lưu AspNetUsers, Identity tạo PasswordHash [115]
    ↓
AddToRoleAsync("Customer") → quan hệ AspNetUserRoles [123]
    ↓
Thất bại: rollback transaction / trả form với lỗi
Thành công: commit → SignInAsync → redirect Portal [131–133]
    ↓
GET /Customer/Portal → đọc đúng user từ phiên đăng nhập → HTML tài khoản riêng
```

Chỉ backend gán role Customer. TierId có thể NULL nếu chưa cấu hình hạng ngưỡng 0. Các quy tắc form nằm ở [RegisterViewModel.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/RegisterViewModel.cs:5).

## 3. Đăng xuất

Nút: [Views/Shared/_AdminLayout.cshtml dòng 268](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Shared/_AdminLayout.cshtml:268). Xử lý: [AccountController.cs dòng 144](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/AccountController.cs:144).

```text
Bấm Đăng xuất → POST /Account/Logout
    ↓
Kiểm tra phiên đăng nhập và anti-forgery token
    ↓
Logout() → SignOutAsync() [146] → đăng xuất cookie
    ↓
RedirectToAction("Login", "Account") [147]
    ↓
Trình duyệt GET /Account/Login → HTML form đăng nhập
```

## 4. Đổi mật khẩu của người đang đăng nhập

Form: [Views/Profile/ChangePassword.cshtml dòng 18](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Profile/ChangePassword.cshtml:18). Xử lý: [ProfileController.cs dòng 39](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/ProfileController.cs:39).

```text
GET ChangePassword() [26–29] → form mật khẩu cũ/mới/xác nhận
    ↓ gửi POST /Profile/ChangePassword
ChangePassword(ChangePasswordViewModel model) [39]
    ↓
ModelState [42] → GetUserAsync(User): lấy user từ cookie [44]
    ↓
ChangePasswordAsync: kiểm tra mật khẩu cũ và lưu hash mới vào AspNetUsers [47]
    ↓
Sai: thêm lỗi → trả lại form [48–57]
Đúng: RefreshSignInAsync → TempData → redirect [61–63]
    ↓
GET lại form → hiển thị thông báo đổi thành công, giữ phiên hiện tại
```

## 5. Danh sách nhân viên, tìm/lọc/phân trang

Form tìm: [Views/Employee/Index.cshtml dòng 50](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Index.cshtml:50). Xử lý: [EmployeeController.cs dòng 26](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:26).

```text
GET /Employee?search=...&status=...&page=...
    ↓
Index(search, status, page) [26]
    ↓
GetUsersInRoleAsync("Staff") → Users/Role/UserRole [29–33]
    ↓
Where tìm tên/mã/điện thoại/username và lọc IsActive [35–48]
    ↓
Count tổng → OrderBy → Skip/Take 10 bản ghi [50–70]
    ↓
EmployeeListViewModel → return View(vm) [72–81]
    ↓
View foreach Model.Employees [dòng 94] → bảng HTML
```

## 6. Tạo nhân viên

Form: [Views/Employee/Create.cshtml dòng 13](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Create.cshtml:13). Xử lý: [EmployeeController.cs dòng 99](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:99).

```text
Bấm Thêm → GET Create() [89–93] → sinh mã và mở form
    ↓ nhập và POST /Employee/Create
Create(CreateEmployeeViewModel model) [99]
    ↓
ModelState → chuẩn hóa thông tin, tạo ApplicationUser [101–117]
    ↓
Transaction → CreateAsync ghi user → AddToRoleAsync("Staff") [120–132]
    ↓
Lỗi: rollback, trả lại form với lỗi
Đúng: CommitAsync [140] → TempData → redirect Index [149–150]
    ↓
GET danh sách Staff → đọc lại SQL → thấy nhân viên mới
```

Nhân viên lưu trong AspNetUsers, không có bảng Employee độc lập. Model form: [EmployeeViewModels.cs dòng 30](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/EmployeeViewModels.cs:30).

## 7. Sửa nhân viên

Form: [Views/Employee/Edit.cshtml dòng 28](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Edit.cshtml:28). Xử lý: [EmployeeController.cs dòng 158](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:158).

```text
Bấm Sửa → GET /Employee/Edit/{id}
    ↓
Edit(id) [158] → GetStaffOrNull [160, helper 317] → đọc AspNetUsers
    ↓
EditEmployeeViewModel → form điền sẵn [163–173]
    ↓ người dùng sửa và POST /Employee/Edit
Edit(model) [179] → ModelState → kiểm tra lại đúng Staff [181–184]
    ↓
Gán FullName/PhoneNumber/EmployeeCode/HireDate → UpdateAsync ghi SQL [186–193]
    ↓
Lỗi: return View(model); đúng: redirect Index [194–204]
```

## 8. Khóa/mở khóa nhân viên

Nút: [Views/Employee/Index.cshtml dòng 116](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Index.cshtml:116). Xử lý: [EmployeeController.cs dòng 214](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:214).

```text
Bấm Khóa/Mở khóa → POST /Employee/ToggleLock/{id}
    ↓
GetStaffOrNull: tìm user và kiểm tra role Staff [216, helper 317]
    ↓
Đảo IsActive [219] → UpdateSecurityStampAsync lưu user [221]
    ↓
TempData thông báo → redirect Index [228–230]
    ↓
Request tiếp theo của user bị khóa: kiểm tra cookie trong Program.cs [37–55]
    ↓
Stamp không hợp lệ hoặc IsActive=false → mất quyền truy cập, phải đăng nhập lại
```

## 9. Xóa nhân viên

Nút: [Views/Employee/Index.cshtml dòng 130](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/Index.cshtml:130). Xử lý: [EmployeeController.cs dòng 240](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:240).

```text
POST /Employee/Delete/{id} → kiểm tra Admin/token
    ↓
GetStaffOrNull [242] → DeleteAsync [247]
    ↓
Có FK/lịch sử chặn xóa: DbUpdateException → thông báo nên khóa [257–260]
Xóa thành công: thông báo thành công [248–250]
    ↓
Redirect Index [266] → đọc lại danh sách SQL
```

## 10. Reset mật khẩu nhân viên

Form: [Views/Employee/ResetPassword.cshtml dòng 16](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Employee/ResetPassword.cshtml:16). Xử lý: [EmployeeController.cs dòng 274](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/EmployeeController.cs:274).

```text
Bấm Reset → GET ResetPassword(id) [274–284] → kiểm tra Staff, mở form
    ↓ POST /Employee/ResetPassword
ResetPassword(ResetStaffPasswordViewModel model) [289]
    ↓
ModelState → GetStaffOrNull(model.UserId) [291–294]
    ↓
GeneratePasswordResetTokenAsync [297]
    ↓
ResetPasswordAsync(user, token, model.NewPassword) [298]
    ↓
Sai: form có lỗi và mật khẩu cũ được giữ; đúng: lưu hash mới vào AspNetUsers
    ↓
TempData → redirect danh sách [306–307]
```

## 11. Dashboard

Xử lý: [DashboardController.cs dòng 16](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/DashboardController.cs:16). View: [Views/Dashboard/Index.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Dashboard/Index.cshtml:1).

```text
GET /Dashboard → kiểm tra quyền Admin → Index() [16]
    ↓
Tính mốc hôm nay/tháng theo UTC+7 [18–21]
    ↓
Đếm Staff/Customer từ Role và UserRole [23, helper 38–40]
Đếm tổng/trống/đang chơi từ BilliardTables [24–26]
Cộng AmountPaid theo mốc ngày/tháng từ PaymentTransactions [27–30]
    ↓
DashboardViewModel → return View(model) [32]
    ↓
Razor dựng HTML các chỉ số
```

## 12. Danh sách, tìm/lọc khách hàng

Form: [Views/Customer/Index.cshtml dòng 29](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Index.cshtml:29). Xử lý: [CustomerController.cs dòng 28](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:28).

```text
GET /Customer?search=...&status=...&tierId=...&page=...
    ↓
Index(search,status,tierId,page) [28], chỉ Admin được vào
    ↓
Lấy role Customer [30–31]; đọc MembershipTiers cho bộ lọc [33–36]
    ↓
Query AspNetUsers → Where tên/email/điện thoại, IsActive, TierId [38–51]
    ↓
Count tổng → OrderBy → Skip/Take 10 bản ghi [53–74]
    ↓
CustomerListViewModel → return View(vm) [77–88]
    ↓
View foreach Customers [65] → @c.FullName [69], email, điểm/hạng/trạng thái
```

Tên khách sai: kiểm tra AspNetUsers.FullName. Tiêu đề/nút sai: kiểm tra View.

## 13. Khóa/mở khóa khách hàng

Nút: [Views/Customer/Index.cshtml dòng 99](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Index.cshtml:99). Xử lý: [CustomerController.cs dòng 98](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:98).

```text
POST /Customer/ToggleLock/{id} → kiểm tra quyền Admin/token
    ↓
FindByIdAsync + IsInRoleAsync("Customer") [100–102]
    ↓
Đảo IsActive [104] → UpdateSecurityStampAsync lưu user [107]
    ↓
TempData → redirect Index [114–117] → danh sách đọc lại SQL
    ↓
Cookie khách bị khóa bị từ chối ở request tiếp theo [Program.cs 37–55]
```

## 14. Trang tài khoản riêng Customer

Xử lý: [CustomerController.cs dòng 125](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/CustomerController.cs:125). View: [Views/Customer/Portal.cshtml](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Customer/Portal.cshtml:1).

```text
GET /Customer/Portal → quyền Customer
    ↓
GetUserId(User): lấy ID từ phiên đăng nhập [127]
    ↓
Users.Where(u => u.Id == id): chỉ đọc chính tài khoản đó [128]
    ↓
CustomerRowViewModel: tên/liên hệ/điểm/hạng → View Portal [129–134]
```

## 15. Danh sách hạng thành viên

Xử lý: [MemberTierController.cs dòng 20](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:20). View: [Views/MemberTier/Index.cshtml dòng 41](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:41).

```text
GET /MemberTier → Index() [20]
    ↓
Đọc MembershipTiers, sắp theo MinPoints [22]
    ↓
Đếm các tài khoản có TierId theo từng hạng [25–30]
    ↓
MemberTierListViewModel → return View(vm) [32–42]
    ↓
View foreach Tiers → HTML danh sách
```

## 16. Thêm hạng thành viên

Nút: [Views/MemberTier/Index.cshtml dòng 25](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:25). Form: [CreateEdit.cshtml dòng 17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/CreateEdit.cshtml:17). Xử lý: [MemberTierController.cs dòng 50](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:50).

```text
Bấm Thêm hạng → GET /MemberTier/Create → Create() [50] mở form
    ↓ nhập tên, điểm tối thiểu, giảm giá rồi bấm Lưu
POST /MemberTier/Create → Create(CreateEditTierViewModel model) [54]
    ↓
ModelState kiểm tra Required/Range [57; Model dòng 19–31]
    ↓
AnyAsync: kiểm tra MinPoints trùng [59], tên trùng [65]
    ↓
Sai: return View("CreateEdit", model) → HTML form có lỗi [62/68]
Đúng: Add MembershipTier → SaveChangesAsync → INSERT MembershipTiers [71–77]
    ↓
TempData → RedirectToAction(Index) [79–80]
    ↓
Trình duyệt GET Index → đọc lại SQL → thấy hạng mới
```

Model: [MemberTierViewModels.cs dòng 19](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/MemberTierViewModels.cs:19). Entity: [MembershipTier.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/MembershipTier.cs:3).

## 17. Sửa hạng

Nút: [Views/MemberTier/Index.cshtml dòng 50](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:50). Xử lý: [MemberTierController.cs dòng 88](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:88).

```text
GET /MemberTier/Edit/{id} → FindAsync(id) đọc hạng [88–90]
    ↓
Gán CreateEditTierViewModel → form điền sẵn [93–99]
    ↓ POST /MemberTier/Edit
Edit(model) [104] → validation + tìm hạng + kiểm tra trùng, loại trừ chính ID đang sửa [106–123]
    ↓
Gán thuộc tính entity → SaveChangesAsync cập nhật MembershipTiers [125–128]
    ↓
Redirect Index [131] → HTML danh sách đọc lại SQL
```

## 18. Xóa hạng

Nút: [Views/MemberTier/Index.cshtml dòng 53](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/MemberTier/Index.cshtml:53). Xử lý: [MemberTierController.cs dòng 140](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/MemberTierController.cs:140).

```text
POST /MemberTier/Delete/{id}
    ↓
FindAsync đọc hạng [142] → Users.AnyAsync(u => u.TierId == id) [145]
    ↓
Có tài khoản sử dụng: thông báo không thể xóa → redirect [146–149]
Không có: Remove → SaveChangesAsync → DELETE MembershipTiers [152–153]
    ↓
Nếu FK chặn: thông báo lỗi [154–158]; thành công: thông báo và redirect [160–161]
```

## 19. Danh sách bảng giá

Xử lý: [PricingController.cs dòng 29](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:29). View: [Views/Pricing/Index.cshtml dòng 34](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/Index.cshtml:34).

```text
GET /Pricing → Index() [29]
    ↓
PricingConfigs.Include(TableType) → đọc giá và loại bàn, sắp theo loại/giờ [31–35]
    ↓
PricingListViewModel → return View(vm) [39–51]
    ↓
View foreach Configs → HTML bảng giá
```

## 20. Thêm bảng giá

Form: [Views/Pricing/CreateEdit.cshtml dòng 17](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Pricing/CreateEdit.cshtml:17). Xử lý: [PricingController.cs dòng 59](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:59).

```text
GET Create() [59] → GetTableTypeOptionsAsync đọc TableTypes [19–22] → form chọn loại bàn
    ↓ nhập loại bàn, giờ bắt đầu/kết thúc và giá
POST /Pricing/Create → Create(CreateEditPricingViewModel model) [67]
    ↓
ModelState [71] → parse giờ hh:mm, bắt đầu < kết thúc [73–78]
    ↓
ValidateConfigAsync: loại bàn tồn tại, không trùng khung giờ [80, helper 166–173]
    ↓
Sai: trả form với lỗi; đúng: Add PricingConfig + SaveChangesAsync [81–88]
    ↓
INSERT PricingConfigs → redirect Index [91] → đọc SQL và hiện giá mới
```

Chồng giờ: `existing.StartHour < new.EndHour && new.StartHour < existing.EndHour`, cùng TableTypeId. Model: [PricingViewModels.cs dòng 20](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Models/PricingViewModels.cs:20).

## 21. Sửa/xóa bảng giá

Xử lý: [PricingController.cs dòng 99](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/PricingController.cs:99).

```text
Sửa: GET Edit(id) [99] → FindAsync PricingConfigs → form điền sẵn [101–113]
    ↓ POST Edit(model) [118]
Validation, giờ, loại bàn và chồng giờ, bỏ qua chính ID đang sửa [121–133]
    ↓
Gán entity → SaveChangesAsync UPDATE [134–138] → redirect Index [141]

Xóa: POST Delete(id) [150] → FindAsync [152]
    ↓
Remove + SaveChangesAsync DELETE [155–156] → redirect Index [159]
```

## 22. Báo cáo doanh thu

Form: [Views/Revenue/Index.cshtml dòng 4](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Revenue/Index.cshtml:4). Xử lý: [RevenueController.cs dòng 19](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:19).

```text
Chọn ngày/nhóm và bấm Xem → GET /Revenue?from=...&to=...&groupBy=...
    ↓
Index(from,to,groupBy) [19]
    ↓
CreateRange: giá trị mặc định, kiểm tra ngày và nhóm [21, helper 46–57]
    ↓
PopulateAsync: đổi mốc ngày UTC+7 sang UTC [63–67]
    ↓
Đọc PaymentTransactions theo khoảng ngày [68–69]
    ↓
Sum AmountPaid; đếm giao dịch; Distinct InvoiceId [70–72]
    ↓
Nhóm ngày/tháng, thêm kỳ 0, nhóm PaymentMethod [73–84]
    ↓
RevenueReportViewModel → return View(model) [23]
    ↓
View hiển thị chỉ số và foreach Periods/Methods [18/21] → HTML báo cáo
```

Entity: [PaymentTransaction.cs](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Data/Entities/PaymentTransaction.cs:3). Không lấy tiền hóa đơn chưa trả để cộng vào doanh thu.

## 23. Xuất CSV

Nút: [Views/Revenue/Index.cshtml dòng 8](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Views/Revenue/Index.cshtml:8). Xử lý: [RevenueController.cs dòng 30](C:/Users/Admin/OneDrive/Documents/GitHub/billiard-management-system/src/Bms.Web/Controllers/RevenueController.cs:30).

```text
Bấm Xuất CSV → GET /Revenue/Export?from=...&to=...&groupBy=...
    ↓
Export() [30] → CreateRange + PopulateAsync [32–34]
    ↓
Ngày sai: BadRequest HTTP 400 [33]
Ngày đúng: dựng CSV từ Periods, mã hóa UTF-8 có BOM [35–38]
    ↓
return File(bytes, "text/csv; charset=utf-8", tên file) [39]
    ↓
Trình duyệt nhận file tải xuống, không nhận View HTML
```

## Cách trả lời thầy trong 20 giây

“Em bắt đầu từ form/nút trong View. Form gửi GET hoặc POST tới action của controller theo routing trong Program. MVC bind tham số vào ViewModel. Controller kiểm tra quyền và dữ liệu rồi dùng DbContext/UserManager đọc hoặc ghi các bảng SQL. Nếu lỗi, em trả lại View kèm ModelState. Nếu lưu thành công, em redirect về action danh sách, trình duyệt GET mới để đọc lại dữ liệu và hiển thị HTML.”

Hạng/giá hiện là CRUD cấu hình; tự cộng điểm/nâng hạng và áp bảng giá vào mở phiên chưa có luồng tích hợp hoàn chỉnh. Không tìm code chưa được triển khai để trình bày như đã có.
