# BMS — Bối cảnh để tiếp tục dự án

Cập nhật: 01/10/2026. Trao đổi và hướng dẫn bằng tiếng Việt, từng bước cho người mới học Git và xây dựng dự án.

**Trạng thái mới nhất:** ngày 01/10/2026 đã soạn SDS phần Hùng, sửa verify chọn nhầm database và bổ sung quan hệ combo có giờ chơi + đồ ăn/nước uống. Database **localhost / BilliardDB** đã có **24 bảng nghiệp vụ, 5 procedure**; SSMS có thêm bảng hỗ trợ dbo.sysdiagrams. Repo có MVC .NET 8 và code tài khoản/nhân viên. Người dùng yêu cầu chia sẻ database qua main, xem mục 12; các cập nhật trước ở mục 9–11. Các mục trước lưu lịch sử; không dùng trạng thái cũ “chưa có project” hoặc database 13 bảng để ghi đè source/schema hiện tại. Không chạy lại Full/seed/05 trên database đã nâng cấp.

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

## 9. SDS phần Hùng — 01/10/2026

Người dùng đã mở rộng phân công tài liệu: Hùng phụ trách AspNetUsers/AspNetRoles/AspNetUserRoles/MembershipTiers, kiến trúc tổng thể, package diagram, class diagram và sequence Login/Tạo nhân viên/Đổi mật khẩu, cơ chế phân quyền/bảo mật. Các thành viên khác nhận bàn/giá/phiên; booking/ca làm; thanh toán VNPay/điểm/lịch sử; F&B/checkout/báo cáo/nâng hạng. Phân công này thay cho giới hạn tài liệu giai đoạn đầu; không có nghĩa mọi module mới đã được code.

- Đọc mẫu SDS template2.docx qua Google Drive trong lượt phân công trước; lần này đối chiếu schema và code local hiện tại.
- Repo sạch trước khi soạn; HEAD được đọc là 4cbb885. Có MVC net8.0, packages Identity EF/EF SQL Server khai báo 8.*; có AccountController, EmployeeController, ProfileController, DashboardController và các controller khung Staff/Customer.
- File schema hiện là BMS_Database_Starter/BilliardDB_Full.sql với 21 CREATE TABLE; MembershipTiers có Id/TierName/DiscountPercent. AspNetUsers thêm RewardPoints/TierId. Chưa kiểm chứng database live đã được nâng cấp lên 21 bảng.
- Tài liệu phần Hùng: docs/SDS_HUNG.md; bản xem có 7 SVG nhúng không cần Internet: docs/SDS_HUNG_OFFLINE.html; bản dựng bằng Mermaid: docs/SDS_HUNG.html. Nguồn 7 sơ đồ (.mmd), ảnh vector (.svg), README và script tạo preview: docs/sds-hung/. Đã kiểm tra cả 7 sơ đồ render thành công trong trình duyệt; HTML offline có đủ 7 SVG, không có script hoặc ký tự thay thế lỗi Unicode. Không coi đây là kiểm thử chức năng ứng dụng.
- SDS ghi rõ thiết kế cần bổ sung: transaction nguyên tử CreateAsync + AddToRoleAsync; kiểm tra IsActive cho cookie; mapping thành viên; cấu hình ngưỡng nâng hạng. Không khẳng định source đã đáp ứng các yêu cầu này.
- Phân công 20 bảng còn thiếu AspNetRoleClaims so với 21 bảng SQL; đề xuất nhóm giao TV2 cùng các bảng Identity phụ, chưa tự chốt thay người dùng.
- Ghi nhận để xử lý sau: script Full còn USE BMS_Starter ở đoạn verify và một số chuỗi seed có dấu hiệu lỗi mã hóa. Không chạy hoặc sửa SQL trong tác vụ viết SDS.
- Chỉ thay đổi tài liệu; chưa sửa code ứng dụng/database, chưa chạy kiểm thử chức năng, chưa commit/push hoặc ghi vào Google Doc chung.

## 10. Sửa lỗi kiểm tra database từ ảnh SSMS — 01/10/2026

- Người dùng chạy BilliardDB_Full trong SSMS kết nối `localhost` (engine 17.0.1000.7), báo tạo 21 bảng/5 procedure/seed thành công nhưng verify lỗi Invalid object name dbo.MembershipTiers.
- Xác nhận chỉ đọc: `localhost` có cả BilliardDB và BMS_Starter; BilliardDB có 21 bảng, 5 procedure, có MembershipTiers. `localhost\SQLEXPRESS` có BilliardDB bản 13 bảng, 5 procedure, chưa có MembershipTiers. Đây là hai instance độc lập, không đổi cấu hình kết nối ứng dụng trong lần này.
- Sửa đúng một dòng trong BMS_Database_Starter/BilliardDB_Full.sql ở phần verify: USE BMS_Starter thành USE BilliardDB. Các phần tạo/seed vốn đã dùng BilliardDB.
- Tách phần verify thành BMS_Database_Starter/Verify_BilliardDB.sql (chỉ đọc) và chạy trên localhost thành công: 21 bảng/5 procedure, tất cả số lượng seed đúng, 3 truy vấn nhất quán trả 0 dòng, số user có mật khẩu = 0.
- Không chạy lại tạo database/bảng/seed, không ghi dữ liệu live. Cảnh báo độ dài khóa ghép vẫn là vấn đề riêng, không gây lỗi thiếu bảng trong ảnh.
- Ảnh còn Msg 102 ở dòng 1 của query đang mở; không có nội dung đầu query nên chưa kết luận nguyên nhân. File trên đĩa bắt đầu bằng comment hợp lệ và có UTF-8 BOM; không coi BOM trong file là bằng chứng chắc chắn của lỗi do copy/paste trong SSMS.
- Kết quả đọc seed ở localhost có chuỗi tiếng Việt lỗi mã hóa, khớp dấu hiệu đã ghi nhận trong script Full. Chưa sửa dữ liệu hoặc seed encoding; cần xử lý riêng, không reset database.
- Tài liệu SDS/HTML/ZIP phản ánh thời điểm trước lần sửa này; ghi chú trong đó về USE sai và chưa xác minh schema đã được cập nhật trạng thái bởi mục 10 này, chưa tái xuất bộ SDS.

## 11. Combo gồm giờ chơi và đồ ăn/nước uống — 01/10/2026

- Người dùng hỏi vì sao Combos đứng riêng trong Database Diagram rồi xác nhận combo có cả đồ ăn/nước uống. Schema cũ chỉ có Combos(Id, Name, Price, PlaytimeHours, IsActive), không có FK. Đã bổ sung cấu trúc quan hệ, chưa triển khai tính tiền/bán combo.
- Tạo script nâng cấp cộng thêm `BMS_Database_Starter/05_AddComboRelations.sql`, đã chạy thành công trên **localhost / BilliardDB**. Tạo 3 bảng trống trong transaction; không tạo lại database, không seed lại hoặc sửa/xóa dòng nghiệp vụ cũ. Script từ chối nếu bất kỳ bảng mới nào đã tồn tại; **không chạy lại script trên database này**.
- `ComboItems`: nối Combos với Products, PK(ComboId, ProductId), Quantity cho mỗi gói > 0.
- `SessionCombos`: ghi lượt mua cho PlaySessions; liên kết Combos và AspNetUsers (CreatedById); lưu Quantity và tên/giá/giờ snapshot lúc mua. Cho phép nhiều lượt mua trong một phiên; không tự chốt chính sách cộng dồn/hủy.
- `SessionComboItems`: lưu ProductId, tên món snapshot và QuantityPerCombo của từng lượt mua; không đọc lại thành phần danh mục để tính quyền lợi của lần mua cũ.
- 7 FK mới đều bật/trusted và NO_ACTION khi xóa. Giữ lịch sử bằng ngừng bán IsActive; snapshot còn cần được service bảo vệ khỏi cập nhật trực tiếp. FK không xác minh quyền Staff/Admin, phiên active hay tồn kho.
- Bổ sung cùng block CREATE TABLE vào `BilliardDB_Full.sql` dành cho **máy mới chưa có database**. Không chạy Full trên database đang có dữ liệu. Verify độc lập và verify trong Full dùng BilliardDB, kỳ vọng 24 bảng nghiệp vụ và 5 procedure; đã loại dbo.sysdiagrams khỏi bộ đếm. Database hiện có tổng 25 bảng nếu tính cả bảng hỗ trợ sơ đồ này.
- Kiểm tra SQL thực tế qua `tests/ComboRelations_Rollback.sql`: gói thử có nước + đồ ăn, mua 2 gói; đổi danh mục nhưng giữ nguyên snapshot; từ chối FK sai, Quantity=0 và xóa combo có tham chiếu. PASS; toàn bộ dòng thử rollback, các số lượng bảng trước/sau không đổi. IDENTITY có thể tăng do insert thử dù rollback; không reset ID.
- Verify sau nâng cấp: số lượng seed cũ đúng, 3 kiểm tra nhất quán không có dòng lỗi, ba bảng combo mới trống; tài khoản mẫu trên localhost vẫn PasswordHash=NULL. Chưa sửa lỗi dấu tiếng Việt trong seed.
- Tài liệu chi tiết: `docs/COMBO_DESIGN.md`. Trong SSMS cần Add Table ba bảng mới vào sơ đồ đã có để hiện đường nối; Refresh Tables nếu chưa thấy. Không suy ra thiếu FK chỉ vì sơ đồ cũ chưa thêm bảng.
- Phân công thêm 3 bảng cho TV4; TV1 phối hợp giờ chơi, TV3 phối hợp hóa đơn. Procedure usp_CloseSession hiện tính toàn bộ giờ theo giá giờ; Invoices chưa tách tiền combo. Cần chốt cách tính giờ vượt, đồ gọi thêm, trừ tồn/giao món/hủy và chống tính/trừ kho hai lần trước khi triển khai mua combo.
- Chưa đổi connection string ứng dụng; localhost\\SQLEXPRESS còn BilliardDB bản 13 bảng riêng. Kiểm tra mục tiêu kết nối trước mọi thao tác tiếp theo. Chưa sửa code ứng dụng, chưa commit/push. Bộ SDS/HTML/ZIP chưa tái xuất theo schema 24 bảng; mục này là trạng thái bàn giao mới nhất.

## 12. Chia sẻ database cho nhóm qua main — 01/10/2026

- Người dùng yêu cầu đưa database mới lên Git và xác nhận **đẩy lên main**. Chỉ chia sẻ script SQL, dữ liệu mẫu và hướng dẫn; không đưa .bak/.mdf/.ldf, mật khẩu, cấu hình kết nối cá nhân hoặc bộ SDS chưa được yêu cầu publish vào commit database.
- Trước khi publish: nhánh làm việc Hung ở 4cbb885, origin/main ở 9837936; phần code dashboard chỉ có trên Hung. Commit database được chuẩn bị từ main để không kèm dashboard vào bản cập nhật database.
- Cập nhật README gốc và BMS_Database_Starter/README_VI.md theo tên file hiện có; bỏ hướng dẫn chạy 01–04 đã gộp. Identity integration ghi rõ repo dùng .NET 8, database BilliardDB và model ví dụ cũ chưa đủ cho schema mở rộng.
- Máy mới: chạy BilliardDB_Full.sql một lần. Bản mở rộng 21 bảng: chỉ chạy 05 rồi Verify. Bản 24 bảng: chỉ Verify. Bản 13 bảng chưa có script nâng lên 24; kiểm tra riêng, không DROP hoặc chạy Full lên dữ liệu cũ.
- Đã sửa chuỗi tiếng Việt bị lỗi mã hóa trong **nguồn seed của Full SQL** (tên người dùng/món/danh mục/ghi chú); không sửa/reseed các dòng hiện có trên localhost / BilliardDB. Các ghi chú lỗi dấu ở mục 10–11 là trạng thái dữ liệu live cũ, vẫn cần xử lý riêng.
- Đã chạy Full SQL đã cập nhật trên database thử riêng mới tạo, xác nhận 24 bảng, 5 procedure, số lượng seed, tên tiếng Việt đúng, mật khẩu NULL và 7 FK combo trusted. Kiểm tra combo rollback cũng PASS trên database thử. Database thử đã được xóa; BilliardDB hiện có không bị thay đổi trong bước kiểm thử này.
- Hướng dẫn lấy bản mới từ nhánh main đã được ghi trong README_VI.md. Git chỉ đồng bộ file; thành viên vẫn phải mở đúng SQL script trong SSMS và áp dụng trên instance local của mình.
