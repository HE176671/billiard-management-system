# WF5 — Quản trị tài khoản, giá bàn, báo cáo vận hành, nhật ký & chu kỳ hạng (TV5 Hùng)

Nhánh: `hung-wf5`. Cập nhật 09/11/2026.

WF5 bao gồm các chức năng quản trị hệ thống, cấu hình giá, phân tích vận hành, nhật ký kiểm toán và tự động hóa chu kỳ hội viên:

> **Admin** tạo tài khoản & phân quyền → **Staff** vận hành trực tiếp trên sơ đồ bàn (realtime SignalR) → **Manager** theo dõi **Báo cáo doanh thu**, **Báo cáo vận hành** (tỷ lệ lấp đầy, heatmap giờ cao điểm, hiệu suất từng bàn, top món F&B) và **Nhật ký hệ thống** → **00:05 job cuối ngày**: phát voucher sinh nhật, xét giữ hạng 12 tháng / ân hạn 30 ngày / hạ tối đa 1 bậc, gửi email tự động.

## Use case và màn hình

| UC | Chức năng | Vai trò | Màn hình / đường dẫn |
|---|---|---|---|
| UC01 | Đăng nhập / đăng xuất | Admin, Manager, Staff | `/Account/Login` |
| UC02 | Đổi mật khẩu | Admin, Manager, Staff | `/Profile/ChangePassword` |
| UC03 | Tài khoản & phân quyền: tạo, sửa, khóa, đặt lại mật khẩu, đổi vai trò (Admin/Manager/Staff) | **Admin** | `/Employee` |
| UC36 | Kiểm kê kho (điều chỉnh tồn, bắt buộc lý do, ghi nhật ký) | Manager | `/Inventory` |
| UC37 | Báo cáo doanh thu theo **ngày / tháng**, biểu đồ, so sánh mục tiêu tháng, xuất CSV | Manager | `/Revenue` |
| UC38 | Nhật ký hệ thống (lọc ngày, thao tác, người thực hiện, từ khóa) | Manager | `/AuditLog` |
| UC39 | **Báo cáo vận hành**: tỷ lệ lấp đầy bàn, heatmap giờ cao điểm (7 thứ × giờ mở cửa), hiệu suất từng bàn (bàn đông nhất / vắng nhất), doanh thu giờ chơi theo loại bàn, top món F&B, xuất CSV | Manager | `/OperationsReport` |
| UC40 | **Mục tiêu doanh thu tháng**: thêm / sửa / xóa, theo dõi % đạt, dự báo cuối tháng, số cần thu mỗi ngày | Manager | `/RevenueTarget` |
| UC41 | Xét hạng định kỳ (job 00:05 + nút "Chạy xét hạng ngay") | System, Manager | `/CrmDashboard` |
| UC42 | Voucher sinh nhật (chỉ dùng trong ngày sinh nhật; sinh 29/02 năm thường nhận vào 28/02) | System, Staff áp dụng | job 00:05 |

Hạ tầng dùng chung: `IAuditLogService` + `AuditActions`, `IEmailService` (SMTP, API-02), `TableHub` (`/hubs/table`, sơ đồ bàn realtime), `[BroadcastTableChange]`, `LoyaltyDailyJob` (BackgroundService chạy 00:05 giờ VN).

## Chi tiết Báo cáo vận hành (`/OperationsReport`)

1. **Validation bộ lọc phía server (`OperationsReportController.ValidateAndCreateFilterAsync`)**:
   - Khoảng ngày `from <= to`, tối đa **92 ngày**, không cho phép chọn ngày trong tương lai (so với giờ Việt Nam UTC+7).
   - Khung giờ mở cửa `openHour` (0–23) và `closeHour` (1–24), bắt buộc `openHour < closeHour` (mặc định `08:00 – 24:00`).
   - Lọc theo loại bàn `tableTypeId` (tùy chọn; nếu truyền lên thì ID phải tồn tại trong bảng `TableTypes`).
2. **Công thức tính toán (`OperationsReportController.PopulateReportAsync`)**:
   - Khoảng thời gian truy vấn nửa mở theo giờ VN: `startUtc = From 00:00 - 7h`, `endUtc = (To + 1) 00:00 - 7h`.
   - Cắt từng `PlaySessionTableSegment` vào `[startUtc, endUtc)` (nếu phiên đang mở `EndAtUtc == null` thì lấy `DateTime.UtcNow`), sau đó tách thành từng lát 1 giờ theo giờ VN để cộng số phút có khách vào từng bàn và ô `(Thứ, Giờ)`.
   - **Tỷ lệ lấp đầy (%)** = `Số phút có khách trong giờ mở cửa / (Số bàn × Số phút mở cửa mỗi ngày × Số ngày) × 100`.
   - **Heatmap giờ cao điểm (7 thứ × khung giờ mở cửa)**: giá trị mỗi ô (%) = `Số phút có khách tại (Thứ, Giờ) / (60 × Số lần thứ đó xuất hiện trong khoảng ngày × Số bàn) × 100`.
   - **Hiệu suất từng bàn**: xếp hạng bàn có công suất cao nhất và thấp nhất để Manager điều phối vị trí bàn.
   - **Top món F&B**: lấy từ `Orders` có `Status = 'Completed'`, `Số lượng thực bán = Quantity - CancelledQuantity`, `Doanh thu = UnitPrice × Số lượng thực bán`.
   - **Xuất CSV**: kèm UTF-8 BOM (`0xEF, 0xBB, 0xBF`) để mở trực tiếp bằng Microsoft Excel không lỗi tiếng Việt.

## Mục tiêu doanh thu tháng (`/RevenueTarget`, UC40)

Bảng `RevenueTargets(Id, Year, Month, TargetAmount, Note, CreatedById, CreatedAtUtc, UpdatedById, UpdatedAtUtc)`, unique `(Year, Month)`, script `19_WF5_RevenueTargets.sql` (web tự áp dụng khi khởi động). Thực thu **không lưu**, luôn tính lại từ `PaymentTransactions` nên luôn khớp Báo cáo doanh thu.

| Mã | Quy tắc | Chỗ kiểm tra |
|---|---|---|
| BR-40.1 | Mỗi tháng tối đa 1 mục tiêu | `AnyAsync` trong `Create` + unique index (bắt `DbUpdateException`) |
| BR-40.2 | Chỉ tháng hiện tại đến tối đa 12 tháng tới; tháng đã qua không thêm/sửa | `ValidateCommon`, `GetState` |
| BR-40.3 | 1 triệu – 100 tỷ VND, làm tròn nghìn | `[Range]` + `amount % 1000` |
| BR-40.4 | Sửa số mục tiêu của tháng đang chạy phải ghi lý do ≥ 10 ký tự | `Edit` POST |
| BR-40.5 | Chỉ xóa mục tiêu của tháng chưa bắt đầu | `Delete` |
| BR-40.6 | Thêm/sửa/xóa đều ghi `AuditLogs` (`RevenueTargetChange`, giá trị cũ/mới, lý do) | `_audit.AddAsync` cùng `SaveChanges` |

Công thức (`RevenueTargetService.Calculate`, có smoke test): `% đạt = thực thu / mục tiêu × 100`; `dự báo = thực thu / số ngày đã qua × số ngày trong tháng`; `cần mỗi ngày = phần còn thiếu / số ngày còn lại (kể cả hôm nay)`. Trạng thái: Chưa bắt đầu / Đúng tiến độ / Chậm tiến độ / Đạt / Không đạt. Tiến độ tháng này hiện trên Dashboard; Báo cáo doanh thu nhóm theo tháng có cột Mục tiêu và % đạt, kèm biểu đồ (cột doanh thu + đường mục tiêu).

## Phân quyền (SRS I.4.4)

- **Admin**: chỉ quản trị tài khoản và phân quyền (`/Employee`, hồ sơ cá nhân). Chặn tự khóa / tự đổi vai trò / tự xóa chính mình; chặn khóa hoặc hạ quyền Admin cuối cùng; không xóa tài khoản Admin.
- **Manager**: vận hành và quản lý quán (tổng quan, báo cáo doanh thu, mục tiêu doanh thu, báo cáo vận hành, nhật ký hệ thống, bảng giá, bàn, kho, hội viên, hạng, quà, duyệt điều chỉnh điểm, gửi ưu đãi). Không hủy món / hủy bill (BR-22).
- **Staff**: đăng nhập vào thẳng sơ đồ bàn (`/Table`), mở/đóng/chuyển bàn, gọi món, thanh toán, tra cứu & đăng ký hội viên tại quầy.

## Ghi nhật ký tự động (`AuditLogs`)

Tạo/khóa/đổi vai trò/đặt lại mật khẩu tài khoản, thêm/sửa/xóa mục tiêu doanh thu (phần TV5); các module khác gọi cùng dịch vụ này khi đổi giá giờ, thêm/đổi tên loại bàn, kiểm kê kho, hủy món/hóa đơn, duyệt điều chỉnh điểm, đổi cấu hình hạng, và mỗi lần chạy chu kỳ xét hạng cuối ngày đều được ghi vào bảng `AuditLogs` kèm người thực hiện, thời điểm UTC+7, giá trị cũ/mới và lý do.

## Email (SMTP)

Cấu hình trong `appsettings.json` mục `Smtp` (hoặc user-secrets). Để trống `Host` = chế độ demo: không gửi thật, chỉ ghi log `[EMAIL DEMO]`.

```
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com" --project src/Bms.Web
dotnet user-secrets set "Smtp:UserName" "<gmail>" --project src/Bms.Web
dotnet user-secrets set "Smtp:Password" "<app password>" --project src/Bms.Web
dotnet user-secrets set "Smtp:FromAddress" "<gmail>" --project src/Bms.Web
```

## Kịch bản demo (mật khẩu demo `123456`)

1. **`admin.demo`**: vào **Tài khoản & phân quyền** (`/Employee`), tạo nhân viên mới, đổi vai trò Staff ↔ Manager, thử tự khóa chính mình → hệ thống chặn.
2. **`manager.demo`**:
   - Vào **Nhật ký hệ thống** (`/AuditLog`) → lọc theo thao tác "Mục tiêu doanh thu" hoặc "Thay đổi tài khoản" để thấy các bản ghi vừa tạo. (Bảng giá do thành viên khác phụ trách, chỉ gọi `IAuditLogService` của TV5 để ghi nhật ký.)
   - Vào **Mục tiêu doanh thu** (`/RevenueTarget`) → đặt mục tiêu tháng này; thử đặt tháng đã qua, số không tròn nghìn, tháng trùng → bị chặn; sửa số của tháng này không ghi lý do → bị chặn; xem Dashboard có thanh tiến độ.
   - Vào **Báo cáo doanh thu** (`/Revenue`) → lọc theo ngày/tháng, xem biểu đồ, nhóm theo tháng thấy cột % đạt mục tiêu, xuất CSV.
   - Vào **Báo cáo vận hành** (`/OperationsReport`) → xem KPI tỷ lệ lấp đầy, bảng Heatmap giờ cao điểm theo thứ, top bàn đông/vắng khách, top món F&B, thử nhập ngày bắt đầu > ngày kết thúc để demo validation server, và bấm **CSV**.
   - Vào **Thống kê hội viên** (`/CrmDashboard`) → bấm **"Chạy xét hạng ngay"** để chạy job xét hạng & phát voucher sinh nhật.
3. **`staff.demo01`**: đăng nhập vào thẳng **Quản lý bàn** (`/Table`), mở bàn / đóng bàn trên 2 cửa sổ trình duyệt để thấy SignalR cập nhật realtime.
