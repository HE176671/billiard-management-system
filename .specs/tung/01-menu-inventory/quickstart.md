# Quickstart & Validation Guide: Quản lý Menu, Kho hàng & Combo (`01-menu-inventory`)

**Date**: 2026-10-11  
**Target Use Cases**: UC41 (Menu & Kho hàng CRUD) & UC42 (Combo CRUD)

---

## 1. Điều kiện Tiên quyết (Prerequisites)

1. **Cơ sở dữ liệu SQL Server**:
   - Instance `localhost` (hoặc theo `DefaultConnection` trong `appsettings.Development.json`) đang chạy database `BilliardDB` với đầy đủ 24 bảng nghiệp vụ.
   - Kiểm tra nhanh bằng lệnh:
     ```powershell
     sqlcmd -S localhost -d BilliardDB -E -C -i BMS_Database_Starter/Verify_BilliardDB.sql
     ```
2. **.NET SDK**:
   - Đã cài đặt .NET 8.0 SDK trở lên.
3. **Tài khoản Admin**:
   - Sử dụng tài khoản `admin.demo` đã được seed trong môi trường Development.

---

## 2. Lệnh Build & Chạy Ứng dụng

```powershell
# 1. Kiểm tra biên dịch toàn bộ project
dotnet build src/Bms.Web/Bms.Web.csproj

# 2. Chạy ứng dụng web
dotnet run --project src/Bms.Web/Bms.Web.csproj
```

---

## 3. Kịch bản Kiểm thử Nghiệm thu (End-to-End Validation Scenarios)

### Kịch bản 1: Quản lý Danh mục & Sản phẩm (UC41)
1. Đăng nhập với tài khoản `Admin` tại `/Account/Login`.
2. Trên thanh Sidebar bên trái, nhấn vào mục **Thực đơn** (`/Menu`).
3. **Tạo Danh mục**:
   - Nhập tên danh mục mới (ví dụ: `"Đồ ăn nhẹ"`) và bấm Lưu.
   - *Kết quả kỳ vọng*: Danh mục xuất hiện trong bộ lọc; nếu nhập trùng tên `"Đồ ăn nhẹ"` lần nữa, hệ thống hiển thị thông báo lỗi trùng tên.
4. **Tạo Sản phẩm mới**:
   - Nhấn nút **Thêm món mới** (`/Menu/Create`).
   - Nhập Tên: `"Khô gà lá chanh"`, Danh mục: `"Đồ ăn nhẹ"`, Giá: `35000`, Tồn kho ban đầu: `20`.
   - *Kết quả kỳ vọng*: Lưu thành công, hiển thị trên danh sách với trạng thái `Đang bán` (`IsActive = true`).

### Kịch bản 2: Điều chỉnh Tồn kho & Kiểm tra Concurrency `RowVersion` (UC41)
1. Mở 2 tab trình duyệt (Tab A và Tab B) cùng trỏ tới màn hình chỉnh sửa hoặc điều chỉnh kho của sản phẩm `"Khô gà lá chanh"`.
2. Trên **Tab A**: Cộng thêm `10` đơn vị vào kho và bấm Lưu $\rightarrow$ Thành công, tồn kho mới là `30`.
3. Trên **Tab B** (chưa tải lại trang, vẫn giữ `RowVersion` cũ): Thử cập nhật giá hoặc cộng kho và bấm Lưu.
4. *Kết quả kỳ vọng*: Hệ thống chặn thao tác ở Tab B và hiển thị cảnh báo: *"Dữ liệu sản phẩm đã được thay đổi bởi người dùng khác. Vui lòng tải lại trang."*

### Kịch bản 3: Tạo & Quản lý Gói Combo (UC42)
1. Truy cập màn hình **Quản lý Combo** (`/Combo`).
2. Nhấn **Tạo gói Combo mới** (`/Combo/Create`).
3. Nhập Tên: `"Combo Chiến Hữu 2H"`, Số giờ chơi: `2.0`, Giá trọn gói: `180000`.
4. Chọn thành phần món đi kèm: `2 x Bò Húc`, `1 x Khô gà lá chanh`.
5. Bấm Lưu.
6. *Kết quả kỳ vọng*:
   - Bản ghi được lưu vào `dbo.Combos` và 2 dòng chi tiết vào `dbo.ComboItems`.
   - Nếu thử tạo Combo mà không chọn món nào hoặc nhập `PlaytimeHours <= 0`, hệ thống báo lỗi validation và không lưu.

### Kịch bản 4: Vô hiệu hóa (Soft Delete) Sản phẩm / Combo
1. Tại danh sách sản phẩm hoặc combo, nhấn nút **Ngừng bán** (`ToggleStatus`).
2. *Kết quả kỳ vọng*: Trạng thái chuyển sang `Ngừng bán` (`IsActive = false`), dữ liệu lịch sử trong database được giữ nguyên 100%.
