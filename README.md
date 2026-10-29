# billiard-management-system
SWP391 - Billiard Management System

[Phân công mới cho 5 thành viên theo workflow (20/10/2026)](docs/PHAN_CONG_WORKFLOW_5_THANH_VIEN.md): đặt bàn và mở bàn cùng một người phụ trách; chủ workflow, dữ liệu, bàn giao code và thứ tự tích hợp; thay cho các phân công cũ.

[Lộ trình code theo tracking — 3 iteration](docs/LO_TRINH_CODE_3_ITER_TRACKING.md): việc từng người, mã BMS, phụ thuộc và tiêu chí demo mỗi iteration.

Database mới: **BilliardDB, 24 bảng nghiệp vụ, 5 procedure** (01/10/2026).

Đọc [hướng dẫn cài mới hoặc nâng cấp database](BMS_Database_Starter/README_VI.md) trước khi chạy SQL. Máy mới dùng `BilliardDB_Full.sql`; máy đã có bản 21 bảng dùng `05_AddComboRelations.sql`. Database đã nâng cấp chỉ cần chạy `Verify_BilliardDB.sql` để kiểm tra. Không tạo lại hoặc seed lại database đang có dữ liệu.

[Thiết kế combo](docs/COMBO_DESIGN.md) · [Bối cảnh dự án để tiếp tục](docs/PROJECT_CONTEXT.md).

[Tiến độ và kịch bản demo ngày 13/10/2026](docs/BAO_CAO_TIEN_DO_2026-10-13.md): Admin khách hàng, hạng thành viên, bảng giá, Dashboard và báo cáo doanh thu; hướng dẫn chạy và kết quả kiểm thử.

[Bản đồ code: file, dòng bắt đầu–kết thúc, câu hỏi khi trình bày](docs/HUONG_DAN_TIM_CODE.md). Dữ liệu demo bổ sung: `BMS_Database_Starter/07_AddPresentationDemoData.sql` (giả lập, giữ dữ liệu cũ, đã áp dụng trên localhost).
