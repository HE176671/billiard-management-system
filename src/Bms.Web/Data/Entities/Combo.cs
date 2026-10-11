using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("Combos", Schema = "dbo")]
public class Combo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên gói Combo không được để trống.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên gói Combo tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Giá gói Combo phải lớn hơn hoặc bằng 0.")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(0.01, 24.0, ErrorMessage = "Số giờ chơi trong Combo phải lớn hơn 0.")]
    [Column(TypeName = "decimal(5,2)")]
    public decimal PlaytimeHours { get; set; }

    [Required]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<ComboItem> ComboItems { get; set; } = new List<ComboItem>();
}
