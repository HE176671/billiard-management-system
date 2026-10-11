# Hướng Dẫn Spec-Driven Development (SDD) với GitHub Spec Kit

Tài liệu này hướng dẫn cách phát triển tính năng và lập trình bằng phương pháp **Spec-Driven Development (SDD)** sử dụng **GitHub Spec Kit** đã được tích hợp hoàn chỉnh vào dự án **Billiard Management System (BMS)**.

---

## 1. Tổng quan về Spec-Driven Development (SDD)

Trong phương pháp lập trình truyền thống hoặc "vibe coding", lập trình viên thường yêu cầu AI viết mã nguồn ngay khi có ý tưởng mà chưa có tài liệu thiết kế hoặc ràng buộc rõ ràng. Điều này dẫn đến:
- Mã nguồn bị trôi dạt (drift), không khớp với cơ sở dữ liệu `BilliardDB`.
- Bỏ sót các ca ngoại lệ (edge cases), logic xung đột, phá vỡ kiến trúc hệ thống.
- Khó rà soát (review fatigue) và khó bảo trì lâu dài.

**Spec-Driven Development (SDD)** đảo ngược quy trình:
> **Đặc tả (Specification) là nguồn chân lý duy nhất (Single Source of Truth). Mọi đoạn mã chỉ được viết khi spec, kiến trúc kỹ thuật và danh sách task đã được thống nhất.**

```
+-----------------------------------------------------------------------------------+
|                               CHU TRÌNH PHÁT TRIỂN SDD                            |
+-----------------------------------------------------------------------------------+
  1. Hiến chương   -> 2. Đặc tả      -> 3. Làm rõ / Kiểm thử  -> 4. Kế hoạch kỹ thuật
  /speckit-constitution /speckit-specify  /speckit-clarify        /speckit-plan
                                          /speckit-checklist
                                                 |
  7. Nghiệm thu    <- 6. Thực thi    <- 5. Phân tích chéo     <- 5. Chia nhỏ Task
  /speckit-converge   /speckit-implement  /speckit-analyze        /speckit-tasks
```

---

## 2. Bảng Tra Cứu Các Slash Command

Tất cả các slash command dưới đây đã được đăng ký native trong `.agents/skills/` (cho Antigravity / AGY) và `.github/prompts/` / `.github/skills/` (cho GitHub Copilot / VS Code):

| Slash Command | Mục đích & Mô tả | Artifacts sinh ra / tác động |
|---|---|---|
| `/speckit-constitution` | Thiết lập hoặc cập nhật các nguyên tắc cốt lõi, kiến trúc và quy chuẩn phát triển dự án. | `.specify/memory/constitution.md` |
| `/speckit-specify` | Khởi tạo nhánh git và tạo bản đặc tả tính năng mới từ mô tả ngôn ngữ tự nhiên. | `specs/<feature-id>/spec.md` |
| `/speckit-clarify` | Quét đặc tả hiện tại, phát hiện điểm mơ hồ và đặt tối đa 5 câu hỏi trọng tâm để làm rõ nghiệp vụ. | Cập nhật `specs/<feature-id>/spec.md` |
| `/speckit-checklist` | Tạo bảng kiểm tra chất lượng (Acceptance Criteria / Quality checklist) để review spec. | `specs/<feature-id>/checklist.md` |
| `/speckit-plan` | Lập kế hoạch kiến trúc kỹ thuật: data model, DTOs, API contracts, services, security. | `specs/<feature-id>/plan.md` |
| `/speckit-tasks` | Phân rã kế hoạch thành danh sách các task chi tiết, có thứ tự phụ thuộc (Phase 1, Phase 2...). | `specs/<feature-id>/tasks.md` |
| `/speckit-analyze` | Phân tích chéo tính nhất quán và khả thi giữa `spec.md`, `plan.md`, `tasks.md` và mã nguồn. | Báo cáo phân tích chất lượng |
| `/speckit-implement` | Tiến hành lập trình tuần tự từng task trong `tasks.md`, chạy test và tích hợp. | Mã nguồn C#, Razor Views, Services |
| `/speckit-converge` | Đánh giá độ phủ thực tế của code so với spec, phát hiện phần còn thiếu và bổ sung task. | Nghiệm thu hoàn thiện tính năng |
| `/speckit-taskstoissues` | Chuyển đổi danh sách task trong `tasks.md` thành GitHub Issues khi cần chia việc cho team. | GitHub Issues |

---

## 3. Cấu Trúc Thư Mục Spec Kit Trong Dự Án

```
billiard-management-system/
├── .agents/
│   └── skills/                       # Slash commands native của Antigravity (AGY)
│       ├── speckit-constitution/SKILL.md
│       ├── speckit-specify/SKILL.md
│       ├── speckit-clarify/SKILL.md
│       ├── speckit-checklist/SKILL.md
│       ├── speckit-plan/SKILL.md
│       ├── speckit-tasks/SKILL.md
│       ├── speckit-analyze/SKILL.md
│       ├── speckit-implement/SKILL.md
│       ├── speckit-converge/SKILL.md
│       └── speckit-taskstoissues/SKILL.md
├── .github/
│   ├── prompts/                      # Slash command prompts cho VS Code Copilot Chat
│   └── skills/                       # Skills cho GitHub Copilot
├── .specify/
│   ├── memory/
│   │   └── constitution.md           # Hiến chương dự án (Nguyên tắc cốt lõi của BMS)
│   ├── templates/                    # Mẫu chuẩn spec, plan, tasks, checklist
│   │   ├── spec-template.md
│   │   ├── plan-template.md
│   │   ├── tasks-template.md
│   │   ├── checklist-template.md
│   │   └── constitution-template.md
│   ├── scripts/
│   │   ├── powershell/               # Bộ script tự động hóa trên Windows (PowerShell)
│   │   └── bash/                     # Bộ script tự động hóa trên Linux / macOS / WSL
│   ├── init-options.json             # Cấu hình runtime của Spec Kit
│   └── extensions.yml                # Cấu hình extension và hook vòng đời
├── specs/                            # Thư mục lưu trữ tài liệu đặc tả của từng tính năng
│   └── <feature-id>-<feature-name>/  # Mỗi tính năng nằm trong 1 folder riêng biệt
│       ├── spec.md                   # Yêu cầu nghiệp vụ & User Stories
│       ├── plan.md                   # Thiết kế kiến trúc & Contract
│       ├── tasks.md                  # Danh sách công việc lập trình chi tiết
│       └── checklist.md              # Bảng kiểm tra chất lượng
└── speckit.manifest.json             # Manifest theo dõi phiên bản file của Spec Kit
```

---

## 4. Hướng Dẫn Thực Hành Từng Bước (Walkthrough)

### Bước 1: Khởi tạo đặc tả tính năng (`/speckit-specify`)
Khi bạn muốn xây dựng một tính năng mới (ví dụ: Chức năng xác nhận khách đến nhận bàn):
Trong khung chat với AI, bạn gõ:
```text
/speckit-specify Xây dựng màn hình xác nhận khách đến nhận bàn đã đặt trước, kiểm tra mã đặt bàn, cập nhật trạng thái bàn sang Đang sử dụng và mở phiên chơi tính giờ.
```

**AI sẽ thực hiện:**
1. Tạo branch git mới: `001-confirm-booking` (hoặc số thứ tự tương ứng).
2. Tạo thư mục `specs/001-confirm-booking/spec.md`.
3. Điền cấu trúc chuẩn: User Stories, Acceptance Criteria, Edge Cases, Out of Scope.

---

### Bước 2: Rà soát và làm rõ đặc tả (`/speckit-clarify` & `/speckit-checklist`)
Trước khi chuyển sang thiết kế kỹ thuật, yêu cầu AI rà soát lỗ hổng nghiệp vụ:
```text
/speckit-clarify
```
AI sẽ phân tích các trường hợp còn mơ hồ (ví dụ: Khách đến trễ hơn 30 phút xử lý thế nào? Nếu bàn đó đang bị quá giờ phiên trước thì sao?) và hỏi bạn tối đa 5 câu hỏi trọng tâm. Câu trả lời của bạn sẽ được bổ sung trực tiếp vào `spec.md`.

Bạn cũng có thể tạo bảng kiểm tra:
```text
/speckit-checklist
```

---

### Bước 3: Lập kế hoạch kiến trúc kỹ thuật (`/speckit-plan`)
Sau khi đặc tả đã hoàn chỉnh, lập kế hoạch kỹ thuật:
```text
/speckit-plan
```
**AI sẽ:**
- Đối chiếu với `.specify/memory/constitution.md` (ràng buộc SQL Server `BilliardDB`, Identity, Layered Architecture).
- Thiết kế Data Models / ViewModels (ví dụ: `BookingConfirmationViewModel`).
- Xác định Controller actions, Services và Stored Procedures cần gọi (ví dụ: `usp_CheckInBooking`).
- Xuất kết quả ra `specs/001-confirm-booking/plan.md`.

---

### Bước 4: Phân rã thành danh sách Task (`/speckit-tasks`)
```text
/speckit-tasks
```
AI sẽ chia nhỏ plan thành các task thực thi độc lập có đánh số thứ tự trong `specs/001-confirm-booking/tasks.md`:
- Task 1: Tạo ViewModel và Data Mapping.
- Task 2: Viết Service method gọi Database.
- Task 3: Tạo Controller Action xử lý POST và Validation.
- Task 4: Xây dựng Razor View giao diện Bootstrap.
- Task 5: Kiểm thử luồng thành công và ngoại lệ.

---

### Bước 5: Phân tích kiểm tra chéo (`/speckit-analyze`)
```text
/speckit-analyze
```
AI kiểm tra chéo giữa `spec.md`, `plan.md`, `tasks.md` và mã nguồn hiện tại xem có xung đột nào với Hiến chương (`constitution.md`) hoặc schema database hay không.

---

### Bước 6: Tiến hành lập trình (`/speckit-implement`)
```text
/speckit-implement
```
AI sẽ lần lượt thực hiện từng task trong `tasks.md`, chỉnh sửa mã nguồn C#, tạo Razor Views, tích hợp service và chạy `dotnet build` để đảm bảo code hoạt động hoàn hảo.

---

### Bước 7: Nghiệm thu và hội tụ (`/speckit-converge`)
```text
/speckit-converge
```
AI sẽ đối chiếu lại toàn bộ mã nguồn vừa viết với các tiêu chí trong `spec.md` để đảm bảo 100% yêu cầu đã được đáp ứng, không có tính năng nào bị bỏ sót.

---

## 5. Quy Tắc Cốt Lõi Cần Nhớ (Hiến Chương BMS)

1. **SQL-First**: Database `BilliardDB` là nguồn dữ liệu chuẩn, tuyệt đối không tự ý xóa hay tạo lại database.
2. **Identity**: Dùng chung ASP.NET Core Identity cho Admin, Staff, Customer.
3. **Spec-First**: Không bao giờ viết code trước khi tạo spec và duyệt tasks.
