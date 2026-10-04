# Phạm vi của Doan: Quản lý phiên chơi (Staff)

> Đặt file này tại `docs/sds-doan/SCOPE.md`.
> Mục đích: cho AI agent (và đồng đội) biết rõ phần việc của Doan gồm gì, không gồm gì, và nối với phần của người khác ở đâu.
> Quy ước: điều gì chưa có trong file này hoặc `DB_NOTES.md` thì agent phải ghi "CẦN XÁC NHẬN", không tự quyết.

## 1. Mô tả ngắn

Xây dựng **một màn hình duy nhất: Table Management Screen** (SRS mục 1.1.1) cho Staff (Admin cũng
được dùng). Bố cục hai cột theo wireframe: cột trái là sơ đồ bàn, cột phải là chi tiết phiên chơi
của bàn đang chọn. Wireframe lưu tại `docs/sds-doan/wireframe-table-management.png`.
Giao diện phải theo đúng bố cục trong ảnh này.

## 2. Phần việc GỒM

| # | Chức năng | Ghi chú |
|---|---|---|
| 1 | Danh sách bàn dạng thẻ (cột trái): tên bàn, trạng thái, giờ chơi | Đồng hồ chạy trên bàn đang chơi |
| 2 | Chọn một bàn để xem chi tiết (cột phải) | Table Name, Status, Start Time, End Time, Duration |
| 3 | Hiển thị Customer, Booking Time | Hiện `--` nếu không có; hiện tên khách nếu phiên có `CustomerId` |
| 4 | Nút OPEN TABLE | Gọi `usp_OpenSession`; chỉ bật khi bàn `Available` |
| 5 | Nút CLOSE TABLE | Gọi `usp_CloseSession`; chỉ bật khi bàn `InUse`; hiện tiền giờ sau khi đóng |
| 6 | Người thao tác | Tên Staff đang đăng nhập ở thanh trên; `OpenedById` / `ClosedById` lấy từ Identity |
| 7 | Phân quyền | Chỉ Staff và Admin |

## 3. Phần việc KHÔNG GỒM

### 3.1. Có trên wireframe nhưng KHÔNG viết logic

| Thành phần | Xử lý |
|---|---|
| Nút CONFIRM BOOKING | Hiện nhưng disabled. Check-in booking thuộc Thành viên 2 |
| Nút TRANSFER | Hiện nhưng disabled. DB chưa có procedure, UC23 ngoài phạm vi |
| Nút SPLIT/MERGE | Hiện nhưng disabled. Ngoài phạm vi |

### 3.2. Hoàn toàn ngoài phạm vi

| Chức năng | Use case / mục SRS | Người phụ trách |
|---|---|---|
| Đăng nhập, đăng ký, đổi mật khẩu | UC01, UC02, UC03 | Thành viên 5 (Hung), đã xong |
| Quản lý nhân viên và tài khoản | UC43, UC44 | Thành viên 5 (Hung), đã xong |
| Đặt bàn trước, check-in booking, hủy đặt | UC05, UC11, UC12, UC25, UC26 | Thành viên 2 |
| Customer, hóa đơn, thanh toán (VNPay), điểm thưởng | UC13, UC14, UC27 đến UC29, mục 1.1.3 | Thành viên 3 |
| Thực đơn, tồn kho, combo, gọi món | UC07, UC30, UC41, UC42, mục 1.1.4 | Thành viên 4 |
| Quản lý bàn (thêm / sửa / xóa) và đổi trạng thái cấp Admin | UC36 đến UC40 | Chưa xác định, cần hỏi nhóm |
| Ca làm việc, báo cáo doanh thu | UC34, UC45, UC46, UC47 | Chưa xác định |
| Màn hình 1.1.2 (sơ đồ cho Customer, bộ lọc, popover) | mục 1.1.2 | Không làm |
| Gửi lệnh tắt / bật thiết bị (đèn bàn) khi đóng phiên | mục 1.1.1 | Chưa có thiết bị, bỏ qua |

## 4. Điểm nối với phần của người khác

- **Đóng phiên → thanh toán.** Sau khi đóng phiên, phiên có trạng thái `Closed` và bàn chuyển sang `AwaitingPayment`. `usp_CloseSession` **không** tạo Invoice. Việc tạo hóa đơn và chuyển bàn về `Available` sau thanh toán thuộc Thành viên 3. Phần của Doan chỉ hiển thị đúng trạng thái.
- **Bàn có booking.** Bàn đã check-in booking nhưng chưa mở (ví dụ B06 trong dữ liệu mẫu) vẫn có `Status = Available`. Nếu mở kiểu vãng lai, `usp_OpenSession` ném lỗi **51407**. Code phải dịch lỗi này thành thông báo rõ ràng và hướng dẫn nhân viên dùng chức năng đặt bàn.
- **Danh tính nhân viên.** Dùng ASP.NET Core Identity có sẵn của Hung. Lấy ID từ người dùng đang đăng nhập, không nhận từ client.
- **File dùng chung được phép sửa tối thiểu** (chỉ thêm dòng, không đổi dòng cũ): `Program.cs` (đăng ký service), `Data/ApplicationDbContext.cs` (thêm `DbSet`). Phải báo trưởng nhóm trước, vì Thành viên 2, 3, 4 cũng sẽ sửa hai file này.

## 5. Phụ thuộc vào cơ sở dữ liệu (đã có sẵn, không sửa)

- Bảng: `TableTypes`, `BilliardTables`, `PlaySessions`.
- Stored procedure: `usp_OpenSession`, `usp_CloseSession`.
- Hướng SQL-first: không tạo EF migration, không sửa file `.sql`.
- Chi tiết cột, tham số, mã lỗi, dữ liệu mẫu: xem `DB_NOTES.md` (nguồn sự thật về DB).

## 6. Ánh xạ trạng thái bàn (DB → giao diện)

Wireframe chỉ có vài nhãn, còn DB có 5 trạng thái. Hiển thị theo bảng này:

| Status trong DB | Nhãn hiển thị | Màu đề xuất | OPEN TABLE | CLOSE TABLE |
|---|---|---|:---:|:---:|
| `Available` | Trống | Xanh lá | Bật | Tắt |
| `InUse` | Đang chơi | Đỏ | Tắt | Bật |
| `AwaitingPayment` | Chờ thanh toán | Vàng | Tắt | Tắt |
| `Maintenance` | Bảo trì | Xám | Tắt | Tắt |
| `Inactive` | Ngừng hoạt động | Xám đậm | Tắt | Tắt |

Nhãn "Đã đặt" trong wireframe: DB không có status `Reserved`. Xem quyết định ở mục 7.

## 7. Quyết định với nhóm

### 7.1. Đã chốt

- [x] Chỉ làm màn hình Table Management (mục 1.1.1), không làm mục 1.1.2.
- [x] Tiền giờ do `usp_CloseSession` tính: theo từng giây thực tế, làm tròn về đồng nguyên (`ROUND(..., 0)`), **không** có phí tối thiểu. Không viết lại bằng C#.
- [x] Hóa đơn: không phải việc của Doan. `usp_CloseSession` không tạo Invoice (Thành viên 3 làm).
- [x] Chuyển bàn từ `AwaitingPayment` về `Available`: do phân hệ thanh toán (Thành viên 3).
- [x] OPEN TABLE trong phiên bản đầu: mở bàn **vãng lai** (không truyền `BookingId`, `CustomerId`).
- [x] Chuyển / Tách / Gộp bàn và Confirm Booking: không làm logic ở phiên bản này.
- [ ] - [x] Trang đích Staff: Phương án A. StaffController.Index() chuyển hướng sang TableController.Index()
      (chỉ đổi 1 dòng return). Không sửa AccountController.

### 7.2. Còn mở, cần hỏi nhóm

- [ ] Ba nút chưa làm: hiện disabled kèm tooltip "Chưa hỗ trợ" (đề xuất) hay ẩn đi?
- [ ] Mở bàn theo booking đã check-in (có `BookingId`, như bàn mẫu B06): Doan làm hay Thành viên 2 gọi procedure từ màn hình đặt bàn của họ?
- [ ] Hiển thị "Đã đặt": có lấy từ bảng `Bookings` không? Nếu chưa, bàn chỉ hiện theo 5 status ở mục 6.
- [ ] Thời gian thực: tự làm mới định kỳ (polling, đề xuất cho phiên bản đầu) hay SignalR?
- [ ] Ai phụ trách Quản lý bàn cấp Admin (UC36 đến UC40)?

## 8. Tiêu chí hoàn thành

- Staff đăng nhập thấy danh sách bàn đúng trạng thái trong database, bố cục theo wireframe.
- Bấm vào một bàn thì cột phải hiện đúng chi tiết bàn và phiên.
- Mở bàn thành công thì bàn chuyển sang `InUse`, đồng hồ chạy.
- Hai nhân viên mở cùng một bàn: chỉ một người thành công, người kia nhận thông báo tiếng Việt.
- Đóng phiên hiển thị đúng số tiền giờ, bàn chuyển sang `AwaitingPayment`.
- Mọi lỗi từ procedure (mã 51401 đến 51408, 51501, 51502) được dịch thành tiếng Việt.
- Customer hoặc người chưa đăng nhập không thao tác được.
- `dotnet build` không lỗi, không sửa file của người khác ngoài các dòng đã thỏa thuận.