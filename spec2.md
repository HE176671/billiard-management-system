# Billiard Management System (BMS) - Customer Profile, F&B Order & Checkout/Payment Spec
# Version: 0.3 | Owner: @TrongHiep | Date: 2026-10-29
# Thay đổi v0.3:
# 1. Bỏ hoàn toàn UC17 (Đánh giá dịch vụ).
# 2. Bỏ UC10 (Lịch sử phiên) và UC09/15 (Điểm thưởng) do thành viên khác trong nhóm phụ trách.
# 3. Giữ lại và nâng cấp giao diện UC08 (Hồ sơ cá nhân Customer).
# 4. UC07 Gọi món: Áp dụng Phương án 1 (Khách gửi đơn trạng thái Pending qua QR, Staff duyệt tại quầy trước khi phục vụ để chống gọi trộm từ xa).
# 5. Bổ sung trọn vẹn luồng Checkout & Thanh toán:
#    - Đóng phiên / Tính tiền giờ (PlaySessions)
#    - Tổng hợp tiền món F&B (Orders + OrderDetails)
#    - Áp dụng ưu đãi theo hạng thành viên (MembershipTiers: Đồng 0%, Bạc 5%, Vàng 10%)
#    - Chốt hóa đơn (Invoices)
#    - Thanh toán Tiền mặt hoặc VNPay Sandbox (PaymentTransactions)
#    - Tra cứu & đối soát giao dịch hóa đơn/thanh toán.
# 6. Database: Giữ nguyên 100% schema thực tế đã có trong SQL Server (Orders, OrderDetails, Invoices, PaymentTransactions, MembershipTiers, Combos).

---

## 1. Context & Goal

**Business problem:**
Hệ thống BMS cần hoàn thiện phân hệ dành cho:
1. **Customer**: Tự quản lý và cập nhật hồ sơ cá nhân (`CustomerProfile`); Gọi đồ ăn/uống tại bàn thông qua quét mã QR (`/Order/Table/{tableCode}`).
2. **Staff**: Quản lý duyệt đơn gọi món từ bàn; Gọi đồ tại quầy (`/Order/Counter`); Thực hiện **Checkout & Thanh toán** cho phiên chơi (tiền giờ + tiền F&B - ưu đãi thành viên $\rightarrow$ Hóa đơn $\rightarrow$ Tiền mặt / VNPay $\rightarrow$ Đối soát giao dịch).

**Phân định ranh giới (Scope của @TrongHiep):**
| Chức năng | Phụ trách | Trạng thái |
|---|---|---|
| Hồ sơ cá nhân (UC08) | @TrongHiep | ✅ Giữ lại & Cải tiến UI |
| Lịch sử phiên chơi (UC10) | Thành viên khác | ❌ Bỏ khỏi scope này |
| Điểm thưởng & Hạng (UC09/15) | Thành viên khác | ❌ Bỏ khỏi scope này (chỉ đọc hạng từ `MembershipTiers` để chiết khấu) |
| Đánh giá dịch vụ (UC17) | — | ❌ Hủy bỏ hoàn toàn |
| Gọi đồ QR tại bàn (UC07 - PA1 Staff duyệt) | @TrongHiep | ✅ Phát triển |
| Gọi đồ tại quầy & Duyệt đơn (Staff) | @TrongHiep | ✅ Phát triển |
| Checkout & Hóa đơn (Invoices) | @TrongHiep | ✅ Phát triển |
| Thanh toán Tiền mặt & VNPay (PaymentTransactions) | @TrongHiep | ✅ Phát triển |
| Tra cứu & Đối soát giao dịch | @TrongHiep | ✅ Phát triển |

---

## 2. Database Schema (Khớp 100% cơ sở dữ liệu thực tế)

### 2.1 Bảng `dbo.Orders` & `dbo.OrderDetails`
- `dbo.Orders`:
  - `Id` (PK, int, identity)
  - `SessionId` (FK, int, not null)
  - `OrderTimeUtc` (datetime2(0), not null)
  - `Status` (varchar(20), not null): `'Pending'` (chờ Staff duyệt), `'Served'` (đã phục vụ), `'Cancelled'` (hủy đơn)
- `dbo.OrderDetails`:
  - `Id` (PK, int, identity)
  - `OrderId` (FK, int, not null)
  - `ProductId` (FK, int, not null)
  - `Quantity` (int, not null)
  - `UnitPrice` (decimal(18,2), not null)
  - `Subtotal` (Computed: Quantity * UnitPrice)

### 2.2 Bảng `dbo.Invoices`
- `Id` (PK, int, identity)
- `SessionId` (FK, int, not null)
- `CustomerId` (FK, nvarchar(450), null)
- `TotalPlaytime` (decimal(18,2), not null)
- `TotalFandB` (decimal(18,2), not null)
- `DiscountAmount` (decimal(18,2), not null)
- `FinalAmount` (Computed, decimal(20,2), null)
- `Status` (varchar(20), not null): `'Unpaid'`, `'Paid'`, `'Cancelled'`
- `CreatedAtUtc` (datetime2(0), not null)

### 2.3 Bảng `dbo.PaymentTransactions`
- `Id` (PK, int, identity)
- `InvoiceId` (FK, int, not null)
- `PaymentMethod` (varchar(20), not null): `'Cash'`, `'VNPay'`
- `AmountPaid` (decimal(18,2), not null)
- `GatewayRefCode` (nvarchar(100), null)
- `TransactionTimeUtc` (datetime2(0), not null)

### 2.4 Bảng `dbo.MembershipTiers`
- `Id` (PK, int, identity)
- `TierName` (nvarchar(50), not null): Đồng, Bạc, Vàng, Kim cương
- `MinPoints` (int, not null)
- `DiscountPercent` (decimal(5,2), not null): 0%, 5%, 10%, ...

### 2.5 Bảng `dbo.PlaySessions` & Stored Procedure `usp_CloseSession`
- `PlaySessions`: `Id`, `TableId`, `CustomerId`, `StartAtUtc`, `EndAtUtc`, `HourlyRateSnapshot`, `PlaytimeAmount`, `Status` (`'Active'`, `'Closed'`).
- `usp_CloseSession`: Chốt `EndAtUtc`, tính `PlaytimeAmount = ROUND(DATEDIFF_BIG(second, StartAtUtc, EndAtUtc) * HourlyRateSnapshot / 3600.0, 0)`, chuyển phiên sang `Closed`, chuyển bàn sang `AwaitingPayment`.

---

## 3. Luồng nghiệp vụ chi tiết

### 3.1 Hồ sơ cá nhân (CustomerProfile)
- Xem thông tin tài khoản hiện tại (Họ tên, Username, Email, SĐT, Ngày tạo).
- Sửa họ tên, email, SĐT với kiểm tra trùng email/SĐT với tài khoản khác.
- Giao diện trau chuốt, responsive $\ge$ 360px.

### 3.2 Gọi món F&B (OrderController)
1. **Khách quét QR tại bàn (`/Order/Table/{tableCode}`)**:
   - Truy cập ẩn danh (`[AllowAnonymous]`).
   - Kiểm tra bàn có phiên `Active`. Nếu không có, hiển thị thông báo "Bàn chưa mở phiên chơi".
   - Hiển thị danh mục món (`Products` có `IsActive = 1` và `StockQuantity > 0`).
   - Khách thêm món (1–20 mỗi món) và bấm **Gửi đơn**.
   - Đơn được tạo với trạng thái `Pending`.
   - **Chống gọi trộm (Phương án 1)**: Đơn `Pending` chưa trừ tồn kho ngay và chưa tính cứng vào hóa đơn cho đến khi Staff tại quầy bấm "Xác nhận & Phục vụ".
   - Khách có thể xem danh sách đơn của bàn mình.
2. **Staff quản lý đơn bàn & Gọi tại quầy (`/Order/Counter`, `/Order/Manage`)**:
   - Staff xem danh sách đơn `Pending` từ các bàn, có nút **"Xác nhận & Phục vụ"** (chuyển sang `Served`, trừ tồn kho `StockQuantity`) hoặc **"Hủy đơn"** (`Cancelled`).
   - Staff có thể gọi thêm món trực tiếp cho phiên chơi bất kỳ.
   - Có nút **"Đi đến thanh toán"** dẫn sang màn hình Checkout của phiên chơi đó.

### 3.3 Checkout và Thanh toán (CheckoutController)
1. **Màn hình Checkout (`/Checkout/Session/{sessionId}`)**:
   - Staff mở màn hình thanh toán cho phiên chơi.
   - **Tổng hợp chi phí:**
     + **Tiền giờ (`TotalPlaytime`)**: Nếu phiên chưa đóng, hiển thị thời gian chơi tạm tính đến thời điểm hiện tại và số tiền tạm tính; nếu đã đóng, lấy `PlaytimeAmount`.
     + **Tiền F&B (`TotalFandB`)**: Tổng `Subtotal` của các món trong các đơn có trạng thái `Served` (hoặc bao gồm đơn quầy).
     + **Ưu đãi thành viên (`DiscountAmount`)**: Dựa trên hạng thành viên của khách (hoặc tra cứu từ `MembershipTiers`), chiết khấu % theo hạng (Đồng 0%, Bạc 5%, Vàng 10%).
     + **Tổng thanh toán (`FinalAmount`)**: = `TotalPlaytime + TotalFandB - DiscountAmount`.
2. **Chốt hóa đơn & Phương thức thanh toán:**
   - Khi Staff bấm thanh toán:
     - Gọi `usp_CloseSession` (nếu phiên vẫn đang `Active`) để chốt thời gian và tiền giờ chính xác.
     - Tạo bản ghi `Invoices` với trạng thái `'Unpaid'`.
   - **Phương thức 1: Tiền mặt (Cash):**
     - Staff xác nhận đã thu đủ tiền mặt $\rightarrow$ Lưu `PaymentTransactions` (`PaymentMethod = 'Cash'`, `AmountPaid = FinalAmount`).
     - Cập nhật `Invoices.Status = 'Paid'`.
     - Cập nhật trạng thái bàn `BilliardTables.Status = 'Available'`.
     - Hiển thị màn hình biên lai thành công (có nút In hóa đơn).
   - **Phương thức 2: VNPay Sandbox:**
     - Hệ thống sinh mã giao dịch duy nhất, tạo URL thanh toán VNPay có chữ ký số HmacSHA512.
     - Chuyển hướng người dùng / hiển thị mã QR VNPay để thanh toán.
     - Khi VNPay gọi về `ReturnUrl` / `IPN`:
       + Kiểm tra checksum chữ ký bảo mật.
       + Nếu thành công (`vnp_ResponseCode == "00"`): Lưu `PaymentTransactions` (`PaymentMethod = 'VNPay'`, `GatewayRefCode = vnp_TransactionNo`), cập nhật `Invoices.Status = 'Paid'`, giải phóng bàn thành `Available`.
       + Nếu thất bại/hủy: Báo lỗi và cho phép thanh toán lại.
3. **Tra cứu & Đối soát giao dịch (`/Checkout/Invoices` & `/Checkout/Transactions`):**
   - Màn hình tra cứu danh sách hóa đơn theo ngày, theo bàn, theo mã hóa đơn/phiên.
   - Xem chi tiết từng giao dịch, phương thức thanh toán, mã đối soát VNPay.
