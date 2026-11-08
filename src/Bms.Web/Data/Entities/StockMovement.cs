using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("StockMovements")]
public class StockMovement
{
    public const string TypeImport = "Import";
    public const string TypeOrderDeduct = "OrderDeduct";
    public const string TypeOrderCancelReturn = "OrderCancelReturn";
    public const string TypeComboDeduct = "ComboDeduct";
    public const string TypeAdjust = "StockAdjust";
    public const string TypeAuditAdjust = "StockAdjust";

    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int QuantityChange { get; set; }

    public int BalanceAfter { get; set; }

    [Required]
    [MaxLength(30)]
    public string MovementType { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? ReferenceType { get; set; }

    public int? ReferenceId { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public ApplicationUser? CreatedBy { get; set; }
}
