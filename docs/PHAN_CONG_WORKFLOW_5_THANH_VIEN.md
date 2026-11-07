# Phân công 5 thành viên theo workflow BMS

Cập nhật: 20/10/2026. Thay cho phân công cũ trong tài liệu bàn giao và SDS. Giữ mã TV1–TV5 vì chưa có đủ tên; TV5 là Hùng. Đây là kế hoạch trách nhiệm, chưa xác nhận chức năng đã hoàn thành hay code đã chuyển giao.

## 1. Nguyên tắc chia việc

Chia theo luồng nghiệp vụ mà một người làm và demo từ đầu đến cuối. **Đặt bàn → check-in → mở bàn phải cùng workflow, cùng một người phụ trách.** Không chia theo vai trò Admin/Staff/Customer hoặc số lượng bảng.

Mỗi người làm đủ màn hình, kiểm tra dữ liệu/quyền, backend, dữ liệu, kiểm thử, SDS và demo. Module khác gọi qua service/hợp đồng đã thống nhất; không tự sửa trạng thái hoặc viết lại công thức của nhau.

## 2. Phân công chính

| Thành viên | Workflow | Luồng phải làm trọn | Dữ liệu phụ trách |
|---|---|---|---|
| **TV1** | **Đặt bàn và vận hành bàn** | Cấu hình loại bàn/bàn/giá → tìm bàn trống → đặt/xem/hủy booking → check-in hoặc no-show → mở phiên từ booking hoặc khách vãng lai → theo dõi giờ → đóng phiên, chốt tiền giờ → chờ thanh toán → trả bàn sau xác nhận đủ tiền | `TableTypes`, `BilliardTables`, `PricingConfigs`, `Bookings`, `PlaySessions` |
| **TV2** | **Thành viên và chăm sóc khách** | Cấu hình hạng/ngưỡng điểm/ưu đãi → nhận giao dịch đã trả → cộng điểm/nâng hạng → khách xem quyền lợi và lịch sử sử dụng/hóa đơn → đánh giá | `MembershipTiers`, `Reviews`; logic `RewardPoints`, `TierId` trên user; đọc dữ liệu phiên/hóa đơn/thanh toán |
| **TV3** | **Hóa đơn và thanh toán** | Nhận phiên đã đóng + món/combo → lập hóa đơn → lấy ưu đãi thành viên → thu tiền mặt/VNPay → xác nhận đủ tiền → cung cấp kết quả cho trả bàn, tích điểm và báo cáo → tra cứu/đối soát giao dịch | `Invoices`, `PaymentTransactions` |
| **TV4** | **Gọi món, combo và tồn kho** | Quản lý danh mục/món/tồn → cấu hình combo giờ + món → bán món/combo cho phiên → lưu giá/thành phần lúc bán → phục vụ/hủy/hoàn tồn → bàn giao khoản tính tiền và giờ combo | `ProductCategories`, `Products`, `Orders`, `OrderDetails`, `Combos`, `ComboItems`, `SessionCombos`, `SessionComboItems` |
| **TV5 — Hùng** | **Quản trị, nhân sự, ca làm và báo cáo** | Đăng ký → đăng nhập/đăng xuất → đổi mật khẩu/hồ sơ; Admin tạo/sửa/khóa/mở Staff/Customer → Staff mở ca → làm việc → đóng ca, đối soát tiền mặt → Dashboard/doanh thu/xuất CSV; duy trì Identity, phân quyền và nền chung | Cả 7 bảng `AspNet*`, `WorkShifts`; mapping user chung; đọc dữ liệu vận hành/thanh toán để báo cáo |

TV1 sở hữu cả đặt bàn của Customer và xử lý booking/mở bàn của Staff. TV2 nhận thành viên/chăm sóc khách. TV3 sở hữu toàn bộ checkout, tổng hợp tiền giờ/món/combo và áp dụng ưu đãi do TV2 cung cấp. TV5 nhận Dashboard/báo cáo để nối quản trị với đối soát ca.

**Tài khoản–thành viên:** TV5 quản lý danh tính, role, khóa tài khoản và mapping `ApplicationUser`; TV2 quản lý hạng, điểm và ưu đãi. Đăng ký dùng hạng ban đầu do TV2 cung cấp. TV3 cung cấp kết quả thanh toán, TV2 xử lý điểm/nâng hạng đúng một lần.

**Thanh toán–báo cáo–ca:** TV3 xác nhận giao dịch và cung cấp dữ liệu; TV5 tổng hợp doanh thu và đối soát ca. Không tính VNPay vào tiền mặt trong ca.

## 3. Workflow tổng thể

```text
TV5: tài khoản và quyền
  ↓
TV1: tìm bàn → đặt bàn → check-in → mở phiên → theo dõi → đóng phiên
                                      ↑         ↕             ↓
                          Khách vãng lai    TV4: món/combo   TV3: hóa đơn
                                                              ↕
                                                    TV2: ưu đãi thành viên
                                                              ↓
                                                     TV3: thanh toán
                                                       ↓      ↓
                                              TV1: trả bàn   TV2: điểm/hạng,
                                                             lịch sử/đánh giá

TV3: giao dịch xác nhận → TV5: Dashboard, doanh thu/CSV, đóng ca/đối soát
```

TV1 tự xử lý từ đặt đến mở bàn, nhưng check-in vẫn là bước trạng thái riêng, không mặc định tạo phiên. Đóng phiên không đồng nghĩa đã thanh toán: bàn chuyển `AwaitingPayment`; chỉ trả bàn sau khi TV3 xác nhận đủ tiền. TV1 kiểm tra lại phiên/bàn khi trả để không ghi đè trạng thái mới.

## 4. Hợp đồng giữa các workflow

Đây là đầu ra cần thống nhất khi triển khai, chưa phải tên API/service đã có.

| Bên giao → bên nhận | Nội dung giao | Điều kiện nghiệm thu |
|---|---|---|
| TV5 → cả nhóm | UserId, role, trạng thái tài khoản, Identity chung | Backend lấy người thao tác từ phiên đăng nhập; tài khoản bị khóa mất quyền theo cơ chế chung |
| Nội bộ TV1 | Booking hợp lệ → check-in → mở phiên; hoặc khách vãng lai → mở phiên | Không hai phiên cho một booking hoặc hai phiên active trên một bàn; xử lý xung đột đặt/mở cùng bàn |
| TV4 → TV1 | Giờ combo, số lượng và snapshot lượt mua | Thống nhất giờ thuộc combo/giờ vượt; không tính toàn bộ giờ thường rồi cộng nguyên giá combo |
| TV1 + TV4 → TV3 | Phiên đóng, tiền giờ, món riêng, combo và snapshot | Mỗi khoản chỉ tính một lần; món trong combo không cộng như món thêm; giá mới không sửa lịch sử |
| TV2 → TV3 | Quy tắc ưu đãi và kết quả áp dụng | Chốt cơ sở giảm giá, thời điểm xác định hạng; lưu giá trị áp dụng trên hóa đơn |
| TV3 → TV1 | InvoiceId, SessionId, xác nhận đủ tiền | Yêu cầu lặp không trả bàn hai lần; kiểm tra đúng phiên/bàn |
| TV3 → TV2 | Giao dịch xác nhận, khách, hóa đơn, số tiền, thời điểm | Cộng điểm/nâng hạng một lần kể cả callback lặp |
| TV1 + TV3 → TV2 | Dữ liệu phiên và hóa đơn thuộc khách | Backend kiểm tra ownership; chốt đánh giá sau đóng phiên hay sau trả đủ |
| TV3 → TV5 | Giao dịch xác nhận, phương thức, thời điểm và tiền mặt theo ca | Báo cáo dùng tiền thực ghi nhận; ca không tính VNPay; chốt cách gắn giao dịch với ca vì `PaymentTransactions` chưa có `WorkShiftId` |

TV3 chủ trì chốt tổng tiền với TV1/TV4; TV2 chốt ưu đãi, tỷ lệ điểm và thời điểm nâng hạng. Ghi quy tắc và ví dụ số trước khi triển khai để không có nhiều công thức khác nhau.

## 5. Tiếp nhận code hiện có

Tái sử dụng code đang có. Đường dẫn dưới đây tính từ `src/Bms.Web/`.

| Chủ mới | Phần tiếp nhận | Việc tiếp theo |
|---|---|---|
| TV1 | `Controllers/PricingController.cs`, Views/Models Pricing; entity bàn/loại bàn/giá; schema/procedure booking và phiên | Hoàn thiện đặt/check-in/mở/đóng/trả bàn cho Customer và Staff |
| TV2 | `Controllers/MemberTierController.cs`, Views/Models/entity tương ứng; schema Reviews | Nối hạng/điểm/ưu đãi với thanh toán; bổ sung lịch sử/đánh giá |
| TV3 | Schema hóa đơn/thanh toán và điểm tạo hóa đơn trong procedure đóng phiên | Thống nhất điểm tạo hóa đơn duy nhất; làm tiền mặt/VNPay, xác nhận và chống xử lý lặp |
| TV4 | Schema món/order/combo; `docs/COMBO_DESIGN.md` | Làm bán/phục vụ/hủy/tồn; cung cấp khoản tiền và giờ combo |
| TV5 — Hùng | `AccountController`, `ProfileController`, `EmployeeController`, quản trị tài khoản trong `CustomerController`; `DashboardController`, `RevenueController` và Views/Models; `Program.cs`, Identity/initializer/mapping chung | Hoàn thiện ca, tài khoản, phân quyền, Dashboard/báo cáo/CSV và cấu hình nền |

`CustomerController` hiện chứa quản trị tài khoản và Portal: TV5 giữ tài khoản; TV1 làm booking bằng controller/view riêng; TV2 làm lịch sử/đánh giá bằng controller/view riêng, liên kết từ Portal. `StaffController` là khung chung: dùng controller riêng cho từng workflow, giữ/cập nhật liên kết điều hướng khi tách.

TV1 phụ trách cả `usp_CreateBooking`, `usp_CancelBooking`, `usp_CheckInBooking`, `usp_OpenSession`, `usp_CloseSession`. Hiện `usp_CloseSession` còn tạo hóa đơn: TV1 và TV3 phải thống nhất điểm tạo hóa đơn duy nhất, tránh procedure và service cùng tạo. Thay đổi phải có script nâng cấp và kịch bản kiểm tra.

## 6. Thứ tự triển khai và ghép

1. **Chốt nền/hợp đồng:** TV5 bàn giao cách chạy, role/quyền, Identity. Cả nhóm chốt trạng thái, đầu vào/đầu ra và quy tắc tiền. TV5 tích hợp thay đổi `Program.cs`, `ApplicationDbContext`, layout từ chủ workflow.
2. **Làm từng workflow:** TV1 đặt và vận hành bàn; TV2 thành viên/chăm sóc khách; TV3 checkout/hóa đơn/thanh toán; TV4 món/combo/tồn; TV5 tài khoản/nhân sự/ca/báo cáo. Dùng dữ liệu mẫu theo hợp đồng khi điểm nối chưa xong, ghi rõ chưa tích hợp.
3. **Demo workflow TV1:** đặt → check-in → mở đúng một phiên; thêm khách vãng lai, hủy, no-show và xung đột cùng bàn. Đây là nghiệm thu nội bộ một workflow.
4. **Ghép phiên → món/combo → hóa đơn:** TV1 + TV4 + TV3 đối chiếu tiền giờ/combo/món/tồn; TV2 cung cấp ưu đãi. Chặn thay đổi khoản tiền sau chốt hóa đơn theo quy tắc chung.
5. **Ghép thanh toán → trả bàn/điểm/báo cáo/ca:** TV3 xác nhận; TV1 trả bàn; TV2 cập nhật điểm/lịch sử; TV5 tổng hợp báo cáo và đối soát ca. Kiểm tra callback lặp và quyền xem.
6. **Demo chung và SDS:** từng người demo workflow và điểm nối; TV5 tổng hợp kiến trúc/package, chủ workflow chịu trách nhiệm SDS của mình.

Nhánh đề xuất: `codex/tv1-booking-tables-sessions`, `codex/tv2-loyalty-reviews`, `codex/tv3-billing-payments`, `codex/tv4-fnb-combos`, `codex/tv5-admin-shifts-reports`. Chưa tạo nhánh. PR theo chức năng chạy được, đủ UI/backend và kiểm tra; người liên quan review điểm nối. Không cùng sửa controller chung hoặc cùng chạy lại Full SQL.

Lộ trình triển khai và đối chiếu mã task cũ: [Tracking theo 3 iteration](LO_TRINH_CODE_3_ITER_TRACKING.md).

## 7. Điều kiện hoàn thành

Mỗi workflow đọc/ghi SQL thật, kiểm tra quyền/đầu vào ở backend, giữ dữ liệu sau tải lại, xử lý lỗi rõ và SDS khớp code. Build qua, kiểm tra chức năng/điểm nối có kết quả. Bảng SQL, UI tĩnh hoặc dữ liệu demo chưa đủ để đánh dấu hoàn thành.

Kịch bản nghiệm thu chung:

1. Customer đăng nhập → đặt bàn → Staff check-in → mở đúng một phiên, cùng phần phụ trách TV1.
2. Khách vãng lai → mở phiên → gọi món → đóng phiên → tiền mặt → trả bàn.
3. Combo giờ + món, gọi thêm, chơi vượt giờ → hóa đơn không trùng khoản; tồn đúng quy tắc.
4. VNPay thành công và callback lặp → ghi nhận tiền, trả bàn, cộng điểm/nâng hạng đúng một lần; thất bại chưa thành Paid.
5. Khách chỉ xem lịch sử/đánh giá của mình; ưu đãi đúng hạng; báo cáo đúng tiền ghi nhận; ca chỉ đối soát tiền mặt đúng phạm vi.
6. Hai yêu cầu cùng đặt/mở bàn; gửi lại thanh toán; tài khoản bị khóa; sửa ID xem dữ liệu khách khác → không sai dữ liệu/quyền.

Trọng tâm: **TV1 đặt và vận hành bàn; TV2 thành viên/chăm sóc khách; TV3 checkout/hóa đơn/thanh toán; TV4 món/combo/tồn; TV5 tài khoản/nhân sự/ca/báo cáo.**
