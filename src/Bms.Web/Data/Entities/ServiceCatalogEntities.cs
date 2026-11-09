global using Bms.Web.Data.Entities;

namespace Bms.Web.Data.Entities;

public class ProductCategory
{
    public const string RentalCueCategoryName = "Gậy cho thuê";

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Product> Products { get; set; } = new();
}
