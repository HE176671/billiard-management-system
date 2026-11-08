using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("Combos")]
public class Combo
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlaytimeHours { get; set; }

    public int? TableTypeId { get; set; }

    [ForeignKey(nameof(TableTypeId))]
    public TableType? TableType { get; set; }

    public bool IsActive { get; set; } = true;

    [InverseProperty(nameof(ComboItem.Combo))]
    public ICollection<ComboItem> ComboItems { get; set; } = [];
}
