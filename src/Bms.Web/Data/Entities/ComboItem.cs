using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("ComboItems")]
public class ComboItem
{
    public int ComboId { get; set; }

    [ForeignKey(nameof(ComboId))]
    [InverseProperty(nameof(Entities.Combo.ComboItems))]
    public Combo Combo { get; set; } = null!;

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
}
