using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("StockReceiptDetails")]
public class StockReceiptDetail
{
    public int Id { get; set; }

    public int ReceiptId { get; set; }

    [ForeignKey(nameof(ReceiptId))]
    public StockReceipt? Receipt { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; private set; }
}
