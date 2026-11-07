using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

/// <summary>
/// Ánh xạ bảng dbo.OrderDetails.
/// </summary>
[Table("OrderDetails")]
public class OrderDetail
{
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

    // Navigation
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}

