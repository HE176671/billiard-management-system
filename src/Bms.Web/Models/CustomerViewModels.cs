using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class CustomerListViewModel
{
    public List<CustomerRowViewModel> Customers  { get; set; } = new();
    public string?                    Search      { get; set; }
    public string?                    StatusFilter{ get; set; }
    public int?                       TierFilter  { get; set; }
    public int                        Page        { get; set; } = 1;
    public int                        TotalPages  { get; set; }
    public int                        TotalCount  { get; set; }
    public List<TierOptionViewModel>  TierOptions { get; set; } = new();
}

public class CustomerRowViewModel
{
    public string   Id           { get; set; } = "";
    public string   FullName     { get; set; } = "";
    public string?  UserName     { get; set; }
    public string?  Email        { get; set; }
    public string?  PhoneNumber  { get; set; }
    public bool     IsActive     { get; set; }
    public int      RewardPoints { get; set; }
    public string?  TierName     { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class TierOptionViewModel
{
    public int    Id       { get; set; }
    public string TierName { get; set; } = "";
    public string Name     { get => TierName; set => TierName = value; }
}

/// <summary>Admin sửa thông tin liên hệ của khách. Điểm/hạng do TV2 quản lý nên chỉ hiển thị.</summary>
public class EditCustomerViewModel
{
    public string Id { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Tối đa 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20, ErrorMessage = "Tối đa 20 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    // Chỉ hiển thị
    public string? UserName { get; set; }
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
