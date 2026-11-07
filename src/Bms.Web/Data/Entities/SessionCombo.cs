using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("SessionCombos")]
public class SessionCombo
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    [InverseProperty(nameof(Entities.PlaySession.SessionCombos))]
    public PlaySession PlaySession { get; set; } = null!;

    public int ComboId { get; set; }

    [ForeignKey(nameof(ComboId))]
    public Combo Combo { get; set; } = null!;

    public int Quantity { get; set; }

    [Required]
    [MaxLength(100)]
    public string ComboNameSnapshot { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPriceSnapshot { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlaytimeHoursSnapshot { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime PurchasedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedById { get; set; } = string.Empty;

    [InverseProperty(nameof(SessionComboItem.SessionCombo))]
    public ICollection<SessionComboItem> Items { get; set; } = [];
}
