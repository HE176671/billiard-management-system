# Phase 0 Research: Quản lý Menu, Kho hàng & Combo (`01-menu-inventory`)

**Date**: 2026-10-11  
**Feature**: UC41 (Quản lý Menu & Kho hàng CRUD) & UC42 (Quản lý Combo)  
**Spec**: [`.specs/tung/01-menu-inventory-spec.md`](../01-menu-inventory-spec.md)

---

## 1. Kiến trúc Tầng Ứng dụng & Giao diện (MVC + API Hybrid)

- **Decision**: Triển khai mô hình **ASP.NET Core MVC kết hợp API Endpoints** trong project `Bms.Web`, sử dụng chung `_AdminLayout.cshtml` đã có sẵn của hệ thống.
  - `MenuController`: Quản lý Danh mục (`ProductCategories`), Sản phẩm (`Products`) và Nhập/Điều chỉnh kho (`AdjustStock`).
  - `ComboController` (hoặc tab tích hợp trong `MenuController`): Quản lý gói `Combos` và thành phần `ComboItems`.
- **Rationale**:
  - Khảo sát codebase hiện tại (`src/Bms.Web`) cho thấy dự án đang sử dụng ASP.NET Core 8 MVC với `EmployeeController` và `_AdminLayout.cshtml` (trong đó mục sidebar `<i class="bi bi-cup-straw"></i> Thực đơn` đã được tạo sẵn chờ gắn link `controller="Menu"`).
  - Việc kết hợp Razor Views (cho màn hình quản trị Admin) và JSON API endpoints (cho thao tác modal nhanh và tích hợp với màn hình POS Gọi món ở UC30) giúp đồng bộ 100% giao diện với phần của các thành viên khác mà vẫn đáp ứng đầy đủ đặc tả API.
- **Alternatives Considered**:
  - *Tách project Web API riêng*: Bị loại bỏ vì làm phức tạp hóa cấu trúc solution hiện tại (`src/Bms.Web` đang là project duy nhất) và gây tốn thời gian cấu hình CORS/Auth cookie.

---

## 2. Chiến lược Ánh xạ Cơ sở dữ liệu (SQL-First & EF Core 8)

- **Decision**: Ánh xạ trực tiếp (Code-Second / SQL-First Mapping) các bảng đã tồn tại trong `BilliardDB` (`dbo.ProductCategories`, `dbo.Products`, `dbo.Combos`, `dbo.ComboItems`) vào `ApplicationDbContext` bằng Fluent API kết hợp Data Annotations. Tuyệt đối không chạy `Add-Migration` hay `Database.Migrate()` ghi đè bảng cũ.
- **Rationale**:
  - Tuân thủ nghiêm ngặt **Nguyên tắc II (Database-First & SQL-First Authority)** trong Hiến chương dự án (`.specify/memory/constitution.md`).
  - Database `BilliardDB` đã có sẵn 24 bảng nghiệp vụ cùng các `CHECK` constraints (`CK_Product_Price`, `CK_Product_Stock`, `CK_Combo_Playtime`, `CK_ComboItems_Quantity`) và `UNIQUE` constraints (`UQ_ProductCategories_Name`, `UQ_Products_Name`, `UQ_Combos_Name`).
- **Alternatives Considered**:
  - *EF Core Code-First Migrations*: Bị loại bỏ vì nguy cơ xung đột với schema SQL Server sẵn có của cả nhóm.

---

## 3. Kiểm soát Tương tranh Lạc quan (Optimistic Concurrency với `RowVersion`)

- **Decision**: Sử dụng cột `RowVersion` (`rowversion` / `timestamp` trong SQL Server) trên bảng `dbo.Products`:
  - Trong Entity `Product`: khai báo `public byte[] RowVersion { get; set; }` với `.IsRowVersion()` trong `OnModelCreating`.
  - Trong ViewModel / DTO: truyền tải `RowVersion` dưới dạng chuỗi mã hóa `Base64` (`RowVersionBase64`).
  - Khi cập nhật thông tin sản phẩm hoặc điều chỉnh kho: gán `_context.Entry(product).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersionBase64)` trước khi gọi `SaveChangesAsync()`.
  - Bắt ngoại lệ `DbUpdateConcurrencyException` và trả về thông báo lỗi thân thiện (HTTP `409 Conflict` đối với API hoặc `ModelState.AddModelError` đối với View).
- **Rationale**:
  - Bảng `dbo.Products` đã định nghĩa sẵn cột `RowVersion rowversion NOT NULL`.
  - Ngăn chặn triệt để lỗi "Lost Update" khi 2 Admin cùng cập nhật tồn kho hoặc khi Admin vừa sửa kho đúng lúc nhân viên bấm Order món (UC30).
- **Alternatives Considered**:
  - *Pessimistic Locking (`UPDLOCK`) cho thao tác sửa form của Admin*: Không phù hợp vì người dùng mở form trên trình duyệt lâu trước khi bấm lưu; Optimistic Concurrency với `RowVersion` là chuẩn mực cho CRUD quản trị.

---

## 4. Cơ chế Xóa mềm & Bảo toàn Lịch sử (Soft Delete via `IsActive`)

- **Decision**:
  - Đối với `Products` và `Combos`: Sử dụng thao tác **Chuyển đổi trạng thái (`ToggleStatus` / `IsActive`)** thay vì xóa vật lý (`DELETE`).
  - Nếu người dùng yêu cầu xóa hẳn một `Combo` chưa từng được mua (không có trong `SessionCombos`), hệ thống cho phép xóa các dòng `ComboItems` trước rồi xóa `Combo` trong cùng transaction. Nếu đã có lịch sử mua, chỉ cho phép chuyển `IsActive = false`.
  - Đối với `ProductCategories`: Chặn xóa nếu danh mục đang chứa bất kỳ sản phẩm nào (`Products.Any(p => p.CategoryId == id)`).
- **Rationale**:
  - Tuân thủ tài liệu `docs/COMBO_DESIGN.md` và ràng buộc khóa ngoại `NO ACTION` trong `BilliardDB_Full.sql`.
- **Alternatives Considered**:
  - *Cascade Delete*: Bị cấm hoàn toàn vì làm mất dữ liệu lịch sử hóa đơn và báo cáo tài chính (UC47).

---

## 5. Quản lý Danh sách Món trong Gói Combo (`ComboItems`)

- **Decision**:
  - Khi tạo mới hoặc cập nhật gói `Combo`: Kiểm tra danh sách `Items` phải có ít nhất 1 sản phẩm (`Items.Count >= 1`), gộp các dòng trùng `ProductId` (hoặc báo lỗi nếu chọn trùng), và xác minh mọi `ProductId` đều tồn tại và đang `IsActive = true`.
  - Khi cập nhật `Combo`: Thực hiện trong một Transaction — xóa danh sách `ComboItems` hiện tại của `ComboId` đó và chèn danh sách `ComboItems` mới (vì `ComboItems` chỉ là bảng cấu hình catalog; lịch sử mua của khách đã được snapshot độc lập sang `SessionCombos` và `SessionComboItems`).
- **Rationale**:
  - Theo `docs/COMBO_DESIGN.md`: *"Thay giá/tên/số món của combo hiện tại không cập nhật các snapshot đã mua."* Do đó bảng `ComboItems` có thể được cập nhật tự do cho các gói bán trong tương lai.
