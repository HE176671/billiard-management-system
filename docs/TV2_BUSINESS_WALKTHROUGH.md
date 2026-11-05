# Kịch bản Luồng Nghiệp vụ & Sơ đồ Trực quan — Phân hệ TV2 (Hội viên & Ưu đãi)

Tài liệu này tóm tắt toàn bộ bức tranh nghiệp vụ của phân hệ **TV2 — Quản lý Hội viên & Chăm sóc Khách hàng thân thiết** kèm sơ đồ trực quan và kịch bản trình diễn từng bước trên hệ thống đang chạy tại `http://localhost:5000`.

---

## 1. Bức tranh tổng thể 4 luồng nghiệp vụ cốt lõi

```mermaid
flowchart TB
    subgraph L1["1. TẠI QUẦY THU NGÂN (Nhân viên)"]
        A1["Khách đọc SĐT / đưa thẻ MBxxxxxx"] --> A2{"Đã là hội viên?"}
        A2 -- "Chưa có" --> A3["Đăng ký nhanh tại quầy (<10s)<br/>Tặng ngay +10 điểm mở thẻ"]
        A2 -- "Đã có" --> A4["Mở Hồ sơ hội viên<br/>Xem lưu ý phục vụ & voucher"]
        A3 --> A4
    end

    subgraph L2["2. TÍNH TIỀN & TÍCH ĐIỂM (3 lớp ưu đãi)"]
        A4 --> B1["Nhập Tiền giờ + Tiền đồ ăn/nước"]
        B1 --> B2["Lớp 1: Giảm % theo Hạng (chỉ trên Tiền giờ)"]
        B2 --> B3["Lớp 2: Trừ mã Voucher (nếu có)"]
        B3 --> B4["Lớp 3: Đổi điểm (1đ = 1.000₫, tối đa 50% còn lại)"]
        B4 --> B5["Khách thực trả (FinalAmount)<br/>Tích điểm = ⌊Thực trả / 20.000⌋ × Hệ số hạng"]
    end

    subgraph L3["3. SỔ CÁI ĐIỂM & XÉT HẠNG 12 THÁNG"]
        B5 --> C1["Ghi Sổ cái điểm (PointTransactions)<br/>Chống cộng trùng theo Mã hóa đơn"]
        C1 --> C2{"Chi tiêu 12T vượt ngưỡng?"}
        C2 -- "Đủ ngưỡng mới" --> C3["Nâng hạng ngay lập tức<br/>(Member → Silver → Gold)"]
        C2 -- "Tới kỳ xét 12T mà thiếu" --> C4["Gia hạn 30 ngày giữ hạng<br/>Hết hạn chỉ hạ tối đa 1 bậc"]
    end

    subgraph L4["4. PHÂN NHÓM KHÁCH & GỬI ƯU ĐÃI (Admin)"]
        C1 --> D1["Tự động phân 6 nhóm khách<br/>(Khách VIP, Thân thiết, Ít quay lại...)"]
        D1 --> D2["Lọc nhóm Ít quay lại (vắng 31-90 ngày)<br/>Gửi Voucher & Điểm thưởng mời quay lại"]
    end
```

---

## 2. Sơ đồ vòng đời Hạng thành viên (Chu kỳ 12 tháng)

Khác với cách cộng dồn trọn đời dễ gây "lạm phát hạng VIP", hệ thống xét hạng dựa trên **Tổng chi tiêu thực trả trong 12 tháng gần nhất**:

```mermaid
stateDiagram-v2
    [*] --> Member: Mở thẻ (0 ₫)
    Member --> Silver: Chi tiêu 12T ≥ 2.000.000 ₫ (Lên ngay)
    Silver --> Gold: Chi tiêu 12T ≥ 10.000.000 ₫ (Lên ngay)

    Gold --> Gold_Grace: Sau 12T, chi tiêu 12T < 10.000.000 ₫
    state "Gia hạn giữ hạng Gold (30 ngày)" as Gold_Grace
    Gold_Grace --> Gold: Bù đủ chi tiêu trong 30 ngày
    Gold_Grace --> Silver: Hết 30 ngày chưa đủ (Chỉ hạ 1 bậc)

    Silver --> Silver_Grace: Sau 12T, chi tiêu 12T < 2.000.000 ₫
    state "Gia hạn giữ hạng Silver (30 ngày)" as Silver_Grace
    Silver_Grace --> Silver: Bù đủ chi tiêu trong 30 ngày
    Silver_Grace --> Member: Hết 30 ngày chưa đủ
```

---

## 3. Sơ đồ trình tự: Đổi điểm lấy Voucher & Sử dụng tại quán

```mermaid
sequenceDiagram
    autonumber
    actor K as Khách hàng (Cổng hội viên)
    participant S as Hệ thống BMS (Sổ cái & Voucher)
    actor NV as Nhân viên tại quầy

    K->>S: Chọn phần quà & bấm "Đổi quà" (vd: Nước ngọt - 30 điểm)
    S->>S: Trừ 30 điểm qua Sổ cái (Loại: Redeem)<br/>Sinh mã BMS-XXXX-XXXX + Mã QR (Hạn 30 ngày)
    S-->>K: Hiển thị thẻ Voucher + QR trong mục "Voucher của tôi"

    alt Loại 1: Quà tặng tại quầy (Nước ngọt, Combo)
        K->>NV: Đưa mã BMS-XXXX-XXXX tại quầy
        NV->>S: Nhập mã vào ô "Đổi voucher quà tại quầy" (/CustomerManagement/Lookup)
        S->>S: Kiểm tra còn hạn & chuyển trạng thái -> Đã dùng (Used)
        S-->>NV: Báo xác nhận thành công, giao nước cho khách
    else Loại 2: Giảm tiền giờ / Giảm hóa đơn
        K->>NV: Đọc mã voucher khi thanh toán bàn
        NV->>S: Chọn voucher tại màn hình "Tính tiền & Tích điểm" (/CashierSimulator)
        S->>S: Trừ tiền vào hóa đơn & đánh dấu Voucher -> Đã dùng (Used)
    end
```

---

## 4. Kịch bản trải nghiệm thực tế từng bước (5 phút)

> [!TIP]
> Bạn có thể thực hiện lần lượt 5 cảnh dưới đây ngay trên trình duyệt tại `http://localhost:5000` để nắm trọn vẹn luồng chạy của hệ thống.

### Cảnh 1: Nhân viên tra cứu & đăng ký hội viên mới tại quầy
1. Vào menu **Tra cứu tại quầy** (`/CustomerManagement/Lookup`).
2. **Tìm khách quen:** Gõ `0101` hoặc `Minh Tuấn` vào ô tìm kiếm → Hệ thống hiển thị ngay hội viên **Nguyễn Minh Tuấn** (Hạng Gold, SĐT che giữa `091****101`, mã thẻ `MB900101`).
3. **Đăng ký khách mới:** Bấm nút **Đăng ký hội viên mới** → Nhập SĐT `0988112233` và tên `Lê Minh Khang` → Bấm **Tạo thẻ hội viên**.
4. **Kết quả:** Hệ thống tự động cấp mã thẻ `MBxxxxxx`, cộng **+10 điểm** mở thẻ trong Sổ cái và mở ngay trang **Hồ sơ hội viên**.

### Cảnh 2: Ghi chú phục vụ & Tính tiền 3 lớp ưu đãi
1. Tại trang **Hồ sơ hội viên**, chuyển sang tab **Ghi chú phục vụ** → Nhập *"Khách thích bàn VIP gần cửa sổ, uống Bò húc ít đá"*, tích chọn **Ghim lên đầu hồ sơ** rồi bấm **Thêm ghi chú** → Khung lưu ý màu vàng xuất hiện ngay dưới tên khách.
2. Bấm nút **Tính tiền cho khách** ở góc phải trên cùng (hoặc vào menu **Tính tiền & Tích điểm** `/CashierSimulator`).
3. Chọn khách hàng hạng **Gold** (ví dụ: **Nguyễn Minh Tuấn**), nhập thử:
   - **Tiền giờ chơi:** `200000` (200.000 ₫)
   - **Tiền đồ ăn & nước uống:** `100000` (100.000 ₫)
   - **Đổi điểm trừ tiền:** Bấm **Tối đa**
4. **Quan sát Phiếu tạm tính bên phải:**
   - Ưu đãi hạng Gold (-10% chỉ tính trên tiền giờ): `-20.000 ₫` (còn 280.000 ₫).
   - Đổi điểm tối đa (50% của phần còn lại): `-140.000 ₫` (140 điểm).
   - **Tổng thanh toán:** `140.000 ₫` → Điểm tích lũy mới: `⌊140.000 / 20.000⌋ × 2 = +14 điểm`.
5. Bấm **Xác nhận thanh toán**. Nếu giữ nguyên **Mã hóa đơn** và bấm thanh toán lần 2, hệ thống tự động chặn cộng trùng điểm!

### Cảnh 3: Kiểm soát chéo khi Điều chỉnh điểm thưởng
1. Vào menu **Điều chỉnh điểm** (`/PointAdjustment`).
2. Nếu điều chỉnh nhỏ (`≤ 50 điểm`) bởi Quản trị viên: Điểm được ghi vào Sổ cái ngay lập tức.
3. Nếu điều chỉnh lớn (`> 50 điểm`, ví dụ `+150 điểm` bù sự cố mất điện) hoặc do Nhân viên lập: Phiếu chuyển vào danh sách **Phiếu đang chờ duyệt** và người lập không thể tự bấm duyệt phiếu của chính mình (bắt buộc Quản trị viên khác xác nhận).

### Cảnh 4: Thống kê hội viên & Gửi chương trình ưu đãi (Dành cho Admin)
1. Vào menu **Thống kê hội viên** (`/CrmDashboard`).
2. Quan sát biểu đồ tròn **Nhóm hội viên theo tần suất ghé** (6 nhóm tự động tính từ lịch sử giao dịch thực tế) và bảng **Khách quen vắng mặt trên 30 ngày** ở góc dưới bên phải.
3. Bấm nút **Gửi ưu đãi khách vắng mặt** → Hệ thống chuyển sang màn hình **Gửi ưu đãi** (`/Campaign`) và chọn sẵn nhóm **Ít quay lại**.
4. Chọn tặng kèm 1 Voucher hoặc tặng thêm `20 điểm`, bấm **Xem danh sách nhận** để kiểm tra những hội viên đủ điều kiện, sau đó bấm **Phát hành & gửi ngay**.

### Cảnh 5: Trải nghiệm dưới góc nhìn Khách hàng (Thẻ QR & Đổi quà)
1. Đăng xuất tài khoản Admin, đăng nhập bằng tài khoản Khách hàng (hoặc tự đăng ký tài khoản mới tại `/Account/Register` với cùng SĐT đã tạo ở Cảnh 1 để kiểm tra tính năng **tự động liên kết thẻ tại quầy**).
2. Tại mục **Tổng quan & Thẻ thành viên** (`/CustomerPortal/Profile`), khách nhìn thấy **Thẻ hội viên kèm mã QR**, số điểm quy đổi ra tiền VNĐ và thanh tiến trình xét hạng 12 tháng.
3. Chuyển sang mục **Voucher của tôi & Đổi điểm** (`/CustomerPortal/Promotions`), bấm **Đổi quà** ở một phần quà khả dụng → Nhận ngay mã voucher `BMS-XXXX-XXXX` kèm mã QR để đưa nhân viên quét tại quầy.

