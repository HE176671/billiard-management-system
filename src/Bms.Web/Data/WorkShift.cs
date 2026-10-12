namespace Bms.Web.Data;

/// <summary>
/// Ánh xạ bảng dbo.WorkShifts đã có sẵn trong database.
/// Ghi lại ca trực thực tế của nhân viên (mở/đóng ca, tiền quỹ).
/// </summary>
public class WorkShift
{
    public int Id { get; set; }

    public string StaffId { get; set; } = string.Empty;

    public DateTime StartTimeUtc { get; set; }

    public DateTime? EndTimeUtc { get; set; }

    public decimal StartingCash { get; set; }

    public decimal? EndingCash { get; set; }

    /// <summary>"Active" | "Closed"</summary>
    public string Status { get; set; } = "Active";

    public string? Notes { get; set; }

    // Navigation property
    public ApplicationUser Staff { get; set; } = null!;
}
