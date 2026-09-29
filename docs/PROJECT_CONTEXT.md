# BMS — Bối cảnh để tiếp tục dự án

Cập nhật: 28/09/2026. Trao đổi và hướng dẫn bằng tiếng Việt, từng bước cho người mới học Git và xây dựng dự án.

**Trạng thái mới nhất:** đã khởi tạo và kiểm tra BMS_Starter trên localhost\SQLEXPRESS. Xem mục 7; các kết quả ở mục 2–3 là lần khảo sát trước khi khởi tạo.

## 1. Những điều đã thống nhất với người dùng

- Dự án SWP391: Billiard Management System, quản lý quán bida; nhóm 5 người.
- Công nghệ đã chọn: C# và SQL Server.
- Hướng triển khai được đề xuất: ASP.NET Core MVC, Razor Views, Bootstrap và ASP.NET Core Identity. Chưa có project ứng dụng trong repo tại thời điểm kiểm tra.
- Mục tiêu đợt này: mỗi thành viên có màn hình chạy được và đọc/ghi SQL Server.
- Chưa làm thanh toán, gọi món, chuyển bàn, tách/gộp hóa đơn.
- Dùng chung Identity cho đăng nhập và tài khoản nhân viên; không tạo kho tài khoản Employee riêng.
- SQL-first: SQL quản lý schema. Không áp dụng migration khởi tạo lên bảng đã tồn tại; không gọi EnsureCreated/Migrate để khởi tạo lại database này.
- Phải kiểm tra file trước khi sửa; không tự làm lại database hoặc ghi đè nội dung chưa đọc.

| Thành viên | Phạm vi |
|---|---|
| 1 — Staff | Quản lý bàn, mở phiên, theo dõi thời gian, đóng phiên |
| 2 — Staff | Quản lý đặt bàn, tìm/lọc, chi tiết, xác nhận khách đến, hủy đặt |
| 3 — Customer | Đăng ký/đăng nhập, đặt bàn, xem lượt đặt của mình |
| 4 — Admin | Quản lý thực đơn và tồn kho |
| 5 — người dùng chat này | Quản lý nhân viên: danh sách, tìm kiếm, tạo Staff, sửa thông tin, khóa/mở khóa |

## 2. Nguồn đã tìm thấy và đọc

Trong `BMS_Database_Starter/`:

- `README_VI.md`: phạm vi, hướng dẫn chạy và quy ước nghiệp vụ.
- `IDENTITY_INTEGRATION.md`: model tài khoản, mapping, cấu hình và bảo vệ tài khoản.
- `01_CreateDatabase.sql`: tạo BMS_Starter và 13 bảng; từ chối khởi tạo nếu đã có bảng người dùng.
- `02_BusinessProcedures.sql`: 5 procedure cho đặt bàn và phiên chơi.
- `03_DemoData.sql`: dữ liệu mẫu; từ chối seed khi các bảng nền được kiểm tra đã có dữ liệu.
- `04_Verify.sql`: truy vấn kiểm tra chỉ đọc.
- `VALIDATION.txt`: báo cáo kiểm tra tĩnh từ lần tạo bộ file; không phải bằng chứng đã chạy SQL Server.

Bộ file báo đã parse cú pháp bằng SQLFluff. Phiên kiểm tra này đã đọc file, chưa chạy lại parser, chưa thực thi 01–03, chưa kiểm thử nghiệp vụ/concurrency, chưa biên dịch C# tham khảo.

## 3. Kết quả kiểm tra máy và repo

- Repo: `C:\Users\Admin\OneDrive\Documents\GitHub\billiard-management-system`.
- Git 2.54.0.windows.1; nhánh `main`; commit `be0e0fe` (Initial commit).
- Ban đầu chỉ README.md được theo dõi; thư mục BMS_Database_Starter đang untracked — có trên máy nhưng chưa được Git ghi nhận trong commit.
- Chưa có .sln/.csproj, global.json, appsettings hoặc mã ứng dụng. Chưa có PROJECT_CONTEXT.md trước phiên này.
- Không tìm thấy AGENTS.md trong repo và các thư mục cha đã kiểm tra từ C:\Users\Admin đến thư mục GitHub.
- Có SDK .NET 9.0.308 và 10.0.301; có ASP.NET Core runtime 10.0.9.
- Có sqlcmd dùng ODBC Driver 17. Chưa kiểm tra SSMS/IDE có mở và sử dụng được hay không.

| Server kết nối | Phiên bản engine báo về | Kết quả chỉ đọc |
|---|---|---|
| localhost | 17.0.1000.7 | Kết nối được; không thấy BMS_Starter |
| localhost\SQLEXPRESS | 15.0.2000.5 | Kết nối được; không thấy BMS_Starter |
| localhost\MSSQLSERVER1 | 15.0.2000.5 | Kết nối được; không thấy BMS_Starter |

Instance MSSQLSERVER01 đang dừng; không khởi động và không kiểm tra database của instance này. Không suy ra rằng mọi instance trên máy đều chưa có BMS_Starter.

Kết nối Windows Authentication trong sandbox ban đầu lỗi SSL/security package. Chạy kiểm tra chỉ đọc ngoài sandbox thành công. Không thay cấu hình SQL Server, không tạo/sửa/xóa database. Đã dùng `sqlcmd -E -C` cho kết nối local.

## 4. Các ràng buộc lấy từ bộ starter

- Staff là AspNetUsers có role Staff qua AspNetUserRoles/AspNetRoles. UserId là chuỗi nvarchar(450).
- ApplicationUser bổ sung FullName, IsActive, CreatedAtUtc, EmployeeCode, HireDate. Giữ các trường tài khoản chuẩn của Identity.
- Mapping phải khớp schema Identity V1, khóa login/token dài 128, các độ dài và unique index được mô tả trong hướng dẫn.
- Có 5 tài khoản mẫu: admin.demo, staff.demo01, staff.demo02, customer.demo01, customer.demo02. PasswordHash đều NULL trong seed; chưa có mật khẩu mặc định. staff.demo02 bị vô hiệu hóa.
- Đặt mật khẩu bằng UserManager, lấy bí mật từ cấu hình local; không lưu mật khẩu trong SQL/source. Initializer chỉ chạy Development và chỉ thêm mật khẩu khi chưa có, không reset mật khẩu cũ.
- Tạo tài khoản và gán role trong cùng transaction. Backend gán Staff cho nhân viên và Customer cho đăng ký công khai, không nhận role tùy ý từ form.
- IsActive khác LockoutEnd. Cần chặn đăng nhập và kiểm tra cookie khi tài khoản bị khóa; giữ kiểm tra SecurityStamp. Để khóa có hiệu lực ngay ở request tiếp theo, kiểm tra IsActive mỗi request được bảo vệ.
- Controller nhân viên chỉ cho Admin; POST có chống giả mạo yêu cầu (anti-forgery). Kiểm tra đối tượng thực sự là Staff khi sửa/khóa, kể cả khi ID trên URL bị đổi.
- Không xóa nhân viên có lịch sử. Dùng UserManager cho cập nhật tài khoản và xử lý lỗi trùng username/email/phone/EmployeeCode dễ hiểu.
- Thời điểm Utc lưu UTC, hiển thị giờ Việt Nam; HireDate là ngày, không đổi múi giờ.
- Các luồng booking/phiên chơi dùng procedure có tham số và xác thực người gọi tại backend. Check-in không tự mở phiên; đóng phiên chuyển bàn sang AwaitingPayment, chưa có thanh toán để trả bàn.
- Các quy tắc check-in sớm 15 phút, giá giờ cố định, tính tiền theo giây và một role/tài khoản là đề xuất v1 trong starter, chưa coi là quy tắc được cả nhóm/thầy duyệt.

## 5. Lộ trình tiếp theo — đề xuất, chưa thực hiện

1. Dùng `localhost\SQLEXPRESS` làm instance phát triển local: đây là lựa chọn đề xuất vì đã kết nối được và khớp ví dụ trong starter; chưa cấu hình ứng dụng trỏ vào đó. Trước lúc tạo phải kiểm tra lại database để tránh trạng thái máy đã thay đổi.
2. Chạy lần lượt 01 → 02 → 03 → 04 trên instance đã chọn, dừng ở lỗi đầu tiên. Nếu database đã tồn tại, kiểm tra schema/dữ liệu trước; không DROP hoặc seed lại để xử lý lỗi.
3. Xác nhận 13 bảng, 5 procedure; seed có 3 role, 5 user, 2 loại bàn, 6 bàn, 4 booking, 2 phiên, 2 danh mục, 6 món. Ba truy vấn nhất quán cuối phải không có dòng lỗi; ban đầu số tài khoản có mật khẩu bằng 0.
4. Dựng project MVC tên đề xuất `Bms.Web` dưới `src/Bms.Web`, nhắm net10.0 dựa trên SDK đã cài và hướng dẫn starter. Chốt cùng nhóm phiên bản SDK/package; EF Core SQL Server và Identity EF dùng major 10 tương thích. Chưa chọn bản vá package cụ thể.
5. Tạo .gitignore trước khi build; bỏ qua bin/obj/.vs và file bí mật. Ghi cấu hình kết nối local, dùng user-secrets cho mật khẩu. Tích hợp ApplicationUser/ApplicationDbContext đúng schema SQL, không tạo migration khởi tạo.
6. Làm nền đăng nhập/đăng xuất và phân quyền chung, phối hợp với thành viên 3 để dùng chung một cấu hình Identity. Đặt mật khẩu admin.demo bằng initializer Development, rồi kiểm tra đăng nhập Admin.
7. Làm danh sách Staff từ SQL trước; tiếp theo tìm theo họ tên/mã/số điện thoại, lọc Active/Inactive và phân trang; sau đó tạo Staff, sửa thông tin, khóa/mở khóa.
8. Kiểm thử các luồng ở mục 6 trước khi chia nền cho nhóm. Mỗi người dùng database local riêng, cùng bộ schema; không chia sẻ database cá nhân qua connection string có mật khẩu trong Git.

## 6. Tiêu chí hoàn thành phần nhân viên

- Admin đăng nhập và xem đúng Staff, gồm cả tài khoản bị khóa; không lẫn Admin/Customer.
- Tạo Staff có thể đăng nhập bằng cùng hệ thống Identity; nếu gán role lỗi thì không để tài khoản tạo dở.
- Tìm kiếm/lọc/phân trang hoạt động; sửa được thông tin hợp lệ và giữ dữ liệu sau tải lại trang.
- Trùng username/email/phone/mã nhân viên báo lỗi rõ ràng, không làm hỏng dữ liệu.
- Staff/Customer không truy cập được quản lý nhân viên; sửa ID không cho phép tác động Admin/Customer.
- Khóa Staff chặn đăng nhập mới và request được bảo vệ tiếp theo của phiên đang đăng nhập; mở khóa cho phép đăng nhập lại, đồng thời vẫn tôn trọng khóa tạm Identity nếu còn hiệu lực.
- POST thiếu anti-forgery token bị từ chối; thao tác sửa đồng thời được xử lý qua cơ chế concurrency phù hợp.
- Build thành công và kiểm tra thực tế với SQL Server; không coi giao diện tĩnh là hoàn thành.

## 7. Trạng thái bàn giao mới nhất — đã hoàn thành bước database

Người dùng yêu cầu bắt đầu làm dần. Đã chọn localhost\SQLEXPRESS cho database local và thực hiện bước 1–3 của lộ trình ngày 28/09/2026:

- Kiểm tra lại: BMS_Starter chưa tồn tại trước khi chạy.
- Chạy 01 thành công: 13 bảng. Chạy 02 thành công: 5 procedure.
- Lần chạy 03 đầu tiên lỗi 1934 vì sqlcmd session thiếu QUOTED_IDENTIFIER cho filtered indexes; transaction đã rollback và quy trình dừng ở lỗi đầu tiên.
- Bổ sung các SET options cần thiết ngay trong 03_DemoData.sql để script tự hoạt động với kết nối sqlcmd mới. Không đổi schema, không tạo lại database/bảng.
- Chạy lại riêng 03 rồi 04 thành công. Số lượng tất cả dữ liệu mẫu đúng kỳ vọng; 3 truy vấn nhất quán không có dòng lỗi; số tài khoản có mật khẩu là 0. Hai nhân viên NV001/NV002 hiển thị đúng tiếng Việt và trạng thái.
- SQL Server cảnh báo độ dài tối đa của khóa ghép PK_AspNetUserRoles/PK_AspNetUserTokens có thể vượt 900 byte nếu dùng các chuỗi khóa rất dài. Dữ liệu mẫu đã ghi thành công; khi tạo tài khoản nên dùng ID ngắn mặc định của Identity, không tùy ý đưa ID dài. Chưa thay thiết kế khóa.

Đã xác minh thực thi schema/procedure/seed và truy vấn kiểm tra trên SQL Server thật; chưa kiểm thử gọi từng procedure, yêu cầu đồng thời hoặc tích hợp C#. Chưa tạo website, chưa đặt mật khẩu, chưa commit/push.

Khi tiếp tục: kiểm tra git status, đọc hai hướng dẫn starter, bắt đầu bước 4 — dựng project MVC Bms.Web. Database đã có dữ liệu: không chạy lại 01/03. Có thể chạy 04 để kiểm tra chỉ đọc. Mật khẩu mẫu vẫn chưa được thiết lập.

## 8. Trạng thái bàn giao — đã dựng project MVC (28/09/2026)

Database đổi tên từ BMS_Starter sang **BilliardDB** theo yêu cầu người dùng:
- Chỉnh sửa tên database trong 01, 02, 03. Chạy lại 01→02→03→04 thành công trên BilliardDB.
- Xác nhận 04: 13 bảng, 5 procedure, 3 role, 5 user, 2 loại bàn, 6 bàn, 4 booking, 2 phiên, 6 sản phẩm; 3 truy vấn nhất quán trả 0 lỗi; mật khẩu = 0.
- DROP BMS_Starter thành công. Chỉ còn BilliardDB trên localhost\SQLEXPRESS.

Project ASP.NET Core MVC **Bms.Web** đã tạo tại `src/Bms.Web/`:
- Framework: net8.0. Packages: EF Core SQL Server 8.0.31, Identity EF 8.0.31.
- `Data/ApplicationUser.cs`: kế thừa IdentityUser, thêm FullName/IsActive/CreatedAtUtc/EmployeeCode/HireDate.
- `Data/ApplicationDbContext.cs`: mapping khớp schema SQL V1, filtered indexes, khóa login/token 128.
- `Program.cs`: AddIdentity SchemaVersion=Version1, MaxLengthForKeys=128, cookie paths /Account/Login.
- `appsettings.Development.json`: connection string trỏ BilliardDB (file bị gitignore).
- `.gitignore` tạo ở gốc repo: bỏ qua bin/obj/.vs/secrets/appsettings.Development.json.
- Build: 0 Warning, 0 Error. Khởi động: http://localhost:5000, môi trường Development.

Chưa làm: trang đăng nhập/đăng xuất (Account controller/view), đặt mật khẩu admin.demo, màn hình quản lý nhân viên. Chưa commit/push.

Bước tiếp theo: làm AccountController (Login/Logout) + đặt mật khẩu admin.demo qua initializer Development, rồi kiểm tra đăng nhập Admin thật sự.
