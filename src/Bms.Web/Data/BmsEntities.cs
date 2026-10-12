using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data;

/// <summary>
/// Ánh xạ bảng dbo.BilliardTables (chỉ đọc từ góc Customer).
/// </summary>
[Table("BilliardTables")]
public class BilliardTable
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TableCode { get; set; } = string.Empty;

    public int TableTypeId { get; set; }

    public int FloorNumber { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;   // Available | InUse | AwaitingPayment | Maintenance | Inactive

    public TableType TableType { get; set; } = null!;
}

/// <summary>
/// Ánh xạ bảng dbo.TableTypes.
/// </summary>
[Table("TableTypes")]
public class TableType
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRate { get; set; }
}

/// <summary>
/// Ánh xạ bảng dbo.PlaySessions.
/// CustomerId có thể NULL (walk-in không cần tài khoản).
/// </summary>
[Table("PlaySessions")]
public class PlaySession
{
    [Key]
    public int Id { get; set; }

    public int TableId { get; set; }

    public int? BookingId { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    [Required, MaxLength(450)]
    public string OpenedById { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ClosedById { get; set; }

    public DateTime StartAtUtc { get; set; }

    public DateTime? EndAtUtc { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRateSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PlaytimeAmount { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;   // Active | Closed

    // Navigation
    public BilliardTable Table { get; set; } = null!;
}
