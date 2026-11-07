# Hướng dẫn đọc và kiểm soát mã nguồn BMS (Spec 2 - v0.4)

Tài liệu này dành cho người mới làm quen với C# và ASP.NET Core MVC. Mục tiêu là giúp bạn hiểu ứng dụng đang chạy theo luồng nào, từng nút bấm gửi dữ liệu đi đâu, file nào làm việc gì, và cách tự chỉnh sửa hoặc giải thích code khi review.

---

## 1. Nhìn tổng thể dự án

Ứng dụng web nằm trong `src/Bms.Web`. Đây là ứng dụng **ASP.NET Core MVC** viết bằng C#:

| Thành phần | Hiểu đơn giản | Vị trí trong dự án |
|---|---|---|
| **Model / Entity** | Đại diện cho các bảng trong database | `src/Bms.Web/Data/BmsEntities.cs` |
| **ViewModel** | Túi chứa dữ liệu gom lại để gửi ra giao diện View hoặc nhận từ Form | `src/Bms.Web/Models/CustomerViewModels.cs` |
| **Controller** | Bộ não nhận yêu cầu từ URL, kiểm tra dữ liệu, gọi database và điều hướng | `src/Bms.Web/Controllers/` (`OrderController.cs`, `CustomerProfileController.cs`...) |
| **View** | Giao diện HTML được dựng bằng Razor (`.cshtml`) | `src/Bms.Web/Views/` (`Order/`, `CustomerProfile/`...) |
| **DbContext** | Cầu nối giữa C# và SQL Server, cho phép truy vấn các bảng | `src/Bms.Web/Data/ApplicationDbContext.cs` |
| **Service** | Các hằng số và hàm tính toán tái sử dụng | `src/Bms.Web/Services/` (`OrderConstants.cs`...) |

Luồng web cơ bản:
```text
Trình duyệt → URL/Route → Controller → DbContext/Identity → SQL Server
                                      ↓
Trình duyệt ← View (HTML) ← ViewModel ←┘
```

---

## 2. Đường đọc code nhanh theo từng luồng chức năng

### 2.1 Luồng Hồ sơ cá nhân (UC08)
* **Bắt đầu:** Người dùng bấm menu "Hồ sơ của tôi" trên sidebar.
* **Mở xem:** `CustomerProfileController.cs` $\rightarrow$ Action `Index()` lấy `UserId` từ cookie đăng nhập $\rightarrow$ Nạp dữ liệu từ `dbo.AspNetUsers` $\rightarrow$ Trả về `Views/CustomerProfile/Index.cshtml`.
* **Sửa hồ sơ:**
  1. Nút "Chỉnh sửa" mở Action `Edit()` (GET) $\rightarrow$ Render form `Views/CustomerProfile/Edit.cshtml`.
  2. Khách sửa Họ tên, Email, SĐT $\rightarrow$ Bấm nút "Lưu thay đổi" gửi `POST /Profile/Edit`.
  3. Action `Edit` (POST) kiểm tra tính hợp lệ, kiểm tra trùng lặp email/sđt với tài khoản khác $\rightarrow$ Gọi `UserManager.UpdateAsync` để cập nhật vào `dbo.AspNetUsers` $\rightarrow$ Redirect về `Index` kèm thông báo thành công.

---

### 2.2 Luồng Gọi món QR tại bàn (UC07 - Khách quét mã)
* **Bắt đầu:** Khách quét QR trên bàn, mở link `/Order/Table/B01` (với `B01` là mã bàn).
* **Controller nhận diện:** `OrderController.cs` $\rightarrow$ Action `Table(string tableCode)`.
  1. Cho phép ẩn danh (`[AllowAnonymous]`) nên khách không cần đăng nhập tài khoản.
  2. Tìm bàn `B01` trong `dbo.BilliardTables`.
  3. Tìm phiên chơi `Active` của bàn trong `dbo.PlaySessions`. Nếu không có, hiển thị màn hình báo "Bàn chưa mở phiên chơi".
  4. Nếu bàn đang chơi: Nạp thực đơn từ `dbo.Products` (`IsActive = 1`) và nạp các đơn của phiên đang chơi để khách xem.
  5. Trả về View `Views/Order/TableOrder.cshtml` (giao diện tối ưu cho điện thoại).
* **Gửi gọi món:**
  1. Khách nhập số lượng món hoặc bấm nút `[+]` / `[-]` (tự động kiểm tra và báo nếu vượt tồn kho `StockQuantity`) $\rightarrow$ JavaScript lưu vào giỏ hàng và tính tạm tính.
  2. Bấm nút **"Gửi gọi món"** $\rightarrow$ Gửi `POST /Order/SubmitTable`.
  3. Action `SubmitTable` kiểm tra phiên vẫn đang `Active`, kiểm tra giỏ hàng không rỗng.
  4. Tạo đơn mới trong `dbo.Orders` với `Status = "Pending"` (Chờ xác nhận).
  5. Lưu từng chi tiết vào `dbo.OrderDetails` với `UnitPrice = product.Price` (snapshot giá lúc đặt).
  6. **Cơ chế chống spam (Phương án 1):** Đơn `Pending` **chưa trừ tồn kho ngay**. Nhân viên tại quầy sẽ kiểm tra hoặc hỏi bàn để bấm duyệt thì mới trừ tồn kho thật sự, tránh trường hợp khách ở ngoài quán có link bấm trộm.

---

### 2.3 Luồng Quản lý quầy & Bán F&B (Staff)
* **Duyệt đơn bàn:** `OrderController.cs` $\rightarrow$ Action `Manage()` $\rightarrow$ View `Views/Order/ManageOrders.cshtml`.
  * Hiển thị danh sách các đơn `Pending` từ mã QR bàn.
  * Cạnh mỗi đơn có nút **"Duyệt & Phục vụ"** (`POST /Order/ApproveOrder`): Hệ thống kiểm tra số lượng tồn kho `StockQuantity`, trừ tồn kho và đổi trạng thái đơn thành `Served`.
  * Nút **"Hủy"** (`POST /Order/CancelOrder`): Đổi trạng thái đơn thành `Cancelled`.
* **Gọi món tại quầy (`/Order/Counter`):**
  * **Tab 1: Khách mua lẻ tại quầy (Mang về)**: Không cần vào bàn, staff chọn món $\rightarrow$ Bấm "Xác nhận tạo đơn mang về" (`POST /Order/SubmitCounterTakeaway`) $\rightarrow$ Trừ kho ngay và lưu đơn `Served`.
  * **Tab 2: Gọi thêm cho bàn đang chơi**: Staff chọn bàn đang chơi $\rightarrow$ Thêm món trực tiếp cho khách $\rightarrow$ Bấm "Thêm món vào bàn" (`POST /Order/SubmitStaffOrder`) $\rightarrow$ Đơn tự động chuyển sang `Served` và trừ tồn kho ngay.

---

## 3. Bản đồ File: "Tôi muốn sửa chức năng X thì sửa ở đâu?"

| Muốn sửa việc gì? | File C# Controller / Service | File Giao diện View Razor (.cshtml) |
|---|---|---|
| Sửa thông tin tài khoản, form sửa email, sđt | `Controllers/CustomerProfileController.cs` | `Views/CustomerProfile/Index.cshtml`, `Edit.cshtml` |
| Sửa giao diện gọi món QR trên điện thoại của khách | `Controllers/OrderController.cs` (Action `Table`) | `Views/Order/TableOrder.cshtml` |
| Sửa màn hình gọi món tại quầy của Staff | `Controllers/OrderController.cs` (Action `Counter`) | `Views/Order/Counter.cshtml` |
| Sửa màn hình duyệt đơn QR từ bàn của Staff | `Controllers/OrderController.cs` (Action `Manage`) | `Views/Order/ManageOrders.cshtml` |
| Sửa giới hạn số lượng món, thông báo lỗi | `Services/OrderConstants.cs` | — |
| Thêm/bớt nút trên thanh menu Sidebar bên trái | — | `Views/Shared/_AdminLayout.cshtml` |
| Khai báo thêm cột/bảng trong C# (EF Core) | `Data/BmsEntities.cs`, `Data/ApplicationDbContext.cs` | — |
