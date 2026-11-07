using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

/// <summary>
/// Ánh xạ bảng dbo.Orders.
/// </summary>
[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    public int SessionId { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime OrderTimeUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string Status { get; set; } = "Pending"; // Pending | Preparing | Served | Cancelled

    // Navigation
    [ForeignKey(nameof(SessionId))]
    public PlaySession Session { get; set; } = null!;

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
