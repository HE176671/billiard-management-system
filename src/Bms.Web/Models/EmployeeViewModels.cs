using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ── Dùng cho danh sách nhân viên ──
public class EmployeeListViewModel
{
    public List<EmployeeRowViewModel> Employees { get; set; } = new();
    public string? Search { get; set; }
    public string? StatusFilter { get; set; }   // "active" | "inactive" | null = tất cả
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public class EmployeeRowViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateOnly? HireDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// ── Dùng cho form tạo nhân viên ──
public class CreateEmployeeViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên từ 2 đến 100 ký tự.")]
    [RegularExpression(ValidationRules.FullName, ErrorMessage = ValidationRules.FullNameMessage)]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Từ 3–50 ký tự.")]
    [RegularExpression(ValidationRules.UserName, ErrorMessage = ValidationRules.UserNameMessage)]
    [Display(Name = "Tên đăng nhập")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [StringLength(256, ErrorMessage = "Tối đa 256 ký tự.")]
    [RegularExpression(ValidationRules.Email, ErrorMessage = ValidationRules.EmailMessage)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [RegularExpression(ValidationRules.VnPhone, ErrorMessage = ValidationRules.VnPhoneMessage)]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    [StringLength(20)]
    [Display(Name = "Mã nhân viên")]
    public string? EmployeeCode { get; set; }

    [HireDate]
    [DataType(DataType.Date)]
    [Display(Name = "Ngày vào làm")]
    public DateOnly? HireDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự.")]
    [RegularExpression(ValidationRules.Password, ErrorMessage = ValidationRules.PasswordMessage)]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// ── Dùng cho form sửa nhân viên ──
public class EditEmployeeViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên từ 2 đến 100 ký tự.")]
    [RegularExpression(ValidationRules.FullName, ErrorMessage = ValidationRules.FullNameMessage)]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [RegularExpression(ValidationRules.VnPhone, ErrorMessage = ValidationRules.VnPhoneMessage)]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    [StringLength(20)]
    [Display(Name = "Mã nhân viên")]
    public string? EmployeeCode { get; set; }

    [HireDate]
    [DataType(DataType.Date)]
    [Display(Name = "Ngày vào làm")]
    public DateOnly? HireDate { get; set; }

    // Hiển thị để tham khảo, không cho sửa username/email ở đây
    public string? UserName { get; set; }
    public string? Email { get; set; }
}
