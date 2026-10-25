using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("Bookings")]
public class Booking
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string CustomerId { get; set; } = string.Empty;

    [ForeignKey(nameof(CustomerId))]
    public ApplicationUser Customer { get; set; } = null!;

    public int TableId { get; set; }

    [ForeignKey(nameof(TableId))]
    public BilliardTable BilliardTable { get; set; } = null!;

    [Column(TypeName = "datetime2(0)")]
    public DateTime StartAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime EndAtUtc { get; set; }

    [Required]
    [MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string Status { get; set; } = "Confirmed";

    [Column(TypeName = "datetime2(0)")]
    public DateTime CreatedAtUtc { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? CheckedInAtUtc { get; set; }

    [MaxLength(450)]
    public string? CheckedInById { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? CancelledAtUtc { get; set; }

    [MaxLength(450)]
    public string? CancelledById { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
