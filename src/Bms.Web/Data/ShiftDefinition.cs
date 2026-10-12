using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Data;

/// <summary>
/// Định nghĩa ca làm việc (catalog) - UC45.
/// Bảng: ShiftDefinitions
/// </summary>
public class ShiftDefinition
{
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string ShiftCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ShiftName { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu ca (HH:mm, lưu dạng TimeOnly / time SQL Server)</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Giờ kết thúc ca</summary>
    public TimeOnly EndTime { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
