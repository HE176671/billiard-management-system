using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("ComboItems", Schema = "dbo")]
public class ComboItem
{
    [Required]
    [ForeignKey(nameof(Combo))]
    public int ComboId { get; set; }

    [Required]
    [ForeignKey(nameof(Product))]
    public int ProductId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng món trong Combo phải lớn hơn 0.")]
    public int Quantity { get; set; }

    // Navigation properties
    public virtual Combo Combo { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
