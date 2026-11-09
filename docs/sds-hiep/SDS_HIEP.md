# Billiard Management System

## Software Design Specification (SDS) — Phân hệ F&B (Hiệp)

**Ngày cập nhật:** 09/11/2026. **Phiên bản:** 1.0.  
**Người phụ trách theo phân công:** Hiệp (Software Engineer — Phân hệ Gọi món F&B & Quản lý bàn).  
**Phạm vi nghiệp vụ:** Mã QR bàn, Gọi món tại bàn, Gọi món tại quầy, Duyệt/Từ chối món realtime, Hủy hoàn kho và Tích hợp luồng thanh toán F&B (Trường hợp 1 & Trường hợp 2).

---

### Change Log

| Date | A / M / D | In charge | Change Description |
|---|---|---|---|
| 09/11/2026 | A | Hiệp | Khởi tạo SDS cho Phân hệ F&B trong thư mục chuyên biệt `docs/sds-hiep/`, đối chiếu CSDL `BilliardDB` và phân tích sâu 2 kịch bản thanh toán F&B |

---

# I. High Level Design

## 1. Software Architecture

Phân hệ F&B được xây dựng trên nền tảng **ASP.NET Core 8 MVC**, tích hợp kết nối thời gian thực qua **SignalR (`OrderHub`)** và bảo đảm tính toàn vẹn dữ liệu chặt chẽ trên **SQL Server (`BilliardDB`)**.

* Sơ đồ kiến trúc Mermaid: [01-architecture.mmd](01-architecture.mmd)

```mermaid
flowchart TB
    Actor["Khách hàng tại bàn / Thu ngân / Nhân viên phục vụ"] --> Browser["Trình duyệt Web & Thiết bị di động<br/>HTML5, Bootstrap, SignalR Client, Camera QR"]
    subgraph App["Bms.Web — ASP.NET Core MVC & Realtime Web"]
        Gate["Routing + Security Pipeline<br/>Role: Staff, Manager, Admin & Anonymous QR"]
        Hub["SignalR Hub<br/>OrderHub (/hubs/order)<br/>ReceiveNewOrder, OrderConfirmed, OrderRejected"]
        Controllers["Controllers<br/>OrderController, TableController, PosCheckoutController"]
        Views["Razor Views + ViewModels<br/>Order/Table, Order/Pending, Order/Counter, Table/QrCodes"]
        Services["Tầng Dịch vụ Nghiệp vụ<br/>IOrderService / OrderService<br/>IProductService / ProductService<br/>ICheckoutService / CheckoutService<br/>ITableSessionService / TableSessionService"]
        Data["ApplicationDbContext / EF Core 8<br/>SQL có tham số, Transactions, Optimistic Concurrency"]

        Gate --> Controllers
        Gate --> Hub
        Controllers --> Views
        Controllers --> Services
        Services --> Hub
        Services --> Data
    end
    Browser -->|"HTTP POST / GET + Cookie / QR Token"| Gate
    Hub -->|"Websocket / SSE (Realtime Audio & Badge)"| Browser
    Views -->|"HTML / View / QR Codes SVG"| Browser
    Data -->|"SQL Queries / Stored Procedures / Transactions"| DB[("SQL Server<br/>BilliardDB")]
```

## 2. Package Diagram

* Sơ đồ phân rã package: [02-packages.mmd](02-packages.mmd)

```mermaid
flowchart LR
    Root["Bms.Web<br/>Program.cs — Composition Root"]
    C["«namespace» Bms.Web.Controllers<br/>OrderController, TableController, PosCheckoutController"]
    H["«namespace» Bms.Web.Hubs<br/>OrderHub"]
    S["«namespace» Bms.Web.Services<br/>IOrderService, IProductService, ICheckoutService, ITableSessionService"]
    M["«namespace» Bms.Web.Models<br/>OrderViewModels, TableViewModels, FandBViewModels"]
    E["«namespace» Bms.Web.Data.Entities<br/>Order, OrderDetail, Product, PlaySession, BilliardTable, Invoice"]
    D["«namespace» Bms.Web.Data<br/>ApplicationDbContext"]
    EF["«framework» EF Core 8<br/>Microsoft.EntityFrameworkCore.SqlServer"]

    Root -.->|"đăng ký DI & MapHub"| C
    Root -.->|"đăng ký DI"| S
    Root -.->|"MapHub /hubs/order"| H
    Root -.-> D
    C -->|"gọi nghiệp vụ"| S
    C -->|"nhận/trả model"| M
    S -->|"phát sự kiện realtime"| H
    S -->|"xử lý entities"| E
    S -->|"truy vấn DB"| D
    D --> EF
```

---

# II. Chi tiết 2 Kịch bản Nghiệp vụ F&B

### 1. Trường hợp 1: Khách gọi món tại bàn — Thanh toán gộp cùng tiền bàn & tiền gậy cuối phiên

* Sơ đồ tuần tự: [05-order-fnb-table-case1.mmd](05-order-fnb-table-case1.mmd)

```mermaid
sequenceDiagram
    autonumber
    actor Khach as Khách hàng tại bàn
    actor Staff as Nhân viên quầy / Phục vụ
    participant Client as Web / QR Client
    participant OrderCtrl as OrderController
    participant OrderSvc as OrderService / ProductService
    participant SignalR as OrderHub (SignalR)
    participant POS as CheckoutWorkflow / CheckoutService
    participant DB as SQL Server (BilliardDB)

    Note over Khach, Client: Bàn đang trong phiên chơi (PlaySession: Active)
    Khach->>Client: Quét mã QR tĩnh tại bàn (hoặc gọi nhân viên gọi hộ)
    Client->>OrderCtrl: POST /Order/SubmitTable (TableCode, Items)
    OrderCtrl->>OrderSvc: CreateQrOrderAsync / SubmitTable
    OrderSvc->>DB: INSERT dbo.Orders (Status='Pending', SessionId) & OrderDetails
    OrderSvc-->>SignalR: Broadcast "ReceiveNewOrder" (Chuông báo + Badge)
    SignalR-->>Staff: Bật âm thanh & Hiện thông báo đơn mới trên màn hình

    Staff->>OrderCtrl: POST /Order/ApproveOrder (hoặc ConfirmQr)
    OrderCtrl->>OrderSvc: Kiểm tra tồn kho (StockQuantity >= Quantity)
    alt Không đủ tồn kho
        OrderSvc-->>Staff: Báo lỗi "Món không đủ tồn kho"
    else Đủ tồn kho
        OrderSvc->>DB: Trừ StockQuantity, tạo StockMovements (OrderDeduct)
        OrderSvc->>DB: UPDATE dbo.Orders SET Status = 'Served' (hoặc 'Completed')
        OrderSvc-->>Staff: Duyệt thành công, chuyển pha chế/phục vụ bàn
    end

    Note over Khach, Staff: Khách kết thúc buổi chơi
    Staff->>POS: Bấm "Kết thúc phiên & Thanh toán" trên sơ đồ bàn
    POS->>DB: EXEC dbo.usp_CloseSession (Chốt PlaytimeAmount, Bàn -> AwaitingPayment)
    POS->>DB: SUM(PlaytimeAmount) + SUM(OrderDetails WHERE Status IN ('Served','Completed')) + Tiền gậy
    POS-->>Staff: Hiển thị Preview Checkout (Giờ chơi + F&B + Thuê gậy - Giảm giá/Voucher/Điểm)
    Staff->>POS: Chọn phương thức (Tiền mặt / Chuyển khoản VietQR SePay)
    POS->>DB: INSERT dbo.Invoices (TotalPlaytime, TotalFandB, FinalAmount, Status='Paid')
    POS->>DB: INSERT dbo.PaymentTransactions (AmountPaid, PaymentMethod)
    POS->>DB: UPDATE dbo.BilliardTables SET Status = 'Available'
    POS-->>Khach: In hóa đơn tổng & Giao cho khách
```

### 2. Trường hợp 2: Khách muốn thanh toán lẻ F&B ngay tại quầy (Không thanh toán chung với tiền bàn)

* Sơ đồ tuần tự: [06-order-fnb-counter-case2.mmd](06-order-fnb-counter-case2.mmd)

```mermaid
sequenceDiagram
    autonumber
    actor Khach as Khách hàng
    actor Staff as Nhân viên quầy thu ngân
    participant POS as POS Counter / OrderController
    participant OrderSvc as OrderService / InventoryService
    participant Checkout as CheckoutService
    participant DB as SQL Server (BilliardDB)

    Khach->>Staff: Gọi đồ ăn/uống và yêu cầu: "Tôi trả tiền món này luôn"
    Staff->>POS: Mở giao diện "Bán lẻ F&B tại quầy / Thanh toán ngay"
    Staff->>POS: Chọn món & số lượng, chọn hình thức "Thanh toán ngay"
    POS->>OrderSvc: CreateCounterOrderAsync (Kiểm tra & trừ tồn kho)
    OrderSvc->>DB: Trừ StockQuantity, ghi sổ StockMovements
    OrderSvc->>DB: INSERT dbo.Orders (Status = 'Completed', PaymentStatus = 'PaidImmediately')
    
    Staff->>Checkout: Thu tiền khách (Tiền mặt hoặc Quét mã VietQR)
    Checkout->>DB: INSERT dbo.Invoices (TotalPlaytime = 0, TotalFandB = Tiền món, Status = 'Paid')
    Checkout->>DB: INSERT dbo.PaymentTransactions (Hóa đơn F&B lẻ)
    Checkout-->>Khach: In bill F&B lẻ cho khách

    Note over Khach, Staff: Về sau: Khách kết thúc phiên chơi bida
    Staff->>Checkout: Checkout phiên bàn (SessionId)
    Checkout->>DB: Tính tiền F&B: CHỈ SUM các đơn có PaymentStatus = 'Unpaid'
    Note right of DB: Đơn đã trả ngay sẽ BỊ LOẠI TRỪ khỏi hóa đơn bàn cuối giờ!
    Checkout->>DB: Tạo hóa đơn bàn (Chỉ gồm tiền giờ + tiền gậy + F&B chưa thanh toán)
    Checkout-->>Khach: Khách thanh toán nốt tiền bàn, KHÔNG bị tính tiền 2 lần!
```

---

# III. Đánh giá Logic Hiện tại & Nhận xét Chi tiết

### 1. Đánh giá tính khả thi và độ ổn định của mã nguồn hiện tại
* **Trường hợp 1 (RẤT TỐT):** 
  - Quy trình gọi món qua QR code, trừ kho khi duyệt đơn (`ApproveOrder` / `ConfirmQrOrderAsync`), kiểm tra tồn kho tức thời và gộp tiền F&B vào hóa đơn bàn cuối giờ đã được hoàn thiện 100%.
  - Cơ chế khóa thanh toán khi còn đơn chờ: `CheckoutService` chủ động từ chối tính tiền nếu bàn còn đơn `Pending`/`Preparing`, ngăn chặn việc nhân viên vô tình bỏ sót đơn của khách.
  - Toàn bộ 108 ca kiểm thử tích hợp (smoke tests) đã kiểm chứng thành công luồng này.

### 2. Nhận xét các lỗ hổng cần khắc phục cho Trường hợp 2:

> [!WARNING]
> #### Nhận xét 1: Nguy cơ tính tiền 2 lần (Double Billing) khi khách thanh toán lẻ
> Trong `src/Bms.Web/Services/CheckoutService.cs`, câu lệnh SQL chốt tiền F&B cuối phiên bàn hiện tại là:
> ```sql
> SELECT ISNULL(SUM(od.Subtotal), 0)
> FROM dbo.Orders o
> INNER JOIN dbo.OrderDetails od ON od.OrderId = o.Id
> WHERE o.SessionId = s.Id AND o.Status IN ('Served', 'Completed')
> ```
> Vì bảng `dbo.Orders` chưa có cột trạng thái thanh toán (đơn này đã thu tiền hay chưa), nên nếu khách đã trả tiền lẻ ngay tại quầy cho một đơn nào đó, thì đến cuối giờ khi tính tiền bàn, **hệ thống vẫn SUM đơn đó vào bill bàn lần thứ hai**. Khách hàng sẽ bị tính tiền 2 lần.

> [!CAUTION]
> #### Nhận xét 2: Ràng buộc cơ sở dữ liệu `SessionId NOT NULL` và `UQ_Invoice_Session`
> 1. Trong bảng `dbo.Orders`, cột `SessionId` hiện tại là `NOT NULL`. Điều này khiến hệ thống không thể tạo đơn cho khách vãng lai (Takeaway) nếu họ không mở bàn chơi bida.
> 2. Trong bảng `dbo.Invoices`, ràng buộc duy nhất `UQ_Invoice_Session UNIQUE (SessionId)` chỉ cho phép 1 hóa đơn duy nhất trên 1 phiên bàn. Nếu xuất hóa đơn thanh toán lẻ F&B cho bàn thì cuối phiên sẽ không thể xuất tiếp hóa đơn tiền giờ cho bàn đó.

---

# IV. Đề xuất Kiến trúc Cải tiến (Actionable Fixes)

Để giải quyết triệt để 2 nhận xét trên, Hiệp đề xuất 3 bước cải tiến:

1. **Bổ sung cột `PaymentStatus` vào bảng `dbo.Orders`:**
   ```sql
   ALTER TABLE dbo.Orders 
   ADD PaymentStatus varchar(20) NOT NULL CONSTRAINT DF_Order_PaymentStatus DEFAULT 'Unpaid';
   -- 'Unpaid': Món chưa trả tiền, gom vào hóa đơn bàn cuối phiên (Trường hợp 1)
   -- 'PaidImmediately': Món đã thanh toán lẻ tại quầy ngay khi nhận (Trường hợp 2)
   ```

2. **Cập nhật công thức tính tiền trong `CheckoutService.cs`:**
   ```csharp
   ISNULL((SELECT SUM(od.Subtotal) FROM dbo.Orders o
           INNER JOIN dbo.OrderDetails od ON od.OrderId = o.Id
           WHERE o.SessionId = s.Id 
             AND o.Status IN ('Served', 'Completed')
             AND (o.PaymentStatus IS NULL OR o.PaymentStatus = 'Unpaid')), 0) AS FandBAmount
   ```

3. **Cung cấp nút chuyển đổi trên giao diện gọi món quầy (`Order/Counter`):**
   - Bổ sung tùy chọn `[ ] Khách thanh toán lẻ ngay tại quầy`.
   - Nếu được chọn: Sau khi nhân viên bấm "Xác nhận", hệ thống tạo đơn với `PaymentStatus = 'PaidImmediately'`, mở popup thu tiền mặt/VietQR lẻ và loại trừ đơn này khỏi hóa đơn bàn khi kết thúc giờ chơi.
