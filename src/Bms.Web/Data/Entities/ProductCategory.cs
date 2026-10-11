using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("ProductCategories", Schema = "dbo")]
public class ProductCategory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    [StringLength(60, MinimumLength = 1, ErrorMessage = "Tên danh mục tối đa 60 ký tự.")]
    public string Name { get; set; } = string.Empty;

    // Navigation properties
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
