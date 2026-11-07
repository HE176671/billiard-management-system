# HƯỚNG DẪN THỰC THI DÀNH CHO AI AGENT (ANTIGRAVITY) - PHẦN VIỆC THÀNH VIÊN 2 (TV2)

## 1. NGỮ CẢNH DỰ ÁN & QUY CHUẨN KỸ THUẬT (BẮT BUỘC TUÂN THỦ)
* **Dự án:** Hệ thống quản lý quán Bida (Billiard Management System - BMS).
* **Tech Stack:** ASP.NET Core 8.0 MVC, Entity Framework Core, ASP.NET Core Identity, SQL Server (`BilliardDB`).
* **Cấu trúc thư mục chính:** `src/Bms.Web/`
  * Controllers: `src/Bms.Web/Controllers/`
  * Views: `src/Bms.Web/Views/{ControllerName}/`
  * Models / ViewModels: `src/Bms.Web/Models/`
  * Data / Entities: `src/Bms.Web/Data/`
* **Quy tắc bảo mật & Git:** 
  * **TUYỆT ĐỐI KHÔNG** sửa hoặc commit các file: `appsettings.Development.json`, thư mục `bin/`, `obj/`.
* **Quy tắc Giao diện (UI/CSS Standards):**
  * Tất cả các View quản trị/nhân viên/khách hàng phải sử dụng: `Layout = "_AdminLayout";`
  * Sử dụng các class CSS có sẵn của hệ thống:
    * `data-card`: Khung card trắng bo góc.
    * `data-card-header`: Header của card.
    * `btn-primary-bms`: Nút đỏ chính.
    * `btn-outline-bms`: Nút viền đỏ.
    * `btn-sm-icon`: Nút icon nhỏ.
    * `badge-active`: Badge xanh (Active / Đang hoạt động).
    * `badge-inactive`: Badge đỏ (Locked / Đã khóa).
    * `stat-card`: Card thống kê (dùng cho Dashboard).
    * `form-card`: Khung form nhập liệu.

---

## 2. TRẠNG THÁI DATABASE HIỆN TẠI (ĐÃ CẬP NHẬT TRÊN SQL SERVER)
Database `BilliardDB` đã được chạy script cập nhật trực tiếp. **KHÔNG chạy `Add-Migration` hay `Update-Database` làm hỏng cấu trúc hiện tại**, hãy cập nhật trực tiếp vào các class Entity C# và `ApplicationDbContext.cs` để đồng bộ:

1. **Bảng `MembershipTiers`:**
   * Các cột hiện có: `Id` (int, PK), `TierName` (nvarchar), `DiscountPercent` (decimal(5,2)), `SpendThreshold` (decimal(18,2)), `PointMultiplier` (int).
2. **Bảng `AspNetUsers` (`ApplicationUser`):**
   * Các cột mở rộng: `FullName` (nvarchar), `EmployeeCode` (nvarchar), `IsActive` (bit), `TierId` (int?, FK -> MembershipTiers), `RewardPoints` (int, NOT NULL, Default 0), `TotalSpend` (decimal(18,2), NOT NULL, Default 0), `LastPaidDate` (datetime2, NULL).
3. **Bảng mới `PointTransactions`:**
   * `Id` (int, PK), `CustomerId` (nvarchar(450), FK -> AspNetUsers), `Amount` (int), `TransactionType` (varchar(20): 'Earn', 'Redeem', 'Expire'), `CreatedAt` (datetime2).
4. **Bảng mới `Reviews`:**
   * `Id` (int, PK), `CustomerId` (nvarchar(450), FK -> AspNetUsers), `SessionId` (int, FK -> PlaySessions), `Rating` (int, 1-5), `Comment` (nvarchar(MAX), null), `AdminReply` (nvarchar(MAX), null), `CreatedAt` (datetime2).
5. **Bảng mới `Notifications`:**
   * `Id` (int, PK), `UserId` (nvarchar(450), FK -> AspNetUsers), `Title` (nvarchar(200)), `Content` (nvarchar(MAX)), `IsRead` (bit, default 0), `Type` (varchar(50), null), `CreatedAt` (datetime2).

---

## 3. BỘ QUY TẮC NGHIỆP VỤ CỐT LÕI (BUSINESS RULES)
1. **Quy tắc Tích điểm (Earn):**
   * Tính trên `FinalAmount` (số tiền thực trả sau khi trừ khuyến mãi).
   * Tỷ lệ: $20,000 \text{ VNĐ} = 1 \text{ Điểm}$ (nhân với `PointMultiplier` của hạng hiện tại).
   * Làm tròn xuống (Lấy phần nguyên: `Math.Floor(FinalAmount / 20000) * PointMultiplier`).
2. **Quy tắc Phân hạng (Tiering):**
   * Dựa trên `TotalSpend` (Tổng chi tiêu lũy kế, không bao giờ bị giảm khi đổi điểm).
   * **Member:** $0 \rightarrow 1,999,000 \text{ VNĐ}$ (Giảm $0\%$, Hệ số nhân: $1$).
   * **Silver:** $2,000,000 \rightarrow 4,999,000 \text{ VNĐ}$ (Giảm $5\%$ tiền giờ, Hệ số nhân: $1$).
   * **Gold:** $\ge 5,000,000 \text{ VNĐ}$ (Giảm $10\%$ tiền giờ, Hệ số nhân: $2$).
3. **Quy tắc Đổi điểm (Redeem):**
   * $1 \text{ Điểm} = 1,000 \text{ VNĐ}$.
   * **Giảm trừ trực tiếp hóa đơn:** Tối đa không vượt quá $50\%$ giá trị hóa đơn cần thanh toán.
   * **Đổi quà/dịch vụ:** $20 \text{ Điểm}$ (Nước ngọt/suối/bò húc), $50 \text{ Điểm}$ (Đĩa trái cây/hướng dương/khô gà), $80 \text{ Điểm}$ (Free 1 giờ chơi).
4. **Quy tắc Hết hạn điểm (Expire):**
   * Hạng thành viên và `TotalSpend` bảo lưu vĩnh viễn.
   * `RewardPoints` tự động reset về $0$ nếu `LastPaidDate` cách thời điểm hiện tại quá $180$ ngày. Mỗi khi có hóa đơn mới, `LastPaidDate` được cập nhật mới.

---

## 4. NGUYÊN TẮC XỬ LÝ PHỤ THUỘC DỮ LIỆU (MOCK DATA STRATEGY)
> **QUAN TRỌNG:** Các bảng `Invoices`, `PlaySessions`, `Products`, `Combos` thuộc phạm vi code của thành viên khác. Nếu các bảng này trong Database chưa có dữ liệu hoặc code của thành viên khác chưa hoàn thiện:
> 1. Hãy ưu tiên hoàn thiện đầy đủ **Giao diện (UI/View)** chuẩn chỉnh theo `_AdminLayout`.
> 2. Trong Controller/Service, hãy kiểm tra nếu truy vấn DB trả về rỗng (hoặc chưa thể join), hãy tạo **Dữ liệu mẫu (Mock/Seed Data in-memory)** trả về cho View để giao diện hiển thị đẹp mắt, đầy đủ biểu đồ/bảng biểu phục vụ việc demo trước.
> 3. Đánh dấu comment `// TODO: Connect real data from Member X` tại các vị trí dùng Mock Data để dễ dàng thay thế sau.

---

## 5. DANH SÁCH CÁC MODULE VÀ MÀN HÌNH CẦN CODE (THEO THỨ TỰ)

### BƯỚC 1: Đồng bộ tầng Data (`src/Bms.Web/Data/`)
1. Cập nhật class `ApplicationUser` và `MembershipTier` với các thuộc tính mới.
2. Tạo các class Entity mới: `PointTransaction.cs`, `Review.cs`, `Notification.cs`.
3. Khai báo `DbSet` tương ứng trong `ApplicationDbContext.cs` và cấu hình Fluent API trong `OnModelCreating` nếu cần.

### BƯỚC 2: Module Quản trị Khách hàng & Cấu hình (Admin & Staff)
1. **Controller: `TierConfigController.cs` (`[Authorize(Roles = "Admin")]`)**
   * **Màn hình Cấu hình Hạng & Ưu đãi (`Index.cshtml`):** Hiển thị bảng danh sách `MembershipTiers` trong `data-card`. Cho phép Admin chỉnh sửa `SpendThreshold`, `DiscountPercent`, `PointMultiplier`.
   * **Logic:** Khi lưu thay đổi ngưỡng chi tiêu, viết hàm quét toàn bộ user trong `AspNetUsers` để cập nhật lại `TierId` tương ứng với `TotalSpend` hiện có của họ.
2. **Controller: `CustomerManagementController.cs` (`[Authorize(Roles = "Admin,Staff")]`)**
   * **Màn hình Quản lý Khách hàng (`Index.cshtml` - Theo SRS mục 2):**
     * Thiết kế Layout 2 cột (Bootstrap Grid: `col-md-7` bên trái cho Danh sách, `col-md-5` bên phải cho Panel Chi tiết `form-card`).
     * **Bên trái:** Thanh tìm kiếm (Tên/SĐT), Dropdown lọc theo Hạng (All/Member/Silver/Gold) và Trạng thái (Active/Locked), Nút `+ Add Customer` (`btn-primary-bms`). Bảng hiển thị: Full Name, Phone, Tier, Points (`RewardPoints`), Status (`badge-active` / `badge-inactive`).
     * **Bên phải (Customer Detail Panel):** Khi click vào 1 dòng bên trái (hoặc bấm Add), load thông tin vào form: Full Name (Required), Phone Number (Required, 10 số, Unique), Email (Optional, Unique), Membership Tier (Read-only, auto), Total Points (Read-only, auto), Total Spend (Read-only), Account Status (Radio: Active/Locked).
     * **Action:** Thêm mới khách hàng (tạo user Identity với role `Customer`), Cập nhật thông tin, Khóa/Mở khóa tài khoản, và nút "Gắn thẻ thành viên" (cập nhật mã thẻ vào cột `EmployeeCode` hoặc trường định danh).

### BƯỚC 3: Module Cổng thông tin Khách hàng (Customer Portal)
1. **Controller: `AccountController.cs` (Bổ sung nếu chưa có)**
   * **Màn hình Đăng ký thành viên (`Register.cshtml`):** Cho phép khách tự đăng ký tài khoản (Validate trùng SĐT/Email, gán Role `Customer`, gán hạng mặc định là `Member`).
2. **Controller: `CustomerPortalController.cs` (`[Authorize(Roles = "Customer")]`)**
   * **Màn hình Hồ sơ + Điểm + Hạng (`Profile.cshtml`):** Sử dụng các `stat-card` để hiển thị nổi bật: Hạng hiện tại, Điểm khả dụng (`RewardPoints`), Tổng chi tiêu (`TotalSpend`), tiến trình lên hạng tiếp theo (Progress bar), và bảng lịch sử biến động điểm từ `PointTransactions`.
   * **Màn hình Lịch sử chơi & Hóa đơn (`History.cshtml`):** Có phân trang (Pagination). Hiển thị danh sách các phiên chơi và hóa đơn đã thanh toán. *(Áp dụng Mock Data nếu bảng `Invoices` và `PlaySessions` đang trống)*.
   * **Màn hình Xem Khuyến mãi & Đổi điểm (`Promotions.cshtml`):** Hiển thị danh sách các gói Combo ưu đãi và danh mục đổi điểm (20 điểm = Nước, 50 điểm = Đồ ăn, 80 điểm = 1 Giờ chơi). Có nút "Đổi điểm" gọi xuống Service để kiểm tra điều kiện và trừ điểm.

### BƯỚC 4: Module Đánh giá & Thông báo (Reviews & Notifications)
1. **Controller: `ReviewController.cs`**
   * **Action cho Customer:** Form/Modal gửi đánh giá (Chọn sao từ 1-5, nhập Comment) cho các phiên chơi đã thanh toán. Lưu vào bảng `Reviews`. *(Hỗ trợ Mock SessionId nếu chưa có dữ liệu phiên chơi thực)*.
   * **Màn hình cho Admin/Staff (`Index.cshtml` - `[Authorize(Roles = "Admin,Staff")]`):** Danh sách đánh giá của khách hàng, có bộ lọc theo số sao. Cho phép Admin nhập phản hồi vào cột `AdminReply`.
   * **Logic liên kết:** Ngay khi Admin bấm lưu `AdminReply`, tự động tạo 1 bản ghi mới trong bảng `Notifications` gửi tới `CustomerId` đó.
2. **Controller: `NotificationController.cs` (`[Authorize]`)**
   * **Màn hình/Component Thông báo (`Index.cshtml`):** Hiển thị danh sách thông báo của User đang đăng nhập, phân biệt chưa đọc (in đậm) và đã đọc. Có nút "Đánh dấu đã đọc tất cả".

### BƯỚC 5: Module Core Logic Service & Background Job
1. **Tạo Service: `ILoyaltyService.cs` và `LoyaltyService.cs` (`src/Bms.Web/Services/`)**
   * Đăng ký Dependency Injection (Scoped) trong `Program.cs`.
   * Viết hàm `ProcessPaymentLoyaltyAsync(string customerId, decimal finalAmount)`: Thực hiện cộng `TotalSpend`, xét nâng hạng `TierId`, tính điểm thưởng cộng vào `RewardPoints`, cập nhật `LastPaidDate = DateTime.UtcNow`, ghi log `PointTransactions` ('Earn') và bắn `Notifications` nếu khách được thăng hạng.
   * Viết hàm `RedeemPointsForBillAsync(string customerId, int pointsToRedeem, decimal currentBillAmount)`: Validate điểm khả dụng và quy tắc $\le 50\%$ hóa đơn, trừ điểm, ghi log `PointTransactions` ('Redeem') và trả về số tiền được giảm.
   * *(Ghi chú: Tạo thêm 1 trang Test nhỏ hoặc nút bấm giả lập Thanh toán trên giao diện Admin để TV2 có thể tự test luồng Tích điểm/Đổi điểm này độc lập mà không cần chờ màn hình Checkout của thành viên khác).*
2. **Tạo Background Job: `PointExpirationBackgroundService.cs` (`BackgroundService`)**
   * Đăng ký `AddHostedService` trong `Program.cs`.
   * Chạy định kỳ mỗi ngày 1 lần: Quét các user có `RewardPoints > 0` và `LastPaidDate < DateTime.UtcNow.AddDays(-180)`. Ghi log `PointTransactions` ('Expire', số âm) và reset `RewardPoints = 0`.

### BƯỚC 6: Module Dashboard Quản trị (`DashboardController.cs` - `[Authorize(Roles = "Admin")]`)
* **Màn hình Dashboard (`Index.cshtml`):**
  * Sử dụng `stat-card` hiển thị 4 chỉ số tổng quan: Tổng doanh thu tháng, Tổng số giờ chơi, Tổng số khách hàng thành viên, Đánh giá trung bình (sao).
  * Vẽ biểu đồ (dùng Chart.js qua CDN) hiển thị Doanh thu 7 ngày gần nhất và Tỷ lệ phân bổ Hạng thành viên (Pie Chart: Member/Silver/Gold).
  * **Lưu ý Mock Data:** Truy vấn thật từ bảng `AspNetUsers` và `Reviews`. Riêng phần Doanh thu từ `Invoices` và Giờ chơi từ `PlaySessions`, nếu query tổng bằng $0$ (do chưa có dữ liệu từ TV khác), hãy tự động nạp mảng dữ liệu mẫu (Mock Data) lên biểu đồ để giao diện Dashboard luôn trực quan, sinh động.