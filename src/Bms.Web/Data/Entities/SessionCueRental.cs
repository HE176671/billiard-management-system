using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

/// <summary>
/// Một dòng thuê gậy theo lượt gắn với phiên chơi. Đơn giá và tên gậy được chụp lại (snapshot) tại thời điểm thuê.
/// Gậy được trừ tồn khi thuê và được cộng trả lại (IsReturned = true) khi đóng bàn.
/// </summary>
[Table("SessionCueRentals")]
public class SessionCueRental
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public PlaySession PlaySession { get; set; } = null!;

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPriceSnapshot { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime RentedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedById { get; set; } = string.Empty;

    public bool IsReturned { get; set; }

    [Column(TypeName = "datetime2(0)")]
    public DateTime? ReturnedAtUtc { get; set; }
}
