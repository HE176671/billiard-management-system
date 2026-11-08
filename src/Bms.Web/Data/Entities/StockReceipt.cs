using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("StockReceipts")]
public class StockReceipt
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string ReceiptCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string SupplierName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime ReceiptDateUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(450)]
    public string CreatedById { get; set; } = string.Empty;

    [ForeignKey(nameof(CreatedById))]
    public ApplicationUser? CreatedBy { get; set; }

    public ICollection<StockReceiptDetail> Details { get; set; } = new List<StockReceiptDetail>();
}
