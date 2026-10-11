# Feature Specification: Quản lý Menu, Kho hàng & Combo (F&B Catalog & Inventory)

**Feature Identifier**: `01-menu-inventory`  
**Assigned Engineer**: Tùng (Module: F&B, Billing & Reporting)  
**Target Use Cases**: UC41 (Quản lý Menu & Kho hàng CRUD) & UC42 (Quản lý Combo)  
**Iteration**: 1  
**Status**: Approved / Ready for Implementation  
**Created Date**: 2026-10-11  

---

## 1. Feature Scope & User Stories

### 1.1 Tổng quan phạm vi (Feature Scope)
Module cung cấp khả năng quản lý toàn bộ danh mục sản phẩm đồ ăn, đồ uống (F&B), danh mục mặt hàng, tồn kho tức thời và các gói Combo kết hợp giữa giờ chơi bi-a và đồ ăn/thức uống. Hệ thống đảm bảo tính toàn vẹn dữ liệu, kiểm soát tranh chấp dữ liệu đồng thời (Optimistic Concurrency Control) và bảo vệ lịch sử giao dịch bằng cơ chế vô hiệu hóa (Soft Delete / Inactive) thay vì xóa cứng.

- **Actors**:
  - `Admin`: Toàn quyền thêm, sửa, xem, vô hiệu hóa danh mục, sản phẩm, điều chỉnh kho và cấu hình gói Combo.
  - `Staff` / `Cashier`: Xem danh sách menu, giá niêm yết, tình trạng còn hàng/hết hàng để tư vấn và nhận order từ khách.
- **Pre-conditions**:
  - Người dùng đã đăng nhập hệ thống và có quyền `Admin` (hoặc `Staff` đối với quyền chỉ đọc).
  - Cơ sở dữ liệu `BilliardDB` đã có bảng `ProductCategories`, `Products`, `Combos`, `ComboItems`.
- **Post-conditions**:
  - Sản phẩm/Combo mới được hiển thị trên menu bán hàng.
  - Tồn kho được cập nhật chính xác theo số liệu nhập/xuất.
  - Các thay đổi cấu trúc Combo không làm ảnh hưởng đến các gói Combo đã được khách mua trong lịch sử (`SessionCombos`).

---

### 1.2 User Stories & Testing

#### User Story 1 - Quản lý Danh mục & Sản phẩm F&B (Priority: P1)
> Là một **Admin**, tôi muốn tạo mới, cập nhật thông tin và quản lý danh mục món ăn/thức uống để menu của quán luôn đầy đủ và giá cả chính xác.

- **Why this priority**: Là nền tảng tiên quyết (Prerequisite) cho việc hiển thị menu, gọi món (UC30) và tính hóa đơn (UC27-29).
- **Independent Test**: Có thể kiểm thử độc lập qua màn hình quản lý sản phẩm hoặc API endpoints: tạo Category, tạo Product, cập nhật giá và kiểm tra validation.
- **Acceptance Scenarios**:
  1. **Given** Admin nhập đầy đủ thông tin sản phẩm hợp lệ (Tên chưa tồn tại, CategoryId hợp lệ, Giá > 0, Tồn kho >= 0), **When** Admin gửi yêu cầu tạo sản phẩm, **Then** Sản phẩm được lưu thành công vào database và hiển thị ở trạng thái `IsActive = true`.
  2. **Given** Đã có sản phẩm tên "Bò Húc" trong hệ thống, **When** Admin tạo sản phẩm mới cùng tên "Bò Húc" (không phân biệt hoa thường), **Then** Hệ thống từ chối và trả về lỗi `400 Bad Request` với thông báo trùng tên sản phẩm.

#### User Story 2 - Quản lý Tồn kho & Kiểm soát đồng thời Concurrency (Priority: P1)
> Là một **Admin**, tôi muốn cập nhật số lượng tồn kho của sản phẩm khi nhập hàng mới hoặc kiểm kê kho, đảm bảo không bị ghi đè dữ liệu khi nhiều người cùng thao tác.

- **Why this priority**: Đảm bảo số lượng tồn kho phản ánh đúng thực tế để nhân viên không bán vượt tồn (Overselling).
- **Independent Test**: Giả lập 2 request cập nhật kho đồng thời với cùng một `RowVersion`, request đến sau phải bị chặn với lỗi `DbUpdateConcurrencyException`.
- **Acceptance Scenarios**:
  1. **Given** Sản phẩm có số lượng tồn hiện tại là 20, **When** Admin điều chỉnh cộng thêm 30 đơn vị, **Then** Tồn kho mới là 50 và `RowVersion` được làm mới.
  2. **Given** Admin A và Admin B cùng mở trang sửa sản phẩm X (cùng `RowVersion`), **When** Admin A lưu thành công, sau đó Admin B nhấn lưu thay đổi khác, **Then** Yêu cầu của Admin B bị từ chối với mã lỗi `409 Conflict` (Dữ liệu đã bị thay đổi bởi người khác, vui lòng tải lại).

#### User Story 3 - Quản lý Gói Combo Giờ chơi & F&B (Priority: P2)
> Là một **Admin**, tôi muốn tạo các gói Combo hấp dẫn (ví dụ: Combo 2 giờ chơi + 2 nước ngọt + 1 snack) với giá ưu đãi để tăng doanh thu.

- **Why this priority**: Combo là nghiệp vụ kinh doanh chiến lược của quán bida nhằm thu hút khách hàng trải nghiệm trọn gói dịch vụ.
- **Independent Test**: Tạo Combo kèm danh sách `ComboItems`, kiểm tra tính toàn vẹn khóa ngoại và ràng buộc số lượng món > 0, giờ chơi > 0.
- **Acceptance Scenarios**:
  1. **Given** Admin chọn các sản phẩm F&B đang hoạt động và nhập số giờ chơi hợp lệ (> 0), giá gói hợp lệ (>= 0), **When** Admin lưu gói Combo, **Then** Hệ thống tạo bản ghi trong `Combos` và các dòng chi tiết trong `ComboItems`.
  2. **Given** Admin cố tình tạo Combo không có món ăn/nước uống nào hoặc số giờ chơi <= 0, **When** Gửi yêu cầu lưu, **Then** Hệ thống chặn lại với lỗi validation cụ thể.

#### User Story 4 - Vô hiệu hóa (Soft Delete) Sản phẩm & Combo (Priority: P2)
> Là một **Admin**, tôi muốn ngừng kinh doanh một món hoặc gói combo mà không làm hỏng lịch sử hóa đơn cũ đã phát sinh.

- **Why this priority**: Tuân thủ nguyên tắc toàn vẹn dữ liệu (Referential Integrity) theo Hiến chương dự án.
- **Independent Test**: Thử chuyển trạng thái `IsActive = false` của sản phẩm đã nằm trong `OrderDetails`, xác nhận món không còn xuất hiện trên menu order mới nhưng hóa đơn cũ vẫn hiển thị đúng.
- **Acceptance Scenarios**:
  1. **Given** Sản phẩm đã có trong các đơn hàng trước đó, **When** Admin chọn "Ngừng bán", **Then** Hệ thống cập nhật `IsActive = false`. Món này bị ẩn khỏi menu order mới nhưng không bị xóa khỏi CSDL.

---

### 1.3 Edge Cases
- **Tồn kho âm**: Không cho phép số lượng tồn kho giảm xuống dưới 0 (`StockQuantity >= 0`).
- **Giá sản phẩm = 0**: Theo schema database `CK_Product_Price CHECK (Price > 0)`, mọi sản phẩm lẻ phải có đơn giá lớn hơn 0 (không hỗ trợ giá 0đ hoặc âm cho sản phẩm lẻ).
- **Giá Combo = 0**: Theo schema database `CK_Combo_Price CHECK (Price >= 0)`, Combo có thể có giá bằng 0 nếu là gói quà tặng/khuyến mãi tri ân.
- **Xóa danh mục đang chứa sản phẩm**: Chặn xóa Category nếu còn sản phẩm trực thuộc (kể cả sản phẩm Inactive).

---

## 2. Data Models & Schema Constraints

Dựa trên schema chuẩn của database `BilliardDB` (đã triển khai trong `BMS_Database_Starter/BilliardDB_Full.sql`):

### 2.1 Entity: `ProductCategory`
```csharp
[Table("ProductCategories")]
public class ProductCategory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên danh mục không được để trống")]
    [StringLength(60, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    // Navigation properties
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
```

### 2.2 Entity: `Product`
```csharp
[Table("Products")]
public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Category))]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm")]
    public int StockQuantity { get; set; } = 0;

    [Required]
    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Navigation properties
    public virtual ProductCategory Category { get; set; } = null!;
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public virtual ICollection<ComboItem> ComboItems { get; set; } = new List<ComboItem>();
}
```

### 2.3 Entity: `Combo` & `ComboItem`
```csharp
[Table("Combos")]
public class Combo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên combo không được để trống")]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Giá combo phải lớn hơn hoặc bằng 0")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(0.1, 24.0, ErrorMessage = "Số giờ chơi trong combo phải lớn hơn 0")]
    [Column(TypeName = "decimal(5,2)")]
    public decimal PlaytimeHours { get; set; }

    [Required]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<ComboItem> Items { get; set; } = new List<ComboItem>();
}

[Table("ComboItems")]
public class ComboItem
{
    [Required]
    [ForeignKey(nameof(Combo))]
    public int ComboId { get; set; }

    [Required]
    [ForeignKey(nameof(Product))]
    public int ProductId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng món trong combo phải lớn hơn 0")]
    public int Quantity { get; set; }

    // Navigation properties
    public virtual Combo Combo { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
```

### 2.4 SQL Schema Constraints Tham Chiếu
- `UQ_ProductCategories_Name`: `UNIQUE(Name)`
- `UQ_Products_Name`: `UNIQUE(Name)`
- `CK_Product_Price`: `CHECK (Price > 0)`
- `CK_Product_Stock`: `CHECK (StockQuantity >= 0)`
- `PK_ComboItems`: Composite Primary Key `(ComboId, ProductId)`
- `CK_ComboItems_Quantity`: `CHECK (Quantity > 0)`
- `CK_Combo_Playtime`: `CHECK (PlaytimeHours > 0)`

---

## 3. Business Rules & Validations

1. **BR-FNB-01 (Tên duy nhất)**: Tên Category, tên Product và tên Combo là duy nhất trên toàn hệ thống (so sánh case-insensitive, tự động loại bỏ khoảng trắng thừa đầu/cuối bằng `Trim()`).
2. **BR-FNB-02 (Ràng buộc giá và tồn kho)**:
   - `Product.Price > 0`.
   - `Product.StockQuantity >= 0`.
   - `Combo.PlaytimeHours > 0`.
   - `Combo.Price >= 0`.
3. **BR-FNB-03 (Quy tắc Combo Items)**: Mỗi combo phải chứa ít nhất một sản phẩm F&B (`Items.Count >= 1`). Không được phép đưa sản phẩm đã bị vô hiệu hóa (`IsActive = false`) vào combo mới.
4. **BR-FNB-04 (Bảo toàn lịch sử & Xóa mềm)**:
   - Không được xóa vật lý (Hard Delete) sản phẩm đã từng phát sinh trong bất kỳ `OrderDetails` hoặc `SessionComboItems` nào.
   - Thao tác xóa sẽ được tự động chuyển thành cập nhật cờ `IsActive = false`.
5. **BR-FNB-05 (Kiểm soát Concurrency)**: Mọi thao tác cập nhật sản phẩm phải gửi kèm chuỗi `RowVersion` (dạng Base64). Nếu `RowVersion` trong CSDL không khớp, hệ thống từ chối cập nhật và yêu cầu client refresh lại dữ liệu.

---

## 4. API & Endpoint Specifications

### 4.1 Danh mục sản phẩm (Categories)
- **`GET /api/v1/fnb/categories`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200**: `List<CategoryResponseDto>`
- **`POST /api/v1/fnb/categories`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Request Body**: `CreateCategoryDto { string Name }`
  - **Response 201**: `CategoryResponseDto`
  - **Error 400**: Tên trống hoặc đã tồn tại (`ERR_CATEGORY_NAME_DUPLICATE`).

### 4.2 Sản phẩm (Products)
- **`GET /api/v1/fnb/products?categoryId={id}&activeOnly={bool}&keyword={text}`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200**: `PagedResult<ProductListDto>`
- **`POST /api/v1/fnb/products`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Request Body**: `CreateProductDto { int CategoryId, string Name, decimal Price, int StockQuantity }`
  - **Response 201**: `ProductDetailDto`
  - **Error 400**: Dữ liệu không hợp lệ / Trùng tên (`ERR_PRODUCT_NAME_DUPLICATE`).
- **`PUT /api/v1/fnb/products/{id}`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Request Body**: `UpdateProductDto { int CategoryId, string Name, decimal Price, string RowVersionBase64 }`
  - **Response 200**: `ProductDetailDto`
  - **Error 409**: Xung đột phiên bản (`ERR_CONCURRENCY_CONFLICT`).
- **`PATCH /api/v1/fnb/products/{id}/adjust-stock`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Request Body**: `AdjustStockDto { int QuantityDelta, string Reason, string RowVersionBase64 }`
  - **Response 200**: `{ int NewStockQuantity, string NewRowVersionBase64 }`
  - **Error 400**: Tồn kho sau điều chỉnh bị âm (`ERR_STOCK_CANNOT_BE_NEGATIVE`).

### 4.3 Gói Combo (Combos)
- **`GET /api/v1/fnb/combos?activeOnly={bool}`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200**: `List<ComboResponseDto>`
- **`POST /api/v1/fnb/combos`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Request Body**:
    ```json
    {
      "name": "Combo Sinh Viên 2H",
      "price": 150000,
      "playtimeHours": 2.0,
      "items": [
        { "productId": 1, "quantity": 2 },
        { "productId": 5, "quantity": 1 }
      ]
    }
    ```
  - **Response 201**: `ComboResponseDto`
  - **Error 400**: Danh sách sản phẩm rỗng hoặc sản phẩm không tồn tại (`ERR_INVALID_COMBO_ITEMS`).

---

## 5. Acceptance Criteria (Gherkin Format)

### Scenario 1: Tạo mới sản phẩm thành công (Happy Path)
```gherkin
Given Admin đã đăng nhập hệ thống với tài khoản quản trị
And Đã tồn tại danh mục có Id = 1 mang tên "Nước giải khát"
When Admin gửi yêu cầu tạo sản phẩm với thông tin:
  | Field         | Value       |
  | CategoryId    | 1           |
  | Name          | "Trà đào"   |
  | Price         | 25000       |
  | StockQuantity | 50          |
Then Hệ thống lưu sản phẩm thành công vào bảng Products
And Trả về mã HTTP 201 Created kèm dữ liệu sản phẩm vừa tạo
And Sản phẩm có IsActive = true và RowVersion hợp lệ
```

### Scenario 2: Tạo sản phẩm trùng tên bị từ chối (Exception Path)
```gherkin
Given Trong hệ thống đã tồn tại sản phẩm mang tên "Sting Dâu"
When Admin cố gắng tạo thêm một sản phẩm mới mang tên "sting dâu"
Then Hệ thống từ chối lưu dữ liệu
And Trả về mã lỗi HTTP 400 Bad Request
And Thông báo lỗi hiển thị rõ "Tên sản phẩm 'Sting Dâu' đã tồn tại trong hệ thống."
```

### Scenario 3: Tranh chấp dữ liệu đồng thời khi sửa sản phẩm (Concurrency Conflict)
```gherkin
Given Sản phẩm "Khăn lạnh" có Id = 10, Price = 5000 và RowVersion hiện tại là "AAAAAAAAB9E="
And Admin A và Admin B cùng mở trang cập nhật sản phẩm này
When Admin A cập nhật Price = 6000 kèm RowVersion "AAAAAAAAB9E=" thành công
And Admin B sau đó gửi cập nhật Price = 7000 vẫn kèm RowVersion cũ "AAAAAAAAB9E="
Then Yêu cầu của Admin B bị chặn lại
And Hệ thống trả về mã lỗi HTTP 409 Conflict
And Thông báo lỗi: "Dữ liệu sản phẩm đã được thay đổi bởi người dùng khác. Vui lòng làm mới trang."
```

### Scenario 4: Tạo Combo với danh sách sản phẩm hợp lệ (Happy Path)
```gherkin
Given Admin muốn tạo gói khuyến mãi "Combo Đêm 3H"
And Các sản phẩm "Redbull" (Id = 2) và "Mì tôm trứng" (Id = 8) đang hoạt động bình thường
When Admin gửi yêu cầu tạo Combo với:
  | Field         | Value          |
  | Name          | "Combo Đêm 3H" |
  | Price         | 220000         |
  | PlaytimeHours | 3.0            |
  | Items         | (Id:2, Qty:2), (Id:8, Qty:1) |
Then Combo được tạo thành công trong bảng Combos
And Có đúng 2 bản ghi chi tiết được ghi vào bảng ComboItems
And Trả về HTTP 201 Created
```
