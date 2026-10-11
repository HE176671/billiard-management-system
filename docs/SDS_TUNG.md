# Billiard Management System

## Software Design Specification — Phần của Tùng (F&B, Billing & Reporting)

**Ngày cập nhật:** 11/10/2026. **Phiên bản:** 1.0. **Người phụ trách theo phân công:** Tùng (Software Engineer — Thành viên 4).  
**Phạm vi nghiệp vụ:** Phân hệ Thực đơn, Kho hàng F&B, Gói Combo, Gọi món, Tính tiền / Thanh toán / In Bill và Báo cáo Doanh thu tài chính (UC41, UC42, UC30, UC27, UC28, UC29, UC47).

Tài liệu cung cấp thiết kế kiến trúc, cấu trúc package, chi tiết 10 bảng dữ liệu trong CSDL `BilliardDB` và thiết kế luồng xử lý chi tiết kèm Class Diagram và Sequence Diagram. Cấu trúc đánh số bám sát chuẩn SDS của nhóm và tương thích hoàn toàn với tài liệu phần Hùng (`docs/SDS_HUNG.md`).

---

### Change Log

| Date | A / M / D | In charge | Change Description |
|---|---|---|---|
| 11/10/2026 | A | Tùng | Khởi tạo SDS cho phân hệ Thực đơn, Kho hàng, Combo, Đặt món, Thanh toán & Báo cáo tài chính; đối chiếu với schema 24 bảng trong `BilliardDB` |

---

# I. High Level Design

## 1. Software Architecture

### 1.1 Kiến trúc tổng thể
Phân hệ Thực đơn, Gọi món, Thanh toán và Báo cáo của Tùng được tích hợp trực tiếp vào dự án ASP.NET Core MVC `src/Bms.Web` (nhắm .NET 8), kết nối trực tiếp vào cơ sở dữ liệu SQL Server `BilliardDB`:
- **Admin**: Sử dụng Razor Views kế thừa layout `_AdminLayout.cshtml` để quản lý Danh mục, Sản phẩm, Điều chỉnh tồn kho, Quản lý Combo và xem Báo cáo doanh thu.
- **Staff / Cashier**: Sử dụng giao diện Gọi món cho phiên chơi và Thu ngân (Checkout / Preview / In bill).
- **Web API Endpoints**: Cung cấp các REST API (`/api/v1/fnb/...`, `/api/v1/reports/...`) phục vụ tương tác bất đồng bộ (AJAX) trên màn hình POS và giao tiếp thời gian thực qua SignalR.
- **Nguồn dữ liệu duy nhất**: Toàn bộ dữ liệu được truy vấn và cập nhật vào SQL Server `BilliardDB` thật thông qua Entity Framework Core 8 (`ApplicationDbContext`) và Stored Procedures (`dbo.usp_CloseSession`). Tuyệt đối không sử dụng Mock Data.

```mermaid
flowchart TB
    Actor["Admin / Staff / Cashier"] --> Browser["Trình duyệt Web<br/>HTML5, CSS, Bootstrap, JavaScript"]
    subgraph App["Bms.Web — ASP.NET Core MVC & Web API"]
        Gate["Routing + Authentication + Authorization<br/>Anti-forgery cho POST Form"]
        Controllers["Controllers<br/>MenuController, ComboController<br/>OrderController, BillingController, ReportController<br/>Api/FnbCatalogApiController"]
        Views["Razor Views + ViewModels<br/>Thực đơn, Combo, Order POS, In Bill, Báo cáo"]
        Services["Tầng Dịch vụ Nghiệp vụ<br/>IMenuService / MenuService<br/>IOrderService, IBillingService, IReportService"]
        Data["ApplicationDbContext / EF Core 8<br/>SQL có tham số, Transactions, Optimistic Concurrency"]
        Adapters["Report Adapters<br/>EPPlus (.xlsx) | QuestPDF (.pdf)"]

        Gate --> Controllers
        Controllers --> Views
        Controllers --> Services
        Services --> Data
        Services --> Adapters
    end
    Browser -->|"HTTPS request + Cookie + Form/JSON"| Gate
    Views -->|"HTML / View / POS Thermal Preview"| Browser
    Adapters -->|"Stream File Download (.xlsx, .pdf)"| Browser
    Data -->|"SQL Queries / Stored Procedures / Transactions"| DB[("SQL Server<br/>BilliardDB")]
```

**Hình I.1 — Kiến trúc logic phân hệ F&B, Billing & Reporting.**

### 1.2 Architecture component descriptions

| No | Component | Description |
|---|---|---|
| **C01** | Client Web Browser | Hiển thị giao diện quản lý thực đơn, modal nhập kho, giao diện POS gọi món và xem trước hóa đơn thanh toán nhiệt 80mm. |
| **A01** | Routing & Security Pipeline | Phân quyền nghiêm ngặt: `[Authorize(Roles = "Admin")]` cho quản lý Menu/Kho/Combo/Báo cáo; `[Authorize(Roles = "Admin,Staff")]` cho Gọi món và Thanh toán; kiểm tra Anti-Forgery Token trên toàn bộ request POST. |
| **A02** | Controllers | `MenuController` (CRUD món/kho), `ComboController` (gói combo), `OrderController` (gọi món), `BillingController` (tính tiền, chốt bill), `ReportController` (báo cáo), `FnbCatalogApiController` (REST API). |
| **A03** | Services Layer | `IMenuService`, `IOrderService`, `IBillingService`, `IReportService`. Đóng gói toàn bộ transaction, concurrency check (`RowVersion`), kiểm tra tồn kho và công thức tính tiền. |
| **A04** | Data Access | `ApplicationDbContext` (EF Core 8) ánh xạ trực tiếp vào 10 bảng của Tùng trong `BilliardDB`. Thực thi `dbo.usp_CloseSession` khi kết thúc phiên. |
| **A05** | Reporting Adapters | Sử dụng thư viện **EPPlus** xuất file Excel (.xlsx) định dạng chuẩn kế toán và **QuestPDF** xuất file PDF khổ in A4. |
| **D01** | SQL Server (`BilliardDB`) | Cơ sở dữ liệu duy nhất chứa 24 bảng nghiệp vụ, bảo vệ toàn vẹn bằng PK, FK, CHECK constraints, UNIQUE indexes và RowVersion concurrency tokens. |

### 1.3 Ranh giới trách nhiệm
- **Phạm vi của Tùng**: Quản lý Menu & Kho hàng (`ProductCategories`, `Products`), Combo (`Combos`, `ComboItems`), Gọi món cho phiên (`Orders`, `OrderDetails`), Mua combo (`SessionCombos`, `SessionComboItems`), Tính hóa đơn & Thanh toán (`Invoices`, `PaymentTransactions`), và Báo cáo tài chính (EPPlus, QuestPDF).
- **Ranh giới với Thành viên khác**:
  - Không quản lý danh sách tài khoản hay mật khẩu (do Hùng phụ trách).
  - Không tự mở phiên chơi hay đổi trạng thái bàn sang `InUse` (do Thành viên 1 phụ trách). Khi thanh toán hoàn tất, Tùng chuyển bàn từ `AwaitingPayment` về `Available`.
  - Không can thiệp vào quy trình đặt bàn (Booking do Thành viên 2 & 3 phụ trách).

---

## 2. Package Diagram

```mermaid
flowchart LR
    Root["Bms.Web<br/>Program.cs — Composition Root"]
    C["«namespace» Bms.Web.Controllers<br/>MenuController, ComboController<br/>OrderController, BillingController, ReportController<br/>Api/FnbCatalogApiController"]
    S["«namespace» Bms.Web.Services<br/>IMenuService / MenuService<br/>IOrderService, IBillingService, IReportService"]
    M["«namespace» Bms.Web.Models<br/>MenuViewModels, ComboViewModels<br/>OrderViewModels, BillingViewModels, ReportViewModels"]
    E["«namespace» Bms.Web.Data.Entities<br/>ProductCategory, Product<br/>Combo, ComboItem, SessionCombo, SessionComboItem<br/>Order, OrderDetail, Invoice, PaymentTransaction"]
    D["«namespace» Bms.Web.Data<br/>ApplicationDbContext"]
    R["«external libs» Reporting<br/>EPPlus (Excel) | QuestPDF (PDF)"]
    EF["«framework» EF Core 8<br/>Microsoft.EntityFrameworkCore.SqlServer"]

    Root -.->|"đăng ký DI"| C
    Root -.->|"đăng ký DI"| S
    Root -.-> D
    C -->|"gọi nghiệp vụ"| S
    C -->|"nhận/trả model"| M
    S -->|"xử lý entities"| E
    S -->|"truy vấn DB"| D
    S -->|"xuất file"| R
    D --> EF
```

**Hình I.2 — Sơ đồ phụ thuộc Package phân hệ F&B, Billing & Reporting.**

---

## 3. Database Design — Mười bảng thuộc phạm vi của Tùng

Tất cả các bảng dưới đây đã được định nghĩa trong `BMS_Database_Starter/BilliardDB_Full.sql` và tồn tại trong `BilliardDB`:

```mermaid
erDiagram
    ProductCategories ||--o{ Products : "1 - N (FK_Product_Category)"
    Combos ||--|{ ComboItems : "1 - N (FK_ComboItems_Combo)"
    Products ||--o{ ComboItems : "1 - N (FK_ComboItems_Product)"

    PlaySessions ||--o{ SessionCombos : "1 - N (FK_SessionCombos_Session)"
    Combos ||--o{ SessionCombos : "1 - N (FK_SessionCombos_Combo)"
    SessionCombos ||--|{ SessionComboItems : "1 - N (FK_SessionComboItems_Purchase)"
    Products ||--o{ SessionComboItems : "1 - N (FK_SessionComboItems_Product)"

    PlaySessions ||--o{ Orders : "1 - N (FK_Order_Session)"
    Orders ||--|{ OrderDetails : "1 - N (FK_OrderDetail_Order)"
    Products ||--o{ OrderDetails : "1 - N (FK_OrderDetail_Product)"

    PlaySessions ||--|| Invoices : "1 - 1 (UQ_Invoice_Session)"
    Invoices ||--o{ PaymentTransactions : "1 - N (FK_Payment_Invoice)"
```

### 3.1 Bảng `dbo.ProductCategories` (Danh mục F&B)
| Tên cột | Kiểu dữ liệu | Ràng buộc / Ý nghĩa |
|---|---|---|
| `Id` | `int IDENTITY` | Primary Key |
| `Name` | `nvarchar(60)` | NOT NULL, UNIQUE (`UQ_ProductCategories_Name`), `LEN(TRIM(Name)) > 0` |

### 3.2 Bảng `dbo.Products` (Sản phẩm F&B & Tồn kho)
| Tên cột | Kiểu dữ liệu | Ràng buộc / Ý nghĩa |
|---|---|---|
| `Id` | `int IDENTITY` | Primary Key |
| `CategoryId` | `int` | FK tới `ProductCategories(Id)` |
| `Name` | `nvarchar(100)` | NOT NULL, UNIQUE (`UQ_Products_Name`) |
| `Price` | `decimal(18,2)` | NOT NULL, `Price > 0` (`CK_Product_Price`) |
| `StockQuantity` | `int` | NOT NULL, `StockQuantity >= 0` (`CK_Product_Stock`), Default `0` |
| `IsActive` | `bit` | NOT NULL, Default `1` (Cờ Soft Delete) |
| `RowVersion` | `rowversion` | NOT NULL (Optimistic Concurrency Token) |

### 3.3 Bảng `dbo.Combos` (Gói Combo)
| Tên cột | Kiểu dữ liệu | Ràng buộc / Ý nghĩa |
|---|---|---|
| `Id` | `int IDENTITY` | Primary Key |
| `Name` | `nvarchar(100)` | NOT NULL, UNIQUE (`UQ_Combos_Name`) |
| `Price` | `decimal(18,2)` | NOT NULL, `Price >= 0` (`CK_Combo_Price`) |
| `PlaytimeHours` | `decimal(5,2)` | NOT NULL, `PlaytimeHours > 0` (`CK_Combo_Playtime`) |
| `IsActive` | `bit` | NOT NULL, Default `1` |

### 3.4 Bảng `dbo.ComboItems` (Định lượng món trong Combo)
| Tên cột | Kiểu dữ liệu | Ràng buộc / Ý nghĩa |
|---|---|---|
| `ComboId` | `int` | Composite PK, FK tới `Combos(Id)` |
| `ProductId` | `int` | Composite PK, FK tới `Products(Id)` |
| `Quantity` | `int` | NOT NULL, `Quantity > 0` (`CK_ComboItems_Quantity`) |

### 3.5 Bảng `dbo.SessionCombos` & `dbo.SessionComboItems` (Snapshot lượt mua Combo)
- `SessionCombos`: `Id`, `SessionId`, `ComboId`, `Quantity`, `ComboNameSnapshot`, `UnitPriceSnapshot`, `PlaytimeHoursSnapshot`, `PurchasedAtUtc`, `CreatedById`.
- `SessionComboItems`: `SessionComboId`, `ProductId`, `ProductNameSnapshot`, `QuantityPerCombo`.
- **Nguyên tắc bảo toàn**: Snapshot giá trị lúc mua; cập nhật catalog combo không làm biến động dữ liệu đã mua trong quá khứ.

### 3.6 Bảng `dbo.Orders` & `dbo.OrderDetails` (Đơn gọi món F&B)
- `Orders`: `Id`, `SessionId`, `OrderTimeUtc`, `Status` (`Pending`, `Preparing`, `Served`, `Cancelled`).
- `OrderDetails`: `Id`, `OrderId` (CASCADE), `ProductId`, `Quantity > 0`, `UnitPrice >= 0`, `Subtotal AS (Quantity * UnitPrice) PERSISTED`.

### 3.7 Bảng `dbo.Invoices` & `dbo.PaymentTransactions` (Hóa đơn & Giao dịch thanh toán)
- `Invoices`: `Id`, `SessionId` (UNIQUE), `CustomerId`, `TotalPlaytime`, `TotalFandB`, `DiscountAmount`, `FinalAmount AS (TotalPlaytime + TotalFandB - DiscountAmount) PERSISTED`, `Status` (`Unpaid`, `Paid`).
- `PaymentTransactions`: `Id`, `InvoiceId` (CASCADE), `PaymentMethod` (`Cash`, `VNPay`, `SePay`, `Transfer`), `AmountPaid > 0`, `GatewayRefCode`, `TransactionTimeUtc`.

---

# II. Detailed Code Design

## 1. Class Diagram phân hệ Thực đơn & Kho hàng

```mermaid
classDiagram
    class MenuController {
        -IMenuService _menuService
        +Index(search, categoryId, status, page) Task~IActionResult~
        +CreateCategory(model) Task~IActionResult~
        +EditCategory(model) Task~IActionResult~
        +Create() Task~IActionResult~
        +Create(model) Task~IActionResult~
        +Edit(id) Task~IActionResult~
        +Edit(id, model) Task~IActionResult~
    }

    class ComboController {
        -IMenuService _menuService
        +Index(search, status) Task~IActionResult~
        +Create() Task~IActionResult~
        +Create(model) Task~IActionResult~
        +Edit(id) Task~IActionResult~
        +Edit(id, model) Task~IActionResult~
        +ToggleStatus(id) Task~IActionResult~
    }

    class IMenuService {
        <<interface>>
        +GetCategoriesAsync() Task~List~ProductCategory~~
        +CreateCategoryAsync(name) Task~ServiceResult~ProductCategory~~
        +UpdateCategoryAsync(id, name) Task~ServiceResult~ProductCategory~~
        +GetProductsPagedAsync(search, categoryId, status, page, pageSize) Task~Tuple~
        +CreateProductAsync(categoryId, name, price, stockQuantity) Task~ServiceResult~Product~~
        +UpdateProductAsync(id, categoryId, name, price, rowVersionBase64) Task~ServiceResult~Product~~
        +AdjustStockAsync(productId, quantityDelta, reason, rowVersionBase64) Task~ServiceResult~Product~~
        +ToggleProductStatusAsync(id, rowVersionBase64) Task~ServiceResult~Product~~
        +GetCombosAsync(search, statusFilter) Task~List~Combo~~
        +CreateComboAsync(name, price, playtimeHours, items) Task~ServiceResult~Combo~~
        +UpdateComboAsync(id, name, price, playtimeHours, items) Task~ServiceResult~Combo~~
        +ToggleComboStatusAsync(id) Task~ServiceResult~Combo~~
    }

    class MenuService {
        -ApplicationDbContext _context
    }

    MenuController --> IMenuService
    ComboController --> IMenuService
    MenuService ..|> IMenuService
```

## 2. Sequence Diagrams

### 2.1 Sequence Diagram: Gọi món F&B và Trừ kho tức thời (UC30)

```mermaid
sequenceDiagram
    actor Staff as Nhân viên (Staff)
    participant UI as Giao diện Gọi món (POS)
    participant Ctrl as OrderController / Api
    participant Svc as OrderService
    participant DB as SQL Server (BilliardDB)

    Staff->>UI: Chọn bàn đang chơi (SessionId) & Danh sách món
    UI->>Ctrl: POST /api/v1/sessions/{id}/orders (items)
    Ctrl->>Svc: PlaceOrderAsync(sessionId, items)
    
    activate Svc
    Svc->>DB: Kiểm tra PlaySessions.Status == 'Active'
    alt Phiên đã đóng ('Closed')
        DB-->>Svc: Status != 'Active'
        Svc-->>Ctrl: ServiceResult.Failure("ERR_SESSION_NOT_ACTIVE")
        Ctrl-->>UI: 400 Bad Request: "Phiên chơi đã kết thúc"
    else Phiên đang Active
        Svc->>DB: BEGIN TRANSACTION (Serializable)
        loop Duyệt từng món đặt
            Svc->>DB: SELECT StockQuantity, RowVersion FROM Products WITH (UPDLOCK) WHERE Id = @id
            alt Quantity > StockQuantity (Thiếu hàng)
                Svc->>DB: ROLLBACK TRANSACTION
                Svc-->>Ctrl: ServiceResult.Failure("ERR_INSUFFICIENT_STOCK", "Không đủ tồn kho")
                Ctrl-->>UI: 400 Bad Request: Báo tên món & số lượng còn lại
            else Đủ tồn kho
                Svc->>DB: UPDATE Products SET StockQuantity = StockQuantity - @qty WHERE Id = @id
            end
        end
        Svc->>DB: INSERT INTO Orders (SessionId, Status='Pending', OrderTimeUtc=NOW)
        Svc->>DB: INSERT INTO OrderDetails (OrderId, ProductId, Quantity, UnitPrice)
        Svc->>DB: COMMIT TRANSACTION
        Svc-->>Ctrl: ServiceResult.Success(OrderDto)
        Ctrl-->>UI: 201 Created: Đơn hàng thành công, hiển thị món đã đặt
    end
    deactivate Svc
```

### 2.2 Sequence Diagram: Đóng phiên, Tính tiền & Thanh toán hóa đơn (UC27, UC28, UC29)

```mermaid
sequenceDiagram
    actor Cashier as Thu ngân (Cashier)
    participant UI as Giao diện Tính tiền
    participant Ctrl as BillingController
    participant Svc as BillingService
    participant DB as SQL Server (BilliardDB)

    Cashier->>UI: Yêu cầu tính tiền bàn chơi (SessionId)
    UI->>Ctrl: GET /sessions/{id}/checkout-preview
    Ctrl->>Svc: GetCheckoutPreviewAsync(sessionId)
    Svc->>DB: Truy vấn PlaySessions, Orders(Served), SessionCombos
    DB-->>Svc: Trả về số liệu tiền giờ tạm tính, tiền F&B, combo
    Svc-->>Ctrl: CheckoutPreviewDto
    Ctrl-->>UI: Hiển thị bảng kê chi tiết

    Cashier->>UI: Bấm "Thanh toán" (Chọn tiền mặt / QR Transfer, Giảm giá)
    UI->>Ctrl: POST /invoices/process-payment (payload)
    Ctrl->>Svc: ProcessPaymentAsync(dto)
    activate Svc
    Svc->>DB: EXEC dbo.usp_CloseSession @SessionId, @StaffId
    Note over DB: Chốt PlaytimeAmount, Status='Closed', Table='AwaitingPayment'
    Svc->>DB: BEGIN TRANSACTION
    Svc->>DB: INSERT/UPDATE Invoices SET Status='Paid', TotalPlaytime, TotalFandB, DiscountAmount
    Svc->>DB: INSERT INTO PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid)
    Svc->>DB: UPDATE BilliardTables SET Status='Available' WHERE Id = @tableId
    Svc->>DB: COMMIT TRANSACTION
    Note over Svc,DB: Khóa bất biến: Invoice chuyển sang Read-Only vĩnh viễn
    Svc-->>Ctrl: ServiceResult.Success(InvoicePaidDto)
    Ctrl-->>UI: 200 OK: Thanh toán hoàn tất, bàn đã chuyển sang Trống
    UI-->>Cashier: Mở hộp thoại In phiếu thu (POS Receipt 80mm)
    deactivate Svc
```

### 2.3 Sequence Diagram: Thống kê Doanh thu & Xuất file Excel / PDF (UC47)

```mermaid
sequenceDiagram
    actor Admin as Quản trị viên (Admin)
    participant UI as Màn hình Báo cáo Doanh thu
    participant Ctrl as ReportController
    participant Svc as ReportService
    participant DB as SQL Server (BilliardDB)
    participant Lib as Thư viện Xuất File (EPPlus / QuestPDF)

    Admin->>UI: Chọn khoảng ngày: [FromDate, ToDate]
    UI->>Ctrl: GET /api/v1/reports/revenue-summary?from=..&to=..
    Ctrl->>Svc: GetRevenueReportAsync(fromUtc, toUtc)
    activate Svc
    Svc->>DB: SELECT SUM(FinalAmount), SUM(TotalPlaytime), SUM(TotalFandB) FROM Invoices WHERE Status = 'Paid' AND CreatedAtUtc BETWEEN @from AND @to
    Svc->>DB: SELECT Top Best-Selling Products từ OrderDetails & Invoices
    DB-->>Svc: Dữ liệu doanh thu thực tế
    Svc-->>Ctrl: RevenueReportDto
    Ctrl-->>UI: Hiển thị bảng chỉ số KPI & Biểu đồ

    Admin->>UI: Nhấn "Xuất Excel" (.xlsx) hoặc "Xuất PDF" (.pdf)
    UI->>Ctrl: GET /reports/export/excel hoặc /reports/export/pdf
    Ctrl->>Svc: ExportReportAsync(format, fromUtc, toUtc)
    Svc->>Lib: Dựng workbook (EPPlus) hoặc document (QuestPDF)
    Lib-->>Svc: byte[] MemoryStream file nhị phân
    Svc-->>Ctrl: FileStreamResult
    Ctrl-->>UI: Tải xuống file BMS_Revenue_Report.xlsx / .pdf
    deactivate Svc
```

---

# III. Other Design Specifications

## 1. Quy tắc Nghiệp vụ Bất biến (Business Invariants)

1. **Nguyên tắc Tuyệt đối không Mock Data (Hiến chương VI)**: Mọi thao tác truy vấn và cập nhật bắt buộc thực hiện trên SQL Server `BilliardDB`. Cấm mọi hành vi hardcode dữ liệu giả.
2. **Kiểm soát Tương tranh Lạc quan (Optimistic Concurrency)**: Bảng `dbo.Products` bắt buộc sử dụng `[Timestamp] byte[] RowVersion` để chống xung đột ghi đè tồn kho (Lost Update). Khi có xung đột, trả về mã `409 Conflict` yêu cầu tải lại dữ liệu.
3. **Quy tắc Bất biến của Hóa đơn đã thanh toán (Immutability Rule)**:
   - Khi `Invoices.Status == 'Paid'`, bản ghi hóa đơn và mọi chi tiết đơn hàng (`OrderDetails`), phiên chơi (`PlaySessions`), giao dịch thanh toán (`PaymentTransactions`) liên quan bị khóa chỉ đọc (Read-Only) vĩnh viễn. Cấm UPDATE/DELETE.
4. **Trừ kho tức thời & Hoàn tồn**:
   - Khi gọi món: Trừ kho ngay lập tức trong Database Transaction. Nếu không đủ hàng, rollback 100%.
   - Khi hủy đơn `Pending`: Hoàn trả số lượng vào kho ngay lập tức. Cấm hủy đơn đã chuyển trạng thái `Served`.

---

# Phụ lục Bàn giao & Đối chiếu

- **Khớp nối với Database**: 100% khớp với 24 bảng trong `BilliardDB_Full.sql`.
- **Khớp nối với Hùng**: Phân quyền dùng chung `AspNetUsers` và Identity; đăng nhập bằng `ApplicationUser`, kiểm tra vai trò bằng `[Authorize(Roles = "...")]`.
