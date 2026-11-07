# Tìm code nhanh trong Bms.Web

Mở [bản đồ code và số dòng từng chức năng](../../docs/HUONG_DAN_TIM_CODE.md) khi chuẩn bị trình bày. File này có luồng MVC, link tới source, khoảng dòng bắt đầu–kết thúc và câu hỏi thường gặp.

| Muốn tìm phần nào? | Mở thư mục/file nào trước? |
|---|---|
| Đăng nhập, đăng ký, đăng xuất | `Controllers/AccountController.cs` |
| Đổi mật khẩu của tài khoản hiện tại | `Controllers/ProfileController.cs` |
| CRUD nhân viên, khóa và reset mật khẩu | `Controllers/EmployeeController.cs` |
| Danh sách/khóa khách, tài khoản riêng | `Controllers/CustomerController.cs` |
| Hạng thành viên và ngưỡng điểm | `Controllers/MemberTierController.cs` |
| Giá theo loại bàn/khung giờ | `Controllers/PricingController.cs` |
| Dashboard | `Controllers/DashboardController.cs` |
| Doanh thu và xuất CSV | `Controllers/RevenueController.cs` |
| Kiểm tra dữ liệu form | `Models/` → tên chức năng tương ứng |
| Giao diện | `Views/` → thư mục cùng tên controller |
| Entity và bảng/cột SQL | `Data/Entities/`, `Data/ApplicationUser.cs`, `Data/ApplicationDbContext.cs` |
| Phân quyền, Identity, cookie và routing | `Program.cs`, `[Authorize]` đầu controller/action |

Controller và Program có `#region` đặt tên theo phần xử lý. Trong Visual Studio dùng Ctrl+G đến số dòng, Ctrl+M rồi Ctrl+O để thu gọn. Sau khi sửa source, từ thư mục gốc repo chạy:

```powershell
pwsh -File scripts/Write-CodeGuide.ps1
```

Dữ liệu trình bày do `BMS_Database_Starter/07_AddPresentationDemoData.sql` thêm trên `localhost / BilliardDB`. Hướng dẫn chạy và phạm vi chức năng ở [báo cáo tiến độ](../../docs/BAO_CAO_TIEN_DO_2026-10-13.md).
