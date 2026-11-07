using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("SessionComboItems")]
public class SessionComboItem
{
    public int SessionComboId { get; set; }

    [ForeignKey(nameof(SessionComboId))]
    [InverseProperty(nameof(Entities.SessionCombo.Items))]
    public SessionCombo SessionCombo { get; set; } = null!;

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    public int QuantityPerCombo { get; set; }
}
