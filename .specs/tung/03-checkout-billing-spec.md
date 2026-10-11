# Feature Specification: Tính Hóa Đơn, Thanh Toán & In Bill (Checkout, Billing & Receipt)

**Feature Identifier**: `03-checkout-billing`  
**Assigned Engineer**: Tùng (Module: F&B, Billing & Reporting)  
**Target Use Cases**: UC27 (Tính hóa đơn cuối), UC28 (Thanh toán hóa đơn), UC29 (In hóa đơn)  
**Iteration**: 2  
**Status**: Approved / Ready for Implementation  
**Created Date**: 2026-10-11  

---

## 1. Feature Scope & User Stories

### 1.1 Tổng quan phạm vi (Feature Scope)
Module đóng vai trò trung tâm trong chu trình tài chính của quán bida: kết thúc phiên chơi, tổng hợp toàn bộ chi phí phát sinh (tiền giờ chơi theo bảng giá cấu hình, tiền đồ ăn nước uống đã phục vụ, tiền các gói Combo đã mua), áp dụng chính sách giảm giá/chiết khấu thành viên, ghi nhận giao dịch thanh toán đa phương thức (Tiền mặt, VNPay, SePay, Chuyển khoản ngân hàng), giải phóng bàn về trạng thái sẵn sàng đón khách (`Available`), và in hóa đơn thanh toán chuẩn POS nhiệt 80mm. 

Quy tắc bất biến cốt lõi (**Immutability Rule**): Một khi hóa đơn đã được thanh toán (`Status = 'Paid'`), toàn bộ dữ liệu tài chính của phiên chơi đó trở thành chỉ đọc vĩnh viễn, ngăn chặn mọi hành vi chỉnh sửa hoặc gian lận sau thanh toán.

- **Actors**:
  - `Cashier` / `Staff`: Thực hiện đóng phiên, xem trước hóa đơn tạm tính, áp dụng giảm giá, chọn phương thức thanh toán và in bill cho khách.
  - `Customer`: Xem chi tiết các khoản phí, quét mã QR thanh toán hoặc trả tiền mặt, nhận hóa đơn.
- **Pre-conditions**:
  - Bàn chơi đang có phiên `PlaySessions.Status = 'Active'` hoặc vừa được đóng sang `PlaySessions.Status = 'Closed'` với trạng thái bàn là `AwaitingPayment`.
  - Toàn bộ các order F&B đã được xác nhận trạng thái (chỉ tính tiền các đơn `Served`).
- **Post-conditions**:
  - Bản ghi `Invoices` được tạo/cập nhật với `Status = 'Paid'` và `FinalAmount` chính xác.
  - Bản ghi `PaymentTransactions` được ghi nhận với phương thức thanh toán và mã tham chiếu (nếu có).
  - Trạng thái bàn `BilliardTables.Status` được chuyển từ `AwaitingPayment` về `Available` (Bàn trống).
  - Trạng thái phiên chơi `PlaySessions.Status` là `Closed` kèm `EndAtUtc` và `PlaytimeAmount`.
  - Hóa đơn chuyển sang trạng thái bất biến (Immutable).

---

### 1.2 User Stories & Testing

#### User Story 1 - Xem trước & Tính tổng hóa đơn cuối phiên (Priority: P1) [UC27]
> Là một **Thu ngân (Cashier)**, tôi muốn hệ thống tự động tổng hợp tiền giờ chơi, tiền F&B và tiền combo khi khách yêu cầu tính tiền để tôi kiểm tra trước khi thu tiền.

- **Why this priority**: Thu ngân cần xem trước bảng kê chi tiết để giải thích cho khách hàng trước khi chốt thanh toán.
- **Independent Test**: Với phiên chơi có 1h30m giờ chơi, 2 order F&B đã served và 1 combo, gọi endpoint preview hóa đơn và kiểm tra các số liệu thành phần.
- **Acceptance Scenarios**:
  1. **Given** Phiên chơi Id = 15 bắt đầu lúc 14:00 và dự kiến kết thúc lúc 15:30 (90 phút) với giá giờ 60.000 VNĐ/giờ (Tiền giờ = 90.000 VNĐ), tiền F&B đã phục vụ là 50.000 VNĐ, không có giảm giá, **When** Thu ngân mở màn hình tính tiền tạm tính, **Then** Hệ thống hiển thị: Tiền giờ = 90.000, Tiền F&B = 50.000, Giảm giá = 0, Tổng thanh toán = 140.000 VNĐ.

#### User Story 2 - Đóng phiên chơi & Chốt tiền giờ (Priority: P1) [UC27]
> Là một **Thu ngân**, tôi muốn bấm "Kết thúc phiên chơi" để dừng đồng hồ tính giờ chính xác đến từng giây thông qua Stored Procedure an toàn.

- **Why this priority**: Ngăn chặn thời gian chơi tiếp tục nhảy trong lúc khách đang rút ví thanh toán.
- **Independent Test**: Gọi thực thi `dbo.usp_CloseSession` và kiểm tra bảng `PlaySessions` có `Status = 'Closed'`, `BilliardTables.Status = 'AwaitingPayment'`.
- **Acceptance Scenarios**:
  1. **Given** Bàn 4 đang có `PlaySessions.Status = 'Active'` và bàn có `Status = 'InUse'`, **When** Thu ngân bấm "Chốt giờ bàn 4", **Then** Hệ thống gọi `dbo.usp_CloseSession`, chốt `EndAtUtc = SYSUTCDATETIME()`, tính `PlaytimeAmount` làm tròn đến đồng và đổi trạng thái bàn thành `AwaitingPayment`.
  2. **Given** Bàn 4 đã bị đóng phiên từ trước (`Status = 'Closed'`), **When** Thu ngân bấm đóng phiên lần nữa, **Then** Stored Procedure ném mã lỗi `51502` ("Only an Active session can be closed") và hệ thống hiển thị thông báo phiên đã được chốt.

#### User Story 3 - Xử lý thanh toán đa phương thức & Giải phóng bàn (Priority: P1) [UC28]
> Là một **Thu ngân**, tôi muốn ghi nhận khách trả bằng Tiền mặt hoặc Chuyển khoản (VNPay / SePay QR) để hoàn tất hóa đơn và mở bàn cho khách mới vào chơi.

- **Why this priority**: Hoàn tất giao dịch kinh doanh và thu hồi tài nguyên bàn chơi để tiếp tục sinh doanh thu.
- **Independent Test**: Gửi yêu cầu thanh toán hóa đơn với phương thức `Cash`, xác nhận `Invoices.Status = 'Paid'`, có bản ghi `PaymentTransactions`, và `BilliardTables.Status = 'Available'`.
- **Acceptance Scenarios**:
  1. **Given** Hóa đơn có `FinalAmount = 140000`, **When** Thu ngân chọn phương thức "Tiền mặt" và bấm "Xác nhận thanh toán", **Then** Hệ thống lưu `PaymentTransactions` với `AmountPaid = 140000`, cập nhật `Invoices.Status = 'Paid'`, và chuyển bàn bi-a về `Status = 'Available'`.
  2. **Given** Khách chọn thanh toán qua mã QR động (VNPay/SePay), **When** Cổng thanh toán gửi Webhook xác nhận giao dịch thành công với mã `GatewayRefCode = "VNP1482910"`, **Then** Hệ thống tự động gán giao dịch, đổi trạng thái hóa đơn sang `Paid` và giải phóng bàn qua SignalR.

#### User Story 4 - Bảo đảm tính bất biến của hóa đơn đã thanh toán (Priority: P1) [BR-INV-01]
> Là một **Chủ quán / Quản trị viên**, tôi muốn tuyệt đối cấm sửa đổi hoặc thêm món vào hóa đơn đã thanh toán để chống gian lận và sai lệch sổ sách.

- **Why this priority**: Yêu cầu kiểm toán và liêm chính tài chính theo Hiến chương dự án (Non-negotiable).
- **Independent Test**: Gửi request gọi thêm món hoặc cập nhật giảm giá vào một phiên có hóa đơn đã `Paid`, xác nhận hệ thống chặn 100% ở tầng Service và Database.
- **Acceptance Scenarios**:
  1. **Given** Hóa đơn Id = 40 đã có `Status = 'Paid'`, **When** Bất kỳ ai cố gắng gọi API thêm món, sửa giá hoặc thanh toán lại, **Then** Hệ thống chặn ngay lập tức với lỗi `403 Forbidden` / `400 Bad Request` ("Hóa đơn đã thanh toán và bị khóa bất biến").

#### User Story 5 - In phiếu thu / Bill hóa đơn chuẩn POS (Priority: P2) [UC29]
> Là một **Thu ngân**, tôi muốn in hóa đơn giấy khổ 80mm hoặc xuất file PDF hóa đơn gửi cho khách hàng lưu giữ.

- **Why this priority**: Cung cấp bằng chứng minh bạch cho khách hàng và hoàn thiện trải nghiệm dịch vụ.
- **Independent Test**: Gọi endpoint lấy dữ liệu in bill và endpoint xuất PDF hóa đơn, kiểm tra định dạng hiển thị đầy đủ thông tin bàn, giờ, món và tổng tiền.
- **Acceptance Scenarios**:
  1. **Given** Hóa đơn đã thanh toán thành công, **When** Thu ngân bấm "In Bill", **Then** Trình duyệt mở hộp thoại in với định dạng phiếu thu nhiệt chuẩn 80mm gồm đầy đủ logo BMS, tên quán, số bàn, danh sách món, tiền giờ và tổng thanh toán.

---

### 1.3 Edge Cases
- **Phiên chơi dưới 1 phút**: Vẫn tính toán theo công thức chính xác, không ném ngoại lệ chia cho 0.
- **Giảm giá vượt quá tổng tiền**: `DiscountAmount` không được lớn hơn tổng chi phí (`TotalPlaytime + TotalFandB`), giá trị `FinalAmount` tối thiểu là 0 VNĐ (không cho phép hóa đơn âm tiền).
- **Thanh toán trùng lặp (Double Payment)**: Chặn thanh toán lại nếu hóa đơn đã có trạng thái `Paid` bằng `UPDLOCK` trên bản ghi Invoice.
- **Bàn bị hủy phiên khi chưa thanh toán**: Trường hợp bất khả kháng phải có quyền `Admin` phê duyệt và ghi log kiểm toán.

---

## 2. Data Models & Schema Constraints

Dựa trên bảng `dbo.Invoices`, `dbo.PaymentTransactions`, `dbo.PlaySessions` trong `BMS_Database_Starter/BilliardDB_Full.sql`:

### 2.1 Entity: `Invoice`
```csharp
[Table("Invoices")]
public class Invoice
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Session))]
    public int SessionId { get; set; }

    [ForeignKey(nameof(Customer))]
    public string? CustomerId { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPlaytime { get; set; } = 0;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalFandB { get; set; } = 0;

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Giảm giá không được âm")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; } = 0;

    // Computed column (Persisted in SQL Server)
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(18,2)")]
    public decimal FinalAmount { get; private set; }

    [Required]
    [StringLength(20)]
    [RegularExpression("Unpaid|Paid", ErrorMessage = "Trạng thái hóa đơn chỉ có thể là Unpaid hoặc Paid")]
    public string Status { get; set; } = "Unpaid";

    [Required]
    [Column(TypeName = "datetime2(0)")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual PlaySession Session { get; set; } = null!;
    public virtual ApplicationUser? Customer { get; set; }
    public virtual ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
}
```

### 2.2 Entity: `PaymentTransaction`
```csharp
[Table("PaymentTransactions")]
public class PaymentTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Invoice))]
    public int InvoiceId { get; set; }

    [Required]
    [StringLength(20)]
    [RegularExpression("Cash|VNPay|SePay|Transfer", ErrorMessage = "Phương thức thanh toán không hợp lệ")]
    public string PaymentMethod { get; set; } = "Cash";

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền thanh toán phải lớn hơn 0")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountPaid { get; set; }

    [StringLength(100)]
    public string? GatewayRefCode { get; set; }

    [Required]
    [Column(TypeName = "datetime2(0)")]
    public DateTime TransactionTimeUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Invoice Invoice { get; set; } = null!;
}
```

### 2.3 SQL Schema Constraints Tham Chiếu
- `UQ_Invoice_Session`: `UNIQUE(SessionId)` (Mỗi phiên chơi chỉ có duy nhất 1 hóa đơn)
- `CK_Invoice_Status`: `CHECK (Status IN ('Unpaid', 'Paid'))`
- `FinalAmount AS (TotalPlaytime + TotalFandB - DiscountAmount) PERSISTED`
- `FK_Payment_Invoice`: `FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoices(Id) ON DELETE CASCADE`
- `CK_Payment_Method`: `CHECK (PaymentMethod IN ('Cash', 'VNPay', 'SePay', 'Transfer'))`
- `CK_Payment_Amount`: `CHECK (AmountPaid > 0)`

---

## 3. Business Rules & Invariant Validations

1. **BR-INV-01 (Quy tắc Bất biến của Hóa đơn đã thanh toán - IMMUTABILITY)**:
   - Khi `Invoice.Status == 'Paid'`, hóa đơn được niêm phong vĩnh viễn (Read-Only).
   - Tuyệt đối CẤM mọi thao tác:
     - Thêm/sửa/xóa các bản ghi `Order` hoặc `OrderDetail` của phiên chơi đó.
     - Cập nhật số tiền `TotalPlaytime`, `TotalFandB`, `DiscountAmount`.
     - Thêm `PaymentTransaction` mới vào hóa đơn đó.
     - Đổi trạng thái từ `Paid` ngược lại `Unpaid`.
2. **BR-INV-02 (Công thức tính tiền giờ chơi)**:
   - Được chốt bởi Stored Procedure `dbo.usp_CloseSession`:
     $$\text{PlaytimeAmount} = \text{ROUND}\left(\frac{\text{DATEDIFF\_BIG}(\text{second}, \text{StartAtUtc}, \text{EndAtUtc}) \times \text{HourlyRateSnapshot}}{3600.0}, 0\right)$$
   - Làm tròn đến đơn vị đồng, không có số lẻ thập phân.
3. **BR-INV-03 (Công thức tổng hợp tiền F&B)**:
   - `TotalFandB` = Tổng `Subtotal` của tất cả `OrderDetails` thuộc các đơn hàng có `Status == 'Served'` + Tổng tiền các gói Combo mua trong phiên `SUM(Quantity * UnitPriceSnapshot)` từ bảng `SessionCombos`.
4. **BR-INV-04 (Hợp lệ của khoản giảm giá)**:
   - `0 <= DiscountAmount <= (TotalPlaytime + TotalFandB)`.
   - Nếu áp dụng mã giảm giá làm giảm nhiều hơn tổng bill, `DiscountAmount` tự động được chặn ở mức tối đa bằng đúng tổng bill để `FinalAmount` không bao giờ âm.
5. **BR-INV-05 (Giải phóng bàn chơi)**:
   - Khi `Invoice.Status` chuyển thành `'Paid'`, hệ thống tự động cập nhật `BilliardTables.Status = 'Available'` trong cùng Database Transaction.

---

## 4. API & Endpoint Specifications

### 4.1 Xem trước hóa đơn tính tiền (Checkout Preview)
- **`GET /api/v1/sessions/{sessionId}/checkout-preview`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200 OK**:
    ```json
    {
      "sessionId": 15,
      "tableCode": "B04",
      "tableName": "Bàn 4 - Lỗ",
      "startAtUtc": "2026-10-11T14:00:00Z",
      "currentEndAtUtc": "2026-10-11T15:30:00Z",
      "durationFormatted": "01:30:00",
      "hourlyRate": 60000.00,
      "totalPlaytime": 90000.00,
      "fnbItems": [
        { "productName": "Sting dâu", "quantity": 2, "unitPrice": 15000.00, "subtotal": 30000.00 },
        { "productName": "Mì tôm xúc xích", "quantity": 1, "unitPrice": 25000.00, "subtotal": 25000.00 }
      ],
      "totalFandB": 55000.00,
      "discountAmount": 0.00,
      "estimatedFinalAmount": 145000.00,
      "sessionStatus": "Active"
    }
    ```

### 4.2 Đóng phiên chơi (Close Play Session)
- **`POST /api/v1/sessions/{sessionId}/close`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Process**: Thực thi `dbo.usp_CloseSession(@SessionId, @StaffId)`
  - **Response 200 OK**:
    ```json
    {
      "sessionId": 15,
      "status": "Closed",
      "endAtUtc": "2026-10-11T15:30:15Z",
      "playtimeAmount": 90250.00,
      "tableStatus": "AwaitingPayment"
    }
    ```
  - **Error 400**: Phiên không ở trạng thái Active (`ERR_SESSION_NOT_ACTIVE`).

### 4.3 Thực hiện thanh toán (Process Payment)
- **`POST /api/v1/invoices/process-payment`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Request Body**:
    ```json
    {
      "sessionId": 15,
      "paymentMethod": "Cash",
      "discountAmount": 10000.00,
      "gatewayRefCode": null,
      "customerId": null
    }
    ```
  - **Response 200 OK**:
    ```json
    {
      "invoiceId": 42,
      "sessionId": 15,
      "status": "Paid",
      "totalPlaytime": 90250.00,
      "totalFandB": 55000.00,
      "discountAmount": 10000.00,
      "finalAmount": 135250.00,
      "paymentMethod": "Cash",
      "tableId": 4,
      "newTableStatus": "Available",
      "paidAtUtc": "2026-10-11T15:31:00Z"
    }
    ```
  - **Error Codes**:
    - `400 Bad Request`: `ERR_INVOICE_ALREADY_PAID` (Hóa đơn đã được thanh toán từ trước)
    - `400 Bad Request`: `ERR_INVALID_DISCOUNT` (Giảm giá vượt quá giá trị hóa đơn)
    - `409 Conflict`: `ERR_CONCURRENT_CHECKOUT` (Đang có giao dịch thanh toán khác xử lý trên bàn này)

### 4.4 Lấy dữ liệu in hóa đơn (Receipt Print)
- **`GET /api/v1/invoices/{invoiceId}/receipt`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200 OK**: Trả về cấu trúc JSON format hóa đơn nhiệt 80mm.
- **`GET /api/v1/invoices/{invoiceId}/receipt-pdf`**
  - **Auth**: `[Authorize(Roles = "Admin,Staff")]`
  - **Response 200 OK**: Stream file `application/pdf` hóa đơn in.

---

## 5. Acceptance Criteria (Gherkin Format)

### Scenario 1: Đóng phiên và thanh toán tiền mặt thành công (Happy Path)
```gherkin
Given Bàn 1 đang chơi với PlaySession Id = 101 ở trạng thái "Active"
And Tổng tiền giờ chốt là 80.000 VNĐ, tiền F&B đã dùng là 40.000 VNĐ
When Thu ngân thực hiện đóng phiên và thanh toán với thông tin:
  | Field          | Value   |
  | SessionId      | 101     |
  | PaymentMethod  | "Cash"  |
  | DiscountAmount | 0       |
Then PlaySession Id = 101 chuyển sang Status = "Closed"
And Hóa đơn Invoice được tạo với TotalPlaytime = 80000, TotalFandB = 40000, FinalAmount = 120000
And Trạng thái Invoice là "Paid"
And Bản ghi PaymentTransaction được lưu với PaymentMethod = "Cash" và AmountPaid = 120000
And Bàn 1 tự động chuyển Status về "Available"
And Trả về mã HTTP 200 OK
```

### Scenario 2: Cố tình thanh toán lại hóa đơn đã Paid (Exception Path - Idempotency)
```gherkin
Given Hóa đơn Invoice Id = 88 đã có Status = "Paid"
When Thu ngân hoặc client gửi lại lệnh thanh toán cho Invoice Id = 88
Then Hệ thống từ chối thực hiện giao dịch
And Không tạo thêm bản ghi PaymentTransactions nào
And Trả về mã lỗi HTTP 400 Bad Request
And Thông báo lỗi hiển thị rõ: "Hóa đơn này đã được thanh toán trước đó."
```

### Scenario 3: Cố tình thêm món hoặc sửa đổi hóa đơn đã Paid (Exception Path - Immutability)
```gherkin
Given Phiên chơi Id = 75 đã kết thúc và hóa đơn của phiên đã có Status = "Paid"
When Nhân viên gửi yêu cầu gọi thêm 1 lon nước ngọt cho phiên chơi Id = 75
Then Hệ thống chặn yêu cầu ngay lập tức
And Trả về mã lỗi HTTP 400 Bad Request
And Thông báo lỗi: "Không thể thay đổi dịch vụ của phiên chơi đã thanh toán và khóa sổ."
```

### Scenario 4: Áp dụng giảm giá hợp lệ và hóa đơn tính chính xác
```gherkin
Given Phiên chơi có tiền giờ 100.000 VNĐ và tiền F&B 50.000 VNĐ (Tổng: 150.000 VNĐ)
When Thu ngân nhập mức giảm giá ưu đãi thành viên 20.000 VNĐ và tiến hành thanh toán
Then Cột DiscountAmount được lưu là 20000
And Cột FinalAmount (cột tính toán tự động của SQL Server) có giá trị chính xác là 130000 VNĐ
And Giao dịch thanh toán ghi nhận AmountPaid đúng bằng 130000 VNĐ
```

### Scenario 5: Thanh toán qua Chuyển khoản / QR thành công
```gherkin
Given Hóa đơn tạm tính có FinalAmount = 250.000 VNĐ
When Khách hàng quét mã QR chuyển khoản ngân hàng SePay/VNPay
And Cổng thanh toán gửi mã tham chiếu giao dịch "TXN_998124"
Then Hệ thống tạo PaymentTransaction với PaymentMethod = "Transfer" và GatewayRefCode = "TXN_998124"
And Hóa đơn cập nhật Status = "Paid"
And Bàn chơi được chuyển về "Available"
```
