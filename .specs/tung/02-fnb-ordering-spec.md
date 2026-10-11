# Feature Specification: Gọi món F&B theo Phiên chơi (F&B In-Session Ordering)

**Feature Identifier**: `02-fnb-ordering`  
**Assigned Engineer**: Tùng (Module: F&B, Billing & Reporting)  
**Target Use Case**: UC30 (Order F&B cho khách)  
**Iteration**: 1  
**Status**: Approved / Ready for Implementation  
**Created Date**: 2026-10-11  

---

## 1. Feature Scope & User Stories

### 1.1 Tổng quan phạm vi (Feature Scope)
Tính năng cho phép nhân viên phục vụ (Staff) hoặc thu ngân (Cashier) ghi nhận các yêu cầu gọi đồ ăn, thức uống, thuốc lá, khăn lạnh... trực tiếp cho một bàn đang có phiên chơi hoạt động (`PlaySessions.Status = 'Active'`). Hệ thống thực hiện trừ tồn kho tức thời (Real-time Stock Deduction) có kiểm soát tương tranh nhằm ngăn chặn bán vượt tồn kho (Overselling), đồng thời phát tín hiệu thông báo thời gian thực qua SignalR đến màn hình chuẩn bị của bộ phận Bar/Bếp.

- **Actors**:
  - `Staff` / `Cashier`: Nhận order từ khách, chọn món, xác nhận số lượng, hủy món chưa chế biến.
  - `Bar / Kitchen Staff`: Theo dõi danh sách order theo trạng thái (`Pending` -> `Preparing` -> `Served`).
- **Pre-conditions**:
  - Bàn bi-a phải đang có một phiên chơi hợp lệ ở trạng thái `Active` (`PlaySessions.Status = 'Active'`).
  - Sản phẩm F&B được chọn phải đang mở bán (`IsActive = true`) và còn tồn kho (`StockQuantity > 0`).
- **Post-conditions**:
  - Bản ghi đơn hàng mới được tạo trong `Orders` với trạng thái ban đầu là `Pending`.
  - Các bản ghi con được lưu vào `OrderDetails` kèm đơn giá chốt tại thời điểm đặt (`UnitPrice`).
  - Tồn kho `StockQuantity` của từng sản phẩm tương ứng trong bảng `Products` bị trừ ngay lập tức.
  - Bộ phận pha chế/bếp nhận được thông báo đơn mới qua kênh SignalR `ReceiveNewOrder`.

---

### 1.2 User Stories & Testing

#### User Story 1 - Ghi nhận Order món cho phiên chơi đang hoạt động (Priority: P1)
> Là một **Nhân viên (Staff)**, tôi muốn thêm danh sách món F&B mà khách yêu cầu vào phiên chơi của bàn để phục vụ và tính tiền sau đó.

- **Why this priority**: Là luồng nghiệp vụ cốt lõi tạo ra doanh thu dịch vụ phụ trợ trong suốt thời gian khách chơi bi-a.
- **Independent Test**: Chọn một phiên chơi đang `Active`, gửi payload gồm 2 món còn hàng, kiểm tra `Orders`, `OrderDetails` được tạo và `StockQuantity` bị trừ chính xác.
- **Acceptance Scenarios**:
  1. **Given** Phiên chơi Id = 105 đang ở trạng thái `Active`, và sản phẩm "Bò Húc" còn 15 lon trong kho, **When** Nhân viên đặt 2 lon "Bò Húc", **Then** Đơn hàng được tạo thành công, tồn kho "Bò Húc" giảm xuống còn 13 lon, và tổng tiền tạm tính của phiên chơi tăng lên tương ứng.
  2. **Given** Bàn số 5 chưa mở phiên hoặc đã kết thúc phiên chơi (`Status = 'Closed'`), **When** Nhân viên cố gắng gửi order vào SessionId này, **Then** Hệ thống từ chối với lỗi `400 Bad Request` và thông báo "Phiên chơi không tồn tại hoặc đã kết thúc".

#### User Story 2 - Trừ kho tức thời & Chống bán vượt tồn kho (Priority: P1)
> Là một **Hệ thống Quản lý**, tôi muốn đảm bảo số lượng order không vượt quá tồn kho thực tế và xử lý an toàn khi nhiều nhân viên cùng đặt món cuối cùng trong kho.

- **Why this priority**: Bảo vệ uy tín dịch vụ, tránh tình trạng nhận order của khách nhưng thực tế trong kho đã hết hàng.
- **Independent Test**: Giả lập 2 request đặt món cùng 1 lúc khi kho chỉ còn 1 sản phẩm. Chỉ 1 request được thành công, request còn lại phải bị từ chối rõ ràng.
- **Acceptance Scenarios**:
  1. **Given** Sản phẩm "Mì xào bò" chỉ còn tồn kho là 3 suất, **When** Nhân viên đặt 5 suất, **Then** Hệ thống từ chối toàn bộ đơn hàng với lỗi `400 Bad Request` kèm thông báo: "Sản phẩm 'Mì xào bò' chỉ còn 3 suất trong kho".
  2. **Given** Sản phẩm "Bia Heineken" chỉ còn 1 lon cuối cùng, **When** Nhân viên A (Bàn 1) và Nhân viên B (Bàn 2) cùng gửi order 1 lon gần như cùng 1 tích tắc, **Then** Hệ thống sử dụng Transaction và `RowVersion` để chấp nhận 1 bên và từ chối bên còn lại với mã `409 Conflict` (Sản phẩm vừa hết hàng).

#### User Story 3 - Cập nhật tiến độ phục vụ món (Priority: P2)
> Là một **Nhân viên pha chế / Bếp**, tôi muốn cập nhật trạng thái món từ `Pending` sang `Preparing` và sang `Served` để toàn bộ ca trực nắm được tiến độ phục vụ.

- **Why this priority**: Giúp tăng tốc độ phối hợp giữa nhân viên chạy bàn và nhân viên quầy bar/bếp.
- **Independent Test**: Gửi PATCH request cập nhật trạng thái đơn hàng và kiểm tra SignalR event được broadcast tới client.
- **Acceptance Scenarios**:
  1. **Given** Đơn hàng Id = 50 đang ở trạng thái `Pending`, **When** Nhân viên bếp nhấn "Bắt đầu làm", **Then** Trạng thái đơn đổi sang `Preparing` và phát thông báo real-time tới giao diện chạy bàn.
  2. **Given** Đơn hàng Id = 50 đang ở trạng thái `Preparing`, **When** Nhân viên bưng món ra bàn và xác nhận "Đã phục vụ", **Then** Trạng thái đơn đổi sang `Served`.

#### User Story 4 - Hủy món chưa phục vụ & Tự động hoàn kho (Priority: P2)
> Là một **Nhân viên thu ngân**, tôi muốn hủy món ăn khi khách đổi ý (khi món chưa chế biến) và tự động trả lại số lượng về kho hàng.

- **Why this priority**: Cho phép linh hoạt điều chỉnh order theo yêu cầu khách nhưng không làm thất thoát số liệu tồn kho.
- **Independent Test**: Hủy một Order đang ở trạng thái `Pending`, xác nhận trạng thái chuyển sang `Cancelled` và `StockQuantity` của các sản phẩm liên quan được cộng hoàn trả đầy đủ.
- **Acceptance Scenarios**:
  1. **Given** Đơn hàng Id = 60 gồm 3 chai nước suối đang ở trạng thái `Pending` (kho hiện có 20 chai), **When** Khách báo không lấy và nhân viên bấm "Hủy đơn", **Then** Trạng thái đơn chuyển sang `Cancelled` và tồn kho nước suối tự động được hoàn lại thành 23 chai.
  2. **Given** Đơn hàng Id = 61 đã được bếp chế biến xong và đổi sang trạng thái `Served`, **When** Nhân viên cố tình gửi lệnh hủy đơn, **Then** Hệ thống từ chối với mã lỗi `400 Bad Request` ("Không thể hủy đơn hàng đã được phục vụ").

---

### 1.3 Edge Cases
- **Phiên chơi vừa bị đóng trong lúc đang chọn món**: Nếu thu ngân vừa bấm thanh toán/đóng phiên, lệnh order gửi lên sau đó phải bị rollback hoàn toàn.
- **Sản phẩm bị Admin vô hiệu hóa (`IsActive = false`)**: Không cho phép order các sản phẩm đã ngừng kinh doanh kể cả khi trong kho vẫn còn số dư tồn.
- **Mạng chập chờn / Double Click**: Sử dụng cơ chế Idempotency hoặc Transaction isolation để chống nhân viên vô tình bấm đúp nút "Xác nhận gọi món" làm nhân đôi đơn hàng.

---

## 2. Data Models & Schema Constraints

Dựa trên bảng `dbo.Orders` và `dbo.OrderDetails` trong `BMS_Database_Starter/BilliardDB_Full.sql`:

### 2.1 Entity: `Order`
```csharp
[Table("Orders")]
public class Order
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Session))]
    public int SessionId { get; set; }

    [Required]
    [Column(TypeName = "datetime2(0)")]
    public DateTime OrderTimeUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [StringLength(20)]
    [RegularExpression("Pending|Preparing|Served|Cancelled", ErrorMessage = "Trạng thái đơn hàng không hợp lệ")]
    public string Status { get; set; } = "Pending";

    // Navigation properties
    public virtual PlaySession Session { get; set; } = null!;
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
```

### 2.2 Entity: `OrderDetail`
```csharp
[Table("OrderDetails")]
public class OrderDetail
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Order))]
    public int OrderId { get; set; }

    [Required]
    [ForeignKey(nameof(Product))]
    public int ProductId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng món phải lớn hơn 0")]
    public int Quantity { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Đơn giá không được âm")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    // Computed column (Persisted in SQL Server)
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; private set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
```

### 2.3 SQL Schema Constraints Tham Chiếu
- `FK_Order_Session`: `FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id)`
- `CK_Order_Status`: `CHECK (Status IN ('Pending', 'Preparing', 'Served', 'Cancelled'))`
- `FK_OrderDetail_Order`: `FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id) ON DELETE CASCADE`
- `FK_OrderDetail_Product`: `FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)`
- `CK_OrderDetail_Qty`: `CHECK (Quantity > 0)`
- `CK_OrderDetail_Price`: `CHECK (UnitPrice >= 0)`
- `Subtotal AS (Quantity * UnitPrice) PERSISTED`

---

## 3. Business Rules & Validations

1. **BR-ORD-01 (Điều kiện phiên chơi)**:
   - Order chỉ được tạo khi `PlaySessions.Status == 'Active'`.
   - Nếu `PlaySessions.Status == 'Closed'`, hệ thống lập tức từ chối và không tạo Order.
2. **BR-ORD-02 (Trừ kho tức thời & Atomic Transaction)**:
   - Toàn bộ quá trình kiểm tra tồn kho, trừ tồn kho `StockQuantity` và tạo các bản ghi `Orders`, `OrderDetails` PHẢI được bọc trong một Database Transaction duy nhất (`IsolationLevel.ReadCommitted` hoặc `Serializable`).
   - Nếu có bất kỳ món nào trong đơn có `Quantity > Product.StockQuantity`, Transaction PHẢI rollback toàn bộ. Tuyệt đối không cho phép tạo đơn một phần (Partial fulfillment).
3. **BR-ORD-03 (Snapshot đơn giá)**:
   - `OrderDetail.UnitPrice` phải được đọc trực tiếp từ `Product.Price` tại thời điểm tạo order.
   - Tuyệt đối không nhận `UnitPrice` do client/trình duyệt gửi lên để chống gian lận giá.
4. **BR-ORD-04 (Quy trình chuyển trạng thái đơn hàng)**:
   - `Pending` -> `Preparing` -> `Served`.
   - Chỉ được phép `Cancelled` khi đơn hàng đang ở trạng thái `Pending`.
   - Khi chuyển sang `Cancelled`, hệ thống PHẢI tự động hoàn trả số lượng vào `Product.StockQuantity` trong cùng một transaction.
5. **BR-ORD-05 (Tính tiền vào hóa đơn)**:
   - Khi tính tổng hóa đơn bàn, chỉ những Order có trạng thái `Served` (hoặc khác `Cancelled` tùy nghiệp vụ chốt) mới được cộng vào `TotalFandB`.
   - Các đơn có trạng thái `Cancelled` không tính tiền cho khách.

---

## 4. API & Endpoint Specifications

### 4.1 Gọi món cho phiên chơi
- **`POST /api/v1/sessions/{sessionId}/orders`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Request Body**:
    ```json
    {
      "items": [
        { "productId": 1, "quantity": 2 },
        { "productId": 4, "quantity": 1 }
      ]
    }
    ```
  - **Response 201 Created**:
    ```json
    {
      "orderId": 85,
      "sessionId": 12,
      "status": "Pending",
      "orderTimeUtc": "2026-10-11T14:40:00Z",
      "totalItems": 3,
      "totalAmount": 75000.00,
      "details": [
        { "productId": 1, "productName": "Trà đào", "quantity": 2, "unitPrice": 25000.00, "subtotal": 50000.00 },
        { "productId": 4, "productName": "Khoai tây chiên", "quantity": 1, "unitPrice": 25000.00, "subtotal": 25000.00 }
      ]
    }
    ```
  - **Error Codes**:
    - `400 Bad Request`: `ERR_SESSION_NOT_ACTIVE` (Phiên không hoạt động)
    - `400 Bad Request`: `ERR_INSUFFICIENT_STOCK` (Không đủ tồn kho: kèm tên sản phẩm và số lượng còn lại)
    - `404 Not Found`: `ERR_PRODUCT_NOT_FOUND` (Mã sản phẩm không tồn tại hoặc đã ngừng bán)
    - `409 Conflict`: `ERR_STOCK_CONCURRENCY` (Tranh chấp tồn kho đồng thời)

### 4.2 Lấy danh sách order của phiên chơi
- **`GET /api/v1/sessions/{sessionId}/orders`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200 OK**: `List<OrderSummaryDto>`

### 4.3 Cập nhật trạng thái chuẩn bị / phục vụ
- **`PATCH /api/v1/orders/{orderId}/status`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Request Body**: `{ "status": "Preparing" }` hoặc `{ "status": "Served" }`
  - **Response 200 OK**: `{ "orderId": 85, "newStatus": "Served", "updatedAtUtc": "..." }`
  - **Error 400**: Chuyển đổi trạng thái không hợp lệ (`ERR_INVALID_STATUS_TRANSITION`).

### 4.4 Hủy order (Hoàn kho)
- **`POST /api/v1/orders/{orderId}/cancel`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Request Body**: `{ "reason": "Khách đổi ý không dùng nước" }`
  - **Response 200 OK**: `{ "orderId": 85, "status": "Cancelled", "restockedItems": 2 }`
  - **Error 400**: Đơn hàng đã sang trạng thái `Served`, không thể hủy (`ERR_CANNOT_CANCEL_SERVED_ORDER`).

---

## 5. Acceptance Criteria (Gherkin Format)

### Scenario 1: Đặt món thành công và trừ tồn kho tức thời (Happy Path)
```gherkin
Given Bàn số 3 đang có phiên chơi PlaySession Id = 12 ở trạng thái "Active"
And Sản phẩm "Bò Húc" (Id = 5) có giá 20.000 VNĐ và tồn kho hiện tại là 10
When Nhân viên gửi yêu cầu gọi món gồm 3 lon "Bò Húc" cho phiên chơi Id = 12
Then Hệ thống tạo mới một bản ghi trong Orders với Status = "Pending"
And Bản ghi OrderDetails được tạo với Quantity = 3, UnitPrice = 20000, Subtotal = 60000
And Tồn kho của "Bò Húc" trong bảng Products giảm xuống còn 7
And Broadcast thông báo SignalR "ReceiveNewOrder" đến kênh của quầy pha chế
And Trả về mã HTTP 201 Created
```

### Scenario 2: Đặt món vượt quá tồn kho khả dụng (Exception Path)
```gherkin
Given Sản phẩm "Mì xào hải sản" (Id = 18) chỉ còn lại 2 suất trong kho
When Nhân viên cố gắng gửi order 3 suất "Mì xào hải sản" cho phiên chơi Id = 12
Then Hệ thống từ chối toàn bộ đơn hàng và rollback mọi thay đổi
And Tồn kho của "Mì xào hải sản" vẫn giữ nguyên là 2
And Không có bản ghi nào được thêm vào Orders hoặc OrderDetails
And Trả về mã lỗi HTTP 400 Bad Request
And Thông báo lỗi hiển thị rõ: "Sản phẩm 'Mì xào hải sản' không đủ số lượng tồn kho (Còn lại: 2, Yêu cầu: 3)."
```

### Scenario 3: Đặt món cho bàn đã đóng phiên (Exception Path)
```gherkin
Given Bàn số 2 đã kết thúc phiên chơi và PlaySession Id = 8 có Status = "Closed"
When Nhân viên gửi yêu cầu đặt món F&B cho PlaySession Id = 8
Then Hệ thống từ chối yêu cầu
And Trả về mã HTTP 400 Bad Request
And Thông báo lỗi: "Không thể gọi món vì phiên chơi đã kết thúc."
```

### Scenario 4: Hủy đơn hàng Pending và tự động hoàn trả tồn kho (Happy Path)
```gherkin
Given Đơn hàng Order Id = 92 có Status = "Pending" chứa 2 lon "Nước khoáng Lavie"
And Tồn kho hiện tại của "Nước khoáng Lavie" đang là 48 chai
When Nhân viên gửi yêu cầu hủy đơn hàng Id = 92 với lý do "Khách đổi sang gọi nước ngọt"
Then Trạng thái của Order Id = 92 chuyển thành "Cancelled"
And Tồn kho của "Nước khoáng Lavie" tự động được cộng hoàn lại thành 50 chai
And Trả về mã HTTP 200 OK với thông báo hủy đơn và hoàn kho thành công
```

### Scenario 5: Từ chối hủy đơn hàng đã phục vụ (Exception Path)
```gherkin
Given Đơn hàng Order Id = 95 đã được phục vụ ra bàn và có Status = "Served"
When Nhân viên gửi yêu cầu hủy đơn hàng Id = 95
Then Hệ thống từ chối thao tác hủy
And Trả về mã HTTP 400 Bad Request
And Thông báo lỗi: "Không thể hủy đơn hàng đã được phục vụ cho khách."
And Tồn kho sản phẩm không bị thay đổi
```
