# BMS — Database khởi đầu cho nhóm 5 người

Schema mở rộng • 01/10/2026 • SQL Server 2019 trở lên • **BilliardDB: 24 bảng nghiệp vụ, 5 procedure**.

## 1. Bạn mở file nào trước?

Bộ này chứa SQL tạo database local cho từng thành viên. Repo đã có project MVC .NET 8 ở `src/Bms.Web`; SQL vẫn là nguồn quản lý schema. Git chia sẻ script và dữ liệu mẫu, không đồng bộ dữ liệu đang chạy trên SQL Server của các máy.

Thực hiện theo thứ tự:

1. Cài **SQL Server** (dịch vụ lưu dữ liệu).
2. Cài **SQL Server Management Studio — SSMS** (phần mềm mở và chạy SQL).
3. Mở SSMS, kết nối vào SQL Server trên máy.
4. Đọc mục 3 rồi chọn đúng cách cài mới hoặc nâng cấp; không chạy mọi file SQL theo thứ tự tên.
5. Nối project C# đến đúng instance/database trên máy mình. Đọc `IDENTITY_INTEGRATION.md` và bối cảnh mới nhất trong `../docs/PROJECT_CONTEXT.md`.

Các file `01_CreateDatabase.sql` đến `04_Verify.sql` đã được gộp và không còn trong phiên bản hiện tại.

Chỉ cài SSMS thì chưa có SQL Server để lưu dữ liệu.

Liên kết tải chính thức:
- SQL Server: https://www.microsoft.com/en-us/sql-server/sql-server-downloads
- SSMS: https://learn.microsoft.com/en-us/ssms/install/install

Để học trên Windows, có thể chọn SQL Server Express; hoặc Developer nếu chỉ dùng để phát triển/kiểm thử. Ghi lại tên instance khi cài. Không cần mua bản thương mại cho bài thực hành local này.

## 2. Kết nối bằng SSMS

- Chọn **Database Engine** nếu cửa sổ yêu cầu loại server.
- Server name: nhập tên instance đã cài. Nếu chọn instance `SQLEXPRESS`, thường dùng `localhost\SQLEXPRESS`; nếu cài default instance, thường dùng `localhost`.
- Authentication: **Windows Authentication**.
- Nếu local server dùng chứng chỉ tự ký và SSMS báo không tin cậy, bật **Trust server certificate** trong tùy chọn kết nối cho môi trường học local. Không tự áp dụng lựa chọn này cho server triển khai thật.
- Nhấn Connect.

Tên server là ví dụ, không phải tên chắc chắn trên máy bạn. Nếu kết nối thất bại, kiểm tra dịch vụ SQL Server đang chạy và instance đã chọn khi cài.

## 3. Chọn đúng script theo database trên máy

Trong SSMS chọn **File → Open → File** và mở file SQL tải từ repo. Với file Full chỉ dùng cho database chưa có bảng, không bôi đen một phần; nhấn **Execute** hoặc F5. Nếu xuất hiện lỗi, dừng và kiểm tra toàn bộ tab Messages trước khi chạy tiếp. Không bỏ qua lỗi vì có thông báo thành công ở các batch sau.

| Tình trạng máy | File cần chạy | Kết quả |
|---|---|---|
| Chưa có BilliardDB, hoặc database hoàn toàn trống | `BilliardDB_Full.sql` một lần | Tạo 24 bảng, 5 procedure, dữ liệu mẫu và chạy verify |
| Đã có schema mở rộng 21 bảng, có Combos nhưng chưa có 3 bảng liên kết | `05_AddComboRelations.sql` một lần, sau đó `Verify_BilliardDB.sql` | Thêm 3 bảng trống, giữ dữ liệu cũ |
| Đã có cả ComboItems, SessionCombos, SessionComboItems | Chỉ `Verify_BilliardDB.sql` | Kiểm tra chỉ đọc; không tạo/seed lại |
| Bản 13 bảng cũ hoặc schema không xác định | Dừng và kiểm tra schema, báo người phụ trách database | Chưa có script nâng từ bản 13 lên bản 24; không chạy Full/05 để thay thế |

Nếu một file báo lỗi, dừng và sửa lỗi trước khi chạy file tiếp theo. Không tiếp tục chỉ vì thấy vài thông báo “completed”.

Sau khi chạy thành công: **Databases → Refresh → BilliardDB → Tables → Refresh**. Verify phải báo `DatabaseName=BilliardDB`, `TableCount_Expected24=24`, `ProcedureCount_Expected5=5`. SSMS có thể tạo thêm `dbo.sysdiagrams`; verify loại bảng hỗ trợ này khỏi bộ đếm.

Không DROP database hoặc chạy seed lại để xử lý lỗi. File Full có nhiều batch: không chạy trên database đã có bảng. Script 05 từ chối nếu bảng nâng cấp đã tồn tại; không cố chạy lại khi đã áp dụng thành công.

Trên máy Hùng, bản mới đã có ở **localhost / BilliardDB**. Instance **localhost\\SQLEXPRESS** có database cùng tên nhưng là bản 13 bảng độc lập. Mỗi thành viên kiểm tra server của mình; không copy tên instance mà chưa kiểm tra. Không cần lưu `.bak`, `.mdf`, `.ldf`, mật khẩu hoặc connection string cá nhân lên Git.

Để hiện quan hệ combo trong Database Diagram: chuột phải vùng trắng → Add Table → thêm ComboItems, SessionCombos, SessionComboItems → Save.

## 4. Phạm vi đúng với 5 người đã chia

| Người | Phần | Bảng/chức năng nền |
|---|---|---|
| 1 | Staff quản lý phiên | `TableTypes`, `BilliardTables`, `PlaySessions`; mở và đóng phiên |
| 2 | Staff quản lý đặt bàn | `Bookings`; lọc, xem, check-in, hủy |
| 3 | Customer, đăng ký/đăng nhập, đặt bàn | Bộ bảng Identity và `Bookings` |
| 4 | Admin thực đơn/tồn kho | `ProductCategories`, `Products` |
| 5 — bạn | Admin quản lý nhân viên | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles` |

Bảng trên ghi phạm vi màn hình của giai đoạn đầu. Schema nay đã có MembershipTiers, PricingConfigs, Orders, OrderDetails, Invoices, PaymentTransactions, Combos, WorkShifts và 3 bảng liên kết combo. Có bảng không có nghĩa chức năng đã được triển khai. Phân công SDS mở rộng và trạng thái code xem `../docs/PROJECT_CONTEXT.md`.

Combo có giờ chơi và đồ ăn/nước uống: ComboItems nối Combos–Products; SessionCombos nối PlaySessions–Combos–AspNetUsers; SessionComboItems lưu thành phần lúc mua. Ba bảng mới thuộc TV4, phối hợp TV1 (giờ chơi) và TV3 (hóa đơn). Thiết kế chi tiết và phần tính tiền còn phải làm: `../docs/COMBO_DESIGN.md`.

Không có bảng Employee chứa tài khoản độc lập: thông tin nhân viên nằm trong `AspNetUsers`, xác định bằng role Staff. Như vậy tài khoản do bạn tạo dùng được chung với màn đăng nhập.

## 5. Tài khoản và dữ liệu mẫu

| Username | Role | Trạng thái | Id dùng trong ví dụ |
|---|---|---|---|
| admin.demo | Admin | Active | demo-admin |
| staff.demo01 | Staff | Active | demo-staff-01 |
| staff.demo02 | Staff | Inactive | demo-staff-02 |
| customer.demo01 | Customer | Active | demo-customer-01 |
| customer.demo02 | Customer | Active | demo-customer-02 |

**Không có mật khẩu mặc định.** Tất cả `PasswordHash` là NULL. Những dòng này dùng cho danh sách và liên kết dữ liệu; chưa đăng nhập bằng mật khẩu được. Khi có project C#, dùng `UserManager.AddPasswordAsync` để đặt mật khẩu cho dòng mẫu không có mật khẩu, hoặc `UserManager.CreateAsync(user, password)` để tạo tài khoản mới. Không tự điền mật khẩu vào SQL và không dùng MD5/SHA256 thuần để lưu mật khẩu.

Seed còn có:
- 2 loại bàn, 6 bàn: B01 đang chơi; B02/B03 trống; B04 chờ thanh toán; B05 bảo trì; B06 có khách đã check-in, chưa mở phiên.
- 4 booking: hai lượt ngày mai, một đã check-in, một đã hủy.
- 2 phiên: một đang hoạt động, một đã đóng.
- 2 danh mục, 6 món; có món hết hàng và món ngừng bán.

Ngày/giờ mẫu được tính theo lúc chạy script. Booking đã check-in của B06 chỉ dùng mở phiên trong cửa sổ còn hiệu lực; sau đó tạo booking mới, không chạy lại seed để reset cả database.

## 6. Phần quản lý nhân viên của bạn

Màn hình danh sách: mã nhân viên, họ tên, username, email, số điện thoại, ngày vào làm, trạng thái.

Các chức năng:
1. Tìm theo họ tên/mã nhân viên/số điện thoại; lọc Active/Inactive; phân trang.
2. Thêm nhân viên: tạo `ApplicationUser` bằng UserManager và gán role Staff trong cùng transaction.
3. Sửa: dùng UserManager cho tài khoản để giữ đúng normalized fields và concurrency stamp.
4. Khóa/mở khóa: thay đổi `IsActive`; phía đăng nhập và cookie phải kiểm tra cờ này.
5. Không xóa nhân viên có lịch sử nghiệp vụ. Không cho màn này sửa Admin/Customer bằng cách đổi ID trên URL.

Khi tạo nhân viên, backend gán role Staff cố định; không lấy role từ dropdown do trình duyệt gửi. Public registration chỉ gán Customer. Dùng `[Authorize(Roles = "Admin")]` ở controller quản lý nhân viên, và anti-forgery cho các POST thay đổi dữ liệu.

## 7. Quy ước các trạng thái

| Đối tượng | Trạng thái |
|---|---|
| Tài khoản | `IsActive=1` hoặc `0`; `LockoutEnd` là khóa tạm do đăng nhập sai, khác với khóa quản trị |
| Bàn | Available, InUse, AwaitingPayment, Maintenance, Inactive |
| Booking | Confirmed, CheckedIn, Completed, Cancelled, NoShow |
| Phiên chơi | Active, Closed |
| Món | IsActive + StockQuantity; giao diện tự suy ra Available/OutOfStock/Discontinued |

Đặt bàn vào ngày mai chỉ giữ **khoảng thời gian**, không chuyển trạng thái vật lý của bàn ngay sang Reserved. Nhãn “Reserved” trên giao diện phải được suy ra theo booking/khung giờ đang xem.

Check-in là xác nhận khách đã đến, không tự tạo phiên. Đóng phiên dừng giờ và chuyển bàn sang AwaitingPayment. Chưa có thanh toán trong v1 nên chưa có thủ tục trả bàn về Available sau thanh toán.

## 8. Những lựa chọn thiết kế mới cần cả nhóm thống nhất

- Bản này dùng bảng và khóa tài khoản chuẩn Identity; không sao chép nguyên `UserId int/RoleID` từ ERD cũ. UserId là `nvarchar(450)`; quan hệ role qua `AspNetUserRoles`. ERD phải cập nhật theo nếu nhóm nhận thiết kế này.
- SQL scripts quản lý schema trong giai đoạn đầu. Không chạy đồng thời migrations khởi tạo Identity lên các bảng SQL đã tạo. Xem hướng dẫn tích hợp trước khi dùng `Update-Database`.
- Giá giờ cố định theo loại bàn; khi mở phiên chụp giá vào `HourlyRateSnapshot`, thay giá danh mục sau đó không làm đổi giá phiên cũ.
- Procedure đóng phiên hiện tính tiền giờ = giây thực tế × đơn giá/3600, làm tròn đến 1 VND; chưa áp dụng quyền lợi combo dù schema combo đã có.
- Check-in được phép từ 15 phút trước giờ hẹn đến trước giờ kết thúc booking. Đây là đề xuất v1, không phải quy tắc đã được thầy duyệt; có thể sửa điều kiện trong procedure.
- Không áp dụng auto-cancel 5 phút; trạng thái NoShow được dự phòng nhưng chưa có tác vụ tự chuyển.
- Booking dùng khoảng nửa mở: `[StartAtUtc, EndAtUtc)`. Một lượt kết thúc 15:00 và lượt sau bắt đầu 15:00 không trùng.
- Mọi thời điểm có hậu tố Utc lưu giờ UTC. Giao diện nhập giờ Việt Nam phải đổi sang UTC trước khi lưu và đổi lại khi hiển thị; không trừ 7 giờ hai lần. Ngày vào làm là `date`, không đổi múi giờ.
- Mỗi tài khoản được ứng dụng gán một vai trò trong bản đầu; schema Identity vẫn hỗ trợ nhiều vai trò. UI không cho Customer tự nâng quyền.

## 9. Cách backend gọi nghiệp vụ

Dùng tham số SQL (parameterized commands), không ghép chuỗi SQL từ dữ liệu form.

| Procedure | Mục đích | Trả về |
|---|---|---|
| usp_CreateBooking | Kiểm tra giờ, tài khoản Customer, trùng khoảng rồi thêm booking | BookingId |
| usp_CancelBooking | Hủy Confirmed; Customer chỉ hủy lượt sắp tới của mình, Staff/Admin có thể xử lý | Không có bảng kết quả |
| usp_CheckInBooking | Staff/Admin xác nhận khách đến trong cửa sổ check-in | Không có bảng kết quả |
| usp_OpenSession | Mở bàn Available; kiểm tra booking nếu có; chặn mở trùng; chụp giá | SessionId |
| usp_CloseSession | Đóng Active, tính tiền giờ, cập nhật bàn AwaitingPayment | Thông tin phiên đã đóng |

Các procedure khóa dòng bàn trong transaction để các thao tác trên cùng bàn lần lượt kiểm tra và ghi. Mọi luồng tạo/đổi booking hoặc mở/đóng phiên phải đi qua các procedure này (hoặc một service thay thế có cùng chiến lược). Không gọi EF `.Add(booking)` trực tiếp rồi mặc định rằng CHECK constraint sẽ tự chặn đặt trùng khoảng thời gian.

Các ID người dùng truyền vào phải lấy từ người đang đăng nhập hoặc dữ liệu đã được backend xác thực quyền; kiểm tra role trong SQL không thay thế xác thực người gọi. Database credential có quyền ghi trực tiếp vẫn có thể bỏ qua procedure.

Walk-in chưa có giờ kết thúc dự kiến. Backend cần hiển thị booking sắp tới và nhân viên phải kiểm soát việc chơi quá giờ; v1 không tự bảo đảm bàn sẽ được trả đúng giờ cho mọi booking tương lai. Chuyển bàn và kéo dài lượt đặt chưa được hỗ trợ.

## 10. Kiểm thử sau khi nối C#

- Tạo Staff trùng username/email/điện thoại: phải báo lỗi, không để lại tài khoản nửa chừng.
- Khóa nhân viên: không đăng nhập lại được; phiên đăng nhập hiện có phải bị từ chối theo cơ chế kiểm tra cookie đã cấu hình.
- Đăng ký Customer không thể gửi role Admin để tự nâng quyền.
- Gửi hai yêu cầu đặt cùng bàn/trùng khoảng đồng thời: chỉ một yêu cầu được tạo.
- Hai nhân viên mở cùng bàn: chỉ một phiên Active.
- Check-in không tự mở phiên; mở phiên có BookingId phải đúng bàn và khách.
- Đóng lại phiên Closed: báo lỗi, không ghi đè EndTime.
- Cập nhật tồn kho âm hoặc giá không dương: bị từ chối.
- Dùng RowVersion khi sửa món/bàn/booking để báo xung đột, không ghi đè im lặng.

## 11. Kết quả kiểm tra của bộ file

Lịch sử kiểm tra nằm trong `VALIDATION.txt`. Ngày 01/10/2026 đã chạy script nâng cấp combo trên localhost / BilliardDB: verify đạt 24 bảng/5 procedure, số lượng seed đúng và 3 truy vấn nhất quán trả 0 dòng. Cả 7 FK combo mới đều bật/trusted.

`tests/ComboRelations_Rollback.sql` kiểm tra lưu thông tin lúc mua, đổi danh mục không đổi lịch sử và từ chối FK/số lượng sai. Dữ liệu thử rollback; IDENTITY có thể tăng dù rollback. File test không phải bước cài đặt bắt buộc. Chưa kiểm thử nghiệp vụ bán combo, tồn kho hoặc hóa đơn trong C#.

Các số lượng `ExpectedAfterSeed` chỉ đúng ngay sau seed. Khi nhóm thêm dữ liệu thật, số lượng khác không tự có nghĩa là lỗi.

## 12. Bước tiếp theo

Các thành viên lấy file từ **nhánh main** của repo và thực hiện mục 3. Nếu đang ở main và không có thay đổi local, dùng `git pull --ff-only origin main`. Nếu đang làm trên nhánh riêng, có thể tải file SQL từ main trên GitHub và mở bằng SSMS; không cần chuyển nhánh hoặc ghi đè code đang làm để cài database.

Mỗi người dùng database local riêng, cùng schema. Chọn đúng server/database trong cấu hình local của MVC; repo dùng .NET 8 và package EF/Identity major 8. Không chạy migration khởi tạo lên các bảng đã tồn tại. Tài khoản seed chưa có mật khẩu: đặt bằng UserManager theo hướng dẫn Identity.

Nguồn SQL mới đã sửa các chữ tiếng Việt bị lỗi trong dữ liệu mẫu. Thay đổi file không sửa các dòng đã seed trên database cũ; không chạy lại seed để sửa dấu.

Nguồn kỹ thuật chính thức:
- Identity model: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model
- Identity configuration: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration
- SQL Server filtered indexes: https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes
