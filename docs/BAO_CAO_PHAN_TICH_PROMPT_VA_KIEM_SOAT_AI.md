# BÁO CÁO PHÂN TÍCH LỊCH SỬ PROMPT, RỦI RO, BÀI HỌC & MỨC ĐỘ KIỂM SOÁT AI
**Dự án:** Hệ thống Quản lý CLB Bida (BMS) — Phân hệ Quản trị Tài chính (P&L), Kiểm soát Chi phí & Kiểm toán Thất thoát Doanh thu  
**Vai trò nghiệp vụ:** Nhà Quản lý Tài chính (CFO) / Thành viên phát triển hệ thống  
**Nhánh Git thực thi:** `dung` (tích hợp từ 3 nhánh `feature/iter1-*`, `feature/iter2-*`, `feature/iter3-*`)

---

## 1. TỔNG QUAN HÀNH TRÌNH TƯƠNG TÁC VỚI AI (6 PROMPT CHÍNH)

| STT | Thời điểm | Mục tiêu cốt lõi của Prompt | Mức độ kiểm soát AI | Kết quả đầu ra chính |
| :--- | :--- | :--- | :--- | :--- |
| **Prompt #1** | Khởi tạo bài toán | Định hình bài toán Tài chính (CFO), dữ liệu 12 tháng (lãi/lỗ/sai lệch), 4 luồng chi phí, chặn chồng chéo luồng TV khác, yêu cầu vẽ luồng trước khi code. | **75% (Tốt về nghiệp vụ, rộng về phạm vi)** | Infographic luồng tài chính 4 tầng, bản vẽ HTML tương tác, kiến trúc tổng thể. |
| **Prompt #2** | Đánh giá độ phức tạp | Yêu cầu đo lường độ phức tạp, tổng số màn hình, công nghệ sử dụng và lộ trình tiếp cận cho người mới. | **85% (Kiểm soát tốt rủi ro quá tải kỹ thuật)** | Bảng phân rã 4 bài toán, gom gọn vào 2 màn hình sẵn có (`/Revenue`, `/Dashboard`), lộ trình 4 bước. |
| **Prompt #3** | Chuẩn hóa SRS/SDS & Cắt giảm Scope | Chốt **3 bài toán đầu**, đẩy **AI Agent sang Out-of-Scope**. Yêu cầu trọn bộ SRS/SDS (UseCase, Mockup, Swimlane, BR, MSG tiếng Việt, ERD, Script 12 tháng, State, Sequence, 3 Iterations) trước khi code. | **90% (Kiểm soát chặt quy trình phần mềm)** | Tài liệu [SRS_SDS_FINANCIAL_AUDIT.md](../docs/SRS_SDS_FINANCIAL_AUDIT.md) + Mockup UI [financial_srs_ui_mockup.html]. |
| **Prompt #4** | Ra lệnh thực thi | *"tiến hành toàn bộ quy trình"* | **40% (Ủy quyền quá rộng — Rủi ro cao nhất)** | AI bắt đầu chạy từ Database Seed sang Code hàng loạt nếu không có chốt chặn Git. |
| **Prompt #5** | Chỉnh hướng Git & Iterative | Chặn rủi ro làm gộp (*"đừng làm một đống ra rồi đẩy cả cụm lên"*), bắt buộc tách 3 nhánh `feature/iter*` theo đúng Mục 13 SRS/SDS. | **95% (Khôi phục quyền kiểm soát & truy vết Git)** | 3 nhánh `feature/iter1..3` độc lập, merge `--no-ff` vào `dung`, `58/58` test PASS. |
| **Prompt #6** | Vận hành thực tế | *"chạy demo"* | **70% (Lệnh ngắn, phụ thuộc ngữ cảnh AI tự suy luận)** | Server chạy tại `http://localhost:5000` kèm kịch bản kiểm thử 3 bài toán trên UI. |

---

## 2. PHÂN TÍCH CHI TIẾT TỪNG PROMPT

---

### PROMPT #1: Đặt Bài Toán Nghiệp Vụ Dưới Góc Nhìn Quản Lý Tài Chính (CFO)

#### 1. Nguyên văn Prompt gốc
> *"Theo luồng dự án , nghiệp vụ tôi xoay quanh vấn đề tài chính của hệ thống này. Đứng trên cương vị là 1 nhà quản lý tài chính, tôi cần biết các vấn đề xoay quanh việc đánh giá , phân tích và phải đưa ra được điều gì cần làm cần thay đổi, chứ đừng bao giờ cho tôi những điều vô nghĩa, nó cần phải trả về câu hỏi rằng là điều này nhằm giải quyết vấn đề gì! Dữ liệu cần fill trong vòng 1 năm , cả lỗ, cả lãi, cả sai dữ liệu nhằm đánh giá xem là có thất thoát doanh thu hay không, tìm ra được nó ở đâu, đôi khi nên có 1 agent đánh giá đống dữ liệu tài chính, đề xuất xem điều gì có thể cải thiện được, ví dụ : tháng 3,4,5 có lượng doanh thu cao, tháng 4,5,6 có tỉ lệ bán được nhiều nước, thời gian người dùng chơi đêm nhiều hơn ban ngày, có thể đề xuất cắt giảm nhân sự, mua thêm nước đa dạng vào 1 loại nước nào đó mà khách hàng thích uống, đề xuất các combo để thu hút khách hàng nhiều hơn. Lưu ý không cần làm những luồng khác như phải thêm khách hay order đồ ăn để có thể có dữ liệu vì đó là phần mà người khác có thể làm, nhớ đánh giá cả các luồng mất tiền như bảo trì bảo dưỡng bàn , bia, gậy gộc, điện nước, lương nhân viên. Vẽ lại luồng tài chính này genarate ảnh nếu có thể để nó đúng logic và giúp trực quan hóa việc truyền tải cho tôi trước khi thực hiện việc sửa đổi src code."*

#### 2. Vấn đề muốn giải quyết
- Báo cáo doanh thu hiện có của hệ thống (`/Revenue`) chỉ mới dừng ở việc **cộng tổng tiền thu** (`PaymentTransactions`) — hoàn toàn vô nghĩa với một CFO vì không biết **Lãi hay Lỗ**, không tính **4 luồng chi phí mất tiền** (bảo trì bàn/nỉ/cơ, nhập bia/nước, điện 3 pha/nước, lương nhân sự ngày/đêm), và không phát hiện được **thất thoát doanh thu** (bàn đóng không lên bill, order lậu, lệch két).
- Cần bộ dữ liệu đủ **12 tháng** phản ánh đúng tính mùa vụ (T3-T4-T5 cao điểm giờ chơi; T4-T5-T6 cao điểm nước/bia; khách chơi đêm > ngày) mà **không xâm phạm luồng làm việc của thành viên khác** (TV1 mở bàn, TV3 thanh toán, TV4 gọi món).
- Cần trực quan hóa bằng hình ảnh trước khi chạm vào code.

#### 3. Những gì đã giải quyết được
- Thiết kế hoàn chỉnh kiến trúc luồng tài chính 4 tầng (Dòng tiền vào $\leftrightarrow$ Dòng tiền ra 4 nhóm $\leftrightarrow$ Đối soát thất thoát 5 quy tắc $\leftrightarrow$ Đề xuất hành động).
- Sinh ảnh Infographic luồng tài chính và bản vẽ kiến trúc tương tác trước khi sửa code.
- Gắn chặt triết lý **"Điều này nhằm giải quyết vấn đề gì?"** vào mọi chỉ số tài chính từ đầu đến cuối dự án.

#### 4. Những rủi ro phát sinh từ Prompt này
- **Rủi ro phình đại phạm vi (Scope Creep):** Việc đưa khái niệm *"có 1 agent đánh giá đống dữ liệu tài chính"* vào cùng lúc với hệ thống kế toán/kiểm toán cốt lõi dễ khiến AI sa đà vào việc tích hợp LLM API phức tạp hoặc viết hệ thống AI giả lập quá đồ sộ.
- **Rủi ro thiếu định lượng dữ liệu sai lệch:** Prompt yêu cầu *"cả lỗ, cả lãi, cả sai dữ liệu"* nhưng chưa quy định rõ bao nhiêu tháng lỗ, bao nhiêu kịch bản sai dữ liệu và công thức toán học nào định nghĩa một vụ thất thoát.

#### 5. Những gì tôi học được
- **Đặt vai trò (Role Prompting - "Đứng trên cương vị nhà quản lý tài chính")** và **Đặt câu hỏi kiểm chứng giá trị ("Điều này nhằm giải quyết vấn đề gì?")** là cách cực kỳ hiệu quả để ép AI không sinh ra các biểu đồ trang trí vô nghĩa.
- **Khóa biên giới hệ thống ("không cần làm những luồng khác như thêm khách hay order đồ ăn")** giúp bảo vệ kiến trúc nhóm (5 thành viên), tránh việc AI tự ý viết đè Controller của người khác.
- **Yêu cầu trực quan hóa trước khi code ("Vẽ lại luồng... trước khi sửa đổi src code")** giúp phát hiện sai lệch tư duy ngay từ bước thiết kế.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Chưa tách bạch rõ **In-Scope (bắt buộc code)** và **Out-of-Scope (đề xuất tương lai)** ngay từ Prompt #1, dẫn đến việc AI thiết kế cả tầng "AI CFO Advisor Agent" vào bản kế hoạch đầu tiên.
- Chưa quy định chuẩn tài liệu kỹ thuật phần mềm (SRS/SDS) mà AI phải tuân thủ.

---

### PROMPT #2: Đánh Giá Độ Phức Tạp, Số Lượng Màn Hình & Công Nghệ Cho Người Mới

#### 1. Nguyên văn Prompt gốc
> *"tính toán mức độ phức tập để hoàn thiệm nhóm chức năng này, tổng số màn hình, cách thực thi , công nghệ sử dụng và nhất là đối với 1 người nhìn tất cả thứ này nó đang rất là mới đối với tôi."*

#### 2. Vấn đề muốn giải quyết
- Trước một bản thiết kế tài chính lớn (12 tháng dữ liệu, 4 luồng chi phí, kiểm toán thất thoát, gợi ý chiến lược), người phát triển cảm thấy choáng ngợp và cần biết: **Nó khó ở mức nào? Phải làm bao nhiêu màn hình? Dùng công nghệ gì mới không? Người mới bắt đầu đọc và giải thích code từ đâu?**

#### 3. Những gì đã giải quyết được
- Định lượng rõ độ phức tạp ở mức **Trung bình – Khá (6.5/10)**, trong đó phần khó nhất không phải là học công nghệ mới mà là **Logic truy vấn SQL đối soát chéo các bảng**.
- Chốt phương án **không đẻ thêm hàng loạt màn hình mới** mà chỉ nâng cấp **02 màn hình sẵn có** (`/Revenue` làm Trung tâm Tài chính P&L & Kiểm toán + `/Dashboard` làm hộp cảnh báo nhanh) kèm 1 Modal nhập chi phí.
- Khẳng định sử dụng **100% công nghệ sẵn có của dự án** (ASP.NET Core 8 MVC, EF Core, SQL Server, Bootstrap 5, Chart.js) để người mới dễ nắm bắt.

#### 4. Những rủi ro phát sinh từ Prompt này
- **Rủi ro đơn giản hóa quá mức (Over-simplification):** Khi người dùng nói *"tất cả thứ này đang rất mới đối với tôi"*, AI có xu hướng đề xuất cách làm tắt (như dùng công thức `if-else` đơn giản hoặc gom logic vào một chỗ) dễ dẫn đến thiếu tài liệu đặc tả chuẩn (Use Case Spec, State Diagram, Sequence Diagram) nếu không được yêu cầu tiếp ở bước sau.

#### 5. Những gì tôi học được
- Khi làm việc với AI trên một codebase có sẵn, việc yêu cầu AI **đánh giá độ phức tạp và tái sử dụng màn hình/công nghệ hiện hữu** giúp tránh việc AI tự ý cài thêm thư viện lạ hoặc tạo ra cấu trúc thư mục phức tạp không cần thiết.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Ở bước này tôi mới dừng ở việc hỏi "cách thực thi tổng quan", chưa ép AI phải lập bảng đối chiếu dữ liệu thật trong SQL Server (`BilliardDB`) xem bảng nào đã có Entity C#, bảng nào chưa có trong `ApplicationDbContext`.

---

### PROMPT #3: Cắt Giảm Scope & Yêu Cầu Bộ Tài Liệu Kỹ Thuật SRS/SDS Chuẩn Chỉnh Trước Khi Code

#### 1. Nguyên văn Prompt gốc
> *"tập trung vào 3 bài toán đầu, phần AI coi như là 1 đề xuất trong tương lai nằm trong phần out of scope. trước khi đi vào code => cho tôi phần SRS(gồm nó là usecase gì, usecase spec (viết chi tiết), screen diagram, UI(mockup), main workflow(vẽ dưới dạng swimlane), bổ sung bảng BR, bảng msg code (trả về tiếng việt nếu hiển thị nó dưới dạng UI), Đưa ra phần entity diagram mức độ high level, đưa ra db + script dữ liệu đủ 12th, state diagram, code chuẩn acrhitecture, đưa ra sơ đồ sequence, lưu ý khớp dữ liệu rồi mới code, yêu cầu đúng chuẩn nghiệp vụ của một coder gồm việc tuân thủ quy trình phát triển phần mềm iterative incremental, cuối cùng luôn test lại các chức năng trước khi bàn giao)"*

#### 2. Vấn đề muốn giải quyết
- Loại bỏ phần "AI CFO Agent" ra khỏi phạm vi code hiện tại (đưa vào **Out of Scope**) để tập trung 100% nguồn lực vào **3 bài toán cốt lõi**: (1) Dữ liệu 12 tháng Lãi/Lỗ & Mùa vụ, (2) Báo cáo P&L & 4 luồng chi phí vận hành, (3) Kiểm toán thất thoát doanh thu.
- Bắt buộc AI phải làm việc như một kỹ sư phần mềm chuyên nghiệp: **Có SRS/SDS đầy đủ, khớp schema Database thực tế, thiết kế kiến trúc phân tầng, chia quy trình Iterative & Incremental và có kế hoạch kiểm thử trước khi viết bất kỳ dòng code nào.**

#### 3. Những gì đã giải quyết được
- Hoàn thiện bộ tài liệu đặc tả toàn diện [SRS_SDS_FINANCIAL_AUDIT.md](../docs/SRS_SDS_FINANCIAL_AUDIT.md) gồm đủ 14 mục:
  - 5 Use Cases (`UC-FIN-01` $\rightarrow$ `UC-FIN-05`) kèm đặc tả chi tiết.
  - Screen Diagram, UI Wireframe & bản Mockup HTML tương tác.
  - Main Workflow dạng **Swimlane 4 làn** (TV1/TV4 $\rightarrow$ TV3/TV5 $\rightarrow$ Hệ thống Kiểm toán $\rightarrow$ CFO).
  - Bảng **13 Quy tắc nghiệp vụ (`BR-FIN-01`..`13`)** và **12 Mã thông báo tiếng Việt (`MSG-FIN-01`..`12`)**.
  - High-level ERD (đối chiếu đúng các bảng vật lý của `BilliardDB`), State Diagram, Layered Architecture, Sequence Diagrams.
  - Kế hoạch phát triển **Iterative & Incremental gồm 3 Iterations** và kịch bản kiểm thử tích hợp `T17`..`T20`.

#### 4. Những rủi ro phát sinh từ Prompt này
- **Rủi ro "Nặng tải trong một lượt trả lời" (Single-Turn Overload):** Yêu cầu 14 hạng mục SRS/SDS đồ sộ trong cùng một prompt có thể khiến AI viết lướt hoặc cắt bớt chi tiết SQL nếu không tách bước.
- **Rủi ro lệch Schema ngầm:** Dù đã dặn *"lưu ý khớp dữ liệu rồi mới code"*, trong database thực tế có những bảng tồn tại ở SQL (`PlaySessions`, `Invoices`, `Orders`, `OrderDetails`, `Products`) nhưng **chưa được khai báo `DbSet` trong `ApplicationDbContext.cs`**, hoặc cột tồn tại trong SQL (`FloorNumber`) nhưng không được map trong class C# `BilliardTable.cs`.

#### 5. Những gì tôi học được
- **Kỹ thuật "Scope Pruning" (Cắt tỉa phạm vi):** Chủ động đưa phần chưa cần thiết vào *Out of Scope* giúp sản phẩm cốt lõi sắc bén, khả thi và dễ bảo vệ trước hội đồng.
- **Kỹ thuật "Spec-First / Documentation-Driven Development":** Bắt AI viết bảng `BR-*` (Business Rules) và `MSG-*` (Message Codes) trước khi code giúp toàn bộ câu chữ thông báo lỗi/thành công trên UI và trong Unit/Smoke Test khớp nhau 100%.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Mặc dù đã yêu cầu *"tuân thủ quy trình phát triển phần mềm iterative incremental"*, tôi **chưa quy định rõ quy tắc quản lý nhánh Git (`branch naming convention`) và cách commit/merge cho từng Iteration** ngay trong Prompt #3. Điều này dẫn tới lỗ hổng ở Prompt #4.

---

### PROMPT #4: Lệnh Thực Thi Tổng Lực ("Blank Check Prompt")

#### 1. Nguyên văn Prompt gốc
> *"tiến hành toàn bộ quy trình"*

#### 2. Vấn đề muốn giải quyết
- Sau khi duyệt xong SRS/SDS ở Prompt #3, người dùng muốn AI bắt tay vào thực thi toàn bộ từ Database Seed đến Backend, Frontend và Kiểm thử.

#### 3. Những gì đã giải quyết được
- AI tiến hành tạo và chạy thành công script [11_Financial_12Months_Seed.sql](../BMS_Database_Starter/11_Financial_12Months_Seed.sql) vào SQL Server (`localhost\SQLEXPRESS`), tạo Entity [`OperatingExpense`](../src/Bms.Web/Data/Entities/OperatingExpense.cs), nâng cấp [`ApplicationDbContext`](../src/Bms.Web/Data/ApplicationDbContext.cs), [`SchemaUpgrader`](../src/Bms.Web/Data/SchemaUpgrader.cs), [`ReportViewModels`](../src/Bms.Web/Models/ReportViewModels.cs) và [`FinancialAuditService`](../src/Bms.Web/Services/FinancialAuditService.cs).

#### 4. Những rủi ro phát sinh từ Prompt này (Rủi ro lớn nhất trong phiên làm việc)
- **Rủi ro mất dấu lịch sử Git (Monolithic Execution Risk):** Một câu lệnh ngắn *"tiến hành toàn bộ quy trình"* trao toàn quyền tự chủ cho AI chạy một mạch qua cả 3 Iterations trên cùng một nhánh hiện tại mà không dừng lại tạo nhánh `feature/iter*` hay commit theo từng chặng.
- **Rủi ro lỗi biên dịch khi đụng chạm Entity của thành viên khác:** Khi AI code `FinancialAuditService.cs`, AI giả định class `BilliardTable` có thuộc tính `FloorNumber` (giống trong bảng SQL), dẫn đến lỗi build `CS1061: 'BilliardTable' does not contain a definition for 'FloorNumber'` (sau đó AI đã tự phát hiện khi chạy `dotnet build` và sửa lại).

#### 5. Những gì tôi học được
- **Không bao giờ dùng lệnh ủy quyền trắng ("Làm hết đi / Tiến hành toàn bộ") cho một quy trình nhiều giai đoạn mà không kèm điều kiện chốt chặn (Checkpoint / Git Gate).**
- Khi giao việc lớn cho AI, phải gắn lệnh thực thi với quy tắc quản lý cấu hình (SCM/Git): mỗi Iteration phải tương ứng với 1 nhánh `feature/...`, build xanh, test xanh rồi mới chuyển sang Iteration tiếp theo.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Tôi đã để AI tự chạy thẳng từ Iteration 1 sang Iteration 2 trên working tree mà chưa tách nhánh Git ngay từ đầu, buộc tôi phải can thiệp bằng Prompt #5 để chấn chỉnh lại quy trình Git.

---

### PROMPT #5: Can Thiệp Kịp Thời — Chuẩn Hóa Lịch Sử Git Theo 3 Iterations (`feature/iter*`)

#### 1. Nguyên văn Prompt gốc
> *"lưu ý khi code , để truy vết được đúng lịch sử git bạn cần chuẩn hóa :13. Quy Trình Phát Triển Iterative & Incremental (3 Iterations) & Kế Hoạch Kiểm Thử chứ đừng làm một đống ra rồi đẩy cả cụm lên, chuẩn hóa cách đặt tên /feature"*

#### 2. Vấn đề muốn giải quyết
- Ngăn chặn tình trạng AI code xong tất cả các file rồi mới gom vào 1 commit duy nhất ("đẩy cả cụm lên"), làm mất khả năng truy vết lịch sử phát triển phần mềm theo từng Iteration.
- Yêu cầu chuẩn hóa tên nhánh theo chuẩn `/feature` tương ứng với đúng 3 Iterations đã thiết kế ở Mục 13 của SRS/SDS.

#### 3. Những gì đã giải quyết được
- Chuẩn hóa hoàn toàn lịch sử Git trên nhánh `dung` thông qua 3 nhánh tính năng độc lập và merge `--no-ff` rõ ràng:
  1. `feature/iter1-financial-schema-seed` (Commit `cc6116e` $\rightarrow$ Merge `153dc51`): Database Schema `OperatingExpenses`, Seed 12 tháng & tài liệu SRS/SDS.
  2. `feature/iter2-financial-pnl-audit-service` (Commit `02e530e` $\rightarrow$ Merge `b7a3358`): ViewModels, Bảng mã `MSG-FIN-01..12` và `FinancialAuditService` (P&L + 5-Rule Leakage Audit).
  3. `feature/iter3-financial-ui-and-smoke-tests` (Commit `ace2228` $\rightarrow$ Merge `add70c8`): `RevenueController`, `DashboardController`, Giao diện CFO `/Revenue`, và 4 bộ test tích hợp `T17..T20` (`58/58 PASS`).

#### 4. Những rủi ro phát sinh từ Prompt này
- **Rủi ro xung đột phụ thuộc giữa các Iteration (Cross-Iteration Dependency):** Khi tách file để commit riêng từng nhánh `feature/iter1`, `feature/iter2`, `feature/iter3`, nếu file ở Iteration 1 vô tình gọi đến hàm chỉ có ở Iteration 2 thì bản build của Iteration 1 sẽ bị gãy. (Đã được kiểm soát bằng cách chạy `dotnet build` riêng trên từng nhánh trước khi commit).

#### 5. Những gì tôi học được
- **Giám sát quá trình thực thi của AI (Active AI Supervision):** Người lập trình phải đóng vai trò Tech Lead / Reviewer, kịp thời nắn dòng khi thấy AI có dấu hiệu làm gộp bước.
- Ràng buộc **SRS Mục 13 $\leftrightarrow$ Nhánh Git `feature/iter*` $\leftrightarrow$ Automated Smoke Test** tạo ra sự nhất quán tuyệt đối giữa tài liệu báo cáo và lịch sử mã nguồn thực tế.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Quy tắc đặt tên nhánh (`feature/iter1-...`) và chiến lược merge (`--no-ff`) lẽ ra nên được đưa vào file quy tắc dự án (`.agents/rules/`) hoặc ngay từ Prompt #3 để AI tuân thủ tự động từ đầu thay vì phải nhắc ở Prompt #5.

---

### PROMPT #6: Khởi Chạy Demo Thực Tế

#### 1. Nguyên văn Prompt gốc
> *"chạy demo"*

#### 2. Vấn đề muốn giải quyết
- Khởi chạy ứng dụng web thực tế trên môi trường local để kiểm tra trực quan giao diện và luồng nghiệp vụ sau khi toàn bộ test ngầm đã PASS.

#### 3. Những gì đã giải quyết được
- Khởi chạy server `Bms.Web` ở chế độ chạy nền tại địa chỉ `http://localhost:5000`.
- Cung cấp đường dẫn truy cập trực tiếp và kịch bản 6 bước trình diễn 3 bài toán tài chính trên trình duyệt.

#### 4. Những rủi ro phát sinh từ Prompt này
- **Rủi ro thiếu thông số môi trường:** Lệnh *"chạy demo"* rất ngắn, không chỉ định port, profile (`http` hay `https`), hay tài khoản đăng nhập. Nếu mật khẩu `admin.demo` lưu trong `user-secrets` của máy local chưa được đồng bộ hoặc port `5000` đang bị chiếm dụng, phiên demo có thể gặp trục trặc.

#### 5. Những gì tôi học được
- Ngay cả với các lệnh ngắn như *"chạy demo"*, nên yêu cầu kèm **Checklist kịch bản demo (Demo Script)** gắn với từng Use Case (`UC-FIN-01`..`05`) để buổi trình bày trước giảng viên/hội đồng diễn ra mạch lạc.

#### 6. Những gì tôi chưa kiểm soát được AI ở bước này
- Chưa yêu cầu AI kiểm tra tự động bằng script HTTP request vào các endpoint `/Account/Login` và `/Revenue` sau khi bật server để xác nhận HTTP Status Code thực tế trên port `5000`.

---

## 3. TỔNG KẾT: MA TRẬN KIỂM SOÁT AI & BÀI HỌC CỐT LÕI CHO NGƯỜI PHÁT TRIỂN

### 3.1. Những gì tôi đã làm tốt để làm chủ AI
1. **Giữ vững tư duy nghiệp vụ gốc:** Luôn bắt AI trả lời câu hỏi *"Điều này nhằm giải quyết vấn đề gì?"* dưới góc nhìn CFO, giúp sản phẩm giải quyết đúng bài toán **Lãi/Lỗ thực (P&L)** và **Truy vết thất thoát (`LEAK-01`..`05`)** thay vì chỉ vẽ biểu đồ doanh thu bề nổi.
2. **Bảo vệ ranh giới làm việc nhóm (Team Boundary):** Nêu rõ ngay từ đầu không làm lấn sân sang luồng thêm khách/order đồ ăn của TV1, TV3, TV4; sử dụng dữ liệu seed 12 tháng độc lập và truy vấn SQL an toàn.
3. **Biết cắt giảm phạm vi đúng lúc (Scope Management):** Chủ động đưa phần AI Agent sang *Out-of-Scope* ở Prompt #3 để đảm bảo chất lượng kỹ thuật cho 3 bài toán cốt lõi.
4. **Áp dụng quy trình kỹ nghệ phần mềm chuẩn:** Yêu cầu đủ SRS $\rightarrow$ SDS $\rightarrow$ Khớp Database $\rightarrow$ Chia 3 Iterations trên Git (`feature/iter*`) $\rightarrow$ Kiểm thử tự động (`58/58 PASS`).

### 3.2. Những điểm mù tôi chưa kiểm soát hết AI (Cần rút kinh nghiệm cho các dự án sau)
1. **Ra lệnh ủy quyền quá rộng ở bước chuyển giao từ Thiết kế sang Code (Prompt #4 - *"tiến hành toàn bộ quy trình"*):** Khiến AI có xu hướng chạy liền một mạch qua nhiều bước nếu không bị phanh lại kịp thời ở Prompt #5.
   - *Cách khắc phục:* Luôn ra lệnh theo từng Iteration: *"Hãy thực hiện Iteration 1 trên nhánh `feature/iter1-...`, chạy test và dừng lại báo cáo trước khi sang Iteration 2"*.
2. **Chưa yêu cầu AI kiểm tra sự khác biệt giữa Bảng vật lý SQL và EF Core `DbContext` trước khi thiết kế:** Trong dự án nhóm nhiều thành viên, SQL Server có 24+ bảng nhưng `ApplicationDbContext` chỉ map một phần, và Entity `BilliardTable` không map đủ tất cả các cột.
   - *Cách khắc phục:* Trước khi code, luôn yêu cầu AI chạy lệnh đối chiếu danh sách bảng/cột trong SQL Server với các class trong thư mục `Data/Entities/`.
3. **Chưa định nghĩa quy ước Git (`Branching & Commit Convention`) ngay từ đầu phiên:** Phải đến giữa quá trình code (Prompt #5) mới bổ sung yêu cầu chuẩn hóa tên nhánh `/feature` và lịch sử commit.
   - *Cách khắc phục:* Đưa quy tắc Git Flow / Iterative Branching vào ngay phần mở đầu hoặc cấu hình sẵn trong `.agents/rules/`.
