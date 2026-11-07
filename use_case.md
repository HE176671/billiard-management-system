# ĐẶC TẢ USE CASE HỆ THỐNG QUẢN LÝ CLB BIDA & CHĂM SÓC HỘI VIÊN (BMS)

> **Chuẩn hóa theo quy tắc Software Requirements Specification (SRS):**
> - **Tên Use Case:** Động từ hành động + Bổ ngữ mục tiêu rõ ràng.
> - **Actor (Tác nhân):**
>   - `Admin` (Quản trị viên / Chủ quán)
>   - `Staff` (Nhân viên thu ngân / Phục vụ tại quầy)
>   - `System` (Hệ thống xử lý tự động / Tác vụ ngầm định kỳ `LoyaltyDailyJob`)
> - **Use Case Description:** Mô tả rõ mục đích, dữ liệu đầu vào, quy tắc nghiệp vụ cốt lõi và kết quả đầu ra dựa trên mã nguồn thực tế của dự án.

---

## 1. BẢNG TỔNG HỢP USE CASE TOÀN HỆ THỐNG

### 1.1. Phân hệ Quản lý Hội viên & Chăm sóc Khách hàng tại Quầy (CRM)

| ID | Use Case | Actor | Use Case Description |
| :--- | :--- | :--- | :--- |
| **UC-01** | Tra cứu nhanh hội viên tại quầy | `Staff`, `Admin` | Nhân viên nhập tối thiểu 2 ký tự (họ tên, mã thẻ `MB...` hoặc 4 số điện thoại) tại màn hình Tra cứu tại quầy. Hệ thống trả về tối đa 8 hồ sơ khớp tức thì (ẩn số điện thoại dạng `090****567`), hiển thị hạng thẻ và số điểm tích lũy hiện có. |
| **UC-02** | Đăng ký thẻ hội viên mới tại quầy | `Staff`, `Admin` | Nhân viên nhập Số điện thoại (10 số bắt đầu bằng `0`), Họ tên, Email (đúng định dạng chuẩn `ten@domain.com`) và Ngày sinh. Hệ thống kiểm tra không trùng SĐT/Email, tự động cấp mã thẻ `MBxxxxxx`, gán hạng khởi đầu `Member` và cộng ngay **+10 điểm thưởng chào mừng** vào sổ cái. |
| **UC-03** | Xem hồ sơ hội viên 360 độ | `Staff`, `Admin` | Nhân viên hoặc Admin mở trang chi tiết của một hội viên để xem toàn cảnh: Hạng thẻ hiện tại, tiến độ chi tiêu 12 tháng để lên hạng tiếp theo, nhóm khách RFM (`VIP`, `Thân thiết`, `Khách mới`, `Lâu rồi chưa ghé`), ghi chú lưu ý phục vụ, ví Voucher, lịch sử điểm và lịch sử phiên chơi. |
| **UC-04** | Xem và lọc danh sách hội viên | `Admin` | Admin tra cứu toàn bộ danh sách hội viên kết hợp bộ lọc theo từ khóa (tên, SĐT, mã thẻ), hạng thành viên (`Member`, `Silver`, `Gold`) và 4 nhóm khách hàng RFM (`VIP`, `Thân thiết`, `Khách mới`, `Lâu rồi chưa ghé`). |
| **UC-05** | Cập nhật thông tin liên lạc hội viên | `Staff`, `Admin` | Nhân viên hoặc Admin chỉnh sửa Họ tên, Số điện thoại, Email, Ngày sinh và tùy chọn nhận thông báo ưu đãi của hội viên. Hệ thống kiểm tra trùng lặp SĐT/Email và tự động ghi vết trường thay đổi (trước – sau) vào bảng `CustomerAuditLogs`. |
| **UC-06** | Cấp mới hoặc đổi mã thẻ thành viên | `Staff`, `Admin` | Nhân viên hoặc Admin nhập mã thẻ vật lý mới (tối đa 20 ký tự chữ, số, dấu gạch ngang) hoặc để trống để hệ thống tự sinh mã `MBxxxxxx` mới gắn cho hội viên khi khách làm mất thẻ cũ. |
| **UC-07** | Thêm và ghim ghi chú phục vụ hội viên | `Staff`, `Admin` | Nhân viên ghi chú đặc điểm phục vụ của khách (ví dụ: loại cơ yêu thích, đồ uống quen thuộc) và có thể bấm ghim (`Pin`) để ghi chú luôn nổi bật ở đầu hồ sơ mỗi khi khách ghé quán. |
| **UC-08** | Gộp hồ sơ hội viên bị trùng lặp | `Admin` | Khi một khách hàng vô tình có 2 hồ sơ khác nhau, Admin chọn hồ sơ nguồn và hồ sơ đích để gộp. Hệ thống tự động chuyển toàn bộ số dư điểm (qua bút toán `Adjust`), lịch sử giao dịch, ví Voucher, hóa đơn sang hồ sơ đích và vô hiệu hóa hồ sơ nguồn. |
| **UC-09** | Xuất danh sách hội viên ra file CSV | `Admin` | Admin xuất danh sách hội viên theo điều kiện đang lọc ra tập tin CSV (mã hóa UTF-8 có BOM hiển thị chuẩn tiếng Việt trên Excel) gồm: Họ tên, SĐT, Mã thẻ, Hạng, Nhóm RFM, Điểm hiện có và Chi tiêu 12 tháng. |

---

### 1.2. Phân hệ Tính tiền, Đổi điểm, Voucher & Sổ cái Điểm thưởng (Loyalty)

| ID | Use Case | Actor | Use Case Description |
| :--- | :--- | :--- | :--- |
| **UC-10** | Xem trước ưu đãi và điểm thưởng hóa đơn | `Staff`, `Admin` | Thu ngân chọn hội viên, nhập tiền giờ chơi (`PlaytimeAmount`), tiền dịch vụ ăn uống (`FandBAmount`), chọn mã Voucher và số điểm muốn dùng. Hệ thống tính toán và hiển thị thứ tự giảm giá: **(1) Giảm theo hạng thẻ (chỉ tính trên tiền giờ) $\rightarrow$ (2) Trừ Voucher $\rightarrow$ (3) Đổi điểm (tối đa 50% số tiền còn lại)**, đồng thời báo trước số điểm sẽ tích lũy và khả năng thăng hạng. |
| **UC-11** | Xác nhận thanh toán và tích điểm hóa đơn | `Staff`, `Admin` | Thu ngân xác nhận thanh toán hóa đơn. Hệ thống thực thi trong 1 giao dịch nguyên tử (chống trùng lặp theo `ReferenceCode`): trừ điểm quy đổi (`Redeem`), đánh dấu Voucher đã dùng (`Used`), cộng điểm thưởng mới (`Earn` theo công thức $\lfloor \text{Tiền thực trả} / 20.000 \rfloor \times \text{Hệ số hạng}$) và cập nhật ngày ghé gần nhất. |
| **UC-12** | Đổi điểm tích lũy cấp Voucher tại quầy | `Staff`, `Admin` | Trên hồ sơ 360 của hội viên, nhân viên chọn phần thưởng đang mở trong danh mục và bấm đổi điểm giúp khách. Hệ thống kiểm tra hạng tối thiểu và số dư điểm, trừ điểm trong sổ cái và phát hành mã Voucher (`BMS-XXXX-XXXX`, hạn dùng 30 ngày) vào ví của hội viên. |
| **UC-13** | Xác nhận giao quà tặng bằng mã Voucher | `Staff`, `Admin` | Khi khách sử dụng Voucher loại quà hiện vật tại quầy (nước uống, đồ ăn), nhân viên nhập mã Voucher để xác nhận đã trao quà. Hệ thống chuyển trạng thái Voucher từ `Active` sang `Used` và chặn tái sử dụng mã đó lần thứ hai. |
| **UC-14** | Hủy Voucher và hoàn điểm cho hội viên | `Admin` | Khi khách đổi nhầm Voucher và chưa sử dụng (`Status = Active`), Admin thực hiện thu hồi mã Voucher. Hệ thống chuyển trạng thái Voucher sang `Cancelled` và tự động sinh bút toán đảo (`Reverse`) hoàn trả lại đúng số điểm gốc cho hội viên. |
| **UC-15** | Quản lý danh mục quà đổi điểm & Đóng/Mở mã quà | `Admin` | Admin thêm mới, chỉnh sửa thông tin phần quà (mã quà, tên quà, số điểm đổi, hình thức quà, giá trị quy đổi, hạng tối thiểu) và có thể bấm **Đóng mã / Mở mã** bất cứ lúc nào để tạm ngưng hoặc mở lại việc đổi mã quà tặng đó tại quầy. |
| **UC-16** | Tạo yêu cầu điều chỉnh điểm thủ công | `Staff`, `Admin` | Nhân viên hoặc Admin lập phiếu cộng/trừ điểm thủ công kèm lý do bắt buộc (bù sự cố bàn/điện, sai sót hóa đơn). Nếu số điểm điều chỉnh tuyệt đối $< 100$ điểm (hoặc do Admin tạo), hệ thống ghi sổ cái ngay; nếu Nhân viên điều chỉnh $\ge 100$ điểm, hệ thống chuyển phiếu sang trạng thái chờ duyệt (`Pending`). |
| **UC-17** | Phê duyệt hoặc từ chối điều chỉnh điểm lớn | `Admin` | Admin kiểm tra các phiếu điều chỉnh điểm $\ge 100$ điểm đang chờ duyệt. Hệ thống áp dụng nguyên tắc kiểm soát chéo (**Maker $\neq$ Checker**: người tạo phiếu không được tự duyệt phiếu của mình) trước khi chính thức cộng/trừ điểm vào sổ cái. |

---

### 1.3. Phân hệ Cấu hình Hạng Thành viên, Chiến dịch Ưu đãi & Tự động hóa

| ID | Use Case | Actor | Use Case Description |
| :--- | :--- | :--- | :--- |
| **UC-18** | Xem trước ảnh hưởng khi đổi ngưỡng hạng | `Admin` | Tại màn hình Hạng thành viên, Admin nhập thử ngưỡng chi tiêu 12 tháng mới và bấm "Xem trước thay đổi". Hệ thống mô phỏng trong bộ nhớ và liệt kê số hội viên sẽ được nâng hạng ngay (`Upgrade`) hoặc được giữ hạng chờ xét định kỳ (`Grace`) mà chưa làm thay đổi dữ liệu thật. |
| **UC-19** | Cập nhật cấu hình hạng và quyền lợi hội viên | `Admin` | Admin lưu ngưỡng chi tiêu 12 tháng, % giảm tiền giờ, hệ số nhân điểm và giá trị quà sinh nhật cho từng hạng (`Member`, `Silver`, `Gold`). Hệ thống kiểm tra hạng thấp nhất $= 0\text{ ₫}$, các ngưỡng tăng dần nghiêm ngặt, nâng hạng ngay cho hội viên đủ điều kiện và giữ nguyên hạng hiện tại cho các hội viên khác đến kỳ xét định kỳ. |
| **UC-20** | Tự động thăng hạng tức thì sau thanh toán | `System` | Ngay sau mỗi giao dịch thanh toán hóa đơn, hệ thống tính tổng chi tiêu 12 tháng gần nhất của hội viên. Nếu đạt ngưỡng của hạng cao hơn, hệ thống tự động nâng hạng ngay lập tức, cấp chu kỳ giữ hạng 12 tháng mới và lưu lịch sử thăng hạng. |
| **UC-21** | Tự động xét giữ hạng, ân hạn 30 ngày và hạ bậc | `System` | Hằng ngày, hệ thống quét các hội viên đến hạn xét hạng 12 tháng: nếu đủ chi tiêu thì gia hạn thêm 12 tháng; nếu thiếu chi tiêu thì mở **30 ngày ân hạn**. Khi hết 30 ngày ân hạn mà vẫn chưa đủ chi tiêu, hệ thống chỉ hạ tối đa **1 bậc liền kề** (ví dụ `Gold` xuống `Silver`, không rơi thẳng về `Member`). |
| **UC-22** | Tự động tặng Voucher sinh nhật & hết hạn điểm | `System` | Hằng ngày, hệ thống tự động phát hành Voucher giảm hóa đơn mừng sinh nhật (tối đa 1 lần/năm cho các hạng có giá trị quà sinh nhật $> 0$), chuyển trạng thái các Voucher quá hạn sang `Expired`, và thu hồi điểm thưởng (`Expire`) đối với hồ sơ không phát sinh giao dịch trên 180 ngày. |
| **UC-23** | Xem thống kê hội viên và biểu đồ CRM | `Admin` | Admin theo dõi tổng số hội viên, hội viên mới trong tháng, tỷ lệ khách quay lại 30 ngày, tổng điểm đang lưu hành, biểu đồ phân bổ 4 nhóm khách RFM (`VIP`, `Thân thiết`, `Khách mới`, `Lâu rồi chưa ghé`), bảng Top 10 khách chi tiêu cao nhất và danh sách khách quen vắng mặt trên 30 ngày. |
| **UC-24** | Tạo và phát hành chiến dịch gửi ưu đãi | `Admin` | Admin thiết lập chiến dịch chăm sóc (Gửi theo đợt, Mời khách quay lại, Mừng sinh nhật) nhắm tới nhóm khách RFM hoặc hạng thẻ cụ thể, xem trước danh sách người nhận đủ điều kiện và bấm phát hành để tặng hàng loạt Voucher/Điểm thưởng (đảm bảo không gửi trùng 2 lần cho cùng một hội viên). |

---

### 1.4. Phân hệ Xác thực & Vận hành Nội bộ Quán Bida

| ID | Use Case | Actor | Use Case Description |
| :--- | :--- | :--- | :--- |
| **UC-25** | Đăng nhập và đăng xuất hệ thống nội bộ | `Staff`, `Admin` | Nhân viên và Quản trị viên đăng nhập bằng tên tài khoản/email và mật khẩu. Hệ thống kiểm tra trạng thái hoạt động, chặn mọi hồ sơ hội viên (`Customer`) đăng nhập, và điều hướng `Admin` về trang Tổng quan, `Staff` về trang Nhân viên. |
| **UC-26** | Quản lý hồ sơ cá nhân và đổi mật khẩu | `Staff`, `Admin` | Nhân viên và Admin xem thông tin tài khoản nội bộ của mình và thực hiện đổi mật khẩu bảo mật (xác thực mật khẩu hiện tại trước khi cấp mật khẩu mới). |
| **UC-27** | Mở và kết ca làm việc (Đối soát tiền mặt) | `Staff`, `Admin` | Nhân viên mở ca làm việc với số tiền mặt đầu ca (chặn mở 2 ca cùng lúc và bắt buộc có ca đang mở khi thu tiền mặt). Khi kết ca, hệ thống đối chiếu tiền mặt kỳ vọng (không tính giao dịch chuyển khoản/VNPay) với tiền thực đếm và bắt buộc nhập ghi chú giải trình nếu có chênh lệch. |
| **UC-28** | Xem báo cáo doanh thu và xuất CSV | `Admin` | Admin tra cứu báo cáo doanh thu theo khoảng ngày hoặc theo tháng (tính theo múi giờ Việt Nam UTC+7), thống kê số lượng hóa đơn, cơ cấu phương thức thanh toán và xuất báo cáo ra file CSV. |
| **UC-29** | Quản lý nhân viên và bảng giá giờ chơi | `Admin` | Admin quản lý danh sách tài khoản nhân viên (thêm mới, khóa/mở khóa, đặt lại mật khẩu) và thiết lập bảng giá tiền giờ chơi theo từng loại bàn và khung giờ trong ngày (tự động kiểm tra chống chồng chéo khung giờ). |

