using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class RoleManagementViewModel
{
    public List<UserRoleRowViewModel> Users { get; set; } = new();
    public string? Search { get; set; }
    public string? RoleFilter { get; set; }
    public int AdminCount { get; set; }
    public int ManagerCount { get; set; }
    public int StaffCount { get; set; }
    public CreateInternalUserInputModel CreateForm { get; set; } = new();
}

public class UserRoleRowViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? EmployeeCode { get; set; }
    public string CurrentRole { get; set; } = "Staff";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateInternalUserInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [RegularExpression(@"^0\d{9}$", ErrorMessage = "SĐT phải gồm 10 chữ số bắt đầu bằng 0.")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn vai trò.")]
    public string Role { get; set; } = "Manager";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [MinLength(8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự.")]
    public string Password { get; set; } = "Manager@123";
}
