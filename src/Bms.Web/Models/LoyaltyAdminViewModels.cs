using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ── Cấu hình hạng thành viên ──
public class TierConfigViewModel
{
    public List<TierEditItem> Tiers { get; set; } = new();
}

/// <summary>
/// Số liệu nhập dưới dạng text và được parse bằng InvariantCulture ở controller,
/// tránh lỗi model binding do khác biệt dấu thập phân giữa các culture.
/// </summary>
public class TierEditItem
{
    public int Id { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string SpendThreshold { get; set; } = "0";
    public string DiscountPercent { get; set; } = "0";
    public int PointMultiplier { get; set; } = 1;
    public int CustomerCount { get; set; }
}

// ── Quản lý khách hàng ──
public class CustomerManagementViewModel
{
    public List<CustomerRowViewModel> Customers { get; set; } = new();
    public List<TierOptionViewModel> Tiers { get; set; } = new();
    public string? Search { get; set; }
    public int? TierId { get; set; }
    public string? Status { get; set; }   // "active" | "locked" | null = tất cả
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    /// <summary>null = chưa chọn khách nào và không ở chế độ thêm mới.</summary>
    public CustomerFormViewModel? Form { get; set; }
}



public class CustomerFormViewModel
{
    public string? Id { get; set; }
    public bool IsNew => string.IsNullOrEmpty(Id);

    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Tối đa 100 ký tự.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Số điện thoại phải gồm đúng 10 chữ số.")]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    /// <summary>Radio: true = Active, false = Locked.</summary>
    public bool IsActive { get; set; } = true;

    // ── Chỉ đọc (tự động, không nhận từ form) ──
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }
    public string? CardCode { get; set; }
}
