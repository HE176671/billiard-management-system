# Kế hoạch nâng cấp V2: Quản lý phiên chơi (Doan)

> Đặt tại `docs/sds-doan/PLAN_V2.md`. Đây là nguồn quyết định cho đợt nâng cấp V2.
> Nếu tài liệu khác (SCOPE.md, SDS_DOAN.md) mâu thuẫn với file này về V2 thì file này thắng,
> và phải báo để cập nhật tài liệu kia. Code và database đã triển khai vẫn là chuẩn cho phần V1.

## 1. Phạm vi V2

| # | Chức năng | Ghi chú |
|---|---|---|
| 1 | Tính tiền theo block 15 phút | Làm tròn LÊN mốc 15 phút ở cả giờ bắt đầu và giờ kết thúc tính tiền |
| 2 | Hai hình thức mở bàn | `Open` (không giới hạn) và `Timed` (đăng ký số phút, đếm ngược, cảnh báo, gia hạn) |
| 3 | Chuyển bàn | Giữ nguyên phiên, không reset giờ, lưu từng đoạn bàn để tính đúng khi khác giá |
| 4 | Dọn giao diện | BỎ nút SPLIT/MERGE. Giữ CONFIRM BOOKING ở trạng thái disabled |

Không làm: gộp/tách bàn, gói có giới hạn cứng (tự đóng bàn), gói Combo (Thành viên 4),
trạng thái "dọn dẹp" sau chuyển bàn, đưa bàn từ AwaitingPayment về Available (Thành viên 3).

## 2. Quyết định đã chốt

| # | Vấn đề | Quyết định |
|---|---|---|
| D1 | Làm tròn giờ đóng | Làm tròn LÊN mốc 15 phút (giờ đã đúng mốc thì giữ nguyên) |
| D2 | Thời gian tính tiền bằng 0 | Tính tối thiểu 1 block theo đơn giá của đoạn bàn cuối cùng |
| D3 | Phiên `Timed` tính tiền | Theo thời gian thực chơi tính theo block. Giờ dự kiến kết thúc chỉ để đếm ngược và nhắc |
| D4 | Gói giới hạn cứng | Không làm. Chỉ chừa chỗ để thêm sau |
| D5 | Cảnh báo | Còn 10 phút thì cảnh báo, hết giờ thì báo "Hết giờ" và đếm thời gian quá hạn. KHÔNG tự đóng bàn |
| D6 | Giờ chuyển bàn | Làm tròn LÊN mốc 15 phút khi tính tiền (giờ thực vẫn lưu nguyên) |
| D7 | Bàn cũ sau chuyển | Về `Available` |
| D8 | "Đăng ký thời gian" | Chọn số phút: 30, 60, 90, 120 hoặc tự nhập (bội số của 15, từ 15 đến 720) |

Giờ dự kiến kết thúc của phiên `Timed` = giờ mở bàn thực tế + số phút đăng ký.

## 3. Quy tắc tính tiền

- Làm tròn lên mốc 15 phút (múi giờ +7 là số giờ nguyên nên mốc UTC trùng mốc giờ Việt Nam):
  `ceil15(t) = epoch + CEILING(giây(t - epoch) / 900) * 900`, với epoch cố định (ví dụ 2000-01-01 00:00:00).
- Mỗi phiên có một hoặc nhiều đoạn bàn. Đoạn thứ i tính tiền trong khoảng
  `[ceil15(Start), ceil15(End)]` với đơn giá riêng (`HourlyRateSnapshot` của đoạn đó).
- Tiền của đoạn = `ROUND(số giây tính tiền * đơn giá / 3600.0, 0)` (đồng nguyên).
- `PlaytimeAmount` của phiên = tổng tiền các đoạn. Nếu tổng số giây tính tiền bằng 0 thì tính 1 block
  của đoạn cuối: `ROUND(900 * đơn giá / 3600.0, 0)`.
- `BillingStartAtUtc` = `ceil15(StartAtUtc)` của phiên. `BillingEndAtUtc` = `ceil15(giờ đóng)`
  (khi áp dụng mức tối thiểu thì `BillingEndAtUtc = BillingStartAtUtc + 15 phút`).
- Giờ chuyển bàn là điểm chung: `EndAtUtc` của đoạn cũ bằng `StartAtUtc` của đoạn mới.
- Tính tiền chỉ thực hiện trên SQL Server. Giao diện hiển thị "tiền tạm tính" bằng cách gọi cùng
  hàm SQL, không tự tính ở JavaScript.

Ví dụ bàn Pool 100.000 ₫/giờ (25.000 ₫ một block):

| Mở | Đóng | Tính từ | Tính đến | Tiền |
|---|---|---|---|---|
| 09:00 | 10:00 | 09:00 | 10:00 | 100.000 |
| 08:50 | 10:07 | 09:00 | 10:15 | 125.000 |
| 09:07 | 09:40 | 09:15 | 09:45 | 50.000 |
| 09:07 | 09:10 | 09:15 | 09:15 | 25.000 (tối thiểu 1 block) |
| 09:00 mở bàn Pool, 09:30 chuyển sang Carom (120.000), 10:00 đóng | | | | 50.000 + 60.000 = 110.000 |

## 4. Thiết kế database (file mới, KHÔNG sửa file .sql cũ)

File: `BMS_Database_Starter/06_PlaySessionEnhancements.sql`, chạy lại nhiều lần không lỗi (idempotent).
Đầu file: `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; GO` (bắt buộc vì có filtered index).
Tương thích ngược: các procedure cũ giữ nguyên tham số và mã lỗi, chỉ thêm tham số tùy chọn.

**Thêm cột vào `PlaySessions`:**
- `SessionMode varchar(10) NOT NULL DEFAULT 'Open'`, CHECK `IN ('Open','Timed')`
- `PlannedEndAtUtc datetime2(0) NULL` (bắt buộc khi `Timed`, phải NULL khi `Open`)
- `BillingStartAtUtc datetime2(0) NOT NULL` (backfill bằng `StartAtUtc` cho dữ liệu cũ)
- `BillingEndAtUtc datetime2(0) NULL` (bắt buộc khi `Closed`, backfill bằng `EndAtUtc`)

**Bảng mới `PlaySessionTableSegments`:** `Id`, `SessionId` (FK), `TableId` (FK), `HourlyRateSnapshot`,
`StartAtUtc`, `EndAtUtc` NULL. Filtered unique index: mỗi phiên tối đa một đoạn đang mở
(`EndAtUtc IS NULL`). Backfill: mỗi phiên cũ một đoạn.

**Hàm và procedure:**
- `dbo.fn_CeilTo15Min(@t)`: làm tròn lên mốc 15 phút.
- `dbo.fn_CalcPlaytimeAmount(@SessionId, @AtUtc)`: tiền giờ tại thời điểm `@AtUtc`. Là nguồn duy nhất cho
  đóng bàn lẫn tiền tạm tính.
- `usp_OpenSession` (sửa): thêm `@SessionMode = 'Open'`, `@PlannedMinutes = NULL`. Ghi `BillingStartAtUtc`
  và đoạn đầu.
- `usp_CloseSession` (sửa): chốt đoạn cuối, tính theo block. Result set giữ nguyên 6 cột cũ theo đúng
  thứ tự rồi THÊM cột mới ở cuối.
- `usp_ExtendSession` (mới): chỉ phiên `Timed` đang Active, thêm bội số 15 phút (15 đến 240).
- `usp_TransferSession` (mới): khóa hai bàn theo thứ tự Id tăng dần (tránh deadlock). Bàn đích phải
  `Available`, khác bàn hiện tại, không có booking giữ chỗ (dùng đúng điều kiện của lỗi 51407 trong
  `usp_OpenSession`). Đóng đoạn cũ, mở đoạn mới với đơn giá của bàn đích, cập nhật `PlaySessions.TableId`,
  bàn cũ `Available`, bàn mới `InUse`.

**Mã lỗi mới:**
- Mở bàn: `51409` hình thức không hợp lệ, `51410` số phút không hợp lệ.
- Gia hạn: `51601` quyền, `51602` phiên không Active hoặc không phải Timed, `51603` số phút không hợp lệ.
- Chuyển bàn: `51611` quyền, `51612` phiên không Active, `51613` bàn đích không Trống hoặc không tồn tại,
  `51614` bàn đích trùng bàn hiện tại, `51615` bàn đích đang giữ chỗ cho booking.

## 5. Thay đổi C# và giao diện

- Entity: `PlaySession` thêm 4 thuộc tính, entity mới `PlaySessionTableSegment`, thêm 1 dòng `DbSet`.
- Service: `OpenSessionAsync` thêm tham số hình thức và số phút, mới `ExtendSessionAsync`,
  `TransferSessionAsync`, dịch mã lỗi mới, chi tiết bàn đọc thêm các đoạn và tiền tạm tính.
- Controller: thêm `ExtendSession`, `TransferSession` (POST, CSRF qua trường form, StaffId từ `User`).
- Giao diện: form chọn hình thức khi mở bàn (kèm xem trước "Tính giờ từ ..."), thẻ bàn đếm ngược và đổi màu
  khi còn 10 phút hoặc hết giờ, toast cảnh báo một lần, nút GIA HẠN (chỉ phiên Timed) và CHUYỂN BÀN,
  panel chi tiết thêm hình thức, giờ tính tiền, giờ dự kiến kết thúc, tiền tạm tính, lịch sử bàn.
  Bỏ SPLIT/MERGE.

## 6. Các lượt thực hiện

| Lượt | Nội dung |
|---|---|
| 0 | Cập nhật tài liệu và quy tắc (AGENTS.md, SCOPE.md), nhắn nhóm, sao lưu DB |
| 1 | Viết `06_PlaySessionEnhancements.sql` và script kiểm thử có hoàn tác. Người dùng tự chạy |
| 2 | Entity, DbSet, ViewModel, phần đọc của Service |
| 3 | Mở bàn có hình thức |
| 4 | Đếm ngược, cảnh báo, gia hạn |
| 5 | Đóng bàn theo block (xác nhận hiển thị tiền) |
| 6 | Chuyển bàn |
| 7 | Dọn giao diện |
| 8 | Cập nhật tài liệu, sơ đồ, kiểm thử tổng, Pull Request |

## 7. Rủi ro

- Database dùng chung: mọi thành viên phải chạy file 06 trên DB của mình, nếu chưa chạy thì code mới lỗi.
- `PlaytimeAmount` đổi cách tính (theo block, tối thiểu 1 block). Thành viên 3 cần được báo.
- Phiên Active tạo trước V2 không nằm đúng mốc 15 phút (backfill dùng giờ gốc). Chấp nhận được.
- Đồng hồ hiển thị thời gian thực chơi, tiền tính theo block. Giao diện phải ghi rõ "Tính giờ từ".