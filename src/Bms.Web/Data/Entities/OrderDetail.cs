using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

/// <summary>
/// Ánh xạ bảng dbo.OrderDetails.
/// </summary>
[Table("OrderDetails")]
public class OrderDetail
{
    public const string StatusActive = "Active";
    public const string StatusCancelled = "Cancelled";

    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Subtotal { get; set; }

    [Required, MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string Status { get; set; } = StatusActive;

    public int CancelledQuantity { get; set; } = 0;

    [Column(TypeName = "datetime2(0)")]
    public DateTime? CancelledAtUtc { get; set; }

    [MaxLength(450)]
    public string? CancelledById { get; set; }

    [ForeignKey(nameof(CancelledById))]
    public ApplicationUser? CancelledBy { get; set; }

    [MaxLength(300)]
    public string? CancelReason { get; set; }

    // Navigation
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
