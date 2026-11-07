# BẢN ĐỒ CHI TIẾT CHỨC NĂNG, CƠ SỞ DỮ LIỆU VÀ MÃ NGUỒN (TV2 FULL SYSTEM)

Tài liệu này giải thích chi tiết toàn bộ các chức năng trong hệ thống: **Dữ liệu lưu ở bảng nào**, **chạy câu truy vấn nào (LINQ & SQL)**, **lấy dữ liệu từ đâu (Request/Session/DB)**, và **nằm ở đâu trong source code (File, Class, Method, Dòng cụ thể)**.

---

## MỤC LỤC CÁC PHÂN HỆ
1. [Phân hệ 1: Xác thực, Đăng nhập & Hồ sơ cá nhân (Account & Profile)](#phan-he-1)
2. [Phân hệ 2: Quản lý Nhân viên & Phân quyền (Employee Management)](#phan-he-2)
3. [Phân hệ 3: Quản lý Bảng giá, Loại bàn & Trạng thái bàn (Pricing & Tables)](#phan-he-3)
4. [Phân hệ 4: Quản lý Ca làm việc & Đối soát tiền mặt (Work Shifts & Cash Reconciliation)](#phan-he-4)
5. [Phân hệ 5: Tổng quan Dashboard & Báo cáo doanh thu (Dashboard & Revenue)](#phan-he-5)
6. [Phân hệ 6: Quản lý Khách hàng, Tra cứu 360 & Ghi chú (Customer Management & 360 Profile)](#phan-he-6)
7. [Phân hệ 7: Tích điểm, Tiêu điểm & Sổ cái điểm bất biến (Loyalty Ledger & Checkout Engine)](#phan-he-7)
8. [Phân hệ 8: Đề xuất & Phê duyệt điều chỉnh điểm (Maker - Checker Point Adjustment)](#phan-he-8)
9. [Phân hệ 9: Quản lý Hạng thành viên & Chu kỳ xét giữ hạng (Membership Tiers & Tier Evaluation)](#phan-he-9)
10. [Phân hệ 10: Đổi thưởng, Kho Voucher & Quà hiện vật (Reward Items & Customer Vouchers)](#phan-he-10)
11. [Phân hệ 11: CRM Dashboard (RFM) & Chiến dịch Marketing (CRM RFM & Campaigns)](#phan-he-11)
12. [Phân hệ 12: Cổng hội viên dành cho Khách hàng (Customer Portal)](#phan-he-12)
13. [Phân hệ 13: Đánh giá chất lượng (Reviews) & Thông báo (Notifications)](#phan-he-13)

---

<a id="phan-he-1"></a>
## PHÂN HỆ 1: XÁC THỰC, ĐĂNG NHẬP & HỒ SƠ CÁ NHÂN (ACCOUNT & PROFILE)

### 1.1. Đăng nhập (Login - UC01)
- **Bảng dữ liệu:** `dbo.AspNetUsers`, `dbo.AspNetUserRoles`, `dbo.AspNetRoles`.
- **Dữ liệu lấy từ đâu:** Form POST (`Username`, `Password`, `RememberMe`) + `__RequestVerificationToken`.
- **Câu truy vấn (Code LINQ & SQL):**
  - Tìm user theo Username / Email / Số điện thoại:
    ```csharp
    var user = await _userManager.FindByNameAsync(loginInput)
               ?? await _userManager.FindByEmailAsync(loginInput)
               ?? await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == loginInput);
    ```
    *Tương đương SQL:*
    ```sql
    SELECT TOP(1) * FROM dbo.AspNetUsers 
    WHERE NormalizedUserName = UPPER(@loginInput) 
       OR NormalizedEmail = UPPER(@loginInput) 
       OR PhoneNumber = @loginInput;
    ```
  - Xác thực mật khẩu: `_signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true)` (băm PBKDF2/HMACSHA256 đối chiếu `PasswordHash`).
- **Chạy ở đâu trong code:**
  - Controller: `AccountController.cs` -> Method `Login(LoginViewModel model, string? returnUrl)`
  - Điều hướng sau đăng nhập: `AccountController.RedirectByRoleAsync` (Admin -> `/Dashboard`, Staff -> `/Staff`, Customer -> `/CustomerPortal/Profile`).

### 1.2. Đăng ký tài khoản Khách hàng (Register - UC19)
- **Bảng dữ liệu:** Insert `dbo.AspNetUsers`, `dbo.AspNetUserRoles` (Role='Customer').
- **Dữ liệu lấy từ đâu:** Form POST (`UserName`, `Email`, `PhoneNumber`, `FullName`, `Password`).
- **Câu truy vấn (Code LINQ & SQL):**
  - Kiểm tra trùng:
    ```sql
    SELECT TOP(1) Id FROM dbo.AspNetUsers WHERE NormalizedUserName = @p0 OR NormalizedEmail = @p1 OR PhoneNumber = @p2;
    ```
  - Khởi tạo User với Hạng mặc định (MinPoints=0 hoặc SpendThreshold=0):
    ```csharp
    var user = new ApplicationUser {
        UserName = model.UserName, Email = model.Email, FullName = model.FullName,
        PhoneNumber = phone, AccountSource = "Self", IsActive = true,
        CreatedAtUtc = DateTime.UtcNow, TierId = defaultTier.Id,
        TierAchievedAtUtc = DateTime.UtcNow, TierReviewAtUtc = DateTime.UtcNow.AddMonths(12)
    };
    await _userManager.CreateAsync(user, model.Password);
    await _userManager.AddToRoleAsync(user, "Customer");
    ```
- **Chạy ở đâu trong code:** `AccountController.cs` -> Method `Register(RegisterViewModel model)`.

### 1.3. Nhận lại tài khoản tạo tại quầy qua OTP (F8 - Claim Account)
- **Bảng dữ liệu:** Update `dbo.AspNetUsers`, Insert `dbo.CustomerAuditLogs`.
- **Dữ liệu lấy từ đâu:** Khách hàng gõ SĐT đã từng chơi tại quầy nhưng chưa có mật khẩu (`AccountSource = 'Counter'`).
- **Câu truy vấn (Code LINQ & SQL):**
  - Kiểm tra trạng thái: `existingByPhone.AccountSource == "Counter" && existingByPhone.ClaimedAtUtc == null && string.IsNullOrEmpty(existingByPhone.PasswordHash)`
  - Xác minh OTP: `_userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "ClaimCounterAccount", otpCode)`.
  - Ghi Audit Log:
    ```sql
    INSERT INTO dbo.CustomerAuditLogs (CustomerId, Action, Detail, ActorId, CreatedAtUtc)
    VALUES (@Uid, 'Claim', '{"phone":"0912...","keptPoints":120}', @Uid, SYSUTCDATETIME());
    ```
- **Chạy ở đâu trong code:** `AccountController.cs` -> Method `Register` & `VerifyClaimOtp`.

### 1.4. Đổi mật khẩu & Xem hồ sơ cá nhân (UC03)
- **Bảng dữ liệu:** Update `dbo.AspNetUsers.PasswordHash`, `dbo.AspNetUsers.FullName`, `dbo.AspNetUsers.PhoneNumber`.
- **Dữ liệu lấy từ đâu:** Claims `User.Identity` (ID người đăng nhập hiện tại) + Form POST.
- **Câu truy vấn:**
  - `_userManager.GetUserAsync(User)` -> `SELECT TOP 1 * FROM AspNetUsers WHERE Id = @Id`
  - `_userManager.ChangePasswordAsync(user, oldPassword, newPassword)`
- **Chạy ở đâu trong code:** `ProfileController.cs` -> `Index` & `ChangePassword`.

---

<a id="phan-he-2"></a>
## PHÂN HỆ 2: QUẢN LÝ NHÂN VIÊN & PHÂN QUYỀN (EMPLOYEE MANAGEMENT)

- **Bảng dữ liệu:** `dbo.AspNetUsers`, `dbo.AspNetUserRoles`, `dbo.AspNetRoles`.
- **Quyền truy cập:** Chỉ Admin (`[Authorize(Roles = "Admin")]`).

### 2.1. Danh sách nhân viên & Tìm kiếm (UC43)
- **Câu truy vấn LINQ & SQL:**
  ```csharp
  var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
  // Lọc theo từ khóa: Tên, SĐT, Mã nhân viên (EmployeeCode)
  var query = staffUsers.Where(u => u.FullName.Contains(keyword) || u.PhoneNumber.Contains(keyword) || u.EmployeeCode.Contains(keyword))
                        .OrderBy(u => u.EmployeeCode).Skip((page-1)*pageSize).Take(pageSize);
  ```
- **Chạy ở đâu trong code:** `EmployeeController.cs` -> Method `Index(string? search, int page)`.

### 2.2. Thêm mới nhân viên
- **Thực thi:** Sinh mã `NVxxx`, tạo user `_userManager.CreateAsync(user, initialPassword)`, gán role `_userManager.AddToRoleAsync(user, "Staff")`.
- **Chạy ở đâu trong code:** `EmployeeController.cs` -> Method `Create(CreateEmployeeViewModel model)`.

### 2.3. Khóa / Mở khóa nhân viên & Thu hồi phiên đăng nhập
- **Thực thi:** 
  ```csharp
  user.IsActive = !user.IsActive;
  await _userManager.UpdateSecurityStampAsync(user); // Đổi SecurityStamp làm vô hiệu hóa Cookie phiên làm việc ngay lập tức
  ```
- **Chạy ở đâu trong code:** `EmployeeController.cs` -> Method `ToggleLock(string id)`.

### 2.4. Reset mật khẩu nhân viên (UC44)
- **Thực thi:** Dùng Token reset an toàn (không xóa mật khẩu nếu token lỗi):
  ```csharp
  var token = await _userManager.GeneratePasswordResetTokenAsync(user);
  await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
  ```
- **Chạy ở đâu trong code:** `EmployeeController.cs` -> Method `ResetPassword(ResetPasswordViewModel model)`.

---

<a id="phan-he-3"></a>
## PHÂN HỆ 3: QUẢN LÝ BẢNG GIÁ, LOẠI BÀN & TRẠNG THÁI BÀN (PRICING & TABLES)

### 3.1. Cấu hình bảng giá theo khung giờ & Loại bàn
- **Bảng dữ liệu:** `dbo.PricingConfigs`, `dbo.TableTypes`.
- **Dữ liệu lấy từ đâu:** Form POST (`TableTypeId`, `StartHour`, `EndHour`, `HourlyRate`).
- **Câu truy vấn kiểm tra trùng lắp khoảng giờ (No Overlapping):**
  ```csharp
  bool overlap = await _context.PricingConfigs.AnyAsync(p => 
      p.TableTypeId == model.TableTypeId &&
      p.Id != model.Id &&
      p.StartHour < model.EndHour && model.StartHour < p.EndHour);
  ```
  *Tương đương SQL:*
  ```sql
  SELECT CASE WHEN EXISTS (
      SELECT 1 FROM dbo.PricingConfigs 
      WHERE TableTypeId = @typeId AND Id <> @id 
        AND StartHour < @newEnd AND @newStart < EndHour
  ) THEN 1 ELSE 0 END;
  ```
- **Chạy ở đâu trong code:** `PricingController.cs` -> Method `Create`, `Edit`, `ValidateConfigAsync`.

### 3.2. Quản lý danh sách bàn & Trạng thái hoạt động
- **Bảng dữ liệu:** `dbo.BilliardTables` (`TableCode`, `TableTypeId`, `Status`: 'Available', 'Occupied', 'Maintenance').
- **Câu truy vấn:** `_context.BilliardTables.Include(b => b.TableType).ToListAsync()`.
- **Chạy ở đâu trong code:** `DashboardController.cs` và các màn hình sơ đồ bàn.

---

<a id="phan-he-4"></a>
## PHÂN HỆ 4: QUẢN LÝ CA LÀM VIỆC & ĐỐI SOÁT TIỀN MẶT (WORK SHIFTS & RECONCILIATION)

- **Bảng dữ liệu:** `dbo.WorkShifts`, `dbo.PaymentTransactions`.
- **Service xử lý:** `ShiftService.cs` (`IShiftService`).

### 4.1. Mở ca làm việc (Open Shift)
- **Quy tắc:** Mỗi nhân viên tại một thời điểm chỉ được mở 1 ca đang hoạt động (`Status = 'Active'`).
- **Câu truy vấn:**
  ```sql
  -- Kiểm tra ca đang mở
  SELECT TOP 1 * FROM dbo.WorkShifts WHERE StaffId = @StaffId AND Status = 'Active';
  -- Thêm mới ca
  INSERT INTO dbo.WorkShifts (StaffId, StartTimeUtc, StartingCash, Status, Notes)
  VALUES (@StaffId, SYSUTCDATETIME(), @StartingCash, 'Active', @Notes);
  ```
- **Chạy ở đâu trong code:** `ShiftController.cs` -> Method `Open` gọi `ShiftService.OpenShiftAsync`.

### 4.2. Khóa ca & Đối soát tiền mặt tự động (Close Shift & Cash Reconciliation)
- **Dữ liệu lấy từ:** Input người dùng nhập `EndingCash` (tiền đếm thực tế) và truy vấn doanh thu tiền mặt phát sinh trong ca từ `dbo.PaymentTransactions`.
- **Câu truy vấn tính toán:**
  ```sql
  -- Tổng tiền mặt thu trong ca (loại trừ VNPay/Chuyển khoản)
  SELECT ISNULL(SUM(AmountPaid), 0)
  FROM dbo.PaymentTransactions
  WHERE WorkShiftId = @ShiftId AND PaymentMethod = 'Cash';
  ```
- **Công thức đối soát:**
  - `ExpectedCash` (Tiền dự kiến) = `StartingCash` + `CashCollected`
  - `CashDifference` (Lệch thừa/thiếu) = `EndingCash` - `ExpectedCash`
  - Nếu lệch tiền (`CashDifference != 0`), bắt buộc người đóng ca phải nhập `Notes` giải trình lý do.
- **Chạy ở đâu trong code:** `ShiftController.cs` -> Method `Close` gọi `ShiftService.CloseShiftAsync`.

---

<a id="phan-he-5"></a>
## PHÂN HỆ 5: TỔNG QUAN DASHBOARD & BÁO CÁO DOANH THU (DASHBOARD & REVENUE)

### 5.1. Dashboard tổng quan quản trị (DashboardController)
- **Bảng dữ liệu:** `dbo.PaymentTransactions`, `dbo.BilliardTables`, `dbo.AspNetUsers`, `dbo.AspNetUserRoles`.
- **Câu truy vấn:**
  - Doanh thu hôm nay (tính theo múi giờ Việt Nam UTC+7):
    ```sql
    SELECT ISNULL(SUM(AmountPaid), 0) FROM dbo.PaymentTransactions
    WHERE TransactionTimeUtc >= @TodayStartUtc AND TransactionTimeUtc < @TomorrowStartUtc;
    ```
  - Doanh thu tháng này: `TransactionTimeUtc >= @MonthStartUtc AND TransactionTimeUtc < @NextMonthStartUtc`.
  - Trạng thái các bàn:
    ```sql
    SELECT Status, COUNT(*) FROM dbo.BilliardTables GROUP BY Status;
    ```
- **Chạy ở đâu trong code:** `DashboardController.cs` -> Method `Index()`.

### 5.2. Báo cáo doanh thu & Xuất CSV (RevenueController - UC46)
- **Bảng dữ liệu:** `dbo.PaymentTransactions`.
- **Dữ liệu lấy từ đâu:** Query parameters `from`, `to`, `groupBy` ('day' hoặc 'month').
- **Câu truy vấn tổng hợp:**
  ```csharp
  var payments = await _context.PaymentTransactions
      .Where(p => p.TransactionTimeUtc >= fromUtc && p.TransactionTimeUtc < toUtc)
      .Select(p => new { p.AmountPaid, p.InvoiceId, p.TransactionTimeUtc })
      .ToListAsync();

  // Nhóm theo ngày hoặc tháng theo giờ Việt Nam (UTC+7)
  var grouped = payments.GroupBy(p => VnTime.ToVnDate(p.TransactionTimeUtc))
      .Select(g => new RevenueRowViewModel {
          Date = g.Key,
          TotalRevenue = g.Sum(x => x.AmountPaid),
          PaymentCount = g.Count(),
          InvoiceCount = g.Select(x => x.InvoiceId).Distinct().Count() // Phân biệt hóa đơn trả nhiều lần
      });
  ```
- **Xuất CSV:** Ghi dữ liệu ra định dạng file UTF-8 with BOM (`Encoding.UTF8.GetPreamble()`) để Excel tiếng Việt không bị lỗi font.
- **Chạy ở đâu trong code:** `RevenueController.cs` -> Method `Index` & `Export`.

---

<a id="phan-he-6"></a>
## PHÂN HỆ 6: QUẢN LÝ KHÁCH HÀNG, TRA CỨU 360 & GHI CHÚ (CUSTOMER MANAGEMENT 360)

### 6.1. Tra cứu nhanh tại quầy (Lookup / Search Bar)
- **Bảng dữ liệu:** `dbo.AspNetUsers`, `dbo.MembershipTiers`, `dbo.CustomerNotes`.
- **Dữ liệu lấy từ:** Input tìm kiếm `q` (SĐT, mã thẻ `MBxxxxxx`, hoặc Tên khách).
- **Câu truy vấn LINQ & SQL:**
  ```csharp
  var user = await _context.Users
      .Include(u => u.Tier)
      .Include(u => u.Notes)
      .FirstOrDefaultAsync(u => u.PhoneNumber == q || u.EmployeeCode == q || u.FullName.Contains(q));
  ```
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `Lookup(string? q)` & `Search(string? q)`.

### 6.2. Ghi danh nhanh khách mới tại quầy (Quick Enroll)
- **Bảng dữ liệu:** Insert `dbo.AspNetUsers` (`AccountSource = 'Counter'`), `dbo.AspNetUserRoles`.
- **Dữ liệu lấy từ:** Modal form thu ngân nhập SĐT, Họ tên.
- **Quy tắc:** Tự sinh mã thẻ điện tử `MB` + 6 số ngẫu nhiên (`EmployeeCode`), gán Hạng mặc định, mật khẩu để trống.
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `QuickEnroll(QuickEnrollInputModel input)`.

### 6.3. Hồ sơ khách hàng toàn diện 360 (Customer 360 Profile)
- **Bảng dữ liệu:** `dbo.AspNetUsers`, `dbo.MembershipTiers`, `dbo.CustomerNotes`, `dbo.CustomerVouchers`, `dbo.PointTransactions`, `dbo.CustomerAuditLogs`.
- **Dữ liệu tính toán hiển thị:**
  1. *4 thẻ KPI 12 tháng:*
     - Tổng chi tiêu 12 tháng: `SUM(SpendAmount)` từ `PointTransactions` loại 'Earn' trong 365 ngày qua.
     - Lượt ghé: `COUNT(*)` các hóa đơn thanh toán trong 12 tháng.
     - Lần ghé gần nhất (Recency): Khoảng cách ngày từ `LastPaidDate` đến nay.
     - Tiến độ lên hạng: Số tiền còn thiếu để đạt `SpendThreshold` của hạng tiếp theo.
  2. *5 Tab quản lý:*
     - Tab 1: **Voucher** (`CustomerVouchers` trạng thái 'Active', 'Used', 'Expired').
     - Tab 2: **Lịch sử điểm** (`PointTransactions` sắp xếp ngày giảm dần).
     - Tab 3: **Hóa đơn & Giờ chơi** (Lịch sử thanh toán tại quán).
     - Tab 4: **Ghi chú phục vụ** (`CustomerNotes` có cờ `IsPinned` ghim sở thích lên đầu).
     - Tab 5: **Nhật ký & Gộp tài khoản** (`CustomerAuditLogs` ghi lại mọi thao tác nhân viên tác động lên khách).
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `Profile(string id)`.

### 6.4. Ghi chú sở thích & Ghim phục vụ (Customer Notes)
- **Bảng dữ liệu:** `dbo.CustomerNotes` (`CustomerId`, `Content`, `IsPinned`, `CreatedById`, `CreatedAtUtc`).
- **Thực thi:** Thêm ghi chú mới (`AddNote`) hoặc ghim/bỏ ghim lưu ý phục vụ khách quen (`TogglePinNote`).
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `AddNote` & `TogglePinNote`.

### 6.5. Gộp tài khoản khách bị trùng lặp (Merge Duplicate Customers)
- **Bảng dữ liệu tác động:** `dbo.AspNetUsers`, `dbo.PointTransactions`, `dbo.CustomerVouchers`, `dbo.CustomerNotes`, `dbo.Reviews`, `dbo.CustomerAuditLogs`.
- **Thực thi trong Database Transaction:**
  1. Chuyển toàn bộ FK của khách phụ (`Source`) sang khách chính (`Target`).
  2. Cộng dồn số dư điểm: `target.RewardPoints += source.RewardPoints`.
  3. Cộng dồn tổng chi tiêu: `target.TotalSpend += source.TotalSpend`.
  4. Đánh dấu vô hiệu hóa khách phụ: `source.IsActive = false`.
  5. Ghi Audit Log hành vi gộp tài khoản.
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `MergeDuplicate(string targetCustomerId, string sourceCustomerId)`.

---

<a id="phan-he-7"></a>
## PHÂN HỆ 7: TÍCH ĐIỂM, TIÊU ĐIỂM & SỔ CÁI ĐIỂM BẤT BIẾN (LOYALTY LEDGER & CHECKOUT)

- **Bảng dữ liệu cốt lõi:** `dbo.PointTransactions`, `dbo.AspNetUsers`.
- **Service xử lý:** `LoyaltyService.cs` (`ILoyaltyService`), `PointLedgerService.cs` (`IPointLedgerService`).

### 7.1. Thứ tự trừ tiền và áp dụng ưu đãi chuẩn tại quầy thu ngân (F5 Checkout Order)
Khi thanh toán hóa đơn, công thức chiết khấu được tính tuần tự:
$$\text{Tiền sau giảm hạng} = \text{Tiền giờ chơi} \times (1 - \text{DiscountPercent}) + \text{Tiền dịch vụ}$$
$$\text{Tiền sau voucher} = \max(0, \text{Tiền sau giảm hạng} - \text{Giá trị Voucher})$$
$$\text{Giới hạn tiêu điểm tối đa} = \min(\text{Điểm khách có}, \frac{\text{Tiền sau voucher} \times 50\%}{1.000})$$
$$\text{Tiền thanh toán cuối (FinalAmount)} = \text{Tiền sau voucher} - (\text{Điểm tiêu} \times 1.000)$$
- **Chạy ở đâu trong code:** `LoyaltyService.cs` -> Method `PreviewCheckoutAsync` & `CommitCheckoutAsync`.

### 7.2. Tích điểm hóa đơn (Earn Points)
- **Công thức:** $\text{Điểm tích} = \lfloor \frac{\text{FinalAmount}}{20.000} \rfloor \times \text{PointMultiplier của Hạng}$.
- **Ghi sổ cái bất biến (PointTransactions):**
  ```sql
  INSERT INTO dbo.PointTransactions (CustomerId, Points, TransactionType, ReferenceCode, SpendAmount, BalanceAfter, CreatedAt)
  VALUES (@CustomerId, @PointsEarned, 'Earn', @InvoiceCode, @FinalAmount, @NewBalance, SYSUTCDATETIME());
  ```
- **Chống cộng điểm trùng lặp (Idempotency):** Index `UX_PointTxn_Earn_Ref` trên cặp `(ReferenceCode, TransactionType)` chặn hoàn toàn việc thu ngân bấm thanh toán 2 lần.

### 7.3. Tiêu điểm trừ tiền (Redeem Points)
- **Quy tắc:** Kiểm tra `user.RewardPoints >= pointsToRedeem`. Chặn không cho âm điểm.
- **Ghi sổ cái:** Insert dòng `TransactionType = 'Redeem'` với số điểm âm (`-pointsToRedeem`), tính lại `BalanceAfter`.

---

<a id="phan-he-8"></a>
## PHÂN HỆ 8: ĐỀ XUẤT & PHÊ DUYỆT ĐIỀU CHỈNH ĐIỂM (MAKER - CHECKER)

- **Bảng dữ liệu:** `dbo.PointAdjustmentRequests`, `dbo.PointTransactions`, `dbo.AspNetUsers`.
- **Mục đích:** Chống gian lận điểm số giữa nhân viên và khách hàng.

### 8.1. Quy tắc Maker - Checker
- Nếu là **Admin** hoặc điều chỉnh nhỏ ($\le 100$ điểm): Tự động duyệt ngay và ghi sổ cái.
- Nếu là **Staff** điều chỉnh lớn ($> 100$ điểm): Bắt buộc tạo phiếu chờ duyệt (`Status = 'Pending'`).
- Người duyệt (`Checker`) bắt buộc phải có Role `Admin` và **phải khác** người tạo phiếu (`Maker != Checker`).

### 8.2. Các bước trong code
- **Tạo yêu cầu:** `PointAdjustmentController.cs` -> Method `Create`
  ```csharp
  var req = new PointAdjustmentRequest {
      CustomerId = customerId, Points = points, Reason = $"{category}: {note}",
      Status = "Pending", RequestedById = currentUserId, RequestedAtUtc = DateTime.UtcNow
  };
  _db.PointAdjustmentRequests.Add(req);
  ```
- **Duyệt phiếu (Approve):** `PointAdjustmentController.cs` -> Method `Approve`
  ```csharp
  // Gọi sổ cái cộng/trừ điểm chính thức
  var txn = await _ledger.AdjustPointsAsync(req.CustomerId, req.Points, req.Reason, checkerId);
  req.Status = "Approved";
  req.ReviewedById = checkerId;
  req.PointTransactionId = txn.Id;
  ```
- **Từ chối phiếu (Reject):** Cập nhật `Status = 'Rejected'`, ghi chú lý do từ chối.

---

<a id="phan-he-9"></a>
## PHÂN HỆ 9: QUẢN LÝ HẠNG THÀNH VIÊN & CHU KỲ XÉT HẠNG (TIERS & EVALUATION)

- **Bảng dữ liệu:** `dbo.MembershipTiers`, `dbo.AspNetUsers`, `dbo.Notifications`.
- **Service xử lý:** `TierEvaluationService.cs` (`ITierEvaluationService`).

### 9.1. Cấu hình bảng hạng thành viên
- Lưu tại `dbo.MembershipTiers`: `TierName`, `SpendThreshold` (ngưỡng chi tiêu 12 tháng), `DiscountPercent` (chiết khấu giờ chơi), `PointMultiplier` (hệ số tích điểm), `BirthdayVoucherValue` (quà sinh nhật).
- **Chạy ở đâu trong code:** `TierConfigController.cs` -> `Index`, `Save` và `MemberTierController.cs`.

### 9.2. Nâng hạng ngay lập tức (Immediate Tier Upgrade)
- **Kích hoạt:** Ngay khi thu ngân bấm thanh toán hóa đơn (`CommitCheckoutAsync`), hệ thống tính tổng chi tiêu 12 tháng:
  ```sql
  SELECT ISNULL(SUM(SpendAmount), 0) FROM dbo.PointTransactions
  WHERE CustomerId = @Uid AND TransactionType = 'Earn' AND CreatedAt >= @12MonthsAgo;
  ```
- Nếu `TotalSpend12M >= nextTier.SpendThreshold`:
  - Cập nhật `user.TierId = nextTier.Id`.
  - Đặt hạn xét hạng mới: `user.TierReviewAtUtc = DateTime.UtcNow.AddMonths(12)`.
  - Xóa ân hạn nếu có: `user.TierGraceUntilUtc = null`.
  - Tạo thông báo chúc mừng thăng hạng trong `dbo.Notifications`.

### 9.3. Chu kỳ xét giữ hạng 12 tháng, Ân hạn 30 ngày & Giáng tối đa 1 cấp (F3)
- **Kích hoạt bởi Background Job tự động chạy hàng ngày:** `LoyaltyDailyJob.cs`.
- **Quy tắc bảo vệ khách hàng:**
  - Khi đến ngày `TierReviewAtUtc`, nếu khách không đủ chi tiêu giữ hạng:
    - **Không giáng hạng ngay lập tức!**
    - Chuyển vào trạng thái **Ân hạn 30 ngày**: `user.TierGraceUntilUtc = DateTime.UtcNow.AddDays(30)`.
    - Gửi thông báo cảnh báo khách còn 30 ngày để duy trì hạng.
  - Khi hết 30 ngày ân hạn (`TierGraceUntilUtc < Now`), nếu vẫn không đủ điều kiện:
    - **Chỉ giáng tối đa 1 cấp** (Ví dụ: Gold -> Silver, không bao giờ rớt thẳng xuống Member).
    - Gia hạn chu kỳ mới 12 tháng tiếp theo.

---

<a id="phan-he-10"></a>
## PHÂN HỆ 10: ĐỔI THƯỞNG, KHO VOUCHER & QUÀ HIỆN VẬT (REWARD ITEMS & VOUCHERS)

- **Bảng dữ liệu:** `dbo.RewardItems`, `dbo.CustomerVouchers`, `dbo.PointTransactions`.
- **Service xử lý:** `VoucherService.cs` (`IVoucherService`).

### 10.1. Danh mục đổi thưởng (Reward Items)
- Bảng `dbo.RewardItems`: `Code`, `Name`, `RewardType` ('HourDiscount', 'BillDiscount', 'PhysicalGift'), `PointsCost`, `DiscountValue`, `StockQuantity`.
- Quản lý tại: `RewardItemController.cs` -> `Index`, `Save`.

### 10.2. Khách đổi điểm nhận voucher (Redeem Points for Voucher)
- **Thực thi:** Kiểm tra số dư điểm -> Trừ điểm trong `dbo.PointTransactions` (`TxType = 'Redeem'`) -> Sinh mã voucher ngẫu nhiên `VC-XXXXXX` trạng thái 'Active' với hạn sử dụng 30 ngày trong `dbo.CustomerVouchers`.
- **Chạy ở đâu trong code:** `CustomerPortalController.cs` -> Method `Redeem` gọi `VoucherService.RedeemRewardItemAsync`.

### 10.3. Xác nhận trao quà hiện vật & Hủy voucher hoàn điểm
- **Trao quà:** Thu ngân bấm xác nhận khách nhận bao tay/nước -> Voucher đổi trạng thái thành `Used`.
- **Hủy voucher:** Admin có quyền hủy voucher chưa dùng -> Hệ thống tự sinh bút toán hoàn điểm `Reverse` trong `dbo.PointTransactions` trả lại điểm cho khách.
- **Chạy ở đâu trong code:** `CustomerManagementController.cs` -> Method `ConfirmVoucher` & `CancelVoucher`.

---

<a id="phan-he-11"></a>
## PHÂN HỆ 11: CRM DASHBOARD (RFM) & CHIẾN DỊCH MARKETING (CRM & CAMPAIGNS)

- **Bảng dữ liệu:** `dbo.Campaigns`, `dbo.CustomerVouchers`, `dbo.PointTransactions`, `dbo.Notifications`, `dbo.AspNetUsers`.
- **Service xử lý:** `CustomerSegmentService.cs`, `CampaignService.cs`.

### 11.1. Thuật toán phân khúc khách hàng RFM (F6 Segment Matrix)
Hệ thống tự động chấm điểm từng khách hàng dựa trên:
- **R (Recency):** Số ngày từ lần chơi gần nhất đến nay.
- **F (Frequency):** Số lượt ghé quán trong 12 tháng qua.
- **M (Monetary):** Tổng tiền chi tiêu tích lũy.

| Phân khúc | Điều kiện phân loại | Ý nghĩa & Hành động đề xuất |
| :--- | :--- | :--- |
| **Champion** | $R \le 14$ ngày VÀ $F \ge 8$ lượt | Khách VIP trung thành nhất. Tặng quà độc quyền. |
| **Loyal** | $R \le 30$ ngày VÀ $F \ge 3$ lượt | Khách quen thường xuyên. Duy trì tích điểm. |
| **AtRisk** | $R > 45$ ngày VÀ $F \ge 3$ lượt | Khách quen đang có nguy cơ rời bỏ quán. Gửi voucher kéo khách. |
| **Lost** | $R > 90$ ngày | Khách đã bỏ đi. Chiến dịch ưu đãi lớn để kích hoạt lại. |
| **New** | Khách tạo tài khoản $\le 30$ ngày qua | Khách mới. Tặng điểm chào mừng. |
| **Regular** | Các trường hợp còn lại | Khách chơi định kỳ bình thường. |

- **Chạy ở đâu trong code:** `CustomerSegmentService.cs` -> Method `ClassifySegment(ApplicationUser user, int daysSinceLastPaid, int visits12M)`.

### 11.2. Tạo & Phát hành chiến dịch Marketing (Campaigns)
- **Quy trình:**
  1. Admin chọn nhóm mục tiêu: Phân khúc RFM (`TargetSegment`) hoặc Hạng thành viên (`TargetTierId`).
  2. Chọn hình thức ưu đãi: Tặng điểm thưởng trực tiếp hoặc Tặng Voucher giảm giá.
  3. Bấm **Gửi chiến dịch**: Hệ thống quét danh sách khách phù hợp, kiểm tra điều kiện nhận tin (`MarketingOptIn = true`), tạo voucher hoặc cộng điểm đồng loạt, đồng thời bắn chuông thông báo `dbo.Notifications` đến từng khách hàng.
- **Chống gửi trùng (Idempotent):** Chiến dịch kiểm tra quan hệ `CampaignId` trong bảng `CustomerVouchers` để không phát trùng voucher 2 lần cho 1 khách.
- **Chạy ở đâu trong code:** `CampaignController.cs` -> `Index`, `CreateAndSend` gọi `CampaignService.SendCampaignAsync`.

---

<a id="phan-he-12"></a>
## PHÂN HỆ 12: CỔNG HỘI VIÊN DÀNH CHO KHÁCH HÀNG (CUSTOMER PORTAL)

- **Quyền truy cập:** Khách hàng đăng nhập (`[Authorize(Roles = "Customer")]`).
- **Controller:** `CustomerPortalController.cs`.

### 12.1. Thẻ hội viên điện tử & Tiến độ lên hạng (`/CustomerPortal/Profile`)
- **Hiển thị:** Thẻ thành viên kèm mã QR (`MBxxxxxx`), số dư điểm khả dụng, giá trị quy đổi VNĐ, hạng hiện tại, thanh tiến độ chi tiêu 12 tháng, cảnh báo gia hạn hạng và điểm sắp hết hạn.
- **Câu truy vấn:** `_context.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.Id == customerId)`.

### 12.2. Lịch sử giao dịch điểm & Hóa đơn chơi (`/CustomerPortal/History`)
- **Hiển thị:** Danh sách phân trang toàn bộ các lần cộng/trừ điểm trong `dbo.PointTransactions` và các hóa đơn bàn đã đóng.
- **Câu truy vấn:**
  ```csharp
  var history = await _context.PointTransactions
      .Where(pt => pt.CustomerId == customerId)
      .OrderByDescending(pt => pt.CreatedAt)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .ToListAsync();
  ```

### 12.3. Kho Voucher của tôi & Đổi điểm nhận quà (`/CustomerPortal/Promotions`)
- **Hiển thị:** 
  - Danh sách voucher khách đang sở hữu (`CustomerVouchers` trạng thái 'Active').
  - Danh sách quà / voucher có thể đổi từ `RewardItems` cùng nút "Đổi điểm" tức thì.
- **Chạy ở đâu trong code:** `CustomerPortalController.cs` -> Method `Promotions` & `Redeem`.

---

<a id="phan-he-13"></a>
## PHÂN HỆ 13: ĐÁNH GIÁ (REVIEWS) & THÔNG BÁO (NOTIFICATIONS)

### 13.1. Đánh giá chất lượng phục vụ sau chơi (Reviews)
- **Bảng dữ liệu:** `dbo.Reviews` (`CustomerId`, `Rating` từ 1 đến 5 sao, `Comment`, `AdminReply`, `CreatedAtUtc`).
- **Chức năng:** Khách gửi đánh giá sau khi kết thúc ván chơi; Admin xem danh sách và phản hồi ý kiến của khách.
- **Chạy ở đâu trong code:** `ReviewController.cs` -> `Index`, `Create`, `Reply`.

### 13.2. Thông báo chuông hệ thống (Notifications)
- **Bảng dữ liệu:** `dbo.Notifications` (`UserId`, `Title`, `Message`, `Type`, `IsRead`, `CreatedAt`).
- **Chức năng:** Nhận thông báo tự động khi thăng hạng, khi được tặng voucher sinh nhật, khi chiến dịch gửi tin ưu đãi.
- **API đếm tin chưa đọc:** `/Notification/GetUnreadCount` được gọi định kỳ từ thanh Navbar giao diện.
- **Chạy ở đâu trong code:** `NotificationController.cs` -> `Index`, `MarkAsRead`, `GetUnreadCount`.

---

## BẢNG TỔNG HỢP MAPPING NHANH (QUICK REFERENCE TABLE)

| Tên chức năng | Bảng CSDL lưu trữ | File Controller / Service xử lý | Action / Method chính |
| :--- | :--- | :--- | :--- |
| **Đăng nhập** | `AspNetUsers`, `AspNetUserRoles` | `AccountController.cs` | `Login` (POST) |
| **Đăng ký khách** | `AspNetUsers`, `AspNetUserRoles` | `AccountController.cs` | `Register` (POST) |
| **Nhận lại tài khoản tại quầy** | `AspNetUsers`, `CustomerAuditLogs` | `AccountController.cs` | `VerifyClaimOtp` |
| **Đổi mật khẩu** | `AspNetUsers` | `ProfileController.cs` | `ChangePassword` |
| **Quản lý Nhân viên** | `AspNetUsers`, `AspNetUserRoles` | `EmployeeController.cs` | `Index`, `Create`, `ToggleLock` |
| **Bảng giá theo giờ** | `PricingConfigs`, `TableTypes` | `PricingController.cs` | `Index`, `Create`, `Edit` |
| **Mở ca & Đóng ca** | `WorkShifts`, `PaymentTransactions` | `ShiftController.cs`, `ShiftService.cs` | `Open`, `Close`, `CloseShiftAsync` |
| **Doanh thu & CSV** | `PaymentTransactions` | `RevenueController.cs` | `Index`, `Export` |
| **Tổng quan Dashboard** | `PaymentTransactions`, `BilliardTables` | `DashboardController.cs` | `Index` |
| **Tra cứu khách 360** | `AspNetUsers`, `MembershipTiers`, `Notes` | `CustomerManagementController.cs` | `Lookup`, `Search`, `Profile` |
| **Ghi chú phục vụ** | `CustomerNotes` | `CustomerManagementController.cs` | `AddNote`, `TogglePinNote` |
| **Gộp tài khoản trùng** | `AspNetUsers`, `PointTransactions`, v.v. | `CustomerManagementController.cs` | `MergeDuplicate` |
| **Tích & Tiêu điểm bàn** | `PointTransactions`, `AspNetUsers` | `LoyaltyService.cs`, `PointLedgerService.cs` | `CommitCheckoutAsync`, `RecordEarnAsync` |
| **Duyệt lệch điểm** | `PointAdjustmentRequests`, `PointTransactions` | `PointAdjustmentController.cs` | `Create`, `Approve`, `Reject` |
| **Cấu hình Hạng & Xét hạng**| `MembershipTiers`, `AspNetUsers` | `TierConfigController.cs`, `TierEvaluationService.cs` | `Save`, `EvaluateCustomerTierAsync` |
| **Kho Quà & Đổi điểm** | `RewardItems`, `CustomerVouchers` | `RewardItemController.cs`, `VoucherService.cs` | `Save`, `RedeemRewardItemAsync` |
| **CRM RFM & Chiến dịch** | `Campaigns`, `CustomerVouchers`, `AspNetUsers` | `CrmDashboardController.cs`, `CampaignService.cs` | `Index`, `SendCampaignAsync` |
| **Cổng hội viên khách** | `AspNetUsers`, `PointTransactions`, `Vouchers` | `CustomerPortalController.cs` | `Profile`, `History`, `Promotions` |

