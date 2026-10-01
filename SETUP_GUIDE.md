# Hướng dẫn cài đặt cho thành viên nhóm BMS

## Yêu cầu cần có trên máy
- .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
- SQL Server Express: https://www.microsoft.com/en-us/sql-server/sql-server-downloads
- Visual Studio 2022 (hoặc VS Code + C# extension)
- Git

---

## Bước 1 — Lấy code về máy

Mở PowerShell, chạy:

```powershell
git clone https://github.com/HE176671/billiard-management-system.git
cd billiard-management-system
```

Nếu đã clone rồi thì chỉ cần:
```powershell
git pull
```

---

## Bước 2 — Tạo database trên máy bạn

Chạy lệnh sau để tạo và nạp dữ liệu mẫu cho Database:

```powershell
sqlcmd -S 'localhost\SQLEXPRESS' -E -C -l 5 -b -f 65001 -i 'BMS_Database_Starter/BilliardDB_Full.sql'
```

> Nếu SQL Server của bạn không phải SQLEXPRESS, đổi tên instance cho đúng.
> Ví dụ: localhost\MSSQLSERVER hoặc localhost

---

## Bước 3 — Tạo file connection string (KHÔNG commit file này)

Vào thư mục `src/Bms.Web/`, tạo file tên **`appsettings.Development.json`**:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=BilliardDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> File này đã bị gitignore — KHÔNG tự ý commit lên GitHub.

---

## Bước 4 — Đặt mật khẩu admin để test đăng nhập

Mở PowerShell, vào thư mục project:

```powershell
cd src/Bms.Web
dotnet user-secrets init
dotnet user-secrets set "DevSeed:AdminPassword" "Admin@12345"
```

---

## Bước 5 — Mở project và chạy

**Cách 1 — Visual Studio 2022:**
- Mở VS → Open Project/Solution
- Chọn file `src/Bms.Web/Bms.Web.csproj`
- Nhấn F5

**Cách 2 — Terminal:**
```powershell
cd src/Bms.Web
dotnet run
```

Mở trình duyệt: `http://localhost:5000`

---

## Tài khoản mẫu để test

| Tài khoản | Mật khẩu | Role |
|---|---|---|
| admin.demo | Admin@12345 | Admin |
| staff.demo01 | (chưa có) | Staff |
| customer.demo01 | (chưa có) | Customer |

> Staff và Customer chưa có mật khẩu. Đăng nhập Admin trước, vào /Employee để tạo mật khẩu.

---

## Làm phần của bạn

Sau khi chạy được app, thêm Controller và View của bạn vào:

- Controller: `src/Bms.Web/Controllers/TênController.cs`
- View: `src/Bms.Web/Views/Tên/`
- Dùng `Layout = "_AdminLayout"` để có sidebar giống phần Admin

### Mẫu Controller cơ bản:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Staff")]   // đổi role phù hợp: Staff / Customer / Admin
public class TênController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Tiêu đề trang";
        return View();
    }
}
```

### Mẫu View cơ bản:

```cshtml
@{
    ViewData["Title"] = "Tiêu đề trang";
    Layout = "_AdminLayout";
}

<div class="data-card p-4">
    <h5>Nội dung của bạn ở đây</h5>
</div>
```

### Các class CSS có sẵn trong _AdminLayout:

| Class | Dùng cho |
|---|---|
| `data-card` | Khung card trắng bo góc |
| `data-card-header` | Header của card |
| `btn-primary-bms` | Nút đỏ chính |
| `btn-outline-bms` | Nút viền đỏ |
| `btn-sm-icon` | Nút icon nhỏ |
| `badge-active` | Badge xanh "Đang làm" |
| `badge-inactive` | Badge đỏ "Đã khóa" |
| `stat-card` | Card thống kê |
| `form-card` | Khung form |

---

## Commit code của bạn

```powershell
git add .
git commit -m "feat: ten-phan-cua-ban (mo ta ngan)"
git push
```

> KHÔNG commit: `appsettings.Development.json`, `bin/`, `obj/`
