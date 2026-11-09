# WF5 — Ca làm, quản trị, giá bàn & chu kỳ hạng (TV5 Hùng)

Nhánh: `hung-wf5`. Cập nhật 09/11/2026.

WF5 là **chu trình một ngày làm việc của quán**:

> Admin tạo tài khoản → Manager cấu hình loại bàn & giá → **Staff mở ca** → (WF1–WF4 vận hành, mọi khoản thu gắn vào ca)
> → **Staff đóng ca, đối soát tiền mặt** → Manager xem doanh thu theo ngày/tháng/ca, nhật ký, kiểm kê
> → **00:05 job cuối ngày**: voucher sinh nhật, xét giữ hạng 12 tháng / ân hạn 30 ngày / hạ 1 bậc, gửi email → ngày hôm sau.

## Use case và màn hình

| UC | Chức năng | Vai trò | Màn hình / đường dẫn |
|---|---|---|---|
| UC01 | Đăng nhập / đăng xuất | Admin, Manager, Staff | `/Account/Login` |
| UC02 | Đổi mật khẩu | Admin, Manager, Staff | `/Profile/ChangePassword` |
| UC03 | Tài khoản & phân quyền: tạo, sửa, khóa, đặt lại mật khẩu, đổi vai trò (Admin/Manager/Staff) | **Admin** | `/Employee` |
| UC35 | Thêm / đổi tên loại bàn, sửa giá giờ (1 giá/loại) | Manager | `/Pricing` |
| UC36 | Kiểm kê (điều chỉnh tồn, bắt buộc lý do) | Manager | `/Inventory` |
| UC37 | Báo cáo doanh thu theo **ngày / tháng / ca**, xuất CSV | Manager | `/Revenue` |
| UC38 | Nhật ký hệ thống (lọc ngày, thao tác, người, từ khóa) | Manager | `/AuditLog` |
| UC39 | Mở ca (cả quán chỉ 1 ca — BR-23) | Staff | `/Shift` |
| UC40 | Đóng ca & đối soát (lệch tiền bắt buộc ghi lý do) | Staff (Manager đóng hộ) | `/Shift`, `/Shift/Manage` |
| UC41 | Xét hạng định kỳ (job 00:05 + nút "Chạy xét hạng ngay") | System, Manager | `/CrmDashboard` |
| UC42 | Voucher sinh nhật (chỉ dùng trong ngày sinh nhật) | System, Staff áp dụng | job 00:05 |

Hạ tầng dùng chung: `IShiftService`, `IAuditLogService` + `AuditActions`, `IEmailService` (SMTP, API-02),
`TableHub` (`/hubs/table`, sơ đồ bàn realtime), `[RequireOpenShift]`, `[BroadcastTableChange]`,
script `18_WF5_AuditLog_SingleShift.sql` (web tự áp dụng khi khởi động).

## Quy tắc ca làm (UC39–UC40)

1. **Một ca cho cả quán** (BR-23): một két tiền, một ca đang mở. Ràng buộc DB `UX_WorkShifts_SingleActive`.
2. **Chỉ Staff mở ca.** Đếm két theo **mệnh giá** (nhập số tờ 500k, 200k... hệ thống tự cộng). **Đếm mù**: không điền sẵn số ca trước để lại.
   **Khác tiền ca trước để lại → bắt buộc ghi lý do** (ví dụ chủ quán rút tiền). Chống thất thoát giữa hai ca.
3. **Chưa mở ca thì không mở bàn, không thu tiền** (`[RequireOpenShift]` trên mở bàn và thanh toán).
4. **Tiền mặt kỳ vọng** = tiền đầu ca + các khoản thu **tiền mặt** gắn với ca. QR/chuyển khoản hiện riêng, không tính vào két.
5. **Đóng ca (đếm mù)**: nhân viên đang trực **không thấy** tiền mặt thu và tiền phải có cho tới khi chốt; đếm két theo mệnh giá.
   Lệch thì hệ thống chỉ báo số lệch để đếm lại, **mỗi lần đếm lệch đều ghi Nhật ký** (kèm chi tiết mệnh giá) dù sau đó đếm lại cho khớp.
   Nhân viên đang trực nào cũng đóng được (giao ca), Manager đóng hộ khi quên. Đếm tiền thực tế (số nguyên);
   **lệch → bắt buộc ghi lý do**. Hệ thống chốt snapshot (thu tiền mặt, kỳ vọng, thực tế, người đóng). Bàn còn đang chơi vẫn đóng được, tiền thu vào ca sau.
6. **Ca đã đóng không sửa, không xóa.** Mở/đóng ca, lệch tiền đầu ca và cuối ca đều ghi Nhật ký hệ thống.
7. **Manager**: xem mọi ca, lọc ca lệch tiền, cảnh báo ca mở quá 16 giờ (quên đóng), đóng hộ.

## Phân quyền (SRS I.4.4)

- **Admin**: chỉ quản trị tài khoản và phân quyền (`/Employee`, hồ sơ cá nhân). Không tự khóa / tự đổi vai trò, không hạ hoặc khóa Admin cuối cùng.
- **Manager**: vận hành và quản lý quán (giá, món, kho, ca, báo cáo, nhật ký, hạng, quà, điều chỉnh điểm, email ưu đãi). Không hủy món / hủy bill (BR-22).
- **Staff**: ca, bàn, gọi món, thanh toán, hủy món/bill có lý do, hội viên tại quầy.

## Ghi nhật ký tự động

Mở ca, đóng ca, lệch tiền cuối ca, đổi giá giờ, thêm/đổi tên loại bàn, kiểm kê, mỗi lần chạy chu kỳ cuối ngày.
Thành viên khác ghi thêm (hủy món, hủy bill, điều chỉnh điểm, đổi giá món) bằng `IAuditLogService` — xem `HOP_DONG_CA_LAM_TV5.md`.

## Email (SMTP)

Cấu hình trong `appsettings.json` mục `Smtp` (hoặc user-secrets). Để trống `Host` = chế độ demo: không gửi thật, chỉ ghi log `[EMAIL DEMO]`.
Ví dụ Gmail: Host `smtp.gmail.com`, Port 587, EnableSsl true, UserName/FromAddress là Gmail, Password là App Password.

```
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com" --project src/Bms.Web
dotnet user-secrets set "Smtp:UserName" "<gmail>" --project src/Bms.Web
dotnet user-secrets set "Smtp:Password" "<app password>" --project src/Bms.Web
dotnet user-secrets set "Smtp:FromAddress" "<gmail>" --project src/Bms.Web
```

## Kịch bản demo (mật khẩu demo 123456)

1. `admin.demo`: vào Quản lý nhân viên, tạo 1 nhân viên. Thấy menu chỉ có quản trị tài khoản.
2. `manager.demo`: Bảng giá → thêm loại bàn "Snooker" 70.000đ, đổi giá 1 loại bàn.
3. `staff.demo01`: Quản lý bàn → mở bàn khi chưa có ca → bị chặn. Vào Ca làm của quán → mở ca 500.000đ → mở bàn được.
4. `staff.demo02` (cửa sổ ẩn danh): thử mở ca → báo quán đang có ca của demo01. Mở sơ đồ bàn ở hai cửa sổ, mở/đóng bàn ở một bên → bên kia tự cập nhật.
5. `staff.demo02`: đóng ca với số tiền lệch → bắt ghi lý do → đóng được.
6. `manager.demo`: Báo cáo doanh thu nhóm theo Ca làm; Nhật ký hệ thống thấy đủ các dòng; Thống kê hội viên → "Chạy xét hạng ngay".

## Việc phụ thuộc thành viên khác

- **Thanh toán (Dũng/Hiệp)**: mọi khoản thu tại quầy gọi `RequireActiveShiftIdForCashAsync` để gắn `WorkShiftId` vào ca chung; không để NULL. `CheckoutService` trên nhánh `dung` còn tìm ca theo người thu, phải đổi.
- **Hủy món / hủy bill / điều chỉnh điểm**: gọi `IAuditLogService` để hiện trên Nhật ký hệ thống.
- Chu kỳ hạng (`TierEvaluationService`, `CampaignService`) đã sửa theo scope: giữ hạng 12 tháng, voucher sinh nhật chỉ trong ngày, bỏ hết hạn điểm 100 ngày. Khi merge với nhánh `dung` cần giữ các thay đổi này.
