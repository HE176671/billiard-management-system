using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("Products", Schema = "dbo")]
public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Category))]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên sản phẩm không được để trống.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên sản phẩm tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0.")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm.")]
    public int StockQuantity { get; set; } = 0;

    [Required]
    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Navigation properties
    public virtual ProductCategory Category { get; set; } = null!;
    public virtual ICollection<ComboItem> ComboItems { get; set; } = new List<ComboItem>();
}
