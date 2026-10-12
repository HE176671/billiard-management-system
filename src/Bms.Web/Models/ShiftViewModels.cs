using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ── Dùng cho danh sách ca làm việc (UC45) ──
public class ShiftListViewModel
{
    public List<ShiftRowViewModel> Shifts { get; set; } = new();
    public string? Search { get; set; }
    public string? StatusFilter { get; set; }   // "active" | "closed" | null = tất cả
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
}

public class ShiftRowViewModel
{
    public int Id { get; set; }
    public string StaffId { get; set; } = string.Empty;
    public string? StaffName { get; set; }
    public string? EmployeeCode { get; set; }
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public decimal StartingCash { get; set; }
    public decimal? EndingCash { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }

    // Giờ theo múi giờ địa phương (UTC+7)
    public DateTime StartTimeLocal => StartTimeUtc.AddHours(7);
    public DateTime? EndTimeLocal  => EndTimeUtc?.AddHours(7);
}

// ── Dùng cho form mở ca (Create) ──
public class CreateShiftViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn nhân viên.")]
    [Display(Name = "Nhân viên")]
    public string StaffId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu ca.")]
    [Display(Name = "Giờ bắt đầu")]
    public DateTime StartTimeLocal { get; set; } = DateTime.UtcNow.AddHours(7);

    [Display(Name = "Tiền quỹ đầu ca (VNĐ)")]
    [Range(0, 999_999_999, ErrorMessage = "Tiền quỹ không hợp lệ.")]
    public decimal StartingCash { get; set; } = 0;

    [StringLength(500, ErrorMessage = "Tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    // Dùng để populate dropdown nhân viên
    public List<StaffSelectItem> StaffList { get; set; } = new();
}

// ── Dùng cho form đóng / chỉnh sửa ca (Edit) ──
public class EditShiftViewModel : IValidatableObject
{
    public int Id { get; set; }
    public string? StaffName { get; set; }
    public string? EmployeeCode { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu ca.")]
    [Display(Name = "Giờ bắt đầu")]
    public DateTime StartTimeLocal { get; set; }

    [Display(Name = "Giờ kết thúc")]
    public DateTime? EndTimeLocal { get; set; }

    [Display(Name = "Tiền quỹ đầu ca (VNĐ)")]
    [Range(0, 999_999_999, ErrorMessage = "Tiền quỹ không hợp lệ.")]
    public decimal StartingCash { get; set; }

    [Display(Name = "Tiền quỹ cuối ca (VNĐ)")]
    [Range(0, 999_999_999, ErrorMessage = "Tiền quỹ không hợp lệ.")]
    public decimal? EndingCash { get; set; }

    [Display(Name = "Trạng thái")]
    public string Status { get; set; } = "Active";

    [StringLength(500, ErrorMessage = "Tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndTimeLocal.HasValue && EndTimeLocal.Value <= StartTimeLocal)
            yield return new ValidationResult(
                "Giờ kết thúc phải sau giờ bắt đầu.",
                new[] { nameof(EndTimeLocal) });
    }
}

public class StaffSelectItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
