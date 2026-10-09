using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("PlaySessionTableSegments")]
public class PlaySessionTableSegment
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    [InverseProperty(nameof(Entities.PlaySession.Segments))]
    public PlaySession PlaySession { get; set; } = null!;

    public int TableId { get; set; }

    [ForeignKey(nameof(TableId))]
    public BilliardTable BilliardTable { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRateSnapshot { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime StartAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? EndAtUtc { get; set; }
}
