namespace Bms.Web.Data;

public class ProductCategory
{
    public const string RentalCueCategoryName = "Gậy cho thuê";

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Product> Products { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class Combo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal PlaytimeHours { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ComboItem> Items { get; set; } = new();
}

public class ComboItem
{
    public int ComboId { get; set; }
    public Combo? Combo { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
}
