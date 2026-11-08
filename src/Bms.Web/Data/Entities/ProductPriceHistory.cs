using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("ProductPriceHistory")]
public class ProductPriceHistory
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OldPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewPrice { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime EffectiveDateUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? ChangedById { get; set; }

    [ForeignKey(nameof(ChangedById))]
    public ApplicationUser? ChangedBy { get; set; }

    [MaxLength(255)]
    public string? Note { get; set; }
}
