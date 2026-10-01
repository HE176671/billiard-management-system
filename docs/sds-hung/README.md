# Cách sử dụng phần SDS của Hùng

1. Mở `../SDS_HUNG_OFFLINE.html` bằng Chrome hoặc Edge để đọc toàn bộ tài liệu và 7 sơ đồ, không cần Internet. Có thể dùng Ctrl+P để mở hộp thoại in; kiểm tra bố cục trước khi lưu PDF.
2. `../SDS_HUNG.md` là nội dung nguồn để sửa và sao chép vào báo cáo chung.
3. Các file `.svg` là sơ đồ vector đã xuất, có thể phóng to. Word phiên bản hỗ trợ SVG có thể chèn trực tiếp; với Google Docs nên chuyển ảnh sang PNG trước khi chèn.
4. Các file `.mmd` là nguồn Mermaid tương ứng để chỉnh tên lớp/luồng khi code thay đổi.

| File | Nội dung | Vị trí trong SDS |
|---|---|---|
| 01-architecture | Kiến trúc tổng thể | I.1 |
| 02-packages | Package Diagram | I.2 |
| 03-database | Quan hệ bốn bảng | I.3 |
| 04-classes | Class Diagram | II.1.1 |
| 05-login | Sequence đăng nhập | II.1.2 |
| 06-create-staff | Sequence tạo Staff có transaction | II.1.3 |
| 07-change-password | Sequence đổi mật khẩu | II.1.4 |

Tài liệu đã phân biệt code hiện có với thiết kế cần bổ sung. Phụ lục A liệt kê chênh lệch cần nhóm xử lý trước bản nộp cuối; không xóa ghi chú rồi coi hành vi đó đã được code.

## Tạo lại sau khi sửa nội dung

Từ thư mục gốc repo, chạy `node docs/sds-hung/build-preview.cjs` để tạo `SDS_HUNG.html` và các `.mmd`. Bản HTML này cần Internet để tải thư viện Mermaid; bản OFFLINE cũ chưa tự cập nhật.

Chạy `node docs/sds-hung/preview-server.cjs`, mở `http://127.0.0.1:8766`, chờ đủ 7 sơ đồ rồi bấm “Lưu HTML offline và SVG vào docs” để xuất lại. Máy chủ chỉ lắng nghe localhost. Dừng bằng Ctrl+C sau khi xong.

Không có script nào trong thư mục này kết nối SQL Server hoặc thay đổi code ứng dụng.
