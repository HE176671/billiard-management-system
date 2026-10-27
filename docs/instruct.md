# Hướng dẫn đọc phần đã code trong BMS (Spec 1)

Tài liệu này dành cho người mới làm quen với C# và ASP.NET Core MVC. Mục tiêu là giúp bạn hiểu ứng dụng đang chạy theo luồng nào, mỗi nhóm file làm việc gì và phần nào hiện mới là khung.

> **Nguồn đặc tả:** Bản được đối chiếu là `Spec/spec.md` ở thư mục `SWP391` (nằm ngoài repository ứng dụng), tiêu đề “Customer Experience”, Version 0.1 (DRAFT), ngày 13/10/2026. Spec ghi UC08, UC10, UC09/15, UC07 là các chức năng đã làm theo kế hoạch; UC17 đánh giá đang To Do nhưng vẫn được đánh dấu “Trong đợt này?”. UC14 thanh toán online nằm ngoài phạm vi. Vì tài liệu là bản draft, cần xác nhận với nhóm/giảng viên nếu có bản mới hơn.

## 1. Nhìn tổng thể dự án

Ứng dụng web nằm trong `src/Bms.Web`. Đây là ứng dụng **ASP.NET Core MVC** viết bằng C#:

| Thành phần | Hiểu đơn giản | Ví dụ trong dự án |
|---|---|---|
| **Model / Entity** | Đại diện dữ liệu hoặc bảng trong database | `PlaySession`, `Product`, `ApplicationUser` |
| **ViewModel** | Gói dữ liệu riêng để một trang hiển thị hoặc một form gửi lên | `OrderMenuViewModel`, `EditProfileViewModel` |
| **Controller** | Nhận URL, kiểm tra quyền/dữ liệu, gọi database và chọn trang trả về | `OrderController`, `EmployeeController` |
| **View** | Giao diện HTML được dựng bằng Razor (`.cshtml`) | `Views/Order/Index.cshtml` |
| **DbContext** | Cầu nối giữa C# và SQL Server, cho phép truy vấn các bảng | `ApplicationDbContext` |
| **Service** | Quy tắc tính toán có thể dùng lại, không trực tiếp vẽ giao diện | `LoyaltyConstants`, `OrderConstants` |

Luồng web cơ bản thường là:

```text
Trình duyệt → URL/Route → Controller → DbContext/Identity → SQL Server
                                      ↓
Trình duyệt ← View (HTML) ← ViewModel ←┘
```

Một số từ C# thường gặp:

- `class` là bản mô tả một kiểu đối tượng; ví dụ `Product` mô tả một món trong thực đơn.
- `property` là dữ liệu của đối tượng; ví dụ `Product.Price`.
- `async` / `await` dùng để chờ thao tác I/O như truy vấn database mà không giữ luồng web bận chờ.
- `Task<IActionResult>` là action bất đồng bộ sẽ trả về một kết quả HTTP, thường là trang (`View`), chuyển hướng (`RedirectToAction`) hoặc lỗi (`NotFound`).
- `_db.Products.Where(...).ToListAsync()` là truy vấn bằng Entity Framework Core; biểu thức LINQ được chuyển thành truy vấn SQL khi thực thi.

### Đường đọc code nhanh theo dòng

Mở các vị trí dưới đây theo thứ tự để đi từ lúc website khởi động tới lúc dữ liệu được hiển thị. Số dòng là vị trí bắt đầu; có thể lệch nếu code được sửa sau này.

| Bước | Mở vị trí | Đọc để hiểu |
|---|---|---|
| 1 | [`Program.cs`, dòng 5](../src/Bms.Web/Program.cs#L5), rồi [dòng 11](../src/Bms.Web/Program.cs#L11), [dòng 15](../src/Bms.Web/Program.cs#L15) | Website khởi động, đăng ký kết nối SQL Server, đăng ký Identity |
| 2 | [`ApplicationDbContext.cs`, dòng 7](../src/Bms.Web/Data/ApplicationDbContext.cs#L7), [dòng 12](../src/Bms.Web/Data/ApplicationDbContext.cs#L12) | DbContext là cầu nối; DbSet đại diện các nhóm bảng |
| 3 | [`BmsEntities.cs`, dòng 89](../src/Bms.Web/Data/BmsEntities.cs#L89), [dòng 104](../src/Bms.Web/Data/BmsEntities.cs#L104) | Entity danh mục và món, các cột/quan hệ được ánh xạ |
| 4 | [`OrderController.cs`, dòng 36](../src/Bms.Web/Controllers/OrderController.cs#L36), [dòng 58](../src/Bms.Web/Controllers/OrderController.cs#L58) | Action nạp dữ liệu danh mục và món từ database |
| 5 | [`Views/Order/Index.cshtml`, dòng 248](../src/Bms.Web/Views/Order/Index.cshtml#L248) | Razor lặp qua danh mục và dựng menu trên trang |
| 6 | [`Views/Order/Index.cshtml`, dòng 423](../src/Bms.Web/Views/Order/Index.cshtml#L423), [dòng 473](../src/Bms.Web/Views/Order/Index.cshtml#L473) | JavaScript lưu trạng thái giỏ và dựng lại giao diện |
| 7 | [`OrderController.cs`, dòng 116](../src/Bms.Web/Controllers/OrderController.cs#L116) | POST nhận đơn, kiểm tra nghiệp vụ, ghi dữ liệu và tồn kho |

Để hiểu cách điều khiển URL, tiếp tục đọc [`Program.cs`, dòng 53](../src/Bms.Web/Program.cs#L53) (các route) và dòng 50 (Authentication/Authorization). Các liên kết ở tài liệu Order có hướng dẫn sâu hơn riêng cho phân loại và nhập số lượng.

## 2. Ứng dụng khởi động và định tuyến ra sao?

Đọc [`Program.cs`](../src/Bms.Web/Program.cs) trước:

1. Đăng ký MVC để ứng dụng nhận Controller và Razor View.
2. Đăng ký `ApplicationDbContext` và cấu hình SQL Server bằng connection string `DefaultConnection`.
3. Đăng ký ASP.NET Core Identity để quản lý người dùng, mật khẩu, cookie đăng nhập và role.
4. Xây dựng middleware pipeline. `UseAuthentication()` phải chạy trước `UseAuthorization()`: trước hết xác định người dùng, sau đó mới xét quyền.
5. Khai báo các route riêng như `/Profile`, `/PlayHistory`, `/Loyalty`, `/Order`, rồi đến route mặc định `{controller}/{action}/{id?}`.
6. Trong Development, gọi initializer để đặt mật khẩu cho tài khoản demo `admin.demo` nếu cấu hình `DevSeed:AdminPassword` có giá trị.

Ví dụ: `/Order` được route tới action `Index` của `OrderController`; `/Order/Submit` gửi dữ liệu đặt món tới action `Submit`.

Trong MVC, action GET thường dùng để **mở trang/đọc dữ liệu**. Action POST dùng để **nhận dữ liệu form và thay đổi dữ liệu**. Sau khi POST thành công, nhiều action chuyển hướng sang GET bằng `RedirectToAction` (mẫu Post/Redirect/Get), tránh việc tải lại trang làm gửi form lần nữa.

## 3. Đăng nhập, đăng ký và đổi mật khẩu

Các action đăng nhập/đăng ký/đăng xuất nằm trong [`AccountController.cs`](../src/Bms.Web/Controllers/AccountController.cs); các trường form được mô tả trong `Models/LoginViewModel.cs` và `Models/RegisterViewModel.cs`; giao diện nằm trong `Views/Account/`.

- **Đăng nhập:** tìm tài khoản bằng username, từ chối user không tồn tại hoặc `IsActive == false`, sau đó dùng `SignInManager` kiểm tra mật khẩu và tạo cookie đăng nhập. Sau khi đăng nhập, người dùng được chuyển trang tùy role: Admin tới Employee, Staff tới Staff, Customer tới hồ sơ.
- **Đăng ký công khai:** tạo user qua `UserManager` và gán role `Customer` ở phía server. Form không được tự chọn role Admin/Staff.
- **Đăng xuất:** là POST có `[Authorize]` và `[ValidateAntiForgeryToken]`; thao tác này xóa cookie đăng nhập.
- **Khóa tạm khi nhập sai mật khẩu:** cấu hình trong `Program.cs` giới hạn 5 lần sai và khóa 5 phút.
- **Đổi mật khẩu của chính mình (UC03):** [`ProfileController.cs`](../src/Bms.Web/Controllers/ProfileController.cs) kiểm tra mật khẩu hiện tại, đổi bằng `UserManager`, rồi làm mới cookie.
- **Mật khẩu admin demo:** [`DbInitializer.cs`](../src/Bms.Web/Data/DbInitializer.cs) chỉ làm việc ở môi trường Development. Mật khẩu lấy từ cấu hình/user-secrets, không viết cứng trong code.

### Vì sao dùng `UserManager` thay vì tự viết câu SQL?

Identity không chỉ lưu username và password. Nó còn quản lý mật khẩu đã băm, email chuẩn hóa, security stamp và các quy tắc tài khoản. `UserManager` giúp cập nhật những trường liên quan theo đúng quy ước Identity; không nên tự lưu mật khẩu dạng chữ thường hoặc tự cập nhật bảng Identity bằng SQL.

## 4. Những chức năng Customer đã có

Controller thường giới hạn Customer bằng `[Authorize(Roles = "Customer")]`. Nếu chưa đăng nhập, Identity yêu cầu đăng nhập; nếu đăng nhập nhưng sai role, người dùng không được truy cập.

### 4.1 Xem và sửa hồ sơ — UC08

File chính:

- [`CustomerProfileController.cs`](../src/Bms.Web/Controllers/CustomerProfileController.cs)
- `Models/CustomerViewModels.cs` (`ProfileViewModel`, `EditProfileViewModel`)
- `Views/CustomerProfile/Index.cshtml`, `Views/CustomerProfile/Edit.cshtml`

Luồng sửa hồ sơ:

1. GET `/Profile/Edit` lấy user từ cookie đăng nhập bằng `UserManager.GetUserAsync(User)`.
2. Controller tạo `EditProfileViewModel` và trả về form.
3. Người dùng gửi POST `/Profile/Edit`.
4. Server kiểm tra Data Annotations (họ tên, email, số điện thoại), email/số điện thoại trùng với người khác, rồi cập nhật qua `UserManager`.
5. Thành công thì ghi thông báo vào `TempData` và chuyển về trang hồ sơ.

Họ tên, email và số điện thoại được sửa. Username, role, điểm và hạng không phải dữ liệu người dùng có thể sửa từ form này.

### 4.2 Điểm thưởng và hạng — UC09/15

File chính:

- [`LoyaltyController.cs`](../src/Bms.Web/Controllers/LoyaltyController.cs)
- [`LoyaltyConstants.cs`](../src/Bms.Web/Services/LoyaltyConstants.cs)
- `Views/Loyalty/Index.cshtml`

Điểm không được lưu trong một bảng giao dịch điểm riêng. Trang lấy các phiên chơi của **Customer đang đăng nhập** có trạng thái `Closed` và có `PlaytimeAmount`, rồi tính:

```text
Điểm của phiên = FLOOR(PlaytimeAmount / 10,000)
Tổng điểm       = tổng điểm của các phiên hợp lệ
```

Ngưỡng hạng được đặt trong `LoyaltyConstants`: Đồng dưới 100, Bạc từ 100, Vàng từ 200, Kim cương từ 300 điểm. Trang hiển thị tổng điểm, hạng, số điểm còn thiếu để lên hạng kế tiếp và lịch sử điểm đã phân trang (10 dòng/trang).

### 4.3 Lịch sử và chi tiết phiên chơi — UC10

File chính:

- [`PlayHistoryController.cs`](../src/Bms.Web/Controllers/PlayHistoryController.cs)
- `Models/CustomerViewModels.cs` (`PlayHistoryListViewModel`, `PlayHistoryDetailViewModel`)
- `Views/PlayHistory/Index.cshtml`, `Views/PlayHistory/Detail.cshtml`

Danh sách `/PlayHistory` chỉ truy vấn phiên có `CustomerId` bằng ID lấy từ cookie hiện tại. Kết quả sắp xếp mới nhất trước và phân trang 10 phiên mỗi trang. Chi tiết phiên gồm bàn, giờ bắt đầu/kết thúc, tiền giờ chơi, các món đã gọi, tiền món và tổng tiền.

Khi xem `/PlayHistory/Detail/{id}`, controller tìm phiên theo **cả** `id` lẫn `CustomerId` hiện tại. Nếu không tìm thấy phiên của chính khách đó thì trả về 404. Đây là cách tránh để người dùng đổi số ID trên URL rồi xem phiên của người khác.

### 4.4 Gọi món ăn/uống — UC07

File chính:

- [`OrderController.cs`](../src/Bms.Web/Controllers/OrderController.cs)
- [`OrderConstants.cs`](../src/Bms.Web/Services/OrderConstants.cs)
- `Models/CustomerViewModels.cs` (`OrderMenuViewModel` và các model menu/đơn)
- `Views/Order/Index.cshtml`

Khi mở `/Order`, controller tìm phiên `Active` thuộc về Customer hiện tại:

- Không có phiên: trang không hiển thị menu và báo cần có phiên chơi đang diễn ra.
- Có phiên: hiển thị bàn, menu nhóm theo danh mục và các đơn của khách trong phiên.

Khi gửi đơn, server kiểm tra lại phiên vẫn đang Active; kiểm tra giỏ không rỗng; gộp món trùng; giới hạn số lượng mỗi món từ 1 đến 20; rồi kiểm tra từng món còn bán và đủ tồn kho. Giá gửi từ trình duyệt không được dùng làm giá tin cậy: server đọc giá hiện tại từ database và lưu vào `OrderDetail.UnitPrice` làm **snapshot** (giá tại lúc đặt).

Việc tạo đơn, tạo dòng chi tiết và trừ tồn kho được đặt trong một transaction database. Nếu kiểm tra tồn kho thất bại hoặc lưu lỗi, transaction được rollback thay vì để đơn được tạo một nửa. Đơn mới có trạng thái `Pending`. Mã có thêm kiểm tra đơn giống hệt vừa được gửi trong cửa sổ 5 giây để hạn chế gửi trùng do nhấn nút nhiều lần.

## 5. Chức năng Admin quản lý nhân viên

File chính:

- [`EmployeeController.cs`](../src/Bms.Web/Controllers/EmployeeController.cs)
- [`EmployeeViewModels.cs`](../src/Bms.Web/Models/EmployeeViewModels.cs)
- Các view trong `Views/Employee/`

Toàn bộ `EmployeeController` có `[Authorize(Roles = "Admin")]`. Nhân viên là tài khoản Identity có role `Staff`, không phải một tài khoản đăng nhập tách rời.

Các action hiện có:

| URL/action | Chức năng |
|---|---|
| `/Employee` | Danh sách Staff, tìm theo họ tên/mã/điện thoại/username, lọc Active/Inactive, phân trang 10 dòng |
| `/Employee/Create` | Tạo tài khoản nhân viên và gán role Staff |
| `/Employee/Edit/{id}` | Sửa họ tên, điện thoại, mã nhân viên, ngày vào làm |
| `/Employee/ToggleLock` | Bật/tắt `IsActive` cho Staff |
| `/Employee/ResetPassword/{id}` | Admin đặt lại mật khẩu Staff |

Các action POST có `[ValidateAntiForgeryToken]`. Trước khi sửa/khóa/đặt lại mật khẩu, helper `GetStaffOrNull` xác nhận ID thực sự thuộc về tài khoản role Staff, tránh việc thay ID trên URL để tác động Admin hoặc Customer. Mật khẩu được quản lý bằng Identity `UserManager`.

## 6. Các file dữ liệu và ánh xạ database

- [`ApplicationUser.cs`](../src/Bms.Web/Data/ApplicationUser.cs): mở rộng Identity user với `FullName`, `IsActive`, `CreatedAtUtc`, `EmployeeCode`, `HireDate`.
- [`BmsEntities.cs`](../src/Bms.Web/Data/BmsEntities.cs): các entity nghiệp vụ như `PlaySession`, `BilliardTable`, `TableType`, `Product`, `Order`, `OrderDetail`.
- [`ApplicationDbContext.cs`](../src/Bms.Web/Data/ApplicationDbContext.cs): DbSet và cấu hình độ dài/cột/quan hệ để EF Core ánh xạ C# với bảng SQL.

Ví dụ, `_db.PlaySessions` là tập phiên chơi, `_db.Products` là tập món. `Where` lọc, `OrderBy` sắp xếp, `Select` chọn cột, `ToListAsync` thực thi và lấy kết quả. `Include`/`ThenInclude` dùng để lấy thêm dữ liệu liên quan, ví dụ phiên chơi → bàn → loại bàn.

Các entity ở đây ánh xạ đến schema SQL đang dùng; không có nghĩa ứng dụng được phép tự tạo lại database. Trước khi chạy script database, hãy kiểm tra hướng dẫn và trạng thái máy của bạn.

## 7. Nếu quen Java: DAO ở đâu và sửa dữ liệu như thế nào?

Bạn không nhìn thấy thư mục `DAO` vì project đang dùng **Entity Framework Core (EF Core)**. EF Core đóng vai trò truy cập dữ liệu thông qua `ApplicationDbContext`; trong code hiện tại, controller gọi `_db` trực tiếp thay vì đi qua các lớp DAO/Repository riêng.

| Java thường gặp | Tương đương trong project này |
|---|---|
| Entity/POJO ánh xạ bảng | Entity trong `Data/BmsEntities.cs`, ví dụ `Product` |
| `EntityManager` / Hibernate `Session` | `ApplicationDbContext` |
| `EntityManager.find(...)` / `ProductDao.findById(...)` | `_db.Products.FirstOrDefaultAsync(p => p.Id == id)` |
| JPQL / Criteria / repository query | LINQ như `_db.Products.Where(...).Select(...)` |
| `persist` / `merge` / `UPDATE` entity | Thay thuộc tính trên entity rồi gọi `await _db.SaveChangesAsync()` |
| Mở transaction | `await _db.Database.BeginTransactionAsync()` |
| DAO/Repository class | Trong project hiện tại chưa tách riêng; Controller dùng DbContext trực tiếp |

Đây là lựa chọn kiến trúc, không phải C# không có DAO. Nếu sau này muốn tách tầng truy cập dữ liệu, có thể tạo Repository/Service, nhưng chưa cần tạo một lớp cho mỗi bảng để sửa một truy vấn nhỏ. Khi làm theo code hiện có, hãy tìm Controller đang xử lý chức năng đó và sửa query ở đó.

### A. Phân biệt số lượng đặt mua và số lượng tồn kho

Trong chức năng gọi món có hai “số lượng” khác nhau:

- `OrderItemInputModel.Quantity`: số lượng Customer muốn mua trong **một đơn**, kiểm tra 1–20 món cho mỗi sản phẩm.
- `Product.StockQuantity`: số lượng món hiện còn trong **kho**, được lưu ở bảng `dbo.Products`. SQL schema còn có ràng buộc không cho tồn kho âm.

Khi gửi đơn, [`OrderController.Submit`](../src/Bms.Web/Controllers/OrderController.cs) đọc tồn kho từ `_db.Products`, xác nhận `IsActive` và đủ số lượng rồi trừ `prod.StockQuantity -= item.Quantity`. Vì vậy, không nên sửa giới hạn 1–20 nếu mục đích chỉ là nhập thêm hàng vào kho; hai quy tắc nằm ở hai nơi khác nhau.

### B. Sửa tồn kho một lần để kiểm tra local

Hiện chưa thấy màn hình Admin quản lý món/tồn kho. Để chỉnh dữ liệu local một lần, có thể dùng SSMS:

1. Xác nhận connection string `DefaultConnection` đang trỏ tới đúng SQL Server và database local. Không mặc định rằng mọi máy đều dùng cùng tên instance/database.
2. Xem món và tồn kho hiện tại:

   ```sql
   SELECT Id, Name, StockQuantity, IsActive
   FROM dbo.Products
   ORDER BY Id;
   ```

3. Chọn đúng `Id`, xem lại tên món, rồi cập nhật tồn kho trong database local:

   ```sql
   UPDATE dbo.Products
   SET StockQuantity = 25
   WHERE Id = 1;

   SELECT Id, Name, StockQuantity
   FROM dbo.Products
   WHERE Id = 1;
   ```

Thay `25` và `1` bằng giá trị/ID bạn đã xác minh. Lệnh này chỉ sửa **dữ liệu** món, không sửa cấu trúc bảng. Không chạy lại `03_DemoData.sql` với kỳ vọng nó sẽ cập nhật tồn kho; script seed dùng để thêm dữ liệu khởi đầu và có kiểm tra dữ liệu đã tồn tại.

### C. Sửa qua website (khi xây màn hình Admin)

Để Admin sửa tồn kho từ giao diện, cần bổ sung chức năng quản lý Products vì controller hiện tại chưa có màn CRUD tồn kho. Mẫu luồng sẽ là:

1. Tạo action GET để nạp món theo `id` và trả form.
2. Tạo ViewModel có `ProductId` và `StockQuantity`; kiểm tra tồn kho không âm ở server.
3. Tạo action POST có `[Authorize(Roles = "Admin")]` ở controller và `[ValidateAntiForgeryToken]` trên action.
4. Đọc entity bằng `_db.Products`, kiểm tra có tồn tại, gán `product.StockQuantity = model.StockQuantity`.
5. Gọi `await _db.SaveChangesAsync()` để EF Core ghi `UPDATE` vào SQL Server.
6. Trả lỗi/404 rõ ràng nếu dữ liệu không hợp lệ hoặc món không tồn tại; không nhận giá, tồn kho hay quyền Admin như dữ liệu đáng tin chỉ vì chúng đến từ form.

Ví dụ lõi của thao tác cập nhật (chỉ là phần xử lý, chưa phải controller/view hoàn chỉnh):

```csharp
var product = await _db.Products
    .FirstOrDefaultAsync(p => p.Id == model.ProductId);

if (product == null)
    return NotFound();

if (model.StockQuantity < 0)
{
    ModelState.AddModelError(nameof(model.StockQuantity), "Tồn kho không được âm.");
    return View(model);
}

product.StockQuantity = model.StockQuantity;
await _db.SaveChangesAsync();
```

Nếu muốn đổi **giới hạn số món Customer được đặt**, hãy sửa quy tắc ở `Services/OrderConstants.cs` (`MaxQuantityPerItem`) và kiểm tra giao diện trong `Views/Order/Index.cshtml` có dùng cùng giới hạn. Nếu muốn đổi **số tồn kho ban đầu của dữ liệu seed cho database mới**, sửa `BMS_Database_Starter/03_DemoData.sql`; việc đó không thay đổi database đã khởi tạo.

### D. Chỉnh truy vấn đọc dữ liệu

Ví dụ LINQ dưới đây lấy các món đang bán và còn hàng, sắp xếp theo tên, chỉ lấy cột cần hiển thị:

```csharp
var products = await _db.Products
    .Where(p => p.IsActive && p.StockQuantity > 0)
    .OrderBy(p => p.Name)
    .Select(p => new
    {
        p.Id,
        p.Name,
        p.Price,
        p.StockQuantity
    })
    .ToListAsync();
```

Hãy hiểu các phép LINQ như sau:

- `.Where(...)`: điều kiện `WHERE`.
- `.OrderBy(...)`: điều kiện `ORDER BY`.
- `.Select(...)`: chọn cột hoặc tạo ViewModel.
- `.ToListAsync()`, `.FirstOrDefaultAsync()`, `.CountAsync()`: thực thi truy vấn tới SQL Server.
- `.Include(...)` / `.ThenInclude(...)`: lấy thêm dữ liệu quan hệ khi cần.

Để sửa filter thực đơn hiện có, bắt đầu ở `OrderController.Index`, nơi truy vấn `_db.ProductCategories` và các món liên quan. Không cần đổi `ApplicationDbContext` nếu bảng/cột đã được khai báo; chỉ thay đổi entity/DbContext và SQL schema khi thật sự thêm hoặc đổi cấu trúc dữ liệu, theo quy ước SQL-first của dự án.

**Quy tắc an toàn:** truy vấn bằng LINQ thay vì nối chuỗi SQL thủ công; luôn lọc theo người dùng hiện tại cho dữ liệu riêng tư; với update luôn kiểm tra ID, quyền, dữ liệu hợp lệ rồi mới `SaveChangesAsync`. Nếu đang sửa schema thì dừng để xác nhận script nâng cấp phù hợp — entity C# không tự làm thay đổi bảng SQL.

Để tự lần theo ví dụ gọi món theo từng bước cụ thể (từ bảng danh mục tới ô số lượng và dữ liệu POST), xem [order-ui-guide.md](./order-ui-guide.md), mục **5. Các bước lần theo code theo dòng**.

## 8. Đối chiếu Spec với trạng thái code hiện tại

Bảng sau là đánh giá khi đọc source hiện tại, không dựa riêng vào nhãn Done hoặc dấu `[x]` trong bản spec:

| Use Case | Theo Spec | Có trong code | Nhận xét |
|---|---|---|---|
| UC08 — Hồ sơ | Trong phạm vi, Iteration 1 | Có controller, view, validation và lưu hồ sơ | Luồng xem/sửa đã được triển khai. Cần chạy kiểm thử trên app/database để xác nhận toàn bộ Acceptance Criteria. |
| UC10 — Lịch sử phiên | Trong phạm vi, Iteration 1 | Có danh sách, chi tiết, phân trang và lọc theo Customer hiện tại | Có kiểm tra quyền sở hữu khi mở chi tiết. Bộ unit test hiện không kiểm thử endpoint/database của luồng này. |
| UC09/15 — Điểm/hạng | Trong phạm vi, Iteration 1 | Có tính điểm, xét hạng và lịch sử 10 dòng/trang | Code lọc phiên `Closed` có `PlaytimeAmount`. Điểm được tính lại từ `PlaytimeAmount`, không có thao tác cộng điểm vào DB. Unit test kiểm tra hàm tính, không chứng minh các AC truy vấn database. |
| UC07 — Gọi đồ | Trong phạm vi, Iteration 2 | Có menu, gửi đơn, kiểm tra phiên/tồn kho, snapshot giá và transaction | Phần gọi món có unit test các quy tắc thuần. Chưa có integration test chứng minh toàn bộ luồng với SQL Server. |
| UC17 — Đánh giá | Spec ghi Iteration 3, To Do, nhưng cột “Trong đợt này?” đánh dấu có | Chưa thấy controller, view, model hoặc bảng đánh giá trong source | Đây là khoảng trống cần xác nhận về phạm vi và triển khai; Acceptance Criteria UC17 trong spec đều chưa hoàn tất theo source hiện tại. |
| UC14 — Thanh toán online | Iteration 3, To Do, ngoài phạm vi | Không triển khai | Không phải phần thiếu cần làm trong đợt này theo mục Out of Scope của spec. |

### Lưu ý đặc biệt về UC09/15 và `PlaytimeAmount`

`PlaytimeAmount` là **số tiền giờ chơi của một phiên** (kiểu `decimal?`), không phải điểm. Trong [`LoyaltyController.cs`](../src/Bms.Web/Controllers/LoyaltyController.cs), truy vấn chỉ lấy các phiên của Customer đang đăng nhập, trạng thái `Closed`, và `PlaytimeAmount` có giá trị. Sau đó:

1. `CalcSessionPoints` đổi tiền giờ chơi thành điểm bằng `FLOOR(PlaytimeAmount / 10,000)`.
2. Tổng điểm được tính bằng cách cộng điểm từng phiên đã lọc.
3. `GetRankName` và `PointsToNextRank` suy ra hạng cùng số điểm còn thiếu.
4. Lịch sử điểm lấy các phiên này, sắp xếp mới nhất trước và phân trang 10 dòng.

Ví dụ: `PlaytimeAmount = 150,000` VND thì phiên đó được `15` điểm. Tên cột xuất hiện trong ViewModel lịch sử là tiền giờ; trường `Points` mới là số điểm được tính từ số tiền đó.

Spec quy định khi người dùng ở hạng Kim cương phải thấy “Bạn đang ở hạng cao nhất”; view hiện có chuỗi này. Spec cũng yêu cầu khi chưa có phiên đã đóng phải hiện tổng 0, hạng Đồng và “Chưa có lịch sử điểm”; view hiện có thông báo này.

### Khác biệt schema UC07 giữa Spec và code

Spec đề xuất bảng `SessionOrders` trong mục 5.3 nhưng ghi rõ đề xuất này **chưa được duyệt/chưa chạy**. Code hiện tại lại ánh xạ các bảng `Orders` và `OrderDetails` trong [`BmsEntities.cs`](../src/Bms.Web/Data/BmsEntities.cs), và trạng thái đơn trong code gồm `Pending`, `Preparing`, `Served`, `Cancelled`. Vì vậy, khi đọc code cần theo schema đang được ánh xạ; không tự tạo bảng `SessionOrders` hoặc đổi trạng thái sang `Delivered` nếu chưa được nhóm duyệt và chưa có thay đổi database đồng bộ.

Spec còn yêu cầu xác nhận đơn có gắn bàn và tổng tiền phiên tăng; code gắn đơn với phiên, lấy bàn thông qua phiên, và tính tổng tiền từ chi tiết đơn khi hiển thị. Việc xác nhận yêu cầu tổng tiền chính thức trong database/invoice cần dựa vào schema và luồng Staff hiện có, không suy ra chỉ từ việc màn Customer hiển thị tổng.

## 9. Phần nào chưa được triển khai đầy đủ?

Không nên nhầm việc có controller/view với việc toàn bộ nghiệp vụ Spec đã hoàn tất. Trong source hiện tại:

- `CustomerController` mới là khung và chuyển hướng; chưa có luồng đặt bàn/xem booking của khách.
- `StaffController` mới có dashboard đơn giản; comment trong file ghi rõ phần quản lý bàn/phiên và quản lý booking/check-in/hủy còn cần bổ sung.
- Chưa thấy chức năng đánh giá dịch vụ UC17: lưu điểm sao/nhận xét, chặn đánh giá trùng, kiểm tra phiên đã hoàn tất và chỉ thuộc Customer hiện tại.
- Chưa thấy luồng thanh toán online UC14; phần này được Spec loại khỏi phạm vi đợt này. Thanh toán tại quầy thuộc luồng Staff và Spec Customer chỉ phản ứng với trạng thái sau khi thanh toán.
- Chưa thấy luồng chuyển bàn, tách/gộp hóa đơn.
- Chưa thấy màn hình Admin quản lý danh mục thực đơn/tồn kho trong các controller hiện có.
- Mã kiểm thử hiện kiểm tra các quy tắc thuần như tính điểm/hạng, giới hạn số lượng, tồn kho, tổng tiền và phát hiện đơn trùng. Đây không phải kiểm thử tích hợp toàn bộ website với SQL Server. Dấu `[x]` trong Acceptance Criteria của Spec không tự nó chứng minh test đã tự động chạy trong repository này.

Ngoài ra, Spec có yêu cầu chung về hiển thị thời gian/tiền, giao diện tối thiểu 360px, tốc độ P95, anti-forgery và log có user id/request path. Tài liệu hướng dẫn này không xác nhận các yêu cầu phi chức năng đó đã được đo/kiểm thử đầy đủ; cần kiểm tra bằng test hoặc chạy thử tương ứng.

Do đó, hãy xem đây là bản hướng dẫn **những gì có thể xác nhận từ mã nguồn hiện có**, không phải tuyên bố mọi yêu cầu trong Spec đã hoàn thành.

## 10. Cách tự lần theo một chức năng

Chọn chức năng gọi món làm ví dụ:

1. Mở `Views/Order/Index.cshtml` để thấy form và dữ liệu trang cần.
2. Mở `OrderController.Index` để xem dữ liệu trang được truy vấn và chuyển thành ViewModel như thế nào.
3. Mở `OrderMenuViewModel` trong `Models/CustomerViewModels.cs` để biết hình dạng dữ liệu mà View nhận.
4. Mở entity `Product`, `Order`, `OrderDetail` trong `Data/BmsEntities.cs` để hiểu dữ liệu được lưu ở đâu.
5. Mở `OrderController.Submit` để lần theo kiểm tra dữ liệu, ghi database và thông báo sau khi gửi.
6. Mở `OrderConstants.cs` và `Bms.Tests/OrderTests.cs` để xem các quy tắc và ví dụ kiểm thử.

Khi một trang không chạy như mong đợi, kiểm tra lần lượt: **URL/route → quyền `[Authorize]` → action Controller → ModelState/validation → truy vấn DbContext → ViewModel → View**. Cách này giúp khoanh vùng lỗi mà không cần đọc toàn bộ dự án cùng lúc.

## 11. Chạy kiểm thử quy tắc nghiệp vụ

Từ thư mục gốc của repo, có thể chạy test bằng:

```powershell
dotnet test src/Bms.Tests/Bms.Tests.csproj
```

Các test hiện tại nằm trong `src/Bms.Tests`. Chúng hữu ích để kiểm tra logic thuần không cần database, nhưng việc test pass **không** chứng minh kết nối SQL, đăng nhập, phân quyền hay toàn bộ luồng trình duyệt đã hoạt động.

> **Lưu ý cấu hình:** `Program.cs` đọc connection string có tên `DefaultConnection`. Repo có các tài liệu cài đặt/database được tạo ở những thời điểm khác nhau; hãy kiểm tra cấu hình local và xác nhận đúng tên database trước khi chạy ứng dụng hoặc SQL script. Không đưa mật khẩu thật hoặc connection string chứa bí mật vào Git.
