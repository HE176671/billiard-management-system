# Bàn giao cho AI tiếp theo — dự án Billiard Management System

Ngày bàn giao: 28/09/2026. File này đủ để hiểu cuộc trao đổi trước, nhưng khi viết code phải đọc các file thật trong repo. Không suy đoán nội dung file chưa được cung cấp.

## Lời nhắn của người dùng

Tôi mới bắt đầu học Git và xây dựng dự án SWP391. Hãy giải thích bằng tiếng Việt, từng bước dễ hiểu và làm dần từng phần. Tôi đang tiếp tục công việc với AI trước; hãy đọc trạng thái dưới đây để tiếp tục đúng chỗ, không làm lại từ đầu.

## Dự án và phân công

- Tên: Billiard Management System — quản lý quán bida.
- Công nghệ đã chọn: C# và SQL Server.
- Hướng triển khai được đề xuất: ASP.NET Core MVC, Razor Views, Bootstrap, ASP.NET Core Identity. Chưa tạo project C#.
- Nhóm 5 người; mục tiêu đợt này là mỗi người có màn hình chạy được với SQL Server.

| Người | Chức năng |
|---|---|
| 1 — Staff | Quản lý bàn, mở phiên, theo dõi thời gian, đóng phiên |
| 2 — Staff | Quản lý đặt bàn, tìm/lọc, chi tiết, xác nhận khách đến, hủy đặt |
| 3 — Customer | Đăng ký/đăng nhập, đặt bàn, xem lượt đặt của mình |
| 4 — Admin | Quản lý thực đơn và tồn kho |
| 5 — tôi | Admin quản lý nhân viên: danh sách, tìm kiếm, tạo tài khoản Staff, sửa thông tin, khóa/mở khóa |

Chưa làm thanh toán, gọi món, chuyển bàn, tách/gộp hóa đơn. Không mở rộng phạm vi khi chưa cần thiết.

## Trạng thái mới nhất: database ĐÃ chạy thật, website CHƯA có

Repo trên máy Windows:

```text
C:\Users\Admin\OneDrive\Documents\GitHub\billiard-management-system
```

Shell: PowerShell. Git 2.54.0.windows.1; đang ở nhánh main, commit gần nhất được kiểm tra là be0e0fe (Initial commit).

Máy có .NET SDK 9.0.308 và 10.0.301, ASP.NET Core runtime 10.0.9. Đề xuất tiếp theo dùng net10.0, chưa tạo global.json hay chọn bản vá NuGet. Có sqlcmd (ODBC Driver 17). Chưa xác minh SSMS/IDE sử dụng được.

Database local đã tạo:

```text
Server: localhost\SQLEXPRESS
Database: BMS_Starter
Authentication: Windows Authentication
Engine version: 15.0.2000.5
```

Database này có dữ liệu rồi. KHÔNG chạy lại 01_CreateDatabase.sql hoặc 03_DemoData.sql. KHÔNG DROP database/bảng để bắt đầu lại. Nếu sang máy khác, trạng thái local này không tự đi theo Git: phải kiểm tra máy mới trước.

## AI trước đã thực hiện những gì?

1. Đọc README.md, toàn bộ bộ starter và kiểm tra repo/môi trường.
2. Kiểm tra kết nối chỉ đọc với localhost, localhost\SQLEXPRESS và localhost\MSSQLSERVER1. Lúc khảo sát cả ba chưa có BMS_Starter. Instance MSSQLSERVER01 đang dừng, không khởi động hoặc kiểm tra nó.
3. Sau khi tôi yêu cầu “làm dần cho tôi khởi đầu đi”, chọn localhost\SQLEXPRESS và kiểm tra lại database chưa tồn tại.
4. Chạy 01 thành công, tạo 13 bảng. Chạy 02 thành công, tạo 5 procedure.
5. Script 03 lần đầu lỗi SQL 1934 vì session sqlcmd thiếu QUOTED_IDENTIFIER khi ghi vào bảng có filtered indexes. Transaction rollback; không chạy tiếp khi lỗi.
6. Sửa ngay 03_DemoData.sql, thêm các SET options bên dưới trước transaction, rồi chỉ chạy lại 03 và 04. Cả hai thành công.
7. Cập nhật docs/PROJECT_CONTEXT.md và BMS_Database_Starter/VALIDATION.txt với kết quả thực tế.

Thiết lập đã thêm vào 03:

```sql
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
```

Không thay thiết kế schema. SQL Server có cảnh báo độ dài tối đa của khóa ghép PK_AspNetUserRoles/PK_AspNetUserTokens; dữ liệu mẫu vẫn ghi thành công. Chi tiết trong PROJECT_CONTEXT.md. Chưa có lý do thực tế để tự thiết kế lại khóa.

Kết quả 04_Verify.sql:

| Hạng mục | Số lượng đúng kỳ vọng |
|---|---:|
| Bảng | 13 |
| Procedure nghiệp vụ | 5 |
| Role | 3 |
| User | 5 |
| Loại bàn / bàn | 2 / 6 |
| Booking / phiên chơi | 4 / 2 |
| Danh mục / sản phẩm | 2 / 6 |
| User có mật khẩu | 0 |

Ba truy vấn kiểm tra nhất quán cuối trả về 0 dòng lỗi. Hai nhân viên NV001/NV002 hiển thị đúng tiếng Việt, một Active và một Inactive. Chưa kiểm thử thực thi từng procedure hoặc yêu cầu đồng thời; kết quả trên không chứng minh toàn bộ nghiệp vụ đã đúng.

## File trong repo cần đọc

```text
README.md
BMS_Database_Starter/
  README_VI.md
  IDENTITY_INTEGRATION.md
  01_CreateDatabase.sql
  02_BusinessProcedures.sql
  03_DemoData.sql
  04_Verify.sql
  VALIDATION.txt
docs/
  PROJECT_CONTEXT.md
  AI_HANDOFF_VI.md
```

PROJECT_CONTEXT.md là tài liệu quyết định và tiến độ lâu dài. Mục 2–3 của nó mô tả khảo sát ban đầu; mục 7 là trạng thái sau khi đã chạy database. VALIDATION.txt giữ báo cáo tĩnh ban đầu và bổ sung kết quả chạy local ở cuối.

Git status trước khi tạo file bàn giao:

```text
?? BMS_Database_Starter/
?? docs/
```

Hai thư mục vẫn untracked, chưa commit/push trong cuộc trao đổi này. Chưa có .sln/.csproj, .gitignore, ứng dụng, màn hình, cấu hình kết nối hay mật khẩu. Không nói rằng website đã hoàn thành. Không tìm thấy AGENTS.md trong repo ở lần kiểm tra trước; kiểm tra lại nếu môi trường đã thay đổi.

## Quy tắc tích hợp bắt buộc giữ

- SQL-first: dùng SQL scripts quản lý schema; C# ánh xạ vào bảng đã tồn tại. Không gọi EnsureCreated(), Migrate() hoặc Update-Database để khởi tạo các bảng này; không áp dụng migration Identity từ template lên BMS_Starter.
- Nhân viên và đăng nhập dùng chung Identity. Nhân viên là AspNetUsers có role Staff qua AspNetUserRoles/AspNetRoles; không tạo bảng tài khoản Employee riêng.
- ApplicationUser kế thừa IdentityUser, thêm FullName, IsActive, CreatedAtUtc, EmployeeCode, HireDate. UserId là chuỗi; mapping theo IDENTITY_INTEGRATION.md, Identity schema V1, khóa login/token 128.
- Dùng UserManager để tạo tài khoản, đặt mật khẩu, sửa tài khoản. Tạo user và gán Staff trong cùng transaction; lỗi phải rollback. Public registration chỉ gán Customer từ backend.
- Mẫu gồm admin.demo, staff.demo01, staff.demo02, customer.demo01, customer.demo02. Tất cả PasswordHash NULL; staff.demo02 IsActive=false. Không có mật khẩu mặc định.
- Khi dựng ứng dụng, đặt mật khẩu admin.demo bằng UserManager.AddPasswordAsync trong initializer Development, lấy bí mật từ user-secrets/biến môi trường; chỉ thêm nếu chưa có, không reset mật khẩu cũ và không commit mật khẩu.
- Quản lý nhân viên cần Authorize role Admin và anti-forgery cho POST. Khi sửa/khóa theo ID phải kiểm tra đúng Staff để không tác động Admin/Customer.
- IsActive là khóa quản trị, khác khóa tạm LockoutEnd. Chặn cả đăng nhập mới và cookie phiên đang đăng nhập; giữ kiểm tra SecurityStamp mặc định. Yêu cầu khóa có hiệu lực ở request được bảo vệ tiếp theo.
- Không xóa nhân viên có lịch sử. Xử lý lỗi trùng username/email/phone/EmployeeCode bằng thông báo dễ hiểu.
- Booking/phiên dùng procedure có tham số và backend xác thực người gọi. Check-in không tự mở phiên; đóng phiên đưa bàn sang AwaitingPayment.
- Thời điểm Utc lưu UTC, hiển thị giờ Việt Nam; HireDate không đổi múi giờ. Quy tắc check-in sớm 15 phút và tính giá trong starter vẫn là đề xuất v1, chưa coi là thầy đã duyệt.

## Bước tiếp theo nên làm

Tiếp tục từ nền ứng dụng, không khởi tạo lại database:

1. Kiểm tra git status, file thật và SDK hiện có. Đọc README_VI.md và IDENTITY_INTEGRATION.md trước khi viết code; nếu không truy cập được repo, yêu cầu tôi cung cấp các file cần thiết.
2. Dựng project ASP.NET Core MVC tên đề xuất Bms.Web dưới src/Bms.Web, target net10.0; thêm .gitignore trước khi build. Chọn EF Core SQL Server/Identity EF cùng major tương thích; chưa có bản vá package được chốt.
3. Ánh xạ ApplicationUser/ApplicationDbContext đúng schema SQL, cấu hình kết nối local và chứng minh ứng dụng đọc được database. Không tạo schema bằng EF.
4. Làm nền đăng nhập/đăng xuất và quyền Admin dùng chung với thành viên 3. Tạo mật khẩu Admin qua cơ chế Development ở trên.
5. Làm màn nhân viên từng phần: danh sách → tìm/lọc/phân trang → tạo Staff → sửa → khóa/mở khóa. Kiểm thử từng bước với SQL Server thật.
6. Giải thích ngắn gọn cho tôi mỗi bước đã làm gì, cách chạy/xem kết quả và bước tiếp theo; cập nhật PROJECT_CONTEXT.md sau mỗi mốc.

Connection string đề xuất cho local (chưa ghi vào ứng dụng):

```text
Server=localhost\SQLEXPRESS;Database=BMS_Starter;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;
```

TrustServerCertificate là lựa chọn local, không tự mang sang production. Nếu viết JSON, escape dấu gạch chéo ngược đúng cú pháp.

Lệnh kiểm tra chỉ đọc đã dùng (PowerShell, từ thư mục repo):

```powershell
sqlcmd -S 'localhost\SQLEXPRESS' -E -C -l 5 -b -f 65001 -i 'BMS_Database_Starter/04_Verify.sql'
```

Trong môi trường AI trước, Windows Authentication bên trong sandbox lỗi “No credentials are available in the security package / Encryption not supported on the client”; chạy ngoài sandbox sau khi được cấp quyền thì thành công. Không vội kết luận SQL Server hỏng hoặc thay cấu hình server. Nếu công cụ cần quyền, dùng cơ chế xin quyền của môi trường mới, không tìm cách vượt quyền.

## Cách làm việc với tôi

Tôi muốn AI trực tiếp hỗ trợ làm từng bước, đồng thời giải thích để tôi học. Không đổ toàn bộ kiến trúc phức tạp vào một lần; không hỏi lại những điều đã rõ trong file này. Trước khi sửa phải đọc file hiện có. Phân biệt rõ đã thực hiện, đã kiểm chứng và mới đề xuất. Nếu chuyển qua AI không có quyền truy cập máy, hãy hướng dẫn thao tác/lệnh cụ thể và chờ kết quả thật thay vì khẳng định đã chạy.
