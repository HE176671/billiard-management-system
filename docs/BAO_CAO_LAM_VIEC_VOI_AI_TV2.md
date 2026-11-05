# Báo Cáo Đánh Giá Quá Trình Làm Việc Với AI — Phân Hệ TV2 (CRM & Loyalty)

**Ngày thực hiện:** 30/10/2026  
**Dự án:** Billiard Management System (BMS) — Phân hệ TV2 (Quản lý Hội viên & Chăm sóc Khách hàng thân thiết)  
**Mục tiêu phiên làm việc:** Rà soát thiết kế, hoàn thiện mã nguồn & cơ sở dữ liệu, kiểm thử tự động (Smoke & HTTP Tests), chạy demo thực tế, xử lý lỗi hiển thị tiếng Việt và tinh chỉnh lại UI/UX để loại bỏ cảm giác "AI hóa".

---

## 1. Tổng quan cách làm việc và chiến lược Prompt (Prompting Strategy)

Trong phiên làm việc hôm nay, cách giao tiếp với AI đi theo **chu trình lặp kiểm chứng (Iterative Verification Loop)**: *Yêu cầu phân tích → Cho thực thi → Chạy kiểm thử/Demo thực tế → Phản hồi bằng hình ảnh trực quan → Tinh chỉnh sản phẩm & Đúc kết tài liệu*.

| Giai đoạn | Câu lệnh Prompt tiêu biểu của bạn | Mục đích & Hiệu quả |
| :--- | :--- | :--- |
| **1. Rà soát & Khởi động** | *"kiểm tra lại toàn bộ những gì đã phân tích"* / *"tiếp tục đi"* | Yêu cầu AI đối chiếu lại toàn bộ hiện trạng code với tài liệu thiết kế trước khi viết tiếp, tránh việc AI quên ngữ cảnh hoặc làm sót yêu cầu. |
| **2. Thực thi & Tự kiểm thử** | *"chạy và xử lý toàn bộ vấn đề"* | Giao quyền cho AI tự chạy build, chạy bộ test tích hợp (`Bms.Smoke`) và HTTP test (`http-smoke.ps1`), tự bắt lỗi và sửa cho đến khi đạt 100% PASS. |
| **3. Kiểm tra thực tế (Human-in-the-loop)** | *"chạy demo giúp tôi"* → Chụp màn hình thực tế gửi kèm: *"hãy chẩn đoán lại toàn bộ phông chữ"* | Không chỉ tin vào kết quả test PASS trên terminal mà trực tiếp mở trình duyệt kiểm tra, dùng **hình ảnh chụp màn hình (Multimodal Prompt)** để chỉ ra lỗi hiển thị thực tế. |
| **4. Tinh chỉnh thẩm mỹ & Nghiệp vụ thực tế** | *"hãy tinh chỉnh lại toàn bộ UI/UX những gì chúng ta đã làm ở trên sao cho nó đừng bị AI hóa quá"* <br/> *"bỏ qua giao diện và chức năng của mấy cái run all test hoặc demo test ở trong dự án đi vì nhìn nó rất là AI"* | Nhận diện rất nhạy bén điểm yếu lớn nhất của code do AI sinh ra (nhiều thuật ngữ kỹ thuật, bố cục chia đôi thừa thãi, các nút "giả lập test" phô bày ngay trên giao diện người dùng). |
| **5. Hệ thống hóa kiến thức** | *"Viết cho tôi kịch bản để hiểu được luồng nghiệp vụ của phần này và nếu có thể bạn tạo cho tôi hình ảnh trực quan hóa giúp tôi dễ hiểu"* | Chủ động yêu cầu AI giải thích lại toàn bộ luồng nghiệp vụ bằng sơ đồ trực quan để nắm chắc logic hệ thống trước khi bảo vệ/báo cáo. |

---

## 2. Những vấn đề thực tế đã gặp phải & Cách giải quyết

Trong quá trình làm việc với AI hôm nay, có **4 nhóm vấn đề thực tế** đã bộc lộ và được xử lý triệt để:

### Vấn đề 1: Lỗi kỹ thuật & Logic biên trong code do AI viết
- **Hiện tượng:**
  - Lỗi cú pháp/API của .NET: Gọi `ToHashSetAsync()` (không có sẵn trong EF Core bản hiện tại) và dùng `stackalloc` bên trong hàm `async`.
  - Lỗi xung đột Transaction khi chạy kiểm thử tích hợp (`PointAdjustmentController` mở transaction mới khi bên ngoài đã có ambient transaction).
  - Lỗi lệch mili-giây (`DateTime.UtcNow`) khi tính tổng chi tiêu 12 tháng khiến giao dịch vừa tạo trong cùng mili-giây không được tính ngay vào điều kiện lên hạng.
- **Cách xử lý:** Yêu cầu AI chạy trực tiếp bộ test tích hợp 54 kịch bản (`Bms.Smoke`) và 72 kịch bản HTTP (`http-smoke.ps1`) trên SQL Server thật để phát hiện và sửa dứt điểm từng lỗi.

### Vấn đề 2: Lỗi vỡ phông chữ tiếng Việt (Mojibake) dù bài test tự động vẫn báo PASS
- **Hiện tượng:** Toàn bộ test tự động đều báo `PASS`, nhưng khi mở trang `/CustomerManagement` trên trình duyệt, tên khách hàng bị lỗi phông chữ (`Ä á»— HÃ¹ng DÅ©ng`, `BÃ¹i VÄ© HÃ o`).
- **Nguyên nhân gốc rễ:** Công cụ `sqlcmd` trên Windows khi chạy file `.sql` không có **UTF-8 BOM** và không truyền cờ `-f 65001` đã đọc chuỗi tiếng Việt theo bảng mã ANSI (Windows-1252) rồi ghi thẳng chuỗi lỗi vào SQL Server. Ngoài ra, một số thông báo lỗi trong code C# bị viết không dấu.
- **Cách xử lý:** Chụp ảnh màn hình gửi cho AI chẩn đoán; bổ sung UTF-8 BOM + cờ `-f 65001` cho các file SQL, viết câu lệnh `UPDATE` khôi phục lại toàn bộ dữ liệu tiếng Việt có dấu trong DB và trong các ViewModel/Controller.

### Vấn đề 3: Giao diện và dữ liệu mẫu bị "AI hóa" (Trông giả và nặng tính kỹ thuật)
- **Hiện tượng:**
  - Tên khách hàng mẫu đặt theo tên người nổi tiếng/cầu thủ kèm nhãn phân khúc trong ngoặc: `Nguyễn Quốc Khánh (VIP)`, `Hà Đức Chinh (AtRisk)`, `Đặng Văn Lâm (Lost)`, `Khách mẫu 01`.
  - Giao diện mang các từ khóa đặc tả kỹ thuật của lập trình viên lên màn hình nhân viên thu ngân: `Hợp đồng tích hợp TV3: ILoyaltyService.CommitCheckoutAsync`, `Chạy LoyaltyDailyJob ngay`, `Maker — Checker`, `Mở Customer 360`, `SIM-2026...`.
  - Bố cục trang `Danh sách khách hàng` chia 2 cột 7/5 khiến bảng dữ liệu bị ép hẹp một bên trong khi nửa bên phải chỉ là một khung trống chờ bấm chọn.
- **Cách xử lý:** Yêu cầu AI rà soát lại toàn bộ View theo chuẩn phần mềm POS/CRM thực tế tại Việt Nam: mở rộng bảng danh sách toàn màn hình (`col-12`), Việt hóa tự nhiên các nhãn phân nhóm (`Khách VIP`, `Thân thiết`, `Ít quay lại`...), đổi toàn bộ dữ liệu mẫu sang họ tên tự nhiên.

### Vấn đề 4: AI tự ý đưa các nút "Giả lập / Demo Test" lên giao diện phần mềm thật
- **Hiện tượng:** Để tiện chứng minh tính năng, AI đã tạo thêm tab *"Giả lập Đánh giá & Quét hết hạn điểm"* (nút lùi ngày giao dịch về 185 ngày trước, nút tạo 5 đánh giá mẫu, nút chạy job thủ công) ngay trên giao diện của nhân viên/quản lý.
- **Cách xử lý:** Bạn đã phát hiện và yêu cầu gỡ bỏ toàn bộ các nút/tab mang tính chất "công cụ test/giả lập" khỏi giao diện người dùng để hệ thống giữ đúng tính chất của một sản phẩm thực tế.

---

## 3. Những bài học rút ra (Đã học được gì?)

1. **Test tự động PASS không đồng nghĩa với giao diện thực tế đã hoàn hảo:**
   - Bộ test C# và HTTP chỉ kiểm tra mã trạng thái `200 OK` và logic tính toán, không phát hiện được lỗi hiển thị font chữ (Mojibake) hay bố cục bị hẹp/vỡ trên trình duyệt. Việc kết hợp **chạy test tự động + nhìn trực tiếp trên UI** là bắt buộc.
2. **Sức mạnh của Multimodal Prompt (Giao tiếp bằng hình ảnh):**
   - Chỉ cần 1 tấm ảnh chụp màn hình kèm câu lệnh ngắn (*"hãy chẩn đoán lại toàn bộ phông chữ"*), AI nhận ra ngay cả 2 vấn đề: lỗi encoding UTF-8 dưới DB và sự bất hợp lý của bố cục chia cột trên màn hình thật.
3. **Phân biệt rõ "Ngôn ngữ tài liệu thiết kế (Spec)" và "Ngôn ngữ giao diện người dùng (UI Copy)":**
   - Khi đưa một tài liệu thiết kế chi tiết cho AI, AI có xu hướng bê nguyên các thuật ngữ kỹ thuật (`Idempotent`, `Maker-Checker`, `RFM Segment`, `AtRisk`, `Customer 360`) lên giao diện. Người điều khiển AI cần yêu cầu tách bạch: *bên dưới code giữ đúng kiến trúc kỹ thuật, nhưng bên trên giao diện phải dùng ngôn ngữ đời thực của thu ngân và chủ quán*.
4. **Dùng AI để ngược dòng làm chủ lại kiến thức:**
   - Khi khối lượng code AI sinh ra khá lớn (hơn 10 bảng/cột mới, 6 Services, 7 Controllers), việc yêu cầu AI vẽ lại **sơ đồ luồng trạng thái, sơ đồ trình tự và kịch bản 5 cảnh** giúp nắm bắt nhanh logic cốt lõi mà không bị ngợp trong hàng nghìn dòng code.

---

## 4. Những điều chưa kiểm soát được hoàn toàn khi dùng AI & Hướng khắc phục

Bên cạnh kết quả đạt được, phiên làm việc hôm nay cũng chỉ ra **4 điểm dễ mất kiểm soát khi dùng AI** mà bạn cần lưu ý:

> [!WARNING]
> **4 rủi ro cần kiểm soát chặt hơn ở các lần làm việc tiếp theo:**

1. **Prompt quá ngắn và mở ở giai đoạn đầu (*"tiếp tục đi"*, *"chạy và xử lý toàn bộ vấn đề"*):**
   - **Hệ quả:** Khi giao quyền quá rộng mà không kèm ràng buộc về phong cách giao diện (UI Guidelines), AI sẽ tự quyết định cách đặt tên biến, tự chế ra các nút "Giả lập Demo" trên màn hình và tự đặt tên dữ liệu mẫu kiểu `(VIP)`, `(AtRisk)`, dẫn đến việc phải tốn thêm 2 lượt chỉnh sửa lại toàn bộ UI.
   - **Cách khắc phục:** Ngay từ prompt yêu cầu code giao diện, thêm câu ràng buộc rõ ràng: *"Thiết kế giao diện đồng nhất với màn hình `Shift/Index.cshtml` hiện có, dùng ngôn ngữ tiếng Việt tự nhiên cho nhân viên thu ngân, tuyệt đối không đưa tên hàm/thuật ngữ kỹ thuật hoặc nút giả lập test lên giao diện."*
2. **Khối lượng thay đổi lớn trong một lần chạy (Large Batch Changes):**
   - **Hệ quả:** AI sửa cùng lúc hơn 20 file (từ SQL, Entity, Service, Controller đến View). Nếu không đọc lại sơ đồ luồng hoặc kiểm tra từng phần, người dùng rất dễ rơi vào tình trạng *"chạy được nhưng không nhớ chính xác hàm nào nằm ở file nào khi bảo vệ đồ án"*.
   - **Cách khắc phục:** Chia nhỏ yêu cầu theo từng lớp (Database → Service → Giao diện) hoặc luôn yêu cầu AI xuất kèm bảng ánh xạ **"Tính năng ↔ File/Hàm xử lý"** sau khi code xong.
3. **Sự phụ thuộc vào môi trường thực thi (Encoding, Process Lock):**
   - **Hệ quả:** Khi server web đang chạy ngầm (`Bms.Web.exe`), việc AI chạy lệnh build tiếp theo sẽ bị khóa file `.dll`, hoặc khi chạy `sqlcmd` trên Windows bị lệch bảng mã UTF-8.
   - **Cách khắc phục:** Luôn quy ước quy trình cố định (dừng tiến trình web trước khi build; luôn dùng UTF-8 with BOM và `-f 65001` cho mọi script SQL tiếng Việt).
4. **Ranh giới giữa "Code phục vụ chấm điểm/demo" và "Code sản phẩm thật":**
   - **Hệ quả:** Đôi khi AI cố gắng làm nổi bật các tiêu chí chấm điểm (như hiện chữ `[CHỨNG MINH IDEMPOTENT]` hay tạo tab `Giả lập 185 ngày`) khiến sản phẩm trông giống một bài tập mô phỏng hơn là phần mềm thực tế.
   - **Cách khắc phục:** Giữ toàn bộ phần kiểm chứng kỹ thuật ở trong bộ test tự động (`tests/Bms.Smoke/Program.cs`), còn giao diện web giữ sạch sẽ 100% như phần mềm thương mại.

