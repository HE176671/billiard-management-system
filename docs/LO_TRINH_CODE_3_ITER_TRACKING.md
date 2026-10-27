# Lộ trình code BMS theo tracking — 3 iteration

Cập nhật: 20/10/2026. Phân công theo workflow đã thống nhất: TV1 đặt và vận hành bàn; TV2 thành viên/chăm sóc khách; TV3 checkout/thanh toán; TV4 F&B/combo/tồn; TV5 tài khoản/nhân sự/ca/Dashboard/báo cáo.

Nguồn đối chiếu: `outputs/01a130dd-b7dc-7ed3-b15c-8c57c62e6b61/BMS_TRACKING_NHOM_5_THANH_VIEN.xlsx` và bản inspect đi kèm. Giữ mã BMS-001–068 để truy vết; thay chủ theo bảng bên dưới. Workbook gốc chưa sửa. BMS-069–074 là mã bổ sung đề xuất cho các việc chưa có dòng riêng. Các mã phụ `.1`, `.2`, `.3` tách phần bàn giao theo iteration, không tính thêm công việc ở cả dòng cha và dòng con.

## 1. Khung thời gian và mục tiêu

Đề xuất 6 tuần, mỗi iteration 2 tuần. Chưa có deadline môn học nên dùng tuần tương đối; khi chốt ngày bắt đầu thì điền hạn thật vào tracking. Nếu mỗi iteration dài khác, giữ thứ tự và điều kiện nghiệm thu, điều chỉnh thời lượng theo năng lực nhóm.

| Iteration | Thời gian | Kết quả phải demo |
|---|---|---|
| Iter 1 | Tuần 1–2 | Đặt bàn → check-in → mở phiên → gọi món → đóng phiên → hóa đơn → tiền mặt → trả bàn; có đăng nhập/phân quyền thật |
| Iter 2 | Tuần 3–4 | Mua combo + món thêm + giờ vượt → ưu đãi → VNPay sandbox → trả bàn → điểm/hạng/lịch sử; có mở/đóng ca và báo cáo cơ bản |
| Iter 3 | Tuần 5–6 | Hoàn thiện đánh giá/báo cáo/đối soát, xử lý đồng thời và lỗi, chạy hồi quy, SDS và bàn giao |

Mỗi iteration có chức năng chạy từ UI đến SQL và demo tích hợp. Không dồn UI vào Iter 1, backend vào Iter 2 và toàn bộ kiểm thử vào Iter 3.

## 2. Chốt trước khi chia nhánh — đầu Iter 1

TV5 điều phối, chủ nghiệp vụ tự quyết định phần của mình. Dành khoảng một buổi để chốt, không chờ hoàn thiện toàn bộ thiết kế mới code.

| Chủ trì | Việc phải chốt | Đầu ra dùng chung |
|---|---|---|
| TV5 | Cách chạy, tài khoản/role thử, schema đang dùng, quy trình nâng cấp SQL | Chạy được project trên 5 máy; không chạy lại Full/seed lên DB có dữ liệu |
| TV1 | Trạng thái booking/bàn/phiên, giữ bàn và xung đột thời gian, giá và cách chốt giờ | Bảng chuyển trạng thái + mẫu dữ liệu phiên; định nghĩa booking hợp lệ |
| TV3 + TV1 | Ai tạo hóa đơn khi đóng phiên | Một điểm tạo duy nhất; xử lý `usp_CloseSession` đang tạo hóa đơn để không tạo trùng |
| TV3 + TV2 + TV4 | Giờ combo/giờ vượt, món tính riêng, cơ sở giảm giá, làm tròn, điểm | Ví dụ số có kết quả kỳ vọng và hợp đồng dữ liệu tính tiền |
| TV3 + TV5 | Cách gắn giao dịch với ca và múi giờ báo cáo | Quyết định schema nếu cần `WorkShiftId`; script nâng cấp trước khi tích hợp ca |
| Cả nhóm | Service/DTO và người sở hữu; file chung | Hợp đồng phiên, khoản tiền, ưu đãi, thanh toán đã xác nhận; mỗi người dùng controller/service riêng |

Không xem tên service/DTO trong kế hoạch là API đã có. Mỗi hợp đồng ghi trường bắt buộc, kiểu dữ liệu, trạng thái hợp lệ, lỗi và khóa chống xử lý lặp.

## 3. Iter 1 — Luồng cơ bản chạy thật

Tuần 1 làm màn hình/backend/SQL riêng theo hợp đồng. Trong tuần 1 phải có điểm nối nhỏ đầu tiên; tuần 2 ghép luồng và sửa lỗi. Món/phiên mẫu chỉ dùng để phát triển trước khi bên cung cấp xong, không thay dữ liệu tích hợp khi nghiệm thu.

| Người | Mã tracking | Việc triển khai | Điều kiện hoàn thành |
|---|---|---|---|
| TV1 | BMS-001, 002, 003, 008, 052 | Tiếp nhận CRUD loại bàn/bàn/bảng giá; hiển thị trạng thái; áp giá khi mở phiên | CRUD và validation chạy SQL thật; chọn đúng giá và lưu snapshot cần thiết |
| TV1 | BMS-011, 012, 013, 014, 015, 016, 018 | Đặt/xem/hủy booking, danh sách Staff, check-in rồi mở đúng phiên | Customer chỉ xem/hủy booking của mình; kiểm tra trùng lịch và trạng thái ở backend |
| TV1 | BMS-004, 005, 006 | Mở phiên booking/vãng lai, theo dõi giờ, đóng phiên cơ bản | Không hai phiên active/bàn; đóng lặp không tạo hóa đơn lần hai; bàn chờ thanh toán |
| TV2 | BMS-051, 055 | Tiếp nhận CRUD hạng/ngưỡng; chốt tỷ lệ điểm, cơ sở tính và quy tắc ưu đãi | Cấu hình hợp lệ được lưu; có ví dụ số và kết quả thử để TV3 dùng |
| TV2 | BMS-070.1 | Làm phần cung cấp ưu đãi cho checkout | Đầu vào CustomerId và khoản tiền theo hợp đồng; trả kết quả giảm giá; khách không có hạng vẫn xử lý được |
| TV3 | BMS-021, 022, 038, 029 | Checkout cơ bản: giờ + món; hóa đơn/chi tiết; tiền mặt; yêu cầu TV1 trả bàn | Tổng khớp dữ liệu nguồn; một điểm tạo hóa đơn; chỉ Paid mới trả bàn; chưa tích điểm không chặn nghiệm thu Iter 1 |
| TV4 | BMS-031, 032, 033, 034 | Danh mục/món/tồn, gọi món theo phiên, cập nhật trạng thái phục vụ | Giá lúc gọi được lưu; không bán món ngừng bán/thiếu tồn; khoản món thật sang TV3 |
| TV5 | BMS-041, 042, 043, 044, 045, 046, 050, 054 | Kiểm tra/tái sử dụng tài khoản và quản trị hiện có; bổ sung phần còn thiếu; cấp nền phân quyền cho cả nhóm | 5 máy chạy được; truy cập sai role bị chặn ở server; đăng ký gán Customer, tạo nhân viên gán Staff |
| TV5 | BMS-049.1 | Tiếp nhận Dashboard hiện có và xác định nguồn chỉ số | Các chỉ số cơ bản đọc SQL thật; ghi rõ chỉ số nào chưa nối dữ liệu thanh toán |
| Cả nhóm | BMS-059.1–068.1 | Mỗi người cập nhật thiết kế phần vừa làm, PR nhỏ và kịch bản demo Iter 1 | Có kết quả build/test phù hợp và link bằng chứng; không chờ Iter 3 mới viết SDS |

**Gate G1:** đăng nhập Customer → đặt bàn → Staff check-in/mở phiên → gọi một món → đóng phiên → thanh toán tiền mặt → bàn Available. Đối chiếu được booking, phiên, order, hóa đơn và giao dịch trên SQL. Thử thêm khách vãng lai và truy cập dữ liệu booking của người khác. Các khoản tiền sau khi chốt phải được bảo vệ theo quy tắc đã thống nhất.

Giữ G1 nhỏ: chưa bắt buộc combo, VNPay hay điểm tự động. Tuy vậy Iter 1 vẫn phải có kiểm tra quyền, dữ liệu đầu vào và chống thao tác lặp cơ bản.

## 4. Iter 2 — Nghiệp vụ đầy đủ và tích hợp

| Người | Mã tracking | Việc triển khai | Điều kiện hoàn thành |
|---|---|---|---|
| TV1 | BMS-009, 007, 073 | Tính giờ combo/giờ vượt, tra cứu phiên cho Staff, xử lý booking no-show | Không tính hai lần giờ thuộc combo; quyền và chuyển trạng thái đúng |
| TV2 | BMS-056, 057, 070.2, 071 | Nhận thanh toán thành công để cộng điểm/nâng hạng; hiển thị quyền lợi | Một hóa đơn/giao dịch không cộng điểm lặp; chọn đúng hạng theo ngưỡng; nếu cần lịch sử điểm thì có schema/script phù hợp |
| TV2 | BMS-027, 074 | Lịch sử thanh toán và lịch sử chơi cho Customer | Chỉ dữ liệu của khách đang đăng nhập; xem chi tiết được; không cho đổi ID để đọc khách khác |
| TV3 | BMS-023, 024, 025, 028, 039 | Tạo thanh toán VNPay sandbox; xác thực Return/IPN; chống lặp; dùng ưu đãi TV2; phát kết quả xác nhận | Kiểm tra chữ ký, mã tham chiếu, số tiền, kết quả; không dùng riêng redirect để đánh dấu Paid; không thu vượt hóa đơn |
| TV3 | BMS-026.1 | Chốt hỗ trợ trả nhiều phần hay chỉ trả đủ một lần; cung cấp truy vấn đối soát | Ghi rõ phạm vi; không tự thêm trả nhiều phần nếu nhóm chưa chọn; tổng ghi nhận khớp hóa đơn |
| TV4 | BMS-035, 036, 037, 040.1 | Hủy/hoàn tồn; cấu hình/mua combo và snapshot thành phần | Không trừ/hoàn tồn lặp; đổi giá/thành phần danh mục không sửa lượt mua cũ; bàn giao giờ cho TV1 và tiền cho TV3 |
| TV5 | BMS-017.1, 049.2, 053.1 | Mở/đóng ca, gắn giao dịch theo hợp đồng TV3; nối Dashboard và báo cáo doanh thu cơ bản | Tiền mặt theo đúng ca; VNPay tách riêng; chỉ tổng hợp khoản đã ghi nhận, lọc ngày theo UTC+7 |
| TV5 | BMS-047, 048 | Hoàn thiện khóa/mở, xử lý xóa có tham chiếu và reset mật khẩu | Đúng role đối tượng; khóa chặn truy cập theo cơ chế chung; mật khẩu mới sai không làm mất mật khẩu cũ |
| Cả nhóm | BMS-059.2–068.2 | SDS, PR và demo phần tăng thêm Iter 2 | Có bằng chứng tích hợp, cập nhật tài liệu và lỗi còn lại |

**Gate G2:** đặt/check-in/mở bàn → mua combo có giờ + món → gọi thêm món → chơi vượt giờ → áp ưu đãi → VNPay sandbox xác nhận → trả bàn → cộng điểm/nâng hạng → khách xem lịch sử. Dashboard/báo cáo phản ánh giao dịch, ca không cộng VNPay vào tiền mặt. Chạy thêm một hóa đơn tiền mặt trong ca để đối soát thực tế.

Gửi thông báo thanh toán lặp tại G2: số giao dịch, điểm và trạng thái bàn phải giữ đúng. Kiểm tra thất bại/sai chữ ký/sai số tiền ngay khi làm VNPay; Iter 3 chỉ mở rộng và chạy hồi quy.

## 5. Iter 3 — Hoàn thiện, kiểm tra lỗi và bàn giao

| Người | Mã tracking | Việc triển khai | Điều kiện hoàn thành |
|---|---|---|---|
| TV1 | BMS-010, 019 | Kiểm tra đặt/mở/đóng đồng thời, quyền booking, trả bàn với phiên cũ | Hai request không gây trùng booking/phiên hoặc ghi đè bàn; lỗi rõ; sửa các lỗi tìm được |
| TV1 + TV5 | BMS-020.1, 020.2 | Tách tài liệu cũ đang gộp đặt bàn và ca | TV1 viết phần booking; TV5 viết ca; không để tài liệu gán booking lại cho TV2 |
| TV2 | BMS-069, 072 | Tạo/xem/quản lý đánh giá theo quyền; kiểm tra ngưỡng điểm, nâng hạng và ownership | Chốt điều kiện đánh giá, ví dụ sau hóa đơn Paid; khách chỉ đánh giá phiên đủ điều kiện của mình; giới hạn số lần theo quy tắc đã chọn |
| TV2 | BMS-058 | Chốt cách xử lý khi đổi chính sách hạng và triển khai phần được chọn | Ghi rõ có/không tính lại hạng cũ; tính lại hàng loạt là tùy chọn, không mặc định mở rộng phạm vi |
| TV3 | BMS-026.2, 030 | Hoàn thiện tra cứu/đối soát và kiểm tra lỗi thanh toán, gửi lại, timeout, mất phiên trình duyệt | Thông báo lặp/đến muộn không thu hay cộng điểm lặp; hóa đơn chưa trả đủ không Paid; tích điểm lỗi có cách thử lại không nhân đôi |
| TV4 | BMS-040.2 | Kiểm tra hết tồn, mua đồng thời, hủy lặp, combo + món thêm + giờ vượt | Không âm tồn, không nhân đôi trừ/hoàn; đối chiếu từng khoản với hóa đơn; sửa lỗi |
| TV5 | BMS-017.2, 049.3, 053.2 | Hoàn thiện đối soát đóng ca, Dashboard, lọc báo cáo và CSV | Tiền mặt đầu/thu/cuối ca theo quy tắc; dữ liệu ngoài khoảng ngày và Unpaid không làm sai doanh thu; CSV khớp màn hình |
| TV5 + cả nhóm | BMS-054 (hồi quy) | Kiểm tra quyền truy cập trực tiếp, cookie bị khóa, CSRF và kiểm tra chạy trên máy khác | Mỗi chủ workflow sửa lỗi quyền trong endpoint của mình; TV5 duy trì cơ chế chung, không gánh toàn bộ test thay nhóm |
| Cả nhóm | BMS-059.3–068.3 | Chốt SRS/SDS, build, kiểm tra hồi quy, PR cuối, dữ liệu demo và hướng dẫn cài/chạy | Mỗi người có bằng chứng phần mình; máy khác chạy được bằng hướng dẫn; không đưa secret vào Git |

**Gate G3:** chạy lại G1/G2 trên bản ghép cuối, thêm đánh giá, lọc/xuất báo cáo, đối soát ca và các ca lỗi ở bảng trên. Không còn lỗi nghiêm trọng làm sai tiền, tồn, trạng thái hoặc quyền. Mỗi lỗi còn lại phải có mô tả, người nhận và quyết định xử lý trước bàn giao.

## 6. Đổi người phụ trách trong tracking cũ

| Mã | Chủ mới | Ghi chú |
|---|---|---|
| BMS-001–010 | TV1 | Giữ; BMS-007 là tra cứu phiên Staff, lịch sử cá nhân của Customer do TV2 làm |
| BMS-011–016, 018–019 | TV1 | Booking/check-in/mở phiên cùng một workflow |
| BMS-017 | TV5 | Ca làm; tách .1 mở/đóng cơ bản, .2 đối soát hoàn chỉnh |
| BMS-020 | TV1 + TV5 | Tách .1 tài liệu booking của TV1 và .2 tài liệu ca của TV5 |
| BMS-021–026, 028–030 | TV3 | BMS-029: TV3 phát xác nhận; TV1 sở hữu xử lý cập nhật bàn |
| BMS-027 | TV2 | Lịch sử thanh toán khách; TV3 cung cấp dữ liệu |
| BMS-031–037, 040 | TV4 | F&B/tồn/combo; BMS-040 kiểm tra tích hợp cùng TV1/TV3 |
| BMS-038–039 | TV3 | TV3 sở hữu checkout/áp giảm; TV2 cung cấp quy tắc ưu đãi |
| BMS-041–050 | TV5 | Tài khoản/nhân sự/khách hàng/Dashboard |
| BMS-051 | TV2 | CRUD hạng/ngưỡng |
| BMS-052 | TV1 | Bảng giá |
| BMS-053–054 | TV5 | Báo cáo/CSV và nền phân quyền |
| BMS-055–058 | TV2 | Chính sách điểm/cộng điểm/nâng hạng; các chính sách chưa chốt phải có quyết định trước code |
| BMS-059–060 | TV1 | SRS/SDS; PR/test/demo |
| BMS-061–062 | TV2 | SRS/SDS; PR/test/demo |
| BMS-063–064 | TV3 | SRS/SDS; PR/test/demo |
| BMS-065–066 | TV4 | SRS/SDS; PR/test/demo |
| BMS-067–068 | TV5 | SRS/SDS; PR/test/demo |
| BMS-069 | TV2 | Bổ sung: đánh giá dịch vụ |
| BMS-070 | TV2 | Bổ sung: cung cấp ưu đãi cho checkout (.1 hợp đồng/logic, .2 tích hợp thật) |
| BMS-071 | TV2 | Bổ sung: xem quyền lợi/điểm/hạng cá nhân |
| BMS-072 | TV2 | Bổ sung: kiểm tra điểm/hạng/ownership/đánh giá |
| BMS-073 | TV1 | Bổ sung: no-show booking |
| BMS-074 | TV2 | Bổ sung: lịch sử chơi Customer; tách khỏi tra cứu phiên Staff |

Mọi mã BMS-001–068 đều được giữ trong lộ trình, kể cả việc cần chốt phạm vi. Không coi số dòng task là số đo khối lượng: TV1 có luồng nhiều bước, TV3 có xử lý tiền và callback phức tạp, TV5 có nền chung và báo cáo. Cuối Iter 1 dùng thời gian thực tế để điều chỉnh độ chi tiết/phạm vi tùy chọn, giữ một chủ sở hữu cho mỗi nghiệp vụ.

## 7. Cách cập nhật tracking hằng ngày

Giữ các cột cũ và bổ sung: **Iteration, Phụ thuộc mã task, Hạn có dữ liệu thật, Gate nghiệm thu**. Tách cột Phối hợp khỏi Phụ thuộc: một người phối hợp không có nghĩa phải đợi toàn bộ module của người đó.

| Trạng thái | Khi dùng |
|---|---|
| Chưa cập nhật | Chưa xác minh tình trạng thực tế |
| Todo | Đã nhận, chưa bắt đầu |
| Doing | Đang code/kiểm tra |
| Chờ tích hợp | Phần riêng chạy được, đang dùng dữ liệu mẫu hoặc chưa ghép điểm nối |
| Blocked | Không còn phần hữu ích làm tiếp; ghi rõ thiếu task/dữ liệu/quyết định nào và người xử lý |
| Review/Test | Đã có PR/bản chạy để người khác review và nghiệm thu |
| Done | Đạt tiêu chí, kiểm tra phù hợp và có link bằng chứng; task tích hợp đã chạy dữ liệu thật |

Không tự đánh Done cho code hiện có. Kiểm tra lại theo tiêu chí của iteration; tái sử dụng phần đã đạt, chỉ làm phần thiếu. File tracking cũ đang để Chưa cập nhật nên kế hoạch này không gán tiến độ thực tế cho nhóm.

Ví dụ một dòng: `BMS-022 | TV3 | Iter 1 | Thu tiền mặt | phụ thuộc BMS-021 | Review/Test | link PR + kết quả thử | G1`. Trong lúc chưa có phiên thật, TV3 dùng fixture theo hợp đồng; trước G1 phải thay bằng phiên và món thật từ TV1/TV4.

Mỗi task chia nhỏ thành phần khoảng 0,5–2 ngày khi bắt đầu iteration. Các dòng gộp nhiều mã trong kế hoạch là nhóm công việc, khi nhập tracking vẫn giữ từng mã riêng. Hạn và ước lượng do người nhận xác nhận, không điền ngày giả.

SDS/PR/test/demo BMS-059–068 tách thành ba mốc .1/.2/.3 cho từng iteration. Dòng cha chỉ làm nhóm; thống kê tiến độ từ dòng con để không đếm trùng.

## 8. Cách code song song mà vẫn ghép được

| Điểm nối | Người cung cấp | Người dùng | Hạn có dữ liệu thật |
|---|---|---|---|
| Identity/role/tài khoản thử | TV5 | Tất cả | Đầu Iter 1 |
| Phiên active và phiên đóng, tiền giờ | TV1 | TV4, TV3 | Trong tuần 1; bắt buộc trước G1 |
| Khoản món và snapshot giá | TV4 | TV3 | Trong tuần 1; bắt buộc trước G1 |
| Tiền mặt xác nhận → trả bàn | TV3 | TV1 | Tuần 2, trước G1 |
| Quy tắc và kết quả ưu đãi | TV2 | TV3 | Logic/hợp đồng trong Iter 1, tích hợp thật đầu Iter 2 |
| Combo, giờ combo và tiền combo | TV4 | TV1, TV3 | Tuần 3, trước G2 |
| Thanh toán xác nhận và khóa chống lặp | TV3 | TV2, TV1, TV5 | Tuần 3–4, trước G2 |
| Giao dịch gắn ca/dữ liệu báo cáo | TV3 + TV5 | TV5 | Iter 2, hoàn thiện đối soát ở Iter 3 |

Khi chưa đến hạn bàn giao, dùng dữ liệu mẫu theo cùng hợp đồng để làm tiếp. Khi đến hạn mà thiếu dữ liệu thật, ghi đúng task bị chặn và ghép phần đã có; không đợi đến cuối Iter 3 mới phát hiện lỗi tích hợp. Tính năng thử nghiệm chỉ chạy với mock được đánh Chờ tích hợp, chưa được tính là Done của gate.
