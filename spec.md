# Customer Experience (Hồ sơ – Lịch sử – Điểm thưởng – Gọi đồ – Đánh giá) Spec
# Version: 0.1 (DRAFT) | Owner: @TrongHiep | Date: 2026-10-13

---

## 1. Context & Goal

**Business problem:** Hệ thống BMS (Billiard Management System) hiện đã có phần Admin. Khách hàng (Customer) chưa có màn hình nào để tự xem thông tin cá nhân, tra cứu các phiên chơi đã qua, theo dõi điểm thưởng/hạng thành viên, gọi đồ ăn/uống tại bàn và đánh giá dịch vụ. Mọi việc này đang phải nhờ nhân viên.

**Feature goal:** Hoàn thiện luồng trải nghiệm của Customer:
`xem hồ sơ → xem lịch sử → gọi đồ → thanh toán → đánh giá → nhận điểm`
Trong đợt này **KHÔNG** làm bước thanh toán online (VNPay/SePay) — xem mục 8.

**Success metric:** Customer tự thực hiện được 100% các chức năng trong phạm vi mà không cần nhân viên hỗ trợ; mọi Acceptance Criteria ở mục 7 đều pass.

**Phạm vi theo Use Case & Iteration:**

| Chức năng | UC | Iter | Status | Trong đợt này? |
|---|---|---|---|---|
| Hồ sơ cá nhân (xem/sửa) | UC08 | 1 | Done | ✅ |
| Lịch sử phiên chơi | UC10 | 1 | Done | ✅ |
| Điểm thưởng & Hạng thành viên | UC09/15 | 1 | Done | ✅ |
| Khách gọi đồ ăn/uống | UC07 | 2 | Done | ✅ |
| Thanh toán online (VNPay/SePay) | UC14 | 3 | To Do | ❌ **Làm sau** |
| Đánh giá dịch vụ sau chơi | UC17 | 3 | To Do | ✅ |

**Tech context (đã có sẵn, KHÔNG đổi):**
- .NET 8, ASP.NET Core MVC (Controller + Razor View), project `src/Bms.Web/Bms.Web.csproj`, namespace `Bms.Web`
- SQL Server Express, database `BilliardDB`; script nằm ở `BMS_Database_Starter/` (`01_CreateDatabase.sql`, `02_BusinessProcedures.sql`, `03_DemoData.sql`, `04_Verify.sql`)
- Phân quyền theo Role: `Admin`, `Staff`, `Customer` (dùng `[Authorize(Roles = "...")]`)
- Layout hiện có: `_AdminLayout`; các class CSS có sẵn: `data-card`, `data-card-header`, `btn-primary-bms`, `btn-outline-bms`, `btn-sm-icon`, `badge-active`, `badge-inactive`, `stat-card`, `form-card`
- Controller đặt tại `src/Bms.Web/Controllers/`, View đặt tại `src/Bms.Web/Views/<Tên>/`
- Tài khoản test Customer: `customer.demo01` (chưa có mật khẩu — Admin tạo qua `/Employee`)

---

## 2. Actors & Roles

| Actor | Mô tả | Permissions |
|---|---|---|
| Customer | Khách hàng đã có tài khoản, đã đăng nhập, role = `Customer` | Xem/sửa hồ sơ của **chính mình**; xem lịch sử phiên chơi **của mình**; xem điểm & hạng **của mình**; gọi đồ cho phiên **đang chơi của mình**; đánh giá phiên **đã hoàn tất của mình** |
| System (Automated) | Logic nghiệp vụ phía server / stored procedure | Cộng điểm, tính lại hạng, tính tiền món gọi vào hóa đơn phiên |
| Staff | Nhân viên quầy | Chỉ **nhận** đơn gọi đồ do Customer tạo ra (màn hình xử lý đơn của Staff nằm ngoài phạm vi) |
| Admin | Quản trị viên | Không có thao tác nào mới trong spec này |

**Actors KHÔNG có trong scope:**
- Guest (chưa đăng nhập) — bị redirect về trang login
- Staff/Admin thao tác thay cho Customer

---

## 3. Functional Requirements (EARS)

> Quy ước: `[UC08]`, `[UC10]`... là mã Use Case; `(Iter n)` là iteration triển khai.
> Mọi text hiển thị cho người dùng bằng **tiếng Việt**.

### 3.0 Ubiquitous (áp dụng toàn bộ)

- **FR-00.1** THE hệ thống SHALL chỉ cho phép user có role `Customer` truy cập các màn hình trong spec này (`[Authorize(Roles = "Customer")]`).
- **FR-00.2** THE hệ thống SHALL lấy `CustomerId` từ thông tin đăng nhập (claims) của user hiện tại và KHÔNG BAO GIỜ nhận `CustomerId` từ query string, route hoặc form.
- **FR-00.3** THE hệ thống SHALL hiển thị ngày giờ theo định dạng `dd/MM/yyyy HH:mm` và tiền theo định dạng `#,##0 ₫` (ví dụ `150,000 ₫`).
- **FR-00.4** THE hệ thống SHALL validate dữ liệu ở phía server cho mọi form POST, kể cả khi đã có validate phía client.

### 3.1 [UC08] Hồ sơ cá nhân — xem/sửa (Iter 1)

- **FR-08.1** WHEN Customer mở trang `/Profile`, THE hệ thống SHALL hiển thị: họ tên, tên đăng nhập, email, số điện thoại, hạng thành viên, tổng điểm hiện tại, ngày tham gia.
- **FR-08.2** WHEN Customer nhấn "Chỉnh sửa", THE hệ thống SHALL hiển thị form cho phép sửa **họ tên, email, số điện thoại**; tên đăng nhập, role, hạng, điểm ở trạng thái chỉ đọc.
- **FR-08.3** WHEN Customer nhấn "Lưu", THE hệ thống SHALL validate:
  - Họ tên: bắt buộc, 2–100 ký tự
  - Email: bắt buộc, đúng định dạng email, tối đa 100 ký tự, không trùng với tài khoản khác
  - Số điện thoại: bắt buộc, đúng 10 chữ số, bắt đầu bằng `0`, không trùng với tài khoản khác
- **FR-08.4** WHEN dữ liệu hợp lệ, THE hệ thống SHALL lưu thay đổi và hiển thị thông báo "Cập nhật hồ sơ thành công."
- **FR-08.5** WHERE email hoặc số điện thoại đã thuộc về tài khoản khác, THE hệ thống SHALL từ chối lưu và hiển thị lỗi tại đúng trường: "Email đã được sử dụng." / "Số điện thoại đã được sử dụng."

### 3.2 [UC10] Lịch sử phiên chơi (Iter 1)

- **FR-10.1** WHEN Customer mở trang `/PlayHistory`, THE hệ thống SHALL hiển thị danh sách các phiên chơi của Customer, sắp xếp theo giờ bắt đầu giảm dần, **10 dòng/trang**, mỗi dòng gồm: mã phiên, tên bàn, giờ bắt đầu, giờ kết thúc, thời lượng (giờ:phút), tổng tiền, trạng thái.
- **FR-10.2** WHEN Customer nhấn vào một phiên, THE hệ thống SHALL hiển thị trang chi tiết gồm: thông tin bàn, giờ chơi, tiền giờ chơi, danh sách món đã gọi (tên, số lượng, đơn giá, thành tiền), tổng tiền, điểm đã nhận.
- **FR-10.3** WHERE phiên đang diễn ra (chưa kết thúc), THE hệ thống SHALL hiển thị giờ kết thúc là "—" và badge "Đang chơi".
- **FR-10.4** WHERE Customer chưa có phiên chơi nào, THE hệ thống SHALL hiển thị: "Bạn chưa có phiên chơi nào."
- **FR-10.5** WHERE Customer truy cập chi tiết của phiên không thuộc về mình (hoặc không tồn tại), THE hệ thống SHALL trả về HTTP 404.

### 3.3 [UC09/15] Điểm thưởng & Hạng thành viên (Iter 1)

- **FR-09.1** WHEN Customer mở trang `/Loyalty`, THE hệ thống SHALL hiển thị: tổng điểm hiện tại, hạng hiện tại, số điểm còn thiếu để lên hạng kế tiếp (hoặc "Bạn đang ở hạng cao nhất" nếu đã đạt Kim cương), và danh sách lịch sử phiên chơi đã đóng của Customer (thời gian, mã bàn, tiền giờ, điểm của phiên đó), 10 dòng/trang, mới nhất trước.
- **FR-09.2** Điểm được **tính động** — không lưu bảng riêng. Mỗi `PlaySession` có `Status = 'Closed'` và `CustomerId` trùng với Customer đóng góp `FLOOR(PlaytimeAmount / 10000)` điểm. Tổng điểm = tổng tất cả phiên đã `Closed`. Không có hành động "cộng điểm" chủ động, không có bảng PointTransaction.
- **FR-09.3** Hạng thành viên được **tính động** từ tổng điểm theo hằng số cấu hình trong `LoyaltyConstants` (xem mục 5). Không có bảng MemberRank.
- **FR-09.4** Idempotency **tự bảo đảm bởi thiết kế**: mỗi phiên chỉ `Closed` một lần, tính tổng hợp từ nguồn không thể cộng trùng. Không cần xử lý phía server.
- **FR-09.5** WHERE Customer chưa có phiên chơi nào đã `Closed`, THE hệ thống SHALL hiển thị tổng điểm `0`, hạng `Đồng`, và thông báo "Chưa có lịch sử điểm."

### 3.4 [UC07] Khách gọi đồ ăn/uống (Iter 2)

- **FR-07.1** WHILE Customer có phiên chơi đang diễn ra, THE hệ thống SHALL cho phép truy cập trang `/Order` hiển thị thực đơn.
- **FR-07.2** WHERE Customer không có phiên chơi đang diễn ra, THE hệ thống SHALL ẩn thực đơn và hiển thị: "Bạn cần có phiên chơi đang diễn ra để gọi đồ."
- **FR-07.3** WHEN Customer mở thực đơn, THE hệ thống SHALL hiển thị các món đang kinh doanh, nhóm theo danh mục, mỗi món gồm: tên, đơn giá, tình trạng còn hàng. Món ngừng bán/hết hàng hiển thị mờ và không cho chọn.
- **FR-07.4** WHEN Customer thêm món vào giỏ, THE hệ thống SHALL yêu cầu số lượng là số nguyên từ **1 đến 20** cho mỗi món, và cập nhật tổng tạm tính.
- **FR-07.5** WHEN Customer nhấn "Gửi đơn", THE hệ thống SHALL:
  - Kiểm tra giỏ không rỗng
  - Kiểm tra phiên chơi vẫn đang diễn ra và thuộc về Customer
  - Kiểm tra từng món vẫn còn bán và còn hàng
  - Lưu đơn gắn với phiên chơi + bàn, trạng thái ban đầu "Chờ xử lý"
  - **Lưu đơn giá tại thời điểm đặt** (snapshot), không tham chiếu giá hiện tại về sau
  - Cộng tiền các món vào tổng tiền của phiên
  - Hiển thị: "Đã gửi đơn. Nhân viên sẽ phục vụ ngay."
- **FR-07.6** WHEN Customer mở mục "Đơn của tôi" trong phiên đang chơi, THE hệ thống SHALL hiển thị các đơn đã gọi của phiên đó với trạng thái hiện tại (chỉ xem).
- **FR-07.7** WHERE một hoặc nhiều món đã hết hàng/ngừng bán tại thời điểm gửi đơn, THE hệ thống SHALL từ chối **toàn bộ đơn**, giữ nguyên giỏ và hiển thị: "Món [tên món] hiện không còn. Vui lòng bỏ món này và gửi lại."

### 3.5 [UC17] Đánh giá dịch vụ sau chơi (Iter 3)

- **FR-17.1** WHERE phiên chơi đã hoàn tất/thanh toán và Customer chưa đánh giá phiên đó, THE hệ thống SHALL hiển thị nút "Đánh giá" ở danh sách và trang chi tiết phiên.
- **FR-17.2** WHEN Customer gửi đánh giá, THE hệ thống SHALL validate: số sao là số nguyên **1–5** (bắt buộc), nhận xét **tối đa 500 ký tự** (không bắt buộc).
- **FR-17.3** WHEN dữ liệu hợp lệ, THE hệ thống SHALL lưu: session_id, customer_id, số sao, nhận xét, thời điểm tạo; hiển thị: "Cảm ơn bạn đã đánh giá!"
- **FR-17.4** WHERE phiên đã được đánh giá, THE hệ thống SHALL thay nút "Đánh giá" bằng hiển thị chỉ-đọc số sao + nhận xét đã gửi, và từ chối mọi POST đánh giá mới cho phiên đó.
- **FR-17.5** WHERE phiên chưa hoàn tất hoặc không thuộc về Customer, THE hệ thống SHALL từ chối đánh giá (HTTP 403 hoặc 404, xem mục 6).

---

## 4. Non-functional Requirements

- **Hiệu năng:** Mỗi trang (Profile, PlayHistory, Loyalty, Order) trả về trong **< 1 giây (P95)** trên môi trường dev với SQL Server Express và Customer có tới 1,000 phiên chơi.
- **Phân trang:** Phân trang phía server (query `OFFSET/FETCH` hoặc tương đương), KHÔNG load toàn bộ bảng rồi cắt trong bộ nhớ.
- **Bảo mật — phân quyền:** Mọi action đều có `[Authorize(Roles = "Customer")]`; mọi truy vấn dữ liệu phải lọc theo `CustomerId` của user đang đăng nhập (chống IDOR).
- **Bảo mật — form:** Mọi POST có `[ValidateAntiForgeryToken]`; chống gửi đơn/đánh giá trùng do bấm 2 lần (disable nút sau lần nhấn đầu + chặn trùng phía server).
- **Bảo mật — dữ liệu:** Dùng truy vấn tham số hóa (không nối chuỗi SQL); output trong Razor được HTML-encode (không dùng `@Html.Raw` với dữ liệu người dùng).
- **Toàn vẹn dữ liệu:** Thao tác gửi đơn (FR-07.5) và cộng điểm (FR-09.2) chạy trong **1 transaction**; lỗi giữa chừng thì rollback toàn bộ.
- **Giao diện:** Hiển thị đúng ở chiều rộng **≥ 360px** (khách dùng điện thoại); dùng lại class CSS có sẵn, không thêm framework CSS/JS mới.
- **Logging:** Lỗi hệ thống ghi log qua `ILogger` kèm user id và request path; KHÔNG ghi mật khẩu hay dữ liệu nhạy cảm.

---

## 5. Data Model

> ✅ Mục này đã được đối chiếu với `BMS_Database_Starter/01_CreateDatabase.sql` (13 bảng) và `02_BusinessProcedures.sql` (5 stored procedures). Tên bảng/cột dưới đây là **tên chính thức** trong schema thực tế.

### 5.1 Bảng đã có — dùng trực tiếp

| Tên bảng thật | Dùng cho UC | Cột quan trọng |
|---|---|---|
| `dbo.AspNetUsers` | UC08 | `Id`, `FullName`, `UserName`, `Email`, `PhoneNumber`, `CreatedAtUtc`, `IsActive` |
| `dbo.PlaySessions` | UC10, UC09 | `Id`, `TableId`, `CustomerId`, `StartAtUtc`, `EndAtUtc`, `HourlyRateSnapshot`, `PlaytimeAmount`, `Status` (`'Active'`\|`'Closed'`) |
| `dbo.BilliardTables` | UC10 | `Id`, `TableCode`, `TableTypeId`, `Status` |
| `dbo.TableTypes` | UC10 | `Id`, `Name`, `HourlyRate` |
| `dbo.Products` | UC07 (Iter 2) | `Id`, `CategoryId`, `Name`, `Price`, `StockQuantity`, `IsActive` |
| `dbo.ProductCategories` | UC07 (Iter 2) | `Id`, `Name` |

### 5.2 Điểm & Hạng — tính động trong code (không có bảng)

**Không có bảng `MemberRank` hay `PointTransaction`** — tất cả tính từ `PlaySessions`.

**Công thức điểm:**
```
điểm_phiên = FLOOR(PlaytimeAmount / 10000)   -- 100,000 VND = 10 điểm
tổng_điểm  = SUM(điểm_phiên) WHERE CustomerId = @id AND Status = 'Closed'
```

**Ngưỡng hạng (hằng số trong `LoyaltyConstants.cs`, không đổi nếu không có yêu cầu mới):**

| Hạng | Điểm tối thiểu | Hiển thị |
|---|---|---|
| Đồng (Bronze) | 0 | Mặc định, kể cả khi chưa có điểm |
| Bạc (Silver) | 100 | — |
| Vàng (Gold) | 200 | — |
| Kim cương (Diamond) | 300 | Hạng cao nhất |

**Số điểm còn thiếu để lên hạng kế:**
- Đồng → `100 - tổng_điểm`
- Bạc → `200 - tổng_điểm`
- Vàng → `300 - tổng_điểm`
- Kim cương → hiển thị "Bạn đang ở hạng cao nhất"

### 5.3 Bảng cần tạo thêm (chưa có trong schema)

| Bảng | Dùng cho | Trạng thái |
|---|---|---|
| `dbo.SessionOrders` | UC07 Gọi đồ (Iter 2) | ⏳ Đề xuất bên dưới, chờ duyệt |
| `Review`/`Feedback` | UC17 Đánh giá (Iter 3) | ❌ Chưa thiết kế |

**Đề xuất `SessionOrders` (Iter 2 — chưa chạy, chờ đồng bộ với phần Staff):**
```sql
-- File: BMS_Database_Starter/05_AddSessionOrders.sql
CREATE TABLE dbo.SessionOrders (
    Id                int IDENTITY    NOT NULL CONSTRAINT PK_SessionOrders PRIMARY KEY,
    SessionId         int             NOT NULL,
    ProductId         int             NOT NULL,
    Quantity          int             NOT NULL,
    UnitPriceSnapshot decimal(18,2)  NOT NULL,  -- snapshot giá lúc đặt
    Status            varchar(20)    NOT NULL
                      CONSTRAINT DF_SO_Status DEFAULT 'Pending',
    CreatedAtUtc      datetime2(0)   NOT NULL
                      CONSTRAINT DF_SO_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_SO_Session  FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
    CONSTRAINT FK_SO_Product  FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT CK_SO_Qty      CHECK (Quantity BETWEEN 1 AND 20),
    CONSTRAINT CK_SO_Price    CHECK (UnitPriceSnapshot > 0),
    CONSTRAINT CK_SO_Status   CHECK (Status IN ('Pending','Preparing','Delivered','Cancelled'))
);
CREATE INDEX IX_SO_Session ON dbo.SessionOrders(SessionId);
```

### 5.4 Stored Procedures hiện có

| Tên SP | Dùng khi | Ghi chú |
|---|---|---|
| `usp_OpenSession` | Staff mở phiên | Yêu cầu `StaffId` + `TableId` |
| `usp_CloseSession` | Staff đóng phiên | Tính `PlaytimeAmount`, set `Status='Closed'` |
| `usp_CreateBooking` | Customer đặt bàn | — |
| `usp_CancelBooking` | Hủy booking | — |
| `usp_CheckInBooking` | Staff check-in | — |

**Quy tắc bắt buộc:**
- Nếu đã có stored procedure thì **gọi lại procedure đó**, không viết lại logic.
- Không tự `CREATE TABLE` / `ALTER TABLE` khi chưa có sự đồng ý.

---

## 6. Error Handling

- WHERE user chưa đăng nhập truy cập bất kỳ trang nào trong spec, THE hệ thống SHALL redirect về trang đăng nhập.
- WHERE user đã đăng nhập nhưng không phải role `Customer`, THE hệ thống SHALL hiển thị trang AccessDenied (HTTP 403).
- WHERE Customer truy cập dữ liệu (phiên chơi, đơn, đánh giá) không thuộc về mình hoặc không tồn tại, THE hệ thống SHALL trả về HTTP 404 và KHÔNG tiết lộ dữ liệu đó có tồn tại hay không.
- WHERE validate form thất bại, THE hệ thống SHALL hiển thị lại form, giữ nguyên dữ liệu đã nhập và hiển thị lỗi cạnh từng trường (không mất dữ liệu người dùng).
- WHERE Customer gửi đơn khi phiên đã kết thúc, THE hệ thống SHALL từ chối và hiển thị: "Phiên chơi đã kết thúc, không thể gọi thêm đồ."
- WHERE Customer gửi đánh giá lần thứ hai cho cùng một phiên, THE hệ thống SHALL từ chối và hiển thị: "Bạn đã đánh giá phiên này rồi."
- WHERE Customer đánh giá phiên chưa hoàn tất, THE hệ thống SHALL từ chối và hiển thị: "Chỉ có thể đánh giá sau khi phiên chơi kết thúc."
- WHERE kết nối database thất bại hoặc xảy ra exception không lường trước, THE hệ thống SHALL ghi log lỗi, rollback transaction đang mở và hiển thị trang lỗi thân thiện: "Hệ thống đang bận. Vui lòng thử lại sau." (KHÔNG hiển thị stack trace/chuỗi kết nối).
- WHERE cộng điểm được gọi lại cho phiên đã được cộng, THE hệ thống SHALL bỏ qua (không cộng trùng) và không báo lỗi cho Customer.
- WHERE POST thiếu hoặc sai anti-forgery token, THE hệ thống SHALL từ chối request (HTTP 400).

---

## 7. Acceptance Criteria

> Định dạng Given–When–Then. Mỗi dòng = 1 test case.

### UC08 — Hồ sơ cá nhân
- [ ] **Given** Customer đã đăng nhập, **When** mở `/Profile`, **Then** thấy đúng họ tên, email, SĐT, hạng, điểm của chính mình
- [ ] **Given** đang sửa hồ sơ, **When** để trống họ tên, **Then** bị từ chối và hiện lỗi tại trường họ tên
- [ ] **Given** đang sửa hồ sơ, **When** nhập SĐT `012345` (không đủ 10 số), **Then** bị từ chối
- [ ] **Given** đang sửa hồ sơ, **When** nhập SĐT `0912345678`, **Then** được chấp nhận
- [ ] **Given** email `a@x.com` thuộc tài khoản khác, **When** Customer lưu email này, **Then** bị từ chối với "Email đã được sử dụng."
- [ ] **Given** dữ liệu hợp lệ, **When** nhấn Lưu, **Then** thông báo thành công và mở lại `/Profile` thấy dữ liệu mới
- [ ] **Given** form hồ sơ, **When** sửa trường `username` bằng DevTools rồi gửi, **Then** username trong database KHÔNG đổi

### UC10 — Lịch sử phiên chơi
- [x] **Given** Customer có 25 phiên, **When** mở `/PlayHistory`, **Then** thấy 10 phiên mới nhất ở trang 1 và có 3 trang
- [x] **Given** Customer A, **When** mở chi tiết phiên của Customer B, **Then** nhận HTTP 404
- [x] **Given** Customer chưa có phiên nào, **When** mở `/PlayHistory`, **Then** thấy "Bạn chưa có phiên chơi nào."
- [x] **Given** phiên đang chơi, **When** xem danh sách, **Then** giờ kết thúc là "—" và có badge "Đang chơi"
- [x] **Given** phiên có 2 món đã gọi, **When** xem chi tiết, **Then** tổng tiền = tiền giờ chơi + tổng thành tiền các món

### UC09/15 — Điểm thưởng & Hạng
- [x] **Given** Customer có điểm X, **When** mở `/Loyalty`, **Then** thấy đúng X, đúng hạng theo ngưỡng, đúng số điểm còn thiếu để lên hạng kế
- [x] **Given** Customer ở hạng cao nhất, **When** mở `/Loyalty`, **Then** thấy "Bạn đang ở hạng cao nhất"
- [x] **Given** một phiên vừa chuyển sang hoàn tất, **When** hệ thống xử lý, **Then** điểm được cộng đúng 1 lần và xuất hiện 1 dòng trong lịch sử điểm
- [x] **Given** phiên đã được cộng điểm, **When** hàm cộng điểm chạy lại, **Then** tổng điểm không đổi
- [x] **Given** điểm vượt ngưỡng hạng kế, **When** cộng điểm xong, **Then** hạng tự động nâng

### UC07 — Gọi đồ
- [x] **Given** Customer không có phiên đang chơi, **When** mở `/Order`, **Then** không thấy thực đơn, thấy thông báo yêu cầu có phiên đang chơi
- [x] **Given** có phiên đang chơi, **When** mở `/Order`, **Then** thấy các món đang bán theo danh mục, món hết hàng bị mờ và không chọn được
- [x] **Given** thêm món, **When** nhập số lượng 0 hoặc 21, **Then** bị từ chối; số lượng 1 và 20 được chấp nhận
- [x] **Given** giỏ rỗng, **When** nhấn "Gửi đơn", **Then** bị từ chối
- [x] **Given** giỏ hợp lệ, **When** gửi đơn, **Then** đơn có trạng thái "Chờ xử lý", gắn đúng phiên + bàn, tổng tiền phiên tăng đúng bằng tổng đơn
- [x] **Given** đơn đã gửi với giá 20,000 ₫, **When** Admin đổi giá món thành 25,000 ₫, **Then** đơn cũ vẫn hiển thị 20,000 ₫
- [x] **Given** giỏ có 1 món vừa hết hàng, **When** gửi đơn, **Then** toàn bộ đơn bị từ chối và giỏ được giữ nguyên
- [x] **Given** phiên đã kết thúc, **When** gửi đơn, **Then** bị từ chối với "Phiên chơi đã kết thúc, không thể gọi thêm đồ."
- [x] **Given** gửi đơn gặp lỗi giữa chừng, **When** transaction rollback, **Then** không có đơn nào được lưu và tổng tiền phiên không đổi
- [x] **Given** nhấn "Gửi đơn" 2 lần liên tiếp, **When** server xử lý, **Then** chỉ tạo 1 đơn


### UC17 — Đánh giá
- [ ] **Given** phiên đã hoàn tất, chưa đánh giá, **When** xem lịch sử, **Then** thấy nút "Đánh giá"
- [ ] **Given** form đánh giá, **When** chọn 0 hoặc 6 sao, **Then** bị từ chối; 1 và 5 sao được chấp nhận
- [ ] **Given** form đánh giá, **When** nhận xét 501 ký tự, **Then** bị từ chối; 500 ký tự được chấp nhận; để trống cũng được chấp nhận
- [ ] **Given** đánh giá hợp lệ, **When** gửi, **Then** lưu thành công và nút "Đánh giá" được thay bằng nội dung đánh giá chỉ-đọc
- [ ] **Given** phiên đã đánh giá, **When** POST đánh giá lần 2, **Then** bị từ chối và dữ liệu cũ không đổi
- [ ] **Given** phiên đang chơi, **When** POST đánh giá, **Then** bị từ chối
- [ ] **Given** phiên của Customer B, **When** Customer A POST đánh giá, **Then** bị từ chối (403/404)

### Chung
- [ ] **Given** chưa đăng nhập, **When** truy cập `/Profile`, `/PlayHistory`, `/Loyalty`, `/Order`, **Then** bị redirect về trang login
- [ ] **Given** đăng nhập bằng role Staff hoặc Admin, **When** truy cập các trang trên, **Then** nhận AccessDenied (403)
- [ ] **Given** POST không có anti-forgery token, **When** gửi request, **Then** bị từ chối (400)
- [ ] **Given** màn hình rộng 360px, **When** mở từng trang, **Then** không bị tràn ngang/vỡ layout
- [ ] **Given** toàn bộ code mới, **When** chạy `dotnet build` và `dotnet test`, **Then** build không lỗi và toàn bộ test pass

---

## 8. Out of Scope

### KHÔNG thực hiện trong đợt này:
- **UC14 — Thanh toán online VNPay/SePay (làm sau, Iter 3):**
  - KHÔNG tích hợp bất kỳ cổng thanh toán nào (VNPay, SePay, hoặc cổng khác)
  - KHÔNG tạo controller/service/callback/IPN/webhook cho thanh toán online
  - KHÔNG thêm cấu hình, key, URL của cổng thanh toán vào `appsettings*.json` hay user-secrets
  - KHÔNG hiển thị nút/lựa chọn "Thanh toán online" trên giao diện
  - KHÔNG thêm bảng/cột lưu giao dịch online
- Thanh toán tại quầy do Staff thực hiện (phiên chuyển sang "hoàn tất/đã thanh toán" qua luồng hiện có) — spec này chỉ **phản ứng** khi trạng thái đó xảy ra
- Đăng ký, đăng nhập, quên/đổi mật khẩu
- Upload ảnh đại diện
- Đổi điểm lấy quà / voucher / giảm giá (redeem)
- Customer hủy hoặc sửa đơn gọi đồ sau khi đã gửi
- Sửa hoặc xóa đánh giá sau khi đã gửi
- Màn hình Staff xử lý đơn gọi đồ; màn hình Admin xem/trả lời đánh giá
- Đặt bàn trước (booking), thông báo push/email/SMS
- Lọc/tìm kiếm nâng cao trong lịch sử (chỉ phân trang)
- Đa ngôn ngữ (chỉ tiếng Việt)

### Lý do loại trừ (giúp AI hiểu context):
- VNPay/SePay: cần tài khoản sandbox và cấu hình riêng, tách thành đợt sau để tránh phụ thuộc bên ngoài
- Staff/Admin screens: khác actor, thuộc phần việc của thành viên khác

### Boundary conditions — AI không được tự quyết:
- KHÔNG sửa các file `BMS_Database_Starter/*.sql`, KHÔNG tự thêm migration / `CREATE TABLE` / `ALTER TABLE`
- KHÔNG đổi cơ chế xác thực, cookie, cấu trúc claims hiện tại
- KHÔNG sửa `_AdminLayout` và CSS dùng chung (chỉ **dùng lại**); nếu cần style riêng, đặt vào file riêng của feature
- KHÔNG commit `appsettings.Development.json`, `bin/`, `obj/`
- KHÔNG thêm NuGet package mới nếu chưa hỏi lại
- KHÔNG tự thêm tính năng ngoài danh sách FR ở mục 3

---

## Notes / Open Questions

> ✅ **Tất cả Open Questions đã được chốt (2026-10-13).** Mục này giữ lại làm lịch sử quyết định.

1. **Layout cho Customer:** ✅ **Dùng `_AdminLayout`**, thêm section menu "Khách hàng" vào sidebar với role-check. Không tạo layout riêng.
2. **Quy tắc điểm & hạng:** ✅ **100,000 VND = 10 điểm** (công thức: `FLOOR(PlaytimeAmount / 10000)`). Hạng: Đồng (0) / Bạc (100) / Vàng (200) / Kim cương (300 — hạng cao nhất). Hằng số đặt trong `LoyaltyConstants.cs`.
3. **Thời điểm tính điểm:** ✅ Điểm tính **động** từ `PlaySessions` có `Status = 'Closed'`. Không có bước ghi nhận event. Không cần xử lý timing.
4. **Tên trạng thái phiên:** ✅ **`'Active'`** (đang chơi) / **`'Closed'`** (Staff đóng phiên). Không có `'Paid'`/`'Completed'` ở bảng `PlaySessions`. Bảng `BilliardTables` mới có `'AwaitingPayment'`.
5. **Bảng còn thiếu:** ✅ `MemberRank` và `PointTransaction` **không tạo** — tính động trong code. `SessionOrders` đề xuất ở mục 5.3 cho Iter 2 (chờ duyệt). `Review` thiết kế sau cho Iter 3.
6. **Cách truy cập dữ liệu:** ✅ **EF Core** với `ApplicationDbContext` (`Bms.Web.Data`). Bảng nghiệp vụ (`PlaySessions`, `BilliardTables`...) cần thêm EF mapping hoặc dùng raw SQL — theo pattern đang có trong project.
7. **Phạm vi UC14:** ✅ Cả VNPay lẫn SePay đều Out of Scope đợt này.
8. **Tài khoản test:** ✅ `customer.demo01` / `customer.demo02` có trong demo data, cần Admin set password qua `/Employee` trước khi test.
