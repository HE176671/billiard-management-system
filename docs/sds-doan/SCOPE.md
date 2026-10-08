# Phạm vi của Doan: Quản lý phiên chơi (Staff)

> Đặt file này tại `docs/sds-doan/SCOPE.md`.
> Mục đích: cho AI agent (và đồng đội) biết rõ phần việc của Doan gồm gì, không gồm gì, và nối với phần của người khác ở đâu.
> Quy ước: điều gì chưa có trong file này hoặc `DB_NOTES.md` thì agent phải ghi "CẦN XÁC NHẬN", không tự quyết.

## 1. Mô tả ngắn

Xây dựng **một màn hình duy nhất: Table Management Screen** (SRS mục 1.1.1) cho Staff (Admin cũng
được dùng). Bố cục hai cột theo wireframe: cột trái là sơ đồ bàn, cột phải là chi tiết phiên chơi
của bàn đang chọn. Wireframe lưu tại `docs/sds-doan/wireframe-table-management.png`.
Giao diện phải theo đúng bố cục trong ảnh này (V2 chỉ khác ở chỗ bỏ nút SPLIT/MERGE, xem mục 3.1).

**Nâng cấp V2** (nguồn quyết định: `PLAN_V2.md`; nếu file này mâu thuẫn với `PLAN_V2.md` về V2 thì `PLAN_V2.md` thắng). Cùng màn hình này bổ sung:

- Hai hình thức mở bàn: `Open` (không giới hạn) và `Timed` (đăng ký số phút).
- Đếm ngược và cảnh báo cho phiên `Timed` (cảnh báo khi còn 10 phút, báo "Hết giờ", không tự đóng bàn) và gia hạn.
- Chuyển bàn: giữ nguyên phiên và giờ chơi, lưu từng đoạn bàn để tính đúng khi khác giá.
- Tính tiền theo block 15 phút (tối thiểu 1 block), tiền tạm tính và lịch sử bàn.
- Bỏ nút SPLIT/MERGE; CONFIRM BOOKING vẫn disabled.

## 2. Phần việc GỒM

| # | Chức năng | Ghi chú |
|---|---|---|
| 1 | Danh sách bàn dạng thẻ (cột trái): tên bàn, trạng thái, giờ chơi | Đồng hồ chạy trên bàn đang chơi |
| 2 | Chọn một bàn để xem chi tiết (cột phải) | Table Name, Status, Start Time, End Time, Duration. V2 thêm: hình thức, giờ tính tiền, giờ dự kiến kết thúc, tiền tạm tính, lịch sử bàn |
| 3 | Hiển thị Customer, Booking Time | Hiện `--` nếu không có; hiện tên khách nếu phiên có `CustomerId` |
| 4 | Nút OPEN TABLE | Gọi `usp_OpenSession`; chỉ bật khi bàn `Available`; V2 có chọn hình thức mở (dòng 8) |
| 5 | Nút CLOSE TABLE | Gọi `usp_CloseSession`; chỉ bật khi bàn `InUse`; hiện tiền giờ sau khi đóng (V2: tính theo block 15 phút, dòng 12) |
| 6 | Người thao tác | Tên Staff đang đăng nhập ở thanh trên; `OpenedById` / `ClosedById` lấy từ Identity |
| 7 | Phân quyền | Chỉ Staff và Admin |
| 8 | **V2:** Hai hình thức mở bàn | `Open` (không giới hạn) và `Timed` (đăng ký 30, 60, 90, 120 phút hoặc tự nhập bội số của 15, từ 15 đến 720). Form mở bàn có xem trước "Tính giờ từ ..." |
| 9 | **V2:** Đếm ngược và cảnh báo | Phiên `Timed`: thẻ bàn đếm ngược, còn 10 phút thì đổi màu và toast cảnh báo một lần, hết giờ thì báo "Hết giờ" và đếm thời gian quá hạn. KHÔNG tự đóng bàn |
| 10 | **V2:** Nút GIA HẠN | Chỉ phiên `Timed` đang `Active`; gọi `usp_ExtendSession`, thêm bội số 15 phút (15 đến 240) |
| 11 | **V2:** Nút CHUYỂN BÀN | Gọi `usp_TransferSession`; giữ nguyên phiên, không reset giờ, lưu từng đoạn bàn để tính đúng khi khác giá; bàn cũ về `Available` |
| 12 | **V2:** Tính tiền theo block 15 phút | Làm tròn LÊN mốc 15 phút ở giờ bắt đầu và giờ kết thúc tính tiền, tối thiểu 1 block. Chỉ tính trên SQL Server |
| 13 | **V2:** Tiền tạm tính | Hiển thị khi phiên đang chạy, gọi cùng hàm SQL với lúc đóng bàn, không tính ở JavaScript |
| 14 | **V2:** Lịch sử bàn | Panel chi tiết liệt kê các đoạn bàn của phiên (bàn nào, từ giờ nào đến giờ nào) |

## 3. Phần việc KHÔNG GỒM

### 3.1. Có trên wireframe nhưng KHÔNG viết logic

| Thành phần | Xử lý |
|---|---|
| Nút CONFIRM BOOKING | Vẫn hiện nhưng disabled. Check-in booking thuộc Thành viên 2 |

**Thay đổi ở V2 (xem `PLAN_V2.md`):**

- Nút SPLIT/MERGE: **đã loại bỏ khỏi giao diện**, không còn là nút disabled. Gộp/tách bàn không làm.
- Nút TRANSFER: **không còn nằm ở mục này**, đã chuyển sang phần CÓ làm (mục 2, chức năng CHUYỂN BÀN).

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
| Gộp bàn, tách bàn (SPLIT/MERGE) | Wireframe mục 1.1.1; `PLAN_V2.md` mục 1 | Không làm (V2). Nút đã bị loại bỏ khỏi giao diện |
| Gói giới hạn cứng (tự đóng bàn khi hết gói) | `PLAN_V2.md` mục 1 (D4) | Không làm (V2), chỉ chừa chỗ để thêm sau. Xem mục 7.2 |
| Trạng thái "dọn dẹp" sau chuyển bàn | `PLAN_V2.md` mục 1 (D7) | Không làm (V2): bàn cũ về `Available` ngay. Xem mục 7.2 |

## 4. Điểm nối với phần của người khác

- **Đóng phiên → thanh toán.** Sau khi đóng phiên, phiên có trạng thái `Closed` và bàn chuyển sang `AwaitingPayment`. `usp_CloseSession` **không** tạo Invoice. Việc tạo hóa đơn và chuyển bàn về `Available` sau thanh toán thuộc Thành viên 3. Phần của Doan chỉ hiển thị đúng trạng thái.
- **Bàn có booking.** Bàn đã check-in booking nhưng chưa mở (ví dụ B06 trong dữ liệu mẫu) vẫn có `Status = Available`. Nếu mở kiểu vãng lai, `usp_OpenSession` ném lỗi **51407**. Code phải dịch lỗi này thành thông báo rõ ràng và hướng dẫn nhân viên dùng chức năng đặt bàn.
- **`PlaytimeAmount` (V2).** Vẫn là tổng tiền giờ của cả phiên (cộng các đoạn bàn nếu có chuyển bàn), nhưng nay tính **theo block 15 phút, tối thiểu 1 block** thay vì theo từng giây. Thành viên 3 đọc giá trị này để lập hóa đơn và cần được báo về thay đổi cách tính (xem `PLAN_V2.md` mục 3 và mục 7).
- **Chuyển bàn và booking giữ chỗ (V2).** `usp_TransferSession` dùng đúng điều kiện booking giữ chỗ của `usp_OpenSession` (lỗi 51407); bàn đích đang giữ chỗ cho booking thì bị từ chối bằng lỗi **51615**. Code phải dịch lỗi này thành thông báo tiếng Việt rõ ràng.
- **Đưa bàn về `Available` sau thanh toán (V2 không đổi).** Vẫn thuộc Thành viên 3. Doan chỉ đưa bàn cũ về `Available` khi chuyển bàn (D7), không đụng tới bàn đang `AwaitingPayment`.
- **Danh tính nhân viên.** Dùng ASP.NET Core Identity có sẵn của Hung. Lấy ID từ người dùng đang đăng nhập, không nhận từ client.
- **File dùng chung được phép sửa tối thiểu** (chỉ thêm dòng, không đổi dòng cũ): `Program.cs` (đăng ký service), `Data/ApplicationDbContext.cs` (thêm `DbSet`). Phải báo trưởng nhóm trước, vì Thành viên 2, 3, 4 cũng sẽ sửa hai file này.

## 5. Phụ thuộc vào cơ sở dữ liệu (V1 có sẵn; V2 thêm file 06 mới, không sửa file `.sql` cũ)

- Bảng có sẵn: `TableTypes`, `BilliardTables`, `PlaySessions`.
- Stored procedure có sẵn: `usp_OpenSession`, `usp_CloseSession` (V2 sửa bằng file 06, giữ nguyên tham số cũ và mã lỗi cũ, chỉ thêm tham số tùy chọn).
- Hướng SQL-first: không tạo EF migration, không sửa file `.sql` cũ.
- Chi tiết cột, tham số, mã lỗi, dữ liệu mẫu của V1: xem `DB_NOTES.md` (nguồn sự thật về DB V1).

**V2: file mới `BMS_Database_Starter/06_PlaySessionEnhancements.sql`** (chạy lại nhiều lần không lỗi). Chi tiết thiết kế: xem `PLAN_V2.md` mục 4 (nếu mâu thuẫn, `PLAN_V2.md` thắng). Gồm:

- Cột mới của `PlaySessions`: `SessionMode`, `PlannedEndAtUtc`, `BillingStartAtUtc`, `BillingEndAtUtc`.
- Bảng mới: `PlaySessionTableSegments` (các đoạn bàn của một phiên, phục vụ chuyển bàn và lịch sử bàn).
- Hàm mới: `dbo.fn_CeilTo15Min`, `dbo.fn_CalcPlaytimeAmount`.
- Procedure sửa: `usp_OpenSession` (thêm `@SessionMode`, `@PlannedMinutes`), `usp_CloseSession` (tính theo block, thêm cột ở cuối result set).
- Procedure mới: `usp_ExtendSession`, `usp_TransferSession`.
- Mã lỗi mới: 51409, 51410 (mở bàn); 51601 đến 51603 (gia hạn); 51611 đến 51615 (chuyển bàn).
- Cần chạy file 06 trên database của từng thành viên trước khi chạy code V2 (xem `PLAN_V2.md` mục 7).

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
- [x] Tiền giờ do SQL Server tính (`usp_CloseSession` gọi `dbo.fn_CalcPlaytimeAmount`): **V2 tính theo block 15 phút**, làm tròn về đồng nguyên (`ROUND(..., 0)`), **tối thiểu 1 block** (D1, D2). Không viết lại bằng C# hay JavaScript. (Thay quy tắc V1 "theo từng giây, không có phí tối thiểu".)
- [x] Hóa đơn: không phải việc của Doan. `usp_CloseSession` không tạo Invoice (Thành viên 3 làm).
- [x] Chuyển bàn từ `AwaitingPayment` về `Available`: do phân hệ thanh toán (Thành viên 3).
- [x] OPEN TABLE trong phiên bản đầu: mở bàn **vãng lai** (không truyền `BookingId`, `CustomerId`).
- [x] Gộp / Tách bàn và logic Confirm Booking: không làm. Nút SPLIT/MERGE đã bị loại bỏ khỏi giao diện; CONFIRM BOOKING vẫn disabled. **Chuyển bàn: CÓ làm ở V2** (xem mục 2 và `PLAN_V2.md`).
- [ ] - [x] Trang đích Staff: Phương án A. StaffController.Index() chuyển hướng sang TableController.Index()
      (chỉ đổi 1 dòng return). Không sửa AccountController.

Các quyết định V2 dưới đây trích ngắn từ `PLAN_V2.md` mục 2 (nếu mâu thuẫn, `PLAN_V2.md` thắng):

- [x] **D1:** giờ đóng làm tròn LÊN mốc 15 phút (giờ đã đúng mốc thì giữ nguyên).
- [x] **D2:** thời gian tính tiền bằng 0 thì tính tối thiểu 1 block theo đơn giá của đoạn bàn cuối cùng.
- [x] **D3:** phiên `Timed` tính tiền theo thời gian thực chơi tính theo block; giờ dự kiến kết thúc chỉ để đếm ngược và nhắc.
- [x] **D4:** gói giới hạn cứng: không làm, chỉ chừa chỗ để thêm sau.
- [x] **D5:** còn 10 phút thì cảnh báo; hết giờ thì báo "Hết giờ" và đếm thời gian quá hạn; KHÔNG tự đóng bàn.
- [x] **D6:** giờ chuyển bàn làm tròn LÊN mốc 15 phút khi tính tiền (giờ thực vẫn lưu nguyên).
- [x] **D7:** bàn cũ sau khi chuyển về `Available`.
- [x] **D8:** "Đăng ký thời gian" chọn 30, 60, 90, 120 phút hoặc tự nhập (bội số của 15, từ 15 đến 720).

### 7.2. Còn mở, cần hỏi nhóm

- [ ] Nút CONFIRM BOOKING: giữ disabled kèm tooltip "Chưa hỗ trợ" (đề xuất) hay ẩn đi? (V2: SPLIT/MERGE đã bị loại bỏ khỏi giao diện, không còn là nút; TRANSFER đã chuyển sang phần có làm, xem mục 2 và mục 3.)
- [ ] Gói giới hạn cứng (tự đóng bàn khi hết gói): V2 không làm (D4), chỉ chừa chỗ để thêm sau. Có làm ở bản sau không? CẦN XÁC NHẬN.
- [ ] Gói Combo của Thành viên 4: ngoài phạm vi V2. Cách nối với phiên chơi và tiền giờ: CẦN XÁC NHẬN với Thành viên 4.
- [ ] Trạng thái "dọn dẹp" sau chuyển bàn: V2 không làm, bàn cũ về `Available` ngay (D7). Có thêm trạng thái này ở bản sau không? CẦN XÁC NHẬN.
- [ ] Mở bàn theo booking đã check-in (có `BookingId`, như bàn mẫu B06): Doan làm hay Thành viên 2 gọi procedure từ màn hình đặt bàn của họ?
- [ ] Hiển thị "Đã đặt": có lấy từ bảng `Bookings` không? Nếu chưa, bàn chỉ hiện theo 5 status ở mục 6.
- [ ] Thời gian thực: tự làm mới định kỳ (polling, đề xuất cho phiên bản đầu) hay SignalR?
- [ ] Ai phụ trách Quản lý bàn cấp Admin (UC36 đến UC40)?

## 8. Tiêu chí hoàn thành

- Staff đăng nhập thấy danh sách bàn đúng trạng thái trong database, bố cục theo wireframe.
- Bấm vào một bàn thì cột phải hiện đúng chi tiết bàn và phiên.
- Mở bàn thành công thì bàn chuyển sang `InUse`, đồng hồ chạy.
- Hai nhân viên mở cùng một bàn: chỉ một người thành công, người kia nhận thông báo tiếng Việt.
- Đóng phiên hiển thị đúng số tiền giờ **tính theo block 15 phút** (khớp các ví dụ ở `PLAN_V2.md` mục 3), bàn chuyển sang `AwaitingPayment`.
- Mọi lỗi từ procedure (mã 51401 đến 51408, 51501, 51502 và các mã V2: 51409, 51410, 51601 đến 51603, 51611 đến 51615) được dịch thành tiếng Việt.
- **V2, làm tròn block:** giờ bắt đầu và giờ kết thúc tính tiền đều làm tròn LÊN mốc 15 phút (giờ đã đúng mốc thì giữ nguyên); tiền đoạn làm tròn đồng nguyên. Ví dụ Pool 100.000 ₫/giờ: mở 08:50, đóng 10:07 thì tính 09:00 đến 10:15 = 125.000 ₫.
- **V2, tối thiểu 1 block:** thời gian tính tiền bằng 0 (ví dụ mở 09:07, đóng 09:10) thì tính 1 block theo đơn giá của đoạn bàn cuối (Pool: 25.000 ₫).
- **V2, mở Timed:** chọn số phút (30, 60, 90, 120 hoặc tự nhập bội số của 15, từ 15 đến 720); thẻ bàn đếm ngược; còn 10 phút thì cảnh báo (đổi màu, toast một lần); hết giờ thì báo "Hết giờ" và đếm thời gian quá hạn; hệ thống KHÔNG tự đóng bàn. Phiên `Open` vẫn không giới hạn thời gian.
- **V2, gia hạn:** nút GIA HẠN chỉ có ở phiên `Timed` đang `Active`; thêm bội số 15 phút (15 đến 240) và giờ dự kiến kết thúc cập nhật đúng; phiên `Open` hoặc đã đóng bị từ chối với thông báo tiếng Việt.
- **V2, chuyển bàn:** giữ nguyên phiên, KHÔNG reset giờ; bàn cũ về `Available`, bàn mới `InUse`; bàn đích phải `Available`, khác bàn hiện tại, không có booking giữ chỗ; khi hai bàn khác giá thì tiền tính đúng theo từng đoạn (ví dụ mở Pool 09:00, chuyển sang Carom 120.000 ₫ lúc 09:30, đóng 10:00 = 50.000 + 60.000 = 110.000 ₫); lịch sử bàn hiện đủ các đoạn.
- **V2, tiền tạm tính và hiển thị:** tiền tạm tính lấy từ cùng hàm SQL với lúc đóng bàn (không tính ở JavaScript); giao diện ghi rõ "Tính giờ từ ...".
- **V2, dọn giao diện:** không còn nút SPLIT/MERGE; CONFIRM BOOKING vẫn hiện nhưng disabled.
- Customer hoặc người chưa đăng nhập không thao tác được.
- `dotnet build` không lỗi, không sửa file của người khác ngoài các dòng đã thỏa thuận.