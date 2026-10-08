# Quy tắc cho AI agent: phần Quản lý phiên chơi (Doan)

## Dự án
- BMS: ASP.NET Core MVC, .NET 8, SQL Server, EF Core, theo hướng SQL-FIRST
- Tài liệu: docs/sds-doan/ (của Doan, được tạo/sửa), docs/sds-hung/ và docs/SDS_HUNG.md
  (của Hung, CHỈ ĐỌC). Khi tài liệu và code thật khác nhau, code thật là chuẩn.

## Cơ sở dữ liệu (cập nhật cho V2)
- Cấu trúc DB gốc nằm trong BMS_Database_Starter/BilliardDB_Full.sql. KHÔNG sửa các file .sql đã có.
- Được TẠO MỚI duy nhất: BMS_Database_Starter/06_PlaySessionEnhancements.sql và file kiểm thử
  BMS_Database_Starter/tests/06_PlaySessionEnhancements_tests.sql. Mọi thay đổi database của V2 nằm
  trong file 06. File phải chạy lại nhiều lần không lỗi, tương thích ngược, bắt đầu bằng
  SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON.
- TUYỆT ĐỐI KHÔNG chạy bất kỳ file .sql hay câu lệnh nào lên database (không sqlcmd, không
  dotnet ef). Người dùng tự chạy file bằng SSMS.
- KHÔNG tạo EF migration. Entity C# phải khớp từng cột với DB sau khi chạy file 06.
- Tiền giờ chỉ tính trên SQL Server (hàm trong file 06), không tính ở JavaScript hay C#.

## Phạm vi sửa file
- Được tạo/sửa tự do: file mới thuộc chức năng phiên chơi (entity, service, controller,
  viewmodel, view, js riêng) và docs/sds-doan/
- Được sửa TỐI THIỂU, chỉ thêm dòng, không đổi dòng cũ, và phải báo tôi: Program.cs
  (đăng ký service), ApplicationDbContext.cs (thêm DbSet)
- KHÔNG sửa: Account, Employee, Profile, ApplicationUser, file .sql, docs của Hung.
  Nếu cần sửa file khác, dừng lại và hỏi tôi
- Muốn dọn code của người khác (ví dụ trùng TranslateIdentityError): chỉ báo cáo, không sửa
- - Được sửa TỐI THIỂU, chỉ thêm/đổi đúng chỗ cần thiết, và phải báo tôi: Program.cs (đăng ký service),
  ApplicationDbContext.cs (thêm DbSet), StaffController.cs (chỉ đổi Index() để chuyển hướng
  sang TableController.Index(), làm ở lượt cuối)

## Cách viết code
- Logic đặt trong Services, Controller chỉ nhận request và trả kết quả
- Tuân theo quy ước đặt tên hiện có; giao diện và thông báo dùng tiếng Việt có dấu
- Thời gian lưu UTC, hiển thị giờ Việt Nam (UTC+7)
- Chỉ Staff và Admin được thao tác phiên chơi; lấy ID nhân viên từ người đăng nhập,
  không nhận từ client
- Chống CSRF cho các action POST
- Không cài thêm NuGet package nếu chưa hỏi

## Cách làm việc
- Luôn lập kế hoạch và liệt kê file sẽ tạo/sửa, chờ tôi duyệt rồi mới viết code
- Mỗi lần chỉ làm MỘT nhiệm vụ nhỏ
- Không đọc hay in ra chuỗi kết nối, mật khẩu, secret
- Không tự chạy git commit, push, reset hoặc xóa file
- Sau khi sửa phải chạy dotnet build và báo kết quả
- Điều gì chưa rõ thì ghi "CẦN XÁC NHẬN", không tự đoán