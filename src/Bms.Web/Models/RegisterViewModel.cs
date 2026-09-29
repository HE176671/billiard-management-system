using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Vui long nhap ho ten.")]
    [StringLength(100)]
    [Display(Name = "Ho va ten")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long nhap ten dang nhap.")]
    [StringLength(50, MinimumLength = 3)]
    [Display(Name = "Ten dang nhap")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long nhap email.")]
    [EmailAddress(ErrorMessage = "Email khong hop le.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "So dien thoai khong hop le.")]
    [StringLength(20)]
    [Display(Name = "So dien thoai")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Vui long nhap mat khau.")]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Mat khau")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long xac nhan mat khau.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mat khau xac nhan khong khop.")]
    [Display(Name = "Xac nhan mat khau")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
