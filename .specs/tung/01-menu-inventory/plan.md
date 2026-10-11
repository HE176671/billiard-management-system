# Implementation Plan: Quản lý Menu, Kho hàng & Combo (`01-menu-inventory`)

**Branch**: `01-menu-inventory` | **Date**: 2026-10-11 | **Spec**: [`.specs/tung/01-menu-inventory-spec.md`](../01-menu-inventory-spec.md)  
**Input**: Feature specification từ `.specs/tung/01-menu-inventory-spec.md` (UC41 & UC42)

---

## 1. Summary (Tóm tắt Kế hoạch)

Xây dựng phân hệ **Quản lý Thực đơn, Kho hàng F&B (UC41) và Gói Combo (UC42)** dành cho Admin trên nền tảng ASP.NET Core 8 MVC kết hợp RESTful API, kết nối trực tiếp vào cơ sở dữ liệu SQL Server `BilliardDB`. Giải pháp sử dụng:
- **SQL-First Entity Mapping**: Ánh xạ 4 bảng nghiệp vụ sẵn có (`ProductCategories`, `Products`, `Combos`, `ComboItems`) vào `ApplicationDbContext` mà không làm thay đổi hay ghi đè schema hiện hữu.
- **Optimistic Concurrency Control (`RowVersion`)**: Chống ghi đè dữ liệu (Lost Update) khi nhiều người cùng cập nhật giá hoặc điều chỉnh tồn kho.
- **Soft Delete (`IsActive`)**: Bảo toàn 100% lịch sử đơn hàng và hóa đơn cũ thông qua cơ chế bật/tắt trạng thái kinh doanh thay vì xóa vật lý.
- **Giao diện đồng bộ**: Tích hợp trực tiếp vào thanh điều hướng `_AdminLayout.cshtml` hiện có của dự án.

---

## 2. Technical Context

| Hạng mục | Chi tiết Kỹ thuật |
|---|---|
| **Language / Version** | C# 12 / .NET 8.0 (`net8.0`) |
| **Primary Dependencies** | `Microsoft.AspNetCore.Mvc`, `Microsoft.EntityFrameworkCore.SqlServer (8.*)`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore (8.*)`, Bootstrap 5 + Bootstrap Icons |
| **Storage** | SQL Server (`BilliardDB` — 24 bảng nghiệp vụ, kết nối qua `DefaultConnection`) |
| **Testing** | Kiểm thử tích hợp EF Core + SQL Server constraints & kịch bản Gherkin Acceptance Criteria |
| **Target Platform** | Windows / Cross-platform Web Server (ASP.NET Core Kestrel / IIS) |
| **Project Type** | ASP.NET Core MVC Web Application (`src/Bms.Web`) kèm REST API Endpoints |
| **Performance Goals** | Phản hồi danh sách Menu/Kho < 100ms; xử lý đồng thời các thao tác nhập kho và trừ kho không gây deadlock |
| **Constraints** | Tuân thủ ràng buộc SQL Server: `Price > 0`, `StockQuantity >= 0`, `PlaytimeHours > 0`, `Quantity > 0`, `RowVersion` bắt buộc khi update `Products` |
| **Scale / Scope** | Quản lý ~10-20 danh mục, ~100-500 mặt hàng F&B, ~20-50 gói Combo; 2 Controllers, 4 Entities, 6 Razor Views |

---

## 3. Constitution Check (Kiểm tra Tuân thủ Hiến chương Dự án)

*GATE: Đánh giá trước Phase 0 và xác nhận lại sau khi hoàn tất thiết kế Phase 1.*

| Nguyên tắc Hiến chương (`.specify/memory/constitution.md`) | Trạng thái Gate | Giải trình Thiết kế |
|---|---|---|
| **I. Spec-Driven & Contract-First** | ✅ **PASS** | Có đầy đủ `spec.md`, `data-model.md`, `contracts/interfaces.md` trước khi viết code. |
| **II. Database-First & SQL-First Authority** | ✅ **PASS** | Ánh xạ đúng tên bảng/cột của `BilliardDB_Full.sql`. Tuyệt đối không dùng `EnsureCreated()` hay `Migrate()` ghi đè DB. |
| **III. Identity & Authentication Unification** | ✅ **PASS** | Sử dụng `[Authorize(Roles = "Admin")]` cho toàn bộ thao tác CRUD Menu/Kho/Combo và `[Authorize(Roles = "Admin,Staff")]` cho API tra cứu menu. |
| **IV. Clean Layered Architecture** | ✅ **PASS** | Tách biệt rõ: Entities (`Data/Entities`), ViewModels & DTOs (`Models`), Business Logic (`Services/IMenuService`), Thin Controllers (`Controllers/MenuController`, `ComboController`), và UI (`Views/Menu`, `Views/Combo`). |
| **V. Quality Verification Gate** | ✅ **PASS** | Đảm bảo `dotnet build` 0 lỗi, kiểm tra đầy đủ các trường hợp ngoại lệ (trùng tên, giá âm, tồn kho âm, xung đột `RowVersion`). |

---

## 4. Project Structure (Cấu trúc Thư mục Triển khai)

### 4.1 Hồ sơ Thiết kế (`01-menu-inventory`)
```text
.specs/tung/
├── 01-menu-inventory-spec.md            # Bản đặc tả gốc (UC41 & UC42)
├── 01-menu-inventory-plan.md            # Bản sao kế hoạch kỹ thuật ở cấp thư mục Tùng
└── 01-menu-inventory/                   # Thư mục chuẩn SpecKit cho feature 01
    ├── spec.md                          # Feature Specification
    ├── plan.md                          # Implementation Plan (file này)
    ├── research.md                      # Phase 0: Quyết định kiến trúc & kỹ thuật
    ├── data-model.md                    # Phase 1: Thiết kế Entities, Fluent API & ViewModels
    ├── quickstart.md                    # Phase 1: Hướng dẫn chạy và nghiệm thu
    └── contracts/
        └── interfaces.md                # Phase 1: Hợp đồng MVC Routes & REST API
```

### 4.2 Mã nguồn Ứng dụng (`src/Bms.Web`)
*(Lưu ý: Chỉ thêm mới file của module Menu/Combo và đăng ký DbSet/Service, không chạm vào code của các thành viên khác)*

```text
src/Bms.Web/
├── Controllers/
│   ├── MenuController.cs                # [NEW] Quản lý Danh mục, Sản phẩm & Kho hàng (UC41)
│   ├── ComboController.cs               # [NEW] Quản lý Gói Combo & ComboItems (UC42)
│   └── Api/
│       └── FnbCatalogApiController.cs   # [NEW] REST API (/api/v1/fnb/...)
├── Data/
│   ├── ApplicationDbContext.cs          # [UPDATE] Bổ sung DbSet<ProductCategory>, DbSet<Product>, DbSet<Combo>, DbSet<ComboItem> & Fluent API
│   └── Entities/
│       ├── ProductCategory.cs           # [NEW] Entity bảng dbo.ProductCategories
│       ├── Product.cs                   # [NEW] Entity bảng dbo.Products (kèm RowVersion)
│       ├── Combo.cs                     # [NEW] Entity bảng dbo.Combos
│       └── ComboItem.cs                 # [NEW] Entity bảng dbo.ComboItems
├── Models/
│   ├── MenuViewModels.cs                # [NEW] ViewModels & DTOs cho UC41
│   └── ComboViewModels.cs               # [NEW] ViewModels & DTOs cho UC42
├── Services/
│   ├── IMenuService.cs                  # [NEW] Interface xử lý nghiệp vụ Menu, Kho & Combo
│   └── MenuService.cs                   # [NEW] Implementation (Transaction, Concurrency, Validation)
└── Views/
    ├── Shared/
    │   └── _AdminLayout.cshtml          # [UPDATE NHẸ] Gắn asp-controller="Menu" và "Combo" vào nút Thực đơn trên Sidebar
    ├── Menu/
    │   ├── Index.cshtml                 # [NEW] Danh sách sản phẩm, lọc theo danh mục, modal nhập kho & quản lý danh mục
    │   ├── Create.cshtml                # [NEW] Form thêm mới sản phẩm F&B
    │   └── Edit.cshtml                  # [NEW] Form cập nhật sản phẩm (kèm hidden RowVersionBase64)
    └── Combo/
        ├── Index.cshtml                 # [NEW] Danh sách gói Combo & chi tiết món đi kèm
        ├── Create.cshtml                # [NEW] Form tạo gói Combo (dynamic rows chọn món)
        └── Edit.cshtml                  # [NEW] Form chỉnh sửa gói Combo
```

---

## 5. Chi tiết Luồng Xử lý Kỹ thuật Trọng tâm

### 5.1 Luồng Kiểm soát Concurrency khi Sửa Sản phẩm / Điều chỉnh Kho (UC41)
1. Khi render form Edit hoặc modal AdjustStock, chuyển `byte[] RowVersion` sang chuỗi `Convert.ToBase64String(product.RowVersion)`.
2. Khi nhận POST/PATCH từ người dùng:
   - Nạp bản ghi `Product` hiện tại hoặc attach entity.
   - Thiết lập giá trị gốc của Concurrency Token:
     ```csharp
     _context.Entry(product).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(model.RowVersionBase64);
     ```
   - Kiểm tra logic nghiệp vụ (`Price > 0`, `NewStockQuantity >= 0`, tên không trùng với sản phẩm khác).
   - Gọi `await _context.SaveChangesAsync()`.
   - Bắt `DbUpdateConcurrencyException`: trả về thông báo lỗi thân thiện yêu cầu người dùng tải lại trang vì dữ liệu đã bị thay đổi bởi một phiên làm việc khác.

### 5.2 Luồng Tạo / Cập nhật Gói Combo trong Transaction (UC42)
1. Kiểm tra tính hợp lệ của `ComboFormViewModel`:
   - `Name` không trống và chưa tồn tại ở gói Combo khác.
   - `Price >= 0` và `PlaytimeHours > 0`.
   - Danh sách `Items` phải có ít nhất 1 món hợp lệ (`Quantity > 0`), gộp các dòng có cùng `ProductId`.
   - Xác minh toàn bộ `ProductId` trong danh sách đều tồn tại trong DB và có `IsActive == true`.
2. Mở Transaction `await using var tx = await _context.Database.BeginTransactionAsync()`:
   - Lưu thông tin bảng `Combos`.
   - Nếu là cập nhật (`Edit`): xóa các bản ghi cũ trong `ComboItems` của `ComboId` này, sau đó thêm danh sách `ComboItems` mới.
   - `await _context.SaveChangesAsync()` và `await tx.CommitAsync()`.

---

## 6. Complexity Tracking

*Không có vi phạm Hiến chương dự án. Mọi thành phần đều tuân thủ kiến trúc chuẩn của repository.*
