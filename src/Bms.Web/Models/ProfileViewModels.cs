using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

/// <summary>UC03 mở rộng: người dùng tự sửa hồ sơ. Username/email/role không sửa ở đây.</summary>
public class EditProfileViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Tối đa 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20, ErrorMessage = "Tối đa 20 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    // Chỉ hiển thị
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? EmployeeCode { get; set; }
    public string RoleLabel { get; set; } = "";
}
