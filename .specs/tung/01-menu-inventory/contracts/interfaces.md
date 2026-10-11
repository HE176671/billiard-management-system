# Interface Contracts: Quản lý Menu, Kho hàng & Combo (`01-menu-inventory`)

**Date**: 2026-10-11  
**Module**: F&B Catalog, Inventory & Combos (UC41 & UC42)

---

## 1. Hợp đồng Giao diện MVC (`MenuController` & `ComboController`)

### 1.1 `MenuController` (`[Authorize(Roles = "Admin")]`)
| Action | HTTP Method | Route | Input Parameters / Model | Output / Behavior |
|---|---|---|---|---|
| `Index` | `GET` | `/Menu` | `string? search, int? categoryId, string? status, int page = 1` | Render `Views/Menu/Index.cshtml` hiển thị bảng sản phẩm, tồn kho, danh mục. |
| `CreateCategory` | `POST` | `/Menu/CreateCategory` | `CreateCategoryViewModel { Name }` + AntiForgeryToken | Tạo danh mục mới, Redirect về `/Menu` kèm `TempData["Success"]` hoặc `TempData["Error"]`. |
| `EditCategory` | `POST` | `/Menu/EditCategory` | `EditCategoryViewModel { Id, Name }` | Cập nhật tên danh mục, kiểm tra trùng lặp `UQ_ProductCategories_Name`. |
| `Create` | `GET` | `/Menu/Create` | — | Render `Views/Menu/Create.cshtml` với dropdown danh mục. |
| `Create` | `POST` | `/Menu/Create` | `CreateProductViewModel` + AntiForgeryToken | Kiểm tra `Price > 0`, `StockQuantity >= 0`, `Name` unique. Lưu DB và Redirect `/Menu`. |
| `Edit` | `GET` | `/Menu/Edit/{id}` | `int id` | Nạp sản phẩm kèm `RowVersionBase64`, render `Views/Menu/Edit.cshtml`. |
| `Edit` | `POST` | `/Menu/Edit/{id}` | `int id, EditProductViewModel` | Kiểm tra `RowVersion`, cập nhật `Name`, `CategoryId`, `Price`. Bắt `DbUpdateConcurrencyException`. |
| `AdjustStock` | `POST` | `/Menu/AdjustStock` | `AdjustStockViewModel { ProductId, QuantityDelta, Reason, RowVersionBase64 }` | Điều chỉnh tồn kho có kiểm tra `RowVersion` và `NewStock >= 0`. |
| `ToggleStatus` | `POST` | `/Menu/ToggleStatus/{id}` | `int id, string rowVersionBase64` | Đảo trạng thái `IsActive` (`true` $\leftrightarrow$ `false`). |

### 1.2 `ComboController` (`[Authorize(Roles = "Admin")]`)
| Action | HTTP Method | Route | Input Parameters / Model | Output / Behavior |
|---|---|---|---|---|
| `Index` | `GET` | `/Combo` | `string? search, string? status` | Render `Views/Combo/Index.cshtml` hiển thị các gói Combo kèm danh sách món và giờ chơi. |
| `Create` | `GET` | `/Combo/Create` | — | Render `Views/Combo/Create.cshtml` cho phép chọn nhiều món F&B và nhập số lượng. |
| `Create` | `POST` | `/Combo/Create` | `ComboFormViewModel` | Validate `PlaytimeHours > 0`, `Price >= 0`, `Items.Count >= 1`. Lưu `Combos` + `ComboItems` trong Transaction. |
| `Edit` | `GET` | `/Combo/Edit/{id}` | `int id` | Render `Views/Combo/Edit.cshtml` với các dòng `ComboItems` hiện có. |
| `Edit` | `POST` | `/Combo/Edit/{id}` | `int id, ComboFormViewModel` | Cập nhật `Combos` và làm mới `ComboItems` trong Transaction. |
| `ToggleStatus` | `POST` | `/Combo/ToggleStatus/{id}` | `int id` | Đảo trạng thái `IsActive` của gói Combo. |

---

## 2. Hợp đồng REST API (`/api/v1/fnb/...`)

Phục vụ gọi bất đồng bộ (AJAX) trên giao diện quản trị và cung cấp dữ liệu danh mục cho màn hình Gọi món của Staff (UC30).

### 2.1 Danh sách Sản phẩm & Tồn kho
- **Endpoint**: `GET /api/v1/fnb/products`
- **Quyền truy cập**: `Admin`, `Staff`
- **Query Parameters**: `categoryId` (int?), `activeOnly` (bool = true), `keyword` (string?)
- **Response `200 OK`**:
```json
{
  "items": [
    {
      "id": 1,
      "categoryId": 1,
      "categoryName": "Nước giải khát",
      "name": "Bò Húc",
      "price": 20000.00,
      "stockQuantity": 45,
      "isActive": true,
      "rowVersionBase64": "AAAAAAAAB9E="
    }
  ],
  "totalCount": 1
}
```

### 2.2 Điều chỉnh Tồn kho Tức thời
- **Endpoint**: `PATCH /api/v1/fnb/products/{id}/adjust-stock`
- **Quyền truy cập**: `Admin`
- **Request Body**:
```json
{
  "quantityDelta": 24,
  "reason": "Nhập thêm 1 thùng Bò Húc",
  "rowVersionBase64": "AAAAAAAAB9E="
}
```
- **Response `200 OK`**:
```json
{
  "productId": 1,
  "newStockQuantity": 69,
  "newRowVersionBase64": "AAAAAAAAB9I="
}
```
- **Error Responses**:
  - `400 Bad Request`: `{"errorCode": "ERR_STOCK_CANNOT_BE_NEGATIVE", "message": "Tồn kho sau điều chỉnh không được nhỏ hơn 0."}`
  - `409 Conflict`: `{"errorCode": "ERR_CONCURRENCY_CONFLICT", "message": "Dữ liệu sản phẩm đã bị thay đổi bởi người dùng khác. Vui lòng tải lại trang."}`

### 2.3 Danh sách và Tạo mới Gói Combo
- **Endpoint**: `POST /api/v1/fnb/combos`
- **Quyền truy cập**: `Admin`
- **Request Body**:
```json
{
  "name": "Combo Đêm 3H",
  "price": 220000.00,
  "playtimeHours": 3.0,
  "items": [
    { "productId": 1, "quantity": 2 },
    { "productId": 5, "quantity": 1 }
  ]
}
```
- **Response `201 Created`**: Trả về chi tiết gói Combo vừa tạo.
- **Error `400 Bad Request`**: `{"errorCode": "ERR_INVALID_COMBO_ITEMS", "message": "Combo phải bao gồm ít nhất 1 sản phẩm đang hoạt động."}`
