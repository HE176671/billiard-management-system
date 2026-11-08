# Feature Specification: Báo Cáo Tài Chính & Doanh Thu (Financial Reporting & Export)

**Feature Identifier**: `04-financial-reporting`  
**Assigned Engineer**: Tùng (Module: F&B, Billing & Reporting)  
**Target Use Case**: UC47 (Xuất báo cáo tài chính & thống kê doanh thu Excel/PDF)  
**Iteration**: 3  
**Status**: Approved / Ready for Implementation  
**Created Date**: 2026-10-11  

---

## 1. Feature Scope & User Stories

### 1.1 Tổng quan phạm vi (Feature Scope)
Module cung cấp công cụ phân tích và thống kê tài chính trực quan cho Ban quản lý (Admin / Store Manager). Hệ thống tổng hợp số liệu doanh thu từ các hóa đơn đã thanh toán hợp lệ (`Invoices.Status = 'Paid'`), bóc tách cơ cấu doanh thu giữa Giờ chơi bi-a và Dịch vụ F&B / Combo, xếp hạng các món F&B bán chạy nhất (Top Best Sellers), thống kê tỷ trọng thanh toán theo phương thức (Tiền mặt, VNPay, SePay, Chuyển khoản) và hỗ trợ trích xuất báo cáo ra file Excel (.xlsx) chuẩn định dạng bằng thư viện **EPPlus** cũng như tài liệu PDF chuyên nghiệp bằng thư viện **QuestPDF**.

- **Actors**:
  - `Admin` / `Store Manager`: Chọn khoảng thời gian, xem biểu đồ/bảng số liệu phân tích, tải về báo cáo tài chính định dạng Excel và PDF.
- **Pre-conditions**:
  - Người dùng đăng nhập với quyền `Admin`.
  - Hệ thống đã có các hóa đơn đã thanh toán (`Invoices.Status = 'Paid'`) trong cơ sở dữ liệu `BilliardDB`.
- **Post-conditions**:
  - Dữ liệu thống kê được tính toán chính xác và phản hồi qua API / giao diện Web.
  - File báo cáo Excel / PDF được tạo theo chuẩn và tải xuống máy người dùng.

---

### 1.2 User Stories & Testing

#### User Story 1 - Xem tổng quan doanh thu theo dải ngày (Priority: P1) [UC47]
> Là một **Admin**, tôi muốn chọn dải ngày (Từ ngày -> Đến ngày) để xem tổng doanh thu thực tế, doanh thu giờ chơi, doanh thu F&B và tổng số lượt khách phục vụ.

- **Why this priority**: Cung cấp bức tranh toàn cảnh về hiệu quả hoạt động kinh doanh hàng ngày, hàng tuần hoặc hàng tháng của quán.
- **Independent Test**: Gọi API báo cáo doanh thu với tham số `from` và `to`, so sánh số liệu trả về với câu lệnh SQL `SUM` trực tiếp trên bảng `Invoices` trong `BilliardDB`.
- **Acceptance Scenarios**:
  1. **Given** Có 10 hóa đơn đã thanh toán trong ngày 2026-10-10 với tổng FinalAmount = 2.500.000 VNĐ, **When** Admin chọn dải ngày từ `2026-10-10` đến `2026-10-10`, **Then** Hệ thống trả về tổng doanh thu 2.500.000 VNĐ, số hóa đơn = 10, phân rã chi tiết tiền giờ chơi và tiền F&B chính xác.
  2. **Given** Admin nhập dải ngày không hợp lệ (Ngày bắt đầu lớn hơn Ngày kết thúc), **When** Gửi yêu cầu, **Then** Hệ thống trả về lỗi `400 Bad Request` với thông báo "Ngày bắt đầu không được lớn hơn ngày kết thúc".

#### User Story 2 - Thống kê Top mặt hàng F&B bán chạy (Priority: P2) [UC47]
> Là một **Admin**, tôi muốn biết top 5 hoặc top 10 sản phẩm F&B được khách gọi nhiều nhất trong tháng để lên kế hoạch nhập hàng và dự trữ kho phù hợp.

- **Why this priority**: Tối ưu hóa chuỗi cung ứng và tồn kho, tránh đọng vốn hoặc hết hàng những món bán chạy.
- **Independent Test**: Tổng hợp số lượng bán từ `OrderDetails` của các hóa đơn đã thanh toán, đối chiếu bảng xếp hạng sản phẩm theo số lượng và doanh thu.
- **Acceptance Scenarios**:
  1. **Given** Trong kỳ báo cáo, sản phẩm "Bò Húc" bán được 120 lon, "Sting" bán 80 lon, "Khăn lạnh" bán 200 chiếc, **When** Admin xem Top F&B bán chạy với Limit = 3, **Then** Hệ thống trả về danh sách được xếp hạng đúng thứ tự theo số lượng bán kèm doanh thu đóng góp của từng món.

#### User Story 3 - Phân tích cơ cấu thanh toán (Priority: P2) [UC47]
> Là một **Admin**, tôi muốn theo dõi tỷ lệ thanh toán giữa Tiền mặt và Chuyển khoản/QR Code để kiểm soát tiền mặt tại quầy và đối soát tài khoản ngân hàng.

- **Why this priority**: Ngăn ngừa thất thoát tiền mặt và đảm bảo đối chiếu khớp dòng tiền thực tế.
- **Independent Test**: Tổng hợp số tiền thanh toán theo cột `PaymentMethod` trong bảng `PaymentTransactions`.
- **Acceptance Scenarios**:
  1. **Given** Có 1.500.000 VNĐ thu bằng Tiền mặt và 3.500.000 VNĐ thu qua VNPay/Chuyển khoản, **When** Admin xem cơ cấu thanh toán, **Then** Hệ thống hiển thị: Cash = 30%, Digital Transfer = 70%.

#### User Story 4 - Xuất báo cáo ra file Excel (.xlsx) với EPPlus (Priority: P1) [UC47]
> Là một **Admin**, tôi muốn tải file Excel chứa toàn bộ bảng kê doanh thu và chi tiết hóa đơn để lưu trữ và báo cáo kế toán thuế.

- **Why this priority**: Báo cáo Excel là định dạng bắt buộc cho nghiệp vụ kế toán và kiểm toán định kỳ.
- **Independent Test**: Gọi endpoint xuất Excel, tải file về và xác nhận file mở được bằng Microsoft Excel với đầy đủ header tiếng Việt, định dạng tiền tệ VND, và công thức tính tổng.
- **Acceptance Scenarios**:
  1. **Given** Dải ngày có 25 hóa đơn đã thanh toán, **When** Admin nhấn "Xuất Excel", **Then** Trình duyệt tải về file `BaoCaoDoanhThu_20261001_20261011.xlsx`, nội dung có bảng thống kê tổng hợp và bảng chi tiết từng hóa đơn, các ô số tiền được format kiểu `#,##0 "₫"`.

#### User Story 5 - Xuất báo cáo tài chính ra file PDF chuyên nghiệp với QuestPDF (Priority: P2) [UC47]
> Là một **Admin**, tôi muốn xuất báo cáo tài chính dạng PDF trang in A4 chuẩn để in ra ký duyệt hoặc lưu trữ số hóa không thể chỉnh sửa.

- **Why this priority**: Đảm bảo tính trang trọng, không bị lệch định dạng khi xem trên các thiết bị khác nhau.
- **Independent Test**: Gọi endpoint xuất PDF, kiểm tra file PDF trả về có phân trang, logo quán, bảng biểu rõ ràng và chữ ký người lập biểu.
- **Acceptance Scenarios**:
  1. **Given** Admin muốn xuất báo cáo tuần, **When** Nhấn "Xuất PDF", **Then** Hệ thống sinh file PDF chất lượng cao khổ A4 qua QuestPDF với tiêu đề "BÁO CÁO DOANH THU HOẠT ĐỘNG", biểu đồ tóm tắt, bảng số liệu và chữ ký xác nhận của Quản lý chi nhánh.

---

### 1.3 Edge Cases
- **Dải ngày không có bất kỳ giao dịch nào**: Hệ thống trả về kết quả số liệu bằng 0 (không ném lỗi `NullReferenceException`), file Excel/PDF vẫn xuất ra với thông báo "Không có dữ liệu giao dịch trong khoảng thời gian này".
- **Dải ngày quá dài (ví dụ: > 1 năm)**: Có thể gây nghẽn bộ nhớ; hệ thống giới hạn tối đa dải ngày cho một lần xuất báo cáo là 365 ngày hoặc tự động phân trang theo tháng.
- **Hóa đơn chưa thanh toán (`Status = 'Unpaid'`)**: Tuyệt đối KHÔNG được tính vào doanh thu thực tế (chỉ tính hóa đơn `Paid`).

---

## 2. Data Models & Aggregate DTOs

Dựa trên dữ liệu nguồn từ `dbo.Invoices`, `dbo.OrderDetails`, `dbo.PaymentTransactions` trong `BilliardDB`:

### 2.1 C# Reporting DTOs
```csharp
public class RevenueSummaryReportDto
{
    public DateTime FromDateUtc { get; set; }
    public DateTime ToDateUtc { get; set; }
    public int TotalCompletedInvoices { get; set; }
    public decimal TotalRevenue { get; set; }        // SUM(FinalAmount)
    public decimal TotalPlaytimeRevenue { get; set; } // SUM(TotalPlaytime)
    public decimal TotalFandBRevenue { get; set; }     // SUM(TotalFandB)
    public decimal TotalDiscountGiven { get; set; }    // SUM(DiscountAmount)
    public decimal AverageBillAmount { get; set; }
    public List<DailyRevenueBreakdownDto> DailyBreakdowns { get; set; } = new();
    public List<TopProductSalesDto> TopSellingProducts { get; set; } = new();
    public List<PaymentMethodShareDto> PaymentMethodShares { get; set; } = new();
}

public class DailyRevenueBreakdownDto
{
    public DateTime Date { get; set; }
    public int InvoiceCount { get; set; }
    public decimal PlaytimeRevenue { get; set; }
    public decimal FandBRevenue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class TopProductSalesDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int TotalQuantitySold { get; set; }
    public decimal TotalSalesAmount { get; set; }
}

public class PaymentMethodShareDto
{
    public string PaymentMethod { get; set; } = string.Empty; // Cash, VNPay, SePay, Transfer
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
    public double Percentage { get; set; }
}
```

---

## 3. Business Rules & Calculation Formulas

1. **BR-REP-01 (Điều kiện ghi nhận doanh thu)**:
   - Chỉ những hóa đơn có `Invoices.Status == 'Paid'` mới được tính vào báo cáo tài chính.
   - Các hóa đơn đang ở trạng thái `Unpaid` hoặc phiên chơi chưa kết thúc hoàn toàn bị loại khỏi số liệu doanh thu.
2. **BR-REP-02 (Quy chuẩn thời gian)**:
   - Bộ lọc thời gian sử dụng `CreatedAtUtc` của hóa đơn hoặc `TransactionTimeUtc` của giao dịch thanh toán.
   - Khi người dùng chọn từ ngày $D_1$ đến ngày $D_2$ theo giờ địa phương (GMT+7), hệ thống phải tự động chuyển đổi sang dải thời gian UTC tương ứng:
     $$[\text{StartUtc} = D_1(00:00:00) - 7\text{h}, \quad \text{EndUtc} = D_2(23:59:59) - 7\text{h}]$$
3. **BR-REP-03 (Công thức cân đối tài chính)**:
   $$\text{TotalRevenue} = \text{TotalPlaytimeRevenue} + \text{TotalFandBRevenue} - \text{TotalDiscountGiven}$$
   $$\text{AverageBillAmount} = \frac{\text{TotalRevenue}}{\text{TotalCompletedInvoices}} \quad (\text{với } \text{TotalCompletedInvoices} > 0)$$
4. **BR-REP-04 (Quy chuẩn xuất file Excel với EPPlus)**:
   - Sheet 1: "Tổng Quan Doanh Thu" (Summary KPIs & Bảng phân bổ theo ngày).
   - Sheet 2: "Top Sản Phẩm F&B" (Danh sách món bán chạy).
   - Sheet 3: "Sổ Chi Tiết Hóa Đơn" (Chi tiết từng hóa đơn, mã phiên, số bàn, giờ chơi, tiền F&B, phương thức thanh toán).
   - Font chữ chuẩn: Aptos / Segoe UI / Arial, định dạng tiền tệ Việt Nam `#,##0 "₫"`.
5. **BR-REP-05 (Quy chuẩn xuất file PDF với QuestPDF)**:
   - Khổ giấy A4 dọc, lề chuẩn 1.5 cm.
   - Header có thông tin: Tên quán "Billiard Management System - BMS", Địa chỉ, Ngày giờ xuất báo cáo, Tên người xuất.
   - Footer có số trang: "Trang X / Y" và chữ ký người phê duyệt.

---

## 4. API & Endpoint Specifications

### 4.1 Lấy dữ liệu tổng hợp doanh thu
- **`GET /api/v1/reports/revenue-summary?from={fromDate}&to={toDate}`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Query Params**:
    - `from`: Ngày bắt đầu (định dạng `YYYY-MM-DD`, bắt buộc)
    - `to`: Ngày kết thúc (định dạng `YYYY-MM-DD`, bắt buộc)
  - **Response 200 OK**: Trả về `RevenueSummaryReportDto`.
  - **Error 400 Bad Request**: `ERR_INVALID_DATE_RANGE` (Từ ngày > Đến ngày hoặc vượt quá 365 ngày).

### 4.2 Lấy danh sách Top F&B bán chạy
- **`GET /api/v1/reports/top-products?from={fromDate}&to={toDate}&limit={int}`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Query Params**: `limit` (mặc định: 10, tối đa: 50).
  - **Response 200 OK**: `List<TopProductSalesDto>`.

### 4.3 Xuất file Excel báo cáo doanh thu (.xlsx)
- **`GET /api/v1/reports/export/excel?from={fromDate}&to={toDate}`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Content-Type**: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
  - **Header Response**: `Content-Disposition: attachment; filename="BMS_Revenue_Report_20261001_20261011.xlsx"`
  - **Engine**: **EPPlus** (Thư viện xử lý Excel cho .NET).

### 4.4 Xuất file PDF báo cáo tài chính (.pdf)
- **`GET /api/v1/reports/export/pdf?from={fromDate}&to={toDate}`**
  - **Auth**: `[Authorize(Roles = "Admin")]`
  - **Content-Type**: `application/pdf`
  - **Header Response**: `Content-Disposition: attachment; filename="BMS_Financial_Report_20261001_20261011.pdf"`
  - **Engine**: **QuestPDF** (Thư viện sinh PDF hiện đại cho .NET).

---

## 5. Acceptance Criteria (Gherkin Format)

### Scenario 1: Xem báo cáo doanh thu theo khoảng thời gian hợp lệ (Happy Path)
```gherkin
Given Admin đăng nhập hệ thống với tài khoản quản trị
And Trong khoảng thời gian từ "2026-10-01" đến "2026-10-07" có 50 hóa đơn đã thanh toán với Status = "Paid"
When Admin gửi yêu cầu xem báo cáo doanh thu từ "2026-10-01" đến "2026-10-07"
Then Hệ thống tính toán chính xác tổng doanh thu bằng tổng FinalAmount của 50 hóa đơn
And Số lượng TotalCompletedInvoices trả về đúng bằng 50
And Dữ liệu phân rã gồm đầy đủ doanh thu giờ chơi, F&B, giảm giá và cơ cấu phương thức thanh toán
And Trả về mã HTTP 200 OK
```

### Scenario 2: Nhập khoảng ngày không hợp lệ (Exception Path)
```gherkin
Given Admin mở trang báo cáo tài chính
When Admin chọn Ngày bắt đầu là "2026-10-15" và Ngày kết thúc là "2026-10-10"
Then Hệ thống từ chối truy vấn
And Trả về mã HTTP 400 Bad Request
And Thông báo lỗi hiển thị rõ: "Khoảng thời gian không hợp lệ: Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc."
```

### Scenario 3: Dải ngày không có hóa đơn nào phát sinh (Edge Case)
```gherkin
Given Trong ngày "2026-01-01" quán đóng cửa nghỉ lễ và không có phiên chơi nào
When Admin xem báo cáo doanh thu cho ngày "2026-01-01"
Then Hệ thống phản hồi thành công với mã HTTP 200 OK
And TotalCompletedInvoices có giá trị bằng 0
And TotalRevenue, TotalPlaytimeRevenue, TotalFandBRevenue đều có giá trị bằng 0.00 VNĐ
And Không phát sinh lỗi chia cho 0 tại trường AverageBillAmount (giá trị là 0.00 VNĐ)
```

### Scenario 4: Xuất báo cáo Excel thành công qua EPPlus (Happy Path)
```gherkin
Given Dữ liệu tài chính tháng 10/2026 đã sẵn sàng
When Admin gửi yêu cầu tải file Excel qua endpoint "/api/v1/reports/export/excel?from=2026-10-01&to=2026-10-31"
Then Hệ thống sử dụng EPPlus tạo file bảng tính chuẩn .xlsx
And Trả về mã HTTP 200 OK với Content-Type "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
And File tải về mở được trên Excel, có 3 sheet ("Tổng Quan Doanh Thu", "Top Sản Phẩm F&B", "Sổ Chi Tiết Hóa Đơn")
And Các cột tiền tệ được định dạng hiển thị có dấu phân cách hàng nghìn và ký hiệu "₫"
```

### Scenario 5: Xuất báo cáo PDF thành công qua QuestPDF (Happy Path)
```gherkin
Given Dữ liệu tài chính tuần vừa qua đã được kiểm toán
When Admin gửi yêu cầu tải file PDF qua endpoint "/api/v1/reports/export/pdf?from=2026-10-01&to=2026-10-07"
Then Hệ thống sử dụng QuestPDF dựng tài liệu chuẩn A4
And Trả về mã HTTP 200 OK với Content-Type "application/pdf"
And File PDF có đầy đủ Header tiêu đề báo cáo, bảng kê doanh thu rõ ràng, phân trang đánh số và phần chữ ký đại diện
```
