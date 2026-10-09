using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("PlaySessions")]
public class PlaySession
{
    public int Id { get; set; }

    public int TableId { get; set; }

    [ForeignKey(nameof(TableId))]
    [InverseProperty(nameof(Entities.BilliardTable.PlaySessions))]
    public BilliardTable BilliardTable { get; set; } = null!;

    public int? BookingId { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    [Required]
    [MaxLength(450)]
    public string OpenedById { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ClosedById { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime StartAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? EndAtUtc { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRateSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PlaytimeAmount { get; set; }

    [Required]
    [MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string Status { get; set; } = "Active";

    [Required]
    [MaxLength(10)]
    [Column(TypeName = "varchar(10)")]
    public string SessionMode { get; set; } = "Open";

    [Column(TypeName = "datetime2(0)")]
    public DateTime? PlannedEndAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime BillingStartAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? BillingEndAtUtc { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    [InverseProperty(nameof(PlaySessionTableSegment.PlaySession))]
    public ICollection<PlaySessionTableSegment> Segments { get; set; } = [];
}
