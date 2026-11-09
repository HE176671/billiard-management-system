# Tiến độ BMS — báo cáo ngày 13/10/2026

Kiểm tra và hoàn thiện code ngày 12/10/2026 trên máy Hùng. ASP.NET Core MVC .NET 8, Identity, EF Core và SQL Server `localhost / BilliardDB`.

## Bảng tiến độ

| Chức năng | Use case | Trạng thái trong phạm vi dưới đây | Màn hình / kết quả |
|---|---|---|---|
| Đăng nhập / đăng xuất / đăng ký | UC01/02/19 | Đã có, đã kiểm tra HTTP | `/Account/Login`, `/Account/Register`; đăng ký gán Customer trong transaction; Admin vào Dashboard, Customer vào Portal |
| Đổi mật khẩu | UC03 | Đã có, đã kiểm tra HTTP | `/Profile/ChangePassword`; kiểm tra mật khẩu cũ, giữ phiên hiện tại sau đổi thành công |
| Quản lý nhân viên | UC43/44 | CRUD đã có; đã kiểm tra trang, phân quyền và reset password | `/Employee`; danh sách, tìm/lọc/phân trang, tạo/sửa/xóa/khóa/mở khóa/reset. Xóa bị chặn khi có lịch sử qua FK; kiểm thử lần này chưa chạy toàn bộ CRUD nhân viên qua HTTP |
| Dashboard tổng quan | UC04 | Đã bổ sung, đã kiểm tra SQL và HTTP | `/Dashboard`; số nhân viên/khách hàng/bàn, trạng thái bàn, doanh thu hôm nay/tháng này |
| Quản lý tài khoản khách hàng | UC35 | Đã hoàn thiện, đã kiểm tra SQL và HTTP | `/Customer`; tìm tên/username/email/điện thoại, lọc trạng thái/hạng, phân trang, xem điểm/hạng, khóa/mở khóa. Customer xem tài khoản riêng tại `/Customer/Portal` |
| Quản lý hạng thành viên | Chưa gán mã UC | Đã hoàn thiện CRUD và ngưỡng điểm | `/MemberTier`; tên hạng, điểm tối thiểu, phần trăm giảm; chặn trùng tên/ngưỡng, không xóa hạng đang được tham chiếu |
| Quản lý bảng giá | Chưa gán mã UC | Đã hoàn thiện CRUD cấu hình | `/Pricing`; loại bàn, giờ bắt đầu/kết thúc, giá mỗi giờ; chặn giờ sai, loại bàn không tồn tại và khung giờ chồng nhau |
| Báo cáo doanh thu | UC46 | Đã bổ sung, đã kiểm tra SQL và HTTP | `/Revenue`; lọc khoảng ngày, nhóm ngày/tháng, tổng tiền/số giao dịch/số hóa đơn, theo phương thức thanh toán, xuất CSV |

## Phạm vi cần trình bày chính xác

- Hạng thành viên: phần này cấu hình ngưỡng và mức giảm. Đăng ký mới nhận hạng có ngưỡng 0 nếu đã cấu hình. Chưa triển khai cộng điểm/nâng hạng tự động sau thanh toán hay cập nhật lại hạng của khách cũ khi sửa ngưỡng.
- Bảng giá: CRUD `PricingConfigs` đã chạy; module mở phiên hiện vẫn cần tích hợp đọc cấu hình giá. Không khẳng định đã thay cách tính tiền trong procedure mở/đóng phiên.
- Doanh thu: cộng `PaymentTransactions.AmountPaid` theo `TransactionTimeUtc`; không lấy hóa đơn chưa trả hoặc tiền phiên chưa thanh toán. Ngày tính theo UTC+7, gồm toàn bộ ngày kết thúc. Một hóa đơn nhiều lần thanh toán chỉ đếm một hóa đơn.
- Theo yêu cầu thêm dữ liệu để trình bày, đã chạy script 07: 4 hạng, 4 khách demo, 6 mức giá, 8 phiên/8 hóa đơn và 8 thanh toán giả lập. Đây là dữ liệu demo, không phải giao dịch gateway thật. Có một hóa đơn chưa trả và một hóa đơn trả hai lần để minh họa báo cáo.
- Lọc 01/10–13/10/2026: 1.110.000đ, 7 giao dịch, 6 hóa đơn có thanh toán. Lọc riêng 13/10: 350.000đ; hóa đơn Unpaid 200.000đ không được tính. Xem [bản đồ code và số liệu demo](HUONG_DAN_TIM_CODE.md) để đối chiếu.

## Cách chạy trên máy Hùng

Từ thư mục gốc repository:

```powershell
dotnet run --project src/Bms.Web --no-launch-profile -- --urls http://localhost:5080 --environment Development
```

Mở `http://localhost:5080/Account/Login`. Đăng nhập `admin.demo` với mật khẩu đã cấu hình trước trên máy trong user-secrets. Mật khẩu không được ghi vào tài liệu/source. Ứng dụng đã dùng cấu hình này để đặt mật khẩu cho admin ở database `localhost`; lần chạy tiếp theo giữ mật khẩu hiện có.

`appsettings.Development.json` local đã đổi từ `localhost\SQLEXPRESS` sang `localhost` và được Git bỏ qua. Hai instance có database độc lập; SQLEXPRESS trên máy này còn schema cũ.

Database hiện có đã chạy `BMS_Database_Starter/06_AddMembershipMinPoints.sql` để bổ sung `MinPoints`, giữ dữ liệu. Đã chạy lại script thành công để kiểm tra tính idempotent. Không chạy lại Full hoặc seed trên database hiện có.

Máy thành viên khác: đọc `BMS_Database_Starter/README_VI.md`, chọn đúng instance/schema, chạy script 06 nếu thiếu MinPoints, cấu hình kết nối và tài khoản admin local của mình. Không dùng tên instance của Hùng nếu chưa kiểm tra.

## Kịch bản demo khoảng 5–7 phút

1. Đăng nhập Admin → Dashboard: giải thích số liệu lấy từ SQL và doanh thu thực thu.
2. Nhân viên: tìm/lọc nhân viên, mở form sửa/reset mật khẩu. Nếu demo tạo/xóa, dùng nhân viên demo mới không có lịch sử; không xóa nhân viên đang làm việc.
3. Hạng thành viên: đã có Đồng/Bạc/Vàng/Kim Cương. Tạo một hạng thử tên `Hạng thử báo cáo` (10.000 điểm, 20%), sửa rồi xóa; thử nhập lại ngưỡng 500 để thấy lỗi. Các giá trị trong dữ liệu mẫu phục vụ demo, không phải quy tắc đã được nhóm chốt.
4. Đăng xuất, đăng ký khách mới → Portal; khách nhận hạng Đồng nếu đã có hạng 0 điểm. Thử đổi mật khẩu.
5. Đăng nhập Admin, tìm khách vừa đăng ký, lọc hạng, khóa/mở khóa. Phiên khách bị khóa sẽ bị đưa về Login ở request kế tiếp. Thử xóa hạng Đồng đang dùng: hệ thống chặn.
6. Bảng giá: xem 6 mức giá có sẵn; thử thêm 09:00–11:00 cùng loại để thấy lỗi trùng. Thêm khung trống 06:00–08:00 để demo tạo/sửa/xóa. Giữ 6 mức giá mẫu nếu muốn đối chiếu script 07.
7. Báo cáo: lọc 01/10–13/10/2026, nhóm ngày/tháng, xuất CSV. Riêng ngày 12/10 có 390.000đ, 3 giao dịch nhưng 2 hóa đơn; giải thích hóa đơn trả hai phần. Riêng ngày 13/10 có 350.000đ, hóa đơn chưa trả không được cộng.

## Bằng chứng kiểm tra

- `dotnet build tests/Bms.Smoke --no-restore`: build ứng dụng và bộ kiểm thử, **0 warning / 0 error**.
- `dotnet run --project tests/Bms.Smoke --no-build`: **26 kiểm tra tích hợp SQL qua**. Bao gồm CRUD hạng/giá, ngưỡng trùng, giờ chồng nhau/giờ sai, FK khi xóa hạng, lọc khách hàng, khóa/mở khóa, reset mật khẩu, Dashboard, doanh thu UTC+7, nhóm tháng, kỳ trống, CSV và ngày đảo ngược. Tất cả fixture được rollback trong transaction.
- Khi ứng dụng đang chạy: `pwsh -NoProfile -File tests/http-smoke.ps1`: **37 kiểm tra HTTP qua**. Bao gồm đăng nhập/đăng ký/đăng xuất, đổi mật khẩu đúng/sai, render trang Admin, CSRF, phân quyền Customer, CSV và vô hiệu cookie ngay sau khóa. Script đọc mật khẩu local mà không in ra; chỉ tạo rồi dọn tài khoản khách thử có tên ngẫu nhiên.
- Sau khi thêm dữ liệu demo và chia file: `pwsh -NoProfile -File tests/http-smoke.ps1 -CheckPresentationData`: **41 kiểm tra HTTP qua**, bao gồm 4 lần đối chiếu doanh thu mẫu qua CSV. Build 0 warning/0 error và 26 kiểm tra SQL tiếp tục qua. Không có phiên demo chồng giờ cùng bàn; tổng thanh toán của mỗi hóa đơn Paid khớp FinalAmount.
- Chạy kiểm thử SQL/HTTP cần quyền kết nối Windows Authentication đến SQL Server local. HTTP script mặc định dùng localhost:5080 và user-secrets trên máy Hùng.
- Không coi các kiểm thử tự động này là đã kiểm tra mọi thao tác UI bằng tay hoặc toàn bộ module của các thành viên khác.

## Đoạn trình bày ngắn

“Em đã hoàn thiện phần Admin quản lý tài khoản khách hàng, CRUD hạng thành viên với ngưỡng điểm, CRUD bảng giá theo loại bàn và khung giờ, Dashboard và báo cáo doanh thu có xuất CSV. Em bổ sung mapping còn thiếu, sửa điều hướng sau đăng ký và kiểm tra cookie để tài khoản bị khóa mất quyền truy cập ngay. Bản build không có lỗi; 26 kiểm thử tích hợp SQL và 37 kiểm thử HTTP đã qua. Phần tự động nâng hạng sau thanh toán và sử dụng bảng giá khi mở phiên sẽ phối hợp tích hợp với module liên quan.”

Code và tài liệu nằm local trong working tree; chưa commit/push trong lần làm này. Các tài liệu SDS và sơ đồ đã có thay đổi từ trước được giữ nguyên.

## Tra cứu code khi thầy hỏi

[HUONG_DAN_TIM_CODE.md](HUONG_DAN_TIM_CODE.md) có cây thư mục, số dòng bắt đầu–kết thúc của từng khối, liên kết mở file, luồng MVC và bảng câu hỏi thường gặp. Controller/Program đã có `#region` để thu gọn; ViewModel được tách theo chức năng và Entity có file riêng tại `Data/Entities`. Cập nhật số dòng bằng `pwsh -File scripts/Write-CodeGuide.ps1` sau khi sửa source.
