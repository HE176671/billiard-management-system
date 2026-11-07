# Ca làm và đối soát tiền mặt — hợp đồng TV5 (Hùng)

Cập nhật: 27/10/2026. Mã tracking: BMS-017.1/017.2, BMS-049, BMS-053.

## 1. Cài đặt

1. Chạy `BMS_Database_Starter/08_AddWorkShiftReconciliation.sql` trên `BilliardDB` (sau 05, 06, 07). Script chỉ thêm cột/ràng buộc, chạy lại an toàn.
2. **Bắt buộc chạy trước khi chạy web.** EF đã map `PaymentTransactions.WorkShiftId/ReceivedById` và các cột mới của `WorkShifts`; thiếu cột thì Dashboard/Doanh thu sẽ lỗi `Invalid column name`.
3. Kiểm tra: `dotnet run --project tests/Bms.Smoke` (có thêm 10 kiểm tra ca làm, tự rollback).

## 2. Schema sau nâng cấp

| Bảng | Cột mới | Ý nghĩa |
|---|---|---|
| `WorkShifts` | `CashCollected` | Snapshot lúc đóng ca: tổng `Cash` gắn với ca |
| `WorkShifts` | `ExpectedCash` | Snapshot: `StartingCash + CashCollected` |
| `WorkShifts` | `ClosedById` | Người đóng ca (chính nhân viên hoặc Admin đóng hộ) |
| `PaymentTransactions` | `WorkShiftId` (NULL được) | Ca nhận giao dịch; **bắt buộc với tiền mặt**, NULL với VNPay/IPN |
| `PaymentTransactions` | `ReceivedById` (NULL được) | Nhân viên thu tiền |

Ràng buộc: mỗi nhân viên tối đa một ca `Active` (`UX_WorkShifts_ActiveStaff`); ca `Closed` phải có `EndTimeUtc` và `EndingCash`.

## 3. Hợp đồng cho TV3 — ghi nhận tiền mặt

Inject `IShiftService` (đã đăng ký trong `Program.cs`). Gọi **trong cùng transaction** với lệnh ghi giao dịch:

```csharp
await using var tx = await _db.Database.BeginTransactionAsync();
int shiftId;
try { shiftId = await _shifts.RequireActiveShiftIdForCashAsync(staffId); }
catch (ShiftException ex) when (ex.Code == "NO_ACTIVE_SHIFT")
{
    // Báo nhân viên mở ca: ex.Message đã là tiếng Việt
}
_db.PaymentTransactions.Add(new PaymentTransaction {
    InvoiceId = invoiceId, PaymentMethod = PaymentTransaction.MethodCash,
    AmountPaid = amount, WorkShiftId = shiftId, ReceivedById = staffId });
await _db.SaveChangesAsync();
// ... cập nhật Invoice Paid theo quy tắc TV3
await tx.CommitAsync();
```

- `staffId` lấy từ phiên đăng nhập (`UserManager.GetUserId(User)`), không nhận từ form.
- Hàm khóa dòng ca (`UPDLOCK, HOLDLOCK`); đóng ca dùng cùng khóa nên không có giao dịch tiền mặt "lọt" sau khi đã chốt ca.
- Gọi ngoài transaction sẽ ném `InvalidOperationException` để phát hiện sai sớm.
- VNPay/IPN: không gọi hàm này, để `WorkShiftId = NULL` (hoặc gán ca nếu nhân viên tạo link thanh toán — không tính vào két).
- Chỉ insert vào `PaymentTransactions` khi giao dịch **đã thành công**. Báo cáo doanh thu và Dashboard cộng mọi dòng trong bảng này.

## 4. Quy tắc đối soát

- Tiền kỳ vọng = tiền đầu ca + tổng giao dịch `Cash` có `WorkShiftId` của ca.
- Chênh lệch = tiền đếm thực tế − kỳ vọng (dương: thừa, âm: thiếu). Khác 0 thì bắt buộc ghi lý do.
- Đóng ca lưu snapshot; giao dịch bị sửa về sau không làm đổi kết quả đối soát đã chốt.
- Admin xem mọi ca, lọc theo ngày/nhân viên/trạng thái/ca lệch, và đóng hộ ca nhân viên quên đóng.

## 5. Màn hình

| URL | Quyền | Chức năng |
|---|---|---|
| `/Staff` | Staff, Admin | Trang nhân viên: trạng thái ca, số bàn theo trạng thái |
| `/Shift` | Staff, Admin | Ca của tôi: mở ca hoặc xem số liệu + đóng ca |
| `/Shift/History` | Staff, Admin | Lịch sử ca của tôi |
| `/Shift/Details/{id}` | Chủ ca, Admin | Chi tiết, danh sách giao dịch, Admin đóng hộ |
| `/Shift/Manage` | Admin | Quản lý mọi ca |
| `/Profile` | Mọi người đã đăng nhập | Sửa họ tên, số điện thoại |
| `/Customer/Details/{id}`, `/Customer/Edit/{id}` | Admin | Xem, sửa thông tin liên hệ khách |

## 6. Còn chờ tích hợp

- TV3 gọi `RequireActiveShiftIdForCashAsync` khi thu tiền mặt. Trước khi có, ca chỉ hiện tiền đầu ca, tiền thu = 0.
- TV1/TV4 thay `href="#"` trong menu Nghiệp vụ (`_AdminLayout`) bằng controller của mình.
