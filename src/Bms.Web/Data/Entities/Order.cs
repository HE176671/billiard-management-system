using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("Orders")]
public class Order
{
    public const string StatusPending = "Pending";
    public const string StatusCompleted = "Completed";
    public const string StatusRejected = "Rejected";
    public const string StatusCancelled = "Cancelled";

    public const string SourceStaff = "Staff";
    public const string SourceQR = "QR";

    public int Id { get; set; }

    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public PlaySession? Session { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime OrderTimeUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = StatusPending;

    [Required]
    [MaxLength(20)]
    public string OrderSource { get; set; } = SourceStaff;

    public int? ClientSessionId { get; set; }

    [MaxLength(300)]
    public string? RejectReason { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    [MaxLength(450)]
    public string? CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public ApplicationUser? CreatedBy { get; set; }

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
