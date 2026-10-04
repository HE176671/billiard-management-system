# Trích SRS: phần Quản lý phiên chơi

> Đặt file này tại `docs/sds-doan/SRS_EXTRACT.md`.
> Nguồn: SRS (SWP391 Project Report, Software Requirement Specification, Aug 2026).
> Chỉ trích các phần liên quan đến phiên chơi. Phần mô tả field giữ nguyên tiếng Anh như SRS để dễ đối chiếu.

## 0. Lưu ý quan trọng: SRS và database khác nhau ở đâu

**Khi SRS và database khác nhau, database / code thật là chuẩn.** SRS chỉ là tài liệu yêu cầu.

| Nội dung | Trong SRS | Trong database (`BilliardDB_Full.sql`) |
|---|---|---|
| Trạng thái bàn | Available, Reserved, In-use, Maintenance | `Available`, `InUse`, `AwaitingPayment`, `Maintenance`, `Inactive` |
| Mã / tên bàn | `TableName` (ví dụ "T01") | `TableCode` |
| Loại bàn | `TypeID` (Pool / Carom) | `TableTypeId` -> `TableTypes(Id)` |
| Người thực hiện phiên | `CashierID` (Staff) | `OpenedById`, `ClosedById` |
| Thời gian phiên | `StartTime`, `EndTime` | `StartAtUtc`, `EndAtUtc` |
| Tiền giờ | `PlaytimeSubtotal` | `PlaytimeAmount` (kèm `HourlyRateSnapshot`) |
| Trạng thái bàn khi đóng phiên | (không nêu rõ, "awaiting payment" ở phiên) | Bàn chuyển `AwaitingPayment`, phiên `Closed` |
| Vai trò thao tác | "Receptionist" (mục 1.1.1) và "Staff" (mục khác) | Role `Staff` (Admin cũng được) |
| Real-time | "WebSocket" / "socket connection" | Chưa có, cần quyết định cách làm |

## 1. Tác nhân liên quan

| ID | Actor | Mô tả |
|---|---|---|
| 2 | Customer | Người chơi; xem tình trạng bàn, đặt bàn, gọi món, xem hóa đơn |
| 3 | Staff | Nhân viên tại chỗ, phụ trách vận hành bàn, xử lý order, thanh toán, đăng ký khách |
| 4 | Admin | Toàn quyền: quản lý bàn, kho, ca, vai trò nhân viên, báo cáo |

## 2. Luồng nghiệp vụ liên quan

- **BF-01 Table Reservation Online.** Trigger: khách đăng nhập đặt bàn online hoặc đến trực tiếp. Kết thúc: khách thanh toán xong và staff đóng bàn để giải phóng chỗ.
- **BF-02 Check-in & Open Table.** (SRS chưa mô tả chi tiết.)
- **BF-04 Table Transfer.** (Ngoài phạm vi của Doan.)
- **BF-05 Checkout and Payment.** Trigger: khách yêu cầu thanh toán. Kết thúc: hóa đơn đã thanh toán và bàn sẵn sàng cho khách tiếp theo. (Ngoài phạm vi của Doan, nhưng là nơi phiên đã đóng được xử lý tiếp.)

## 3. Entity liên quan

### 3.1 Billiard Table
| Mục | Nội dung |
|---|---|
| Mục đích | Đại diện một bàn bi-a vật lý |
| Thuộc tính chính | TableName, TypeID (Pool/Carom), Status |
| Định danh | TableName (ví dụ "T01") là duy nhất. Không được xóa nếu đã liên kết với hóa đơn trước đây. |
| Vòng đời trạng thái | Available → Reserved → In-use → Maintenance |

### 3.2 Play Session
| Mục | Nội dung |
|---|---|
| Mục đích | Theo dõi vòng đời thực và thời lượng một bàn được chơi |
| Thuộc tính chính | TableID, CashierID (Staff), StartTime, EndTime, PlaytimeSubtotal, Status |
| Định danh | Một bàn chỉ có **MỘT** phiên "Active" tại một thời điểm |
| Vòng đời trạng thái | Active (đồng hồ chạy) → Closed (đồng hồ dừng, chờ thanh toán) |

### 3.3 Invoice (chỉ để biết điểm nối)
| Mục | Nội dung |
|---|---|
| Thuộc tính chính | SessionID, CustomerID, TotalPlaytime, TotalFandB, DiscountAmount, FinalAmount |
| Định danh | Quan hệ 1-1 chặt với một Play Session đã đóng |
| Vòng đời | Unpaid (tạm tính) → Paid (cuối, không sửa được) |

## 4. Quy tắc dữ liệu liên quan

| Quy tắc | Mô tả | Áp dụng khi |
|---|---|---|
| Strict 1-to-1 Session | Một Billiard_Table chỉ có một Play_Session trạng thái "Active" tại một thời điểm | Mở bàn |
| Immutable Invoices | Hóa đơn đã "Paid" thì không sửa / xóa dữ liệu liên quan (thời gian phiên, chi tiết order) | Xử lý hóa đơn |
| Table Deletion Constraint | Bàn đã có ít nhất một phiên chơi lịch sử thì không được xóa; chỉ chuyển "Maintenance" hoặc "Inactive" | Admin quản lý bàn |
| No Double Booking | Một bàn không được đặt trùng khung giờ | Tạo đặt bàn (ngoài phạm vi) |

## 5. Use case liên quan

| ID | Use case | Mô tả |
|---|---|---|
| UC04 | View Table Status | Khách xem tình trạng bàn theo thời gian thực (Available, Occupied, Reserved) |
| UC20 | Manage Table | Luồng vận hành chính để Staff điều khiển các bàn tại sàn |
| UC21 | Open Table | Bao gồm trong UC20: bắt đầu phiên chơi và tính giờ |
| UC22 | Update Table Status | Bao gồm trong UC20: đổi trạng thái bàn thủ công (Cleaning, Maintenance) |
| UC24 | Close Table | Bao gồm trong UC20: dừng tính giờ và đóng phiên |
| UC23 | Change Customer's Table | Bao gồm trong UC20: chuyển phiên sang bàn khác. **Ngoài phạm vi.** |

## 6. Màn hình 1.1.1 Table Management

Mô tả: màn hình cho Receptionist (Staff) quản lý toàn bộ vòng đời một phiên bàn: xác nhận khách đến (với đặt trước), bắt đầu tính giờ (Open table), dừng tính giờ (Close table) và các thao tác điều chỉnh như Transfer, Split, Merge.

| Nhóm | Field | Mô tả |
|---|---|---|
| Table Information | Table Name / ID | String, read-only. Tên hoặc mã bàn (ví dụ Table 01 - Carom) |
| | Table Status | String, read-only. Trạng thái hiện tại (Available, In-use, Reserved, Maintenance) |
| Booking Confirmation | Customer Name | String, read-only. Tên khách đã đặt trước (nếu có) |
| | Booking Time | DateTime, read-only. Giờ hẹn đến |
| | Confirm Booking (Button) | Bật khi khách đến. Đổi bàn từ "Reserved" sang sẵn sàng để mở |
| Session Control | Start Time | DateTime, read-only. Hệ thống tự ghi khi bấm "Open Table" |
| | End Time | DateTime, read-only. Hệ thống tự ghi khi bấm "Close Table" |
| | Play Duration | HH:mm:ss, read-only. Đồng hồ trực tiếp, tự cập nhật theo thời gian thực |
| Action Buttons | Open Table (Button) | Bắt đầu phiên. Đổi trạng thái sang "In-use" |
| | Close Table (Button) | Dừng đồng hồ. Gửi lệnh tắt thiết bị và chốt hóa đơn tạm, đổi trạng thái sang chờ thanh toán |
| | Transfer Table (Button) | **Ngoài phạm vi.** Chuyển toàn bộ giờ chơi và order sang bàn khác |
| | Split / Merge (Button) | **Ngoài phạm vi.** Gộp hóa đơn hai bàn hoặc tách hóa đơn |

## 7. Màn hình 1.1.2 Real-time Table Layout & Status Monitoring

Mô tả: màn hình cho Customer, Staff và Admin theo dõi sơ đồ sàn và tình trạng bàn theo thời gian thực: xem trạng thái trực tiếp, lọc theo tầng / loại, xem thời gian đang chơi và tóm tắt phiên, chọn nhanh một bàn để thao tác.

| Nhóm | Field | Mô tả |
|---|---|---|
| Floor & Filter | Floor / Area Selector | Dropdown, editable. Chuyển giữa các khu / tầng (ví dụ 1st Floor - Pool, 2nd Floor - Carom & VIP) |
| | Table Type Filter | Multi-select / radio, editable. Lọc theo loại bàn (Pool, 3-Cushion, Snooker, VIP) |
| | Status Filter | Checkbox, editable. Hiện / ẩn theo trạng thái (Available, In-use, Reserved, Maintenance) |
| | Real-time Connection Indicator | Icon / badge, read-only. Trạng thái kết nối trực tiếp (xanh: đã kết nối, đỏ: đang kết nối lại) |
| Interactive Map | Table Layout Grid / Canvas | Sơ đồ đồ họa tương tác. Mỗi bàn tô màu theo trạng thái thời gian thực |
| | Table Code / Label | String, read-only. Mã bàn trên mỗi ô (B01, B02, VIP-01) |
| | Status Badge | Màu: Xanh (Available), Đỏ (In-use), Cam (Reserved), Xám (Maintenance) |
| | Live Playtime Counter | HH:mm:ss, read-only, hiện trên bàn In-use. Cập nhật mỗi giây |
| Table Quick Inspection (pop-over / sidebar) | Selected Table Details | String, read-only. Hiện khi bấm vào bàn: loại bàn, giá giờ, vị trí |
| | Current Occupant / Reservation | String, read-only. Tên khách hoặc mã hội viên đang dùng / đặt bàn (ẩn hoặc che với khách thường) |
| | Active Orders Summary | Số / String, read-only. Số món F&B đang chờ / đã phục vụ và tạm tính của bàn |
| Context Action | Book This Table (Button) | Hiện với Customer. Bật khi bấm bàn "Available". Mở form đặt bàn (ngoài phạm vi Doan) |
| | Manage / Open Session (Button) | Hiện với Staff / Admin. Bật trên bàn "Available" hoặc "Reserved" để mở phiên ngay hoặc xác nhận khách đến |
| | View Provisional Bill (Button) | Hiện với Staff / Admin (và khách của bàn). Đến màn hình hóa đơn / order của bàn (ngoài phạm vi Doan) |
| | Refresh Map (Button) | Tải lại dữ liệu từ server khi mạng chậm hoặc mất kết nối |

## 8. Phân quyền theo SRS (tóm tắt)

- **Customer:** xem sơ đồ bàn (UC04), nhưng thông tin người đang chơi bị ẩn hoặc che.
- **Staff / Admin:** xem sơ đồ bàn, mở bàn, xác nhận khách đến, đóng bàn.
- Customer không được mở / đóng phiên.

## 9. Phạm vi triển khai của Doan trên các màn hình trên

| Thành phần trong wireframe 1.1.1 | Làm | Không làm |
|---|---|---|
| Sơ đồ bàn (cột trái), thẻ bàn có tên, trạng thái, giờ | ✔ | |
| Table Name, Status, Start Time, End Time, Duration | ✔ | |
| Customer, Booking Time | Hiện `--` (hoặc tên khách nếu phiên có CustomerId) | |
| OPEN TABLE, CLOSE TABLE | ✔ | |
| CONFIRM BOOKING, TRANSFER, SPLIT/MERGE | Chỉ hiện nút, disabled | Logic |
| Mọi thứ của mục 1.1.2 | | Không làm |

Các mục "Không làm" vẫn có thể để chỗ trống hoặc nút ẩn trong giao diện, nhưng không viết logic.