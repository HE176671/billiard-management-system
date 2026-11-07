# PHÂN TÍCH ĐỐI CHIẾU DATABASE DỰ ÁN VỚI `BMS_Scope_Review.md` & PHƯƠNG ÁN TỐI ƯU

> **Cơ sở đối chiếu:**
> 1. Database thực tế hiện tại trong dự án (`BilliardDB` — gồm 33 bảng từ `BilliardDB_Full.sql` + `08_AddWorkShiftReconciliation.sql` + `09_TV2_CrmLoyaltyV2.sql`).
> 2. Tài liệu chốt phạm vi hệ thống: `BMS_Scope_Review.md` (26 Quy tắc nghiệp vụ `BR-01` $\rightarrow$ `BR-26` và 40 Use Case Phase 1).

---

## PHẦN 1: CÁC CHỨC NĂNG CỦA BẠN (PHÂN HỆ HỘI VIÊN, HẠNG THẺ & ĐIỂM THƯỞNG) CẦN NHỮNG BẢNG NÀO?

Chiếu theo mục **2.5 (BR-16 $\rightarrow$ BR-19g)** và các **Use Case UC-10, UC-28, UC-29, UC-34 $\rightarrow$ UC-39** trong `BMS_Scope_Review.md`, phân hệ của bạn thực chất chỉ cần **6 bảng cốt lõi** sau đây:

| STT | Tên bảng trong DB | Chức năng & Use Case sử dụng | Lý do bắt buộc phải có |
| :--- | :--- | :--- | :--- |
| **1** | **`dbo.AspNetUsers`** *(kèm `AspNetRoles`, `AspNetUserRoles`)* | - **UC-01, UC-02, UC-03:** Đăng nhập, đổi mật khẩu, quản lý nhân viên (`Admin`, `Manager`, `Staff`).<br>- **UC-28, UC-29:** Đăng ký & cập nhật hồ sơ hội viên tại quầy (`FullName`, `PhoneNumber`, `Email`, `DateOfBirth`, `EmployeeCode` làm mã thẻ), lưu `RewardPoints`, `TierId`, `TierAchievedAtUtc`, `TierReviewAtUtc`, `TierGraceUntilUtc`. | Lưu cả tài khoản nhân sự nội bộ và hồ sơ hội viên cùng trạng thái vòng đời hạng 12 tháng. |
| **2** | **`dbo.MembershipTiers`** | - **UC-10 (BR-17, BR-17a, BR-17g):** Cấu hình 3 hạng `Member`, `Silver`, `Gold` (`SpendThreshold`, `DiscountPercent`, `PointMultiplier`, `BirthdayVoucherValue`) và xem trước thay đổi. | Lưu tham số chính sách hạng thành viên để tính giảm giá giờ chơi, nhân điểm và xét hạng. |
| **3** | **`dbo.PointTransactions`** | - **UC-35a (BR-19, BR-19a):** Tích điểm hóa đơn (`Earn`), trừ điểm vào bill tối đa 50% (`Redeem`).<br>- **UC-35:** Trừ điểm khi đổi quà.<br>- **UC-36 (BR-19b):** Điều chỉnh điểm thủ công (`Adjust`).<br>- **UC-37 (BR-17b):** Tính tổng `SpendAmount` 365 ngày gần nhất để xét nâng/giữ hạng. | Sổ cái bất biến lưu toàn bộ biến động điểm và doanh số xét hạng 12 tháng của hội viên. |
| **4** | **`dbo.RewardItems`** | - **UC-34, UC-35 (BR-19a):** Quản lý danh mục quà đổi điểm (Mã quà, Tên quà, Điểm đổi, Trạng thái Đóng/Mở) và nhân viên thực hiện đổi quà bằng điểm tại quầy. | Lưu danh mục các phần quà/món F&B cho phép đổi bằng điểm tích lũy. |
| **5** | **`dbo.CustomerVouchers`** | - **UC-38 (BR-19c, BR-19f):** Tự động cấp Voucher sinh nhật lúc 00:00 (hết hạn 23:59 cùng ngày, tối đa 1 lần/năm theo khóa `BDAY-{Year}-{UserId}`) và áp dụng trừ ở Bước 2 khi thanh toán. | Quản lý mã voucher sinh nhật của hội viên và kiểm soát trạng thái dùng 1 lần (`Active` / `Used` / `Expired`). |
| **6** | **`dbo.CustomerAuditLogs`** *(hoặc gộp chung thành `AuditLogs`)* | - **UC-16 (BR-17c, BR-19b, BR-24):** Lưu vết kiểm toán khi hội viên lên/hạ hạng, khi Manager điều chỉnh điểm thủ công, sửa thông tin hội viên. | Đáp ứng yêu cầu bắt buộc ghi Audit Log trong BR-17c, BR-19b và BR-24. |

*(Nếu giữ **UC-39 — Gửi ưu đãi qua email** thì dùng thêm bảng **`dbo.Campaigns`** để lưu lịch sử các đợt gửi email ưu đãi theo hạng).*

---

## PHẦN 2: CÁC BẢNG DATABASE ĐANG BỊ THỪA SO VỚI `BMS_Scope_Review.md` & GIẢI THÍCH TẠI SAO

Khi đối chiếu 33 bảng hiện có trong `BilliardDB` với bản chốt `BMS_Scope_Review.md`, có **6 bảng nghiệp vụ đang bị THỪA** (và 4 bảng phụ của Identity không dùng đến):

| STT | Bảng bị thừa trong DB hiện tại | Mức độ | Giải thích chi tiết tại sao thừa (Đối chiếu `BMS_Scope_Review.md`) |
| :--- | :--- | :--- | :--- |
| **1** | **`dbo.Bookings`** *(và cột `PlaySessions.BookingId`)* | **Thừa 100% (Phải bỏ)** | - **Mục 1 (#6, #8) & Mục 4 (Phase 2):** Nhóm đã quyết định **bỏ hoàn toàn tính năng đặt bàn trước** và bỏ trạng thái bàn *"Đã đặt"*.<br>- Trong code hiện tại cũng không có `BookingController`, chỉ có 4 dòng dữ liệu mẫu trong SQL. |
| **2** | **`dbo.Reviews`** | **Thừa 100% (Phải bỏ)** | - **Mục 4 (Phase 2) & Mục 5.2:** Nhóm đã chốt **bỏ tính năng Đánh giá dịch vụ của khách**, bỏ ô "Đánh giá trung bình" trên Dashboard và bỏ menu "Đánh giá dịch vụ".<br>- Khách hàng không còn tài khoản đăng nhập nên không thể gửi đánh giá sau phiên chơi. |
| **3** | **`dbo.PricingConfigs`** | **Thừa 100% (Phải bỏ)** | - **Quy tắc BR-02 & UC-05:** *"Mỗi loại bàn (Thường, VIP, …) có một đơn giá/giờ duy nhất. Không chia khung giờ hay ngày."*<br>- **Mục 4 (Phase 2):** *"Giá theo khung giờ / ngày lễ"* đã đẩy sang Phase 2.<br>- Đơn giá giờ theo loại bàn đã nằm trực tiếp ở cột `HourlyRate` của bảng **`dbo.TableTypes`**, nên bảng `PricingConfigs` (`StartHour`, `EndHour`) là hoàn toàn dư thừa và mâu thuẫn với BR-02. |
| **4** | **`dbo.PointAdjustmentRequests`** | **Thừa so với BR-19b** | - **Quy tắc BR-19b & UC-36:** Chốt rằng chỉ có **Manager** trực tiếp điều chỉnh điểm thủ công (cộng hoặc trừ) kèm lý do và ghi vào Audit Log.<br>- Quy trình phức tạp cũ (*Nhân viên tạo phiếu điều chỉnh $\ge 100$ điểm rồi chờ Admin phê duyệt Maker-Checker*) không nằm trong `BMS_Scope_Review.md`. Khi Manager cộng/trừ điểm thì ghi thẳng vào `PointTransactions` (`Adjust`) + `CustomerAuditLogs`, không cần bảng trung gian `PointAdjustmentRequests`. |
| **5** | **`dbo.Notifications`** | **Thừa so với BR-19g & Mục 5.1d** | - **Mục 5.1 (d) của `BMS_Scope_Review.md` ghi rõ:** *"Code cần sửa cho khớp scope: (d) thông báo hạng gửi email thay vì chỉ ghi bảng Notifications."*<br>- **Quy tắc BR-19g:** Thông báo lên hạng, vào ân hạn, hạ hạng và voucher sinh nhật được **gửi qua Email** (nếu khách có email) và **hiển thị trực tiếp trên màn hình tra cứu hội viên tại quầy** để nhân viên báo miệng (dựa vào `TierGraceUntilUtc`, `Spend12Months` và `CustomerVouchers`), không dùng hộp thư chuông thông báo in-app của khách nữa. |
| **6** | **`dbo.CustomerNotes`** | **Có thể gộp/bỏ** | - Trong 40 Use Case của `BMS_Scope_Review.md` (UC-28, UC-29) không có nghiệp vụ quản lý danh sách nhiều ghi chú ghim (`Pin`) theo từng nhân viên. Nếu cần lưu ghi chú đặc điểm khách hàng, chỉ cần 1 cột `Note nvarchar(500)` trong `AspNetUsers` là đủ. |
| **7** | **`AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`** | **Bảng mặc định của Identity không dùng (`0 rows`)** | - Hệ thống chỉ dùng `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`. Không dùng đăng nhập mạng xã hội (`UserLogins`) hay Token/Claim rời.<br>- *Lưu ý kỹ thuật:* Nếu dùng `IdentityDbContext` mặc định của ASP.NET Core thì giữ nguyên trong SQL để EF Core không báo lỗi, nhưng **không cần đưa 4 bảng này vào sơ đồ ERD báo cáo**. |

---

## PHẦN 3: CÁC CỘT DỮ LIỆU ĐANG LỆCH / THỪA TRONG CÁC BẢNG CÒN LẠI

Ngoài các bảng thừa ở trên, trong các bảng được giữ lại có một số cột cần điều chỉnh để khớp 100% với `BMS_Scope_Review.md`:

1. **Bảng `dbo.MembershipTiers`:**
   - **Cột thừa:** `MinPoints` (Do hệ thống cũ xét hạng theo điểm, còn **BR-17** xét hạng theo `SpendThreshold` — chi tiêu 12 tháng).
   - **Dữ liệu Seed cần sửa theo Mục 2.5 (BR-17) & Mục 5.1c:**
     - `Silver`: Chi tiêu 12 tháng = `2.000.000đ`, Giảm giờ = `5%`, Nhân điểm = `×1`, Quà sinh nhật = **`30.000đ`** *(hiện đang seed 50.000đ)*.
     - `Gold`: Chi tiêu 12 tháng = **`5.000.000đ`** *(hiện đang seed 10.000.000đ)*, Giảm giờ = `10%`, Nhân điểm = `×2`, Quà sinh nhật = **`60.000đ`** *(hiện đang seed 100.000đ)*.
2. **Bảng `dbo.AspNetUsers`:**
   - **Cột thừa:** `AccountSource` (`Self`/`Counter`), `ClaimedAtUtc` (do đã bỏ cổng khách hàng tự đăng ký và gộp thẻ OTP).
   - **Cột thiếu theo BR-16:** Cần thêm cột `IsAgeVerified bit NOT NULL DEFAULT 0` (*"Nhân viên xem CCCD, hệ thống chỉ lưu ngày sinh và cờ 'đã xác minh 18+', không lưu số CCCD"*).
3. **Bảng `dbo.RewardItems`:**
   - Theo **BR-19a**: *"Đổi quà trong danh mục 'Quà đổi điểm'; quà là món F&B thì trừ kho."*
   - Nên có thêm cột khóa ngoại `ProductId int NULL REFERENCES dbo.Products(Id)` để khi nhân viên bấm đổi quà là nước ngọt/đồ ăn tại quầy thì hệ thống biết phải trừ tồn kho (`StockQuantity`) của sản phẩm nào trong bảng `Products`.
4. **Bảng `dbo.Invoices` & `dbo.PaymentTransactions` (Phân hệ Thanh toán BR-20, BR-21):**
   - `Invoices` cần thêm cột `RoundingAmount decimal(18,2) DEFAULT 0` (lưu phần tiền làm tròn lên 1.000đ khi trả tiền mặt theo **BR-21a**) và cho phép `Status = 'Cancelled'` (theo **BR-22**).
   - `PaymentTransactions`: Bỏ cột `GatewayRefCode` và bỏ `'VNPay'`, `'SePay'` trong `CK_Payment_Method` (chỉ giữ `'Cash'` và `'Transfer'` theo **BR-20**).

---

## PHẦN 4: ĐỀ XUẤT PHƯƠNG ÁN TỐI ƯU HÓA DATABASE

Tùy vào mục tiêu hiện tại của bạn (chốt ERD làm báo cáo `Report` chung cho cả nhóm hay sửa trực tiếp trong source code nhánh của bạn), tôi đề xuất **2 phương án tối ưu**:

### Phương án 1: Tối ưu Sơ đồ Thiết kế CSDL (ERD) chuẩn hóa cho Báo cáo Nhóm (Khuyên dùng cho tài liệu SRS / SDS)
Nếu nhóm đang chuẩn hóa lại Database cho khớp 100% với 40 Use Case trong `BMS_Scope_Review.md`:
1. **Xóa 6 bảng dư thừa khỏi ERD & Script khởi tạo:**
   - Bỏ `Bookings`, `Reviews`, `PricingConfigs`, `PointAdjustmentRequests`, `Notifications`, `CustomerNotes`.
   - Ẩn 4 bảng phụ không dùng của Identity (`AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`) khỏi sơ đồ ERD nghiệp vụ.
2. **Gộp bảng Audit Log dùng chung toàn hệ thống (theo BR-24 & UC-16):**
   - Thay vì chỉ có `CustomerAuditLogs` cho riêng khách hàng, đổi/mở rộng thành bảng **`dbo.AuditLogs`** chung (`Id`, `Action`, `EntityType`, `EntityId`, `Detail`, `Reason`, `ActorId`, `CreatedAtUtc`) để phục vụ chung cho cả **BR-24** (*hủy món, hủy bill, sửa giá, chênh lệch ca, điều chỉnh điểm, nâng/hạ hạng*).
3. **Tối giản luồng Đổi quà & Voucher (theo BR-19a & BR-19c):**
   - **Đổi quà tại quầy (UC-35):** Khi khách đổi quà trong `RewardItems`, hệ thống trừ điểm trực tiếp trong `PointTransactions` (`Redeem`) và trừ `Products.StockQuantity` ngay lập tức — **không cần sinh mã Voucher trung gian** cho quà đổi điểm.
   - **Bảng `CustomerVouchers`:** Chỉ chuyên trách lưu **Voucher sinh nhật (UC-38 / BR-19c)** với hạn dùng trong ngày (`00:00` $\rightarrow$ `23:59` ngày sinh nhật).

### Phương án 2: Tối ưu trực tiếp trên Code nhánh `dung` của bạn (An toàn, không làm hỏng code của thành viên khác)
Nếu bạn muốn cập nhật ngay code của bạn để khớp với **Mục 5 ("Việc cần làm còn lại")** của `BMS_Scope_Review.md` mà không làm lỗi build của các bạn khác trong nhóm:
1. **Sửa ngay 4 điểm được chỉ đích danh ở Mục 5.1 của `BMS_Scope_Review.md`:**
   - **(a)** Đổi phân quyền `TierConfigController`, `RewardItemController`, `PointAdjustmentController`, `CampaignController` cho phép vai trò `Manager` (hoặc `"Admin,Manager"`).
   - **(b)** Sửa hạn dùng của Voucher sinh nhật trong [`CampaignService.IssueBirthdayVouchersAsync`](../src/Bms.Web/Services/CampaignService.cs) từ `now.AddDays(30)` thành **23:59:59 cùng ngày sinh nhật theo giờ Việt Nam** (đúng **BR-19c**).
   - **(c)** Cập nhật dữ liệu Seed hạng thành viên trong SQL & `SchemaUpgrader.cs`:
     - `Silver`: Ngưỡng `2.000.000đ`, Giảm `5%`, Nhân điểm `×1`, Quà sinh nhật **`30.000đ`**.
     - `Gold`: Ngưỡng **`5.000.000đ`**, Giảm `10%`, Nhân điểm `×2`, Quà sinh nhật **`60.000đ`**.
   - **(d)** Bổ sung cờ **"Đã xác minh 18+" (`IsAgeVerified`)** khi đăng ký hội viên tại quầy (đúng **BR-16**).
2. **Trên giao diện (Mục 5.2 của `BMS_Scope_Review.md`):**
   - Ẩn hoàn toàn mục menu **"Đánh giá dịch vụ"** trên thanh Sidebar (`_AdminLayout.cshtml`) và bỏ thẻ **"Đánh giá trung bình"** trên trang Dashboard.
   - Bỏ luồng chờ duyệt phiếu `PointAdjustmentRequests` khi điều chỉnh điểm (cho phép Manager/Admin điều chỉnh điểm trực tiếp kèm lý do và ghi `CustomerAuditLogs` theo đúng **BR-19b**).

