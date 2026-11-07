# Billiard Management System (BMS) - Customer Profile & F&B Order Spec
# Version: 0.4 | Owner: @TrongHiep | Date: 2026-11-07
# Thay đổi v0.4:
# 1. Bỏ hoàn toàn Checkout & Thanh toán (Hóa đơn, PaymentTransactions, VNPay) theo yêu cầu - do thành viên khác phụ trách.
# 2. Bỏ UC17 (Đánh giá dịch vụ), UC10 (Lịch sử phiên), UC09/15 (Điểm thưởng) - do thành viên khác phụ trách.
# 3. Tập trung hoàn thiện 100% 2 phân hệ cốt lõi:
#    - Hồ sơ cá nhân (UC08 - CustomerProfile): xem & sửa thông tin cá nhân.
#    - Gọi đồ ăn/uống F&B (UC07 - OrderController):
#      + Quét mã QR tại bàn (/Order/Table/{tableCode}): khách ẩn danh gọi món, cơ chế Phương án 1 (chờ duyệt để chống gọi trộm từ xa).
#      + Mua lẻ tại quầy (/Order/Counter - Tab 1): khách mua mang về không thuê bàn bi-a.
#      + Thêm món cho bàn đang chơi (/Order/Counter - Tab 2): nhân viên thêm món trực tiếp vào phiên đang chơi của bàn.
#      + Duyệt đơn gọi món từ bàn (/Order/Manage): nhân viên kiểm tra tồn kho, duyệt (trừ kho -> Served) hoặc hủy đơn.

---

## 1. Context & Goal

**Phân định ranh giới (Scope của @TrongHiep):**
| Chức năng | Phụ trách | Trạng thái |
|---|---|---|
| Hồ sơ cá nhân (UC08) | @TrongHiep | ✅ Hoàn thiện & Giao diện đẹp |
| Gọi đồ QR tại bàn (UC07 - PA1 Staff duyệt) | @TrongHiep | ✅ Hoàn thiện (Mobile UI, kiểm tra tồn kho) |
| Gọi đồ tại quầy (Bán lẻ mang về & Bàn đang chơi) | @TrongHiep | ✅ Hoàn thiện |
| Duyệt đơn gọi món từ bàn (/Order/Manage) | @TrongHiep | ✅ Hoàn thiện (Duyệt trừ kho / Hủy) |
| Lịch sử phiên chơi (UC10) | Thành viên khác | ❌ Ngoài scope này |
| Điểm thưởng & Hạng (UC09/15) | Thành viên khác | ❌ Ngoài scope này |
| Đánh giá dịch vụ (UC17) | — | ❌ Hủy bỏ hoàn toàn |
| Checkout & Hóa đơn & Thanh toán (Tiền mặt / VNPay) | Thành viên khác | ❌ Ngoài scope này |

---

## 2. Database Schema (Dùng nguyên trạng, không sửa DB)

### 2.1 Bảng `dbo.Orders` & `dbo.OrderDetails`
- `dbo.Orders`:
  - `Id` (PK, int, identity)
  - `SessionId` (FK, int, not null)
  - `OrderTimeUtc` (datetime2(0), not null)
  - `Status` (varchar(20), not null): `'Pending'` (chờ duyệt), `'Served'` (đã phục vụ), `'Cancelled'` (hủy đơn)
- `dbo.OrderDetails`:
  - `Id` (PK, int, identity)
  - `OrderId` (FK, int, not null)
  - `ProductId` (FK, int, not null)
  - `Quantity` (int, not null)
  - `UnitPrice` (decimal(18,2), not null)
  - `Subtotal` (Computed: Quantity * UnitPrice)

### 2.2 Bảng `dbo.Products` & `dbo.ProductCategories`
- `dbo.Products`: `Id`, `CategoryId`, `Name`, `Price`, `StockQuantity`, `IsActive`
- `dbo.ProductCategories`: `Id`, `Name`

### 2.3 Bảng `dbo.PlaySessions` & `dbo.BilliardTables`
- `dbo.PlaySessions`: `Id`, `TableId`, `CustomerId`, `StartAtUtc`, `EndAtUtc`, `HourlyRateSnapshot`, `PlaytimeAmount`, `Status`
- `dbo.BilliardTables`: `Id`, `TableCode`, `TableTypeId`, `FloorNumber`, `Status`

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
   - Khách có thể nhập trực tiếp số lượng món hoặc bấm nút `[+]`/`[-]` (tự động kiểm tra và giới hạn theo tồn kho thực tế).
   - Bấm **Gửi gọi món** $\rightarrow$ Tạo đơn trạng thái `Pending`.
   - **Chống gọi trộm (Phương án 1)**: Đơn `Pending` chưa trừ tồn kho ngay, chờ Staff tại quầy duyệt thì mới trừ tồn kho để chống khách ở nhà spam.
   - Khách có thể xem danh sách đơn đã gọi của bàn mình.

2. **Quầy F&B Staff (`/Order/Counter`)**:
   - **Tab 1: Khách mua lẻ tại quầy (Mang về)**: Không cần chọn bàn. Staff chọn món trên thực đơn $\rightarrow$ Bấm "Xác nhận tạo đơn mang về" $\rightarrow$ Trừ tồn kho, lưu đơn `Served`.
   - **Tab 2: Gọi thêm cho bàn đang chơi**: Staff chọn bàn đang có phiên `Active` $\rightarrow$ Thêm món vào bàn $\rightarrow$ Trừ tồn kho, lưu đơn `Served` vào phiên chơi của bàn đó.

3. **Duyệt đơn bàn QR (`/Order/Manage`)**:
   - Xem danh sách các đơn `Pending` từ mã QR bàn gửi lên.
   - Nút **"Duyệt & Phục vụ"**: Kiểm tra tồn kho, trừ `StockQuantity`, chuyển sang `Served`.
   - Nút **"Hủy"**: Chuyển trạng thái đơn sang `Cancelled`.
