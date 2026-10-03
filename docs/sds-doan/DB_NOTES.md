# Báo cáo kỹ thuật CSDL: Phân hệ Quản lý Phiên chơi (BMS)

**Tài liệu tham chiếu:** `BMS_Database_Starter/BilliardDB_Full.sql`, `BMS_Database_Starter/README_VI.md`, `docs/PROJECT_CONTEXT.md`.  
**Phạm vi:** Bảng `TableTypes`, `BilliardTables`, `PlaySessions` và 2 Stored Procedures `usp_OpenSession`, `usp_CloseSession`.

---

## 1. Chi tiết lược đồ 3 bảng CSDL

### 1.1. Bảng `dbo.TableTypes` (Loại bàn & Giá giờ)
*Vị trí file: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 128–134)*

| Tên cột | Kiểu dữ liệu | Nullable | Mặc định | Khóa / Ràng buộc | Diễn giải |
|---|---|:---:|---|---|---|
| `Id` | `int` IDENTITY | **NOT NULL** | Tự tăng | **PK** `PK_TableTypes` | Mã loại bàn |
| `Name` | `nvarchar(50)` | **NOT NULL** | — | **UNIQUE** `UQ_TableTypes_Name`<br>**CHECK** `CK_TableTypes_Name` (`LEN(LTRIM(RTRIM(Name))) > 0`) | Tên loại bàn (Pool, Carom...) |
| `HourlyRate` | `decimal(18,2)` | **NOT NULL** | — | **CHECK** `CK_TableTypes_Rate` (`HourlyRate > 0`) | Đơn giá giờ cố định của loại bàn |

---

### 1.2. Bảng `dbo.BilliardTables` (Bàn bida)
*Vị trí file: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 135–148)*

| Tên cột | Kiểu dữ liệu | Nullable | Mặc định | Khóa / Ràng buộc | Diễn giải |
|---|---|:---:|---|---|---|
| `Id` | `int` IDENTITY | **NOT NULL** | Tự tăng | **PK** `PK_BilliardTables` | Khóa chính bàn |
| `TableCode` | `nvarchar(20)` | **NOT NULL** | — | **UNIQUE** `UQ_BilliardTables_Code`<br>**CHECK** `CK_Table_Code` (`LEN(LTRIM(RTRIM(TableCode))) > 0`) | Mã bàn hiển thị (B01, B02...) |
| `TableTypeId` | `int` | **NOT NULL** | — | **FK** `FK_Table_Type` ➔ `TableTypes(Id)` | Khóa ngoại loại bàn |
| `FloorNumber` | `int` | **NOT NULL** | `1` | **CHECK** `CK_Table_Floor` (`FloorNumber >= 0`) | Số tầng đặt bàn |
| `Status` | `varchar(20)` | **NOT NULL** | `'Available'` | **CHECK** `CK_Table_Status` (`Status IN ('Available','InUse','AwaitingPayment','Maintenance','Inactive')`) | Trạng thái hoạt động của bàn |
| `RowVersion` | `rowversion` | **NOT NULL** | Hệ thống | — | Token chống cập nhật đồng thời (Concurrency) |

* **Index:** `IX_BilliardTables_Type_Status` trên `(TableTypeId, Status)` (dòng 147).

---

### 1.3. Bảng `dbo.PlaySessions` (Phiên chơi bida)
*Vị trí file: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 180–208)*

| Tên cột | Kiểu dữ liệu | Nullable | Mặc định | Khóa / Ràng buộc | Diễn giải |
|---|---|:---:|---|---|---|
| `Id` | `int` IDENTITY | **NOT NULL** | Tự tăng | **PK** `PK_PlaySessions` | Khóa chính phiên chơi |
| `TableId` | `int` | **NOT NULL** | — | **FK** `FK_Session_Table` ➔ `BilliardTables(Id)` | Bàn diễn ra phiên chơi |
| `BookingId` | `int` | NULL | — | **FK** `FK_Session_Booking` ➔ `Bookings(Id)` | Lượt đặt trước (nếu có) |
| `CustomerId` | `nvarchar(450)` | NULL | — | **FK** `FK_Session_Customer` ➔ `AspNetUsers(Id)` | Khách hàng hội viên (nếu có) |
| `OpenedById` | `nvarchar(450)` | **NOT NULL** | — | **FK** `FK_Session_OpenedBy` ➔ `AspNetUsers(Id)` | Nhân viên thực hiện mở bàn |
| `ClosedById` | `nvarchar(450)` | NULL | — | **FK** `FK_Session_ClosedBy` ➔ `AspNetUsers(Id)` | Nhân viên thực hiện đóng bàn |
| `StartAtUtc` | `datetime2(0)` | **NOT NULL** | — | — | Thời điểm mở phiên (UTC) |
| `EndAtUtc` | `datetime2(0)` | NULL | — | — | Thời điểm đóng phiên (UTC) |
| `HourlyRateSnapshot`| `decimal(18,2)`| **NOT NULL** | — | **CHECK** `CK_Session_Rate` (`HourlyRateSnapshot > 0`) | Đơn giá chốt tại thời điểm mở |
| `PlaytimeAmount` | `decimal(18,2)`| NULL | — | — | Tiền giờ chơi (tính khi đóng) |
| `Status` | `varchar(20)` | **NOT NULL** | `'Active'` | **CHECK** `CK_Session_Status` (`Status IN ('Active','Closed')`) | Trạng thái phiên |
| `RowVersion` | `rowversion` | **NOT NULL** | Hệ thống | — | Token kiểm soát đồng thời |

* **Ràng buộc toàn vẹn vòng đời phiên (`CK_Session_Lifecycle`, dòng 200–203):**
  * Khi `Status = 'Active'`: Bắt buộc `EndAtUtc IS NULL`, `ClosedById IS NULL`, `PlaytimeAmount IS NULL`.
  * Khi `Status = 'Closed'`: Bắt buộc `EndAtUtc IS NOT NULL` (và `>= StartAtUtc`), `ClosedById IS NOT NULL`, `PlaytimeAmount IS NOT NULL` (và `>= 0`).
* **Indexes:**
  * `UX_PlaySessions_ActiveTable`: Filtered Unique Index trên `(TableId) WHERE Status = 'Active'` (mỗi bàn chỉ có duy nhất 1 phiên Active tại một thời điểm, dòng 205).
  * `UX_PlaySessions_Booking`: Filtered Unique Index trên `(BookingId) WHERE BookingId IS NOT NULL` (mỗi lượt booking chỉ mở tối đa 1 phiên, dòng 206).
  * `IX_PlaySessions_Table_Start`: Index trên `(TableId, StartAtUtc)` phục vụ tra cứu lịch sử (dòng 207).

---

## 2. Chi tiết Stored Procedure `usp_OpenSession`
*Vị trí file: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 491–543)*

### 2.1. Tham số vào / ra & Giá trị trả về
* **Tham số đầu vào:**
  * `@TableId` (`int`, bắt buộc): ID bàn cần mở.
  * `@StaffId` (`nvarchar(450)`, bắt buộc): ID nhân viên (hoặc admin) đang đăng nhập thực hiện thao tác.
  * `@BookingId` (`int`, mặc định `NULL`, tùy chọn): ID lượt đặt trước nếu mở cho khách đã check-in.
  * `@CustomerId` (`nvarchar(450)`, mặc định `NULL`, tùy chọn): ID khách hàng hội viên có tài khoản.
* **Tham số đầu ra:** Không dùng tham số `OUTPUT`.
* **Kết quả trả về:** Trả về Result set gồm 1 cột duy nhất: `SessionId` (int) - ID của phiên vừa tạo trong `PlaySessions`.

### 2.2. Danh sách lỗi THROW

| Mã lỗi | Thông điệp lỗi | Điều kiện kích hoạt |
|---|---|---|
| `51401` | `Active Staff or Admin required.` | `@StaffId` không tồn tại, hoặc `IsActive = 0`, hoặc không có Role `Staff` hay `Admin`. |
| `51402` | `Table is not Available.` | Bàn không tồn tại hoặc `Status <> 'Available'`. Khóa bàn bằng `(UPDLOCK, HOLDLOCK)`. |
| `51403` | `Table already has an Active session.` | Bàn đã có một phiên khác đang ở trạng thái `Active`. |
| `51404` | `Use a checked-in, unexpired booking for this table.` | Mở theo booking nhưng booking không ở trạng thái `CheckedIn`, hoặc sai bàn, hoặc đã hết hạn (`EndAtUtc <= @Now`). |
| `51405` | `Customer does not match the booking.` | Có truyền `@CustomerId` nhưng không khớp với khách đã đặt bàn. |
| `51406` | `This booking already has a session.` | Lượt đặt bàn này đã được mở phiên trước đó. |
| `51407` | `Table is reserved for a current booking.` | Mở bàn vãng lai nhưng bàn đang có booking giữ chỗ (`CheckedIn` hoặc `Confirmed` trong khung giờ hiện tại). |
| `51408` | `Customer account is inactive or invalid.` | Khách hàng chỉ định không tồn tại, hoặc bị khóa (`IsActive = 0`), hoặc không có Role `Customer`. |

---

## 3. Chi tiết Stored Procedure `usp_CloseSession`
*Vị trí file: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 545–578)*

### 3.1. Tham số vào / ra & Giá trị trả về
* **Tham số đầu vào:**
  * `@SessionId` (`int`, bắt buộc): ID phiên chơi đang hoạt động cần đóng.
  * `@StaffId` (`nvarchar(450)`, bắt buộc): ID nhân viên (hoặc admin) thực hiện đóng phiên.
* **Tham số đầu ra:** Không dùng tham số `OUTPUT`.
* **Kết quả trả về:** Trả về Result set 1 dòng với các thông tin chốt phiên:
  * `Id` (`int`): ID phiên chơi.
  * `StartAtUtc` (`datetime2(0)`): Thời điểm bắt đầu.
  * `EndAtUtc` (`datetime2(0)`): Thời điểm kết thúc.
  * `HourlyRateSnapshot` (`decimal(18,2)`): Giá giờ snapshot lúc mở.
  * `PlaytimeAmount` (`decimal(18,2)`): Tiền giờ chơi đã tính.
  * `Status` (`varchar(20)`): Trạng thái mới của phiên (`'Closed'`).

### 3.2. Danh sách lỗi THROW

| Mã lỗi | Thông điệp lỗi | Điều kiện kích hoạt |
|---|---|---|
| `51501` | `Active Staff or Admin required.` | `@StaffId` không tồn tại, hoặc `IsActive = 0`, hoặc không có Role `Staff` hay `Admin`. |
| `51502` | `Only an Active session can be closed.` | `@SessionId` không tồn tại hoặc phiên không ở trạng thái `Active`. |

---

## 4. Xử lý Hóa đơn (Invoice) sau khi đóng phiên
* **`usp_CloseSession` KHÔNG tạo Invoice:**
  * Xem xét trực tiếp code procedure (dòng 565–570): Procedure chỉ thực hiện 3 lệnh cập nhật:
    1. Cập nhật `PlaySessions`: tính `PlaytimeAmount`, cập nhật `EndAtUtc`, `ClosedById`, chuyển `Status = 'Closed'`.
    2. Cập nhật `BilliardTables`: chuyển bàn sang `Status = 'AwaitingPayment'` (chờ thanh toán).
    3. Cập nhật `Bookings`: chuyển `Status = 'Completed'` (nếu phiên gắn với booking).
  * **Không có lệnh INSERT vào bảng `Invoices`**.
* **Phân công trách nhiệm:**
  * Bảng `Invoices` và luồng thanh toán thuộc về **Thành viên 3 (Customer & Hóa đơn/Thanh toán)** theo phân công SDS mở rộng (`docs/PROJECT_CONTEXT.md` dòng 22, 68, 134).
  * Thành viên 1 (Staff quản lý phiên) chỉ phụ trách mở phiên, đóng phiên và chuyển bàn sang trạng thái `AwaitingPayment`. Bàn chỉ được giải phóng về `Available` sau khi hóa đơn được thanh toán đầy đủ ở phân hệ thanh toán.

---

## 5. Quy tắc nhóm đã thỏa thuận trong tài liệu

### 5.1. Phân công trách nhiệm 5 thành viên
*Nguồn: `BMS_Database_Starter/README_VI.md` (dòng 60–67), `docs/PROJECT_CONTEXT.md` (dòng 18–25)*

* **Thành viên 1 (Đoan):** Staff quản lý bàn & phiên chơi (`TableTypes`, `BilliardTables`, `PlaySessions`). Chức năng: xem danh sách bàn, mở phiên, theo dõi thời gian thực, đóng phiên.
* **Thành viên 2:** Staff quản lý đặt bàn (`Bookings`). Chức năng: xem danh sách, lọc, chi tiết, check-in khách đến, hủy đặt bàn.
* **Thành viên 3:** Customer (Đăng ký/đăng nhập, đặt bàn cá nhân); mở rộng: Hóa đơn (`Invoices`), tích hợp cổng thanh toán VNPay (`PaymentTransactions`), điểm thưởng.
* **Thành viên 4:** Admin thực đơn & tồn kho (`ProductCategories`, `Products`); mở rộng: Gói Combo (`Combos`, `ComboItems`, `SessionCombos`).
* **Thành viên 5 (Hùng):** Admin quản lý nhân viên & tài khoản (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `MembershipTiers`).

### 5.2. Quy tắc tính tiền giờ và làm tròn
*Nguồn: `BilliardDB_Full.sql` (dòng 564–566), `docs/PROJECT_CONTEXT.md` (dòng 72)*

* **Đơn giá áp dụng:** Lấy theo đơn giá chốt tại thời điểm mở bàn (`HourlyRateSnapshot`), không phụ thuộc vào việc đổi giá sau đó của `TableTypes`.
* **Đơn vị thời gian:** Tính chính xác theo **từng giây thực tế** giữa `StartAtUtc` và `EndAtUtc` bằng hàm `DATEDIFF_BIG(second, @Start, @Now)`.
* **Công thức tính:**
  $$\text{PlaytimeAmount} = \text{ROUND}\left(\frac{\text{Số giây} \times \text{Đơn giá}}{3600}, 0\right)$$
* **Quy tắc làm tròn:** Làm tròn đến **đồng nguyên vẹn (whole VND, 0 chữ số thập phân)** bằng hàm `ROUND(..., 0)`.
* **Phí tối thiểu:** Trong giai đoạn v1 **không áp dụng phí tối thiểu (no minimum charge)**.

---

## 6. Dữ liệu mẫu (Seed Data) có sẵn trong CSDL
*Nguồn: `BMS_Database_Starter/BilliardDB_Full.sql` (dòng 624–647), `BMS_Database_Starter/README_VI.md` (dòng 86–91)*

### 6.1. Loại bàn (`TableTypes`)
1. `Pool`: Đơn giá `100,000` VND/giờ.
2. `Carom`: Đơn giá `120,000` VND/giờ.

### 6.2. Danh sách 6 bàn (`BilliardTables`)
| Mã bàn | Loại bàn | Tầng | Trạng thái hiện tại | Diễn giải nghiệp vụ |
|---|---|:---:|---|---|
| **B01** | Pool | 1 | `InUse` | Đang có khách chơi (phiên đang hoạt động). |
| **B02** | Pool | 1 | `Available` | Bàn trống (có lượt booking ngày mai). |
| **B03** | Pool | 1 | `Available` | Bàn trống (có lượt booking ngày mai). |
| **B04** | Carom | 2 | `AwaitingPayment` | Chờ thanh toán (đã đóng phiên chơi 2 tiếng). |
| **B05** | Carom | 2 | `Maintenance` | Đang bảo trì, không thể mở phiên. |
| **B06** | Carom | 2 | `Available` | Bàn trống; có khách `demo-customer-02` đã check-in booking trước 10 phút, đang chờ nhân viên mở bàn. |

### 6.3. Danh sách 2 phiên chơi mẫu (`PlaySessions`)
1. **Phiên 1 (Đang hoạt động - `Active`):**
   * Bàn: `B01` (Pool).
   * Khách hàng: `demo-customer-01` (Khách mẫu 01).
   * Nhân viên mở: `demo-staff-01` (Nhân viên mẫu 01).
   * Thời điểm mở: 90 phút trước thời điểm seed (`DATEADD(minute, -90, @Now)`).
   * Đơn giá: `100,000` VND/h.
   * `EndAtUtc`, `ClosedById`, `PlaytimeAmount`: `NULL`.
   * Trạng thái: `'Active'`.
2. **Phiên 2 (Đã đóng - `Closed`):**
   * Bàn: `B04` (Carom).
   * Khách hàng: `NULL` (Khách vãng lai).
   * Nhân viên mở & đóng: `demo-staff-01`.
   * Thời gian chơi: Từ 3 giờ trước đến 1 giờ trước thời điểm seed (chơi tròn 2 tiếng).
   * Đơn giá: `120,000` VND/h.
   * Tiền giờ chốt: `240,000` VND.
   * Trạng thái: `'Closed'`. Bàn B04 đang chuyển sang `AwaitingPayment`.
