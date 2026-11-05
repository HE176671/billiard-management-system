using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data;

/// <summary>
/// Ánh xạ bảng dbo.BilliardTables (chỉ đọc từ góc Customer).
/// </summary>
[Table("BilliardTables")]
public class BilliardTable
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TableCode { get; set; } = string.Empty;

    public int TableTypeId { get; set; }

    public int FloorNumber { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;   // Available | InUse | AwaitingPayment | Maintenance | Inactive

    public TableType TableType { get; set; } = null!;
}

/// <summary>
/// Ánh xạ bảng dbo.TableTypes.
/// </summary>
[Table("TableTypes")]
public class TableType
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRate { get; set; }
}

/// <summary>
/// Ánh xạ bảng dbo.PlaySessions.
/// CustomerId có thể NULL (walk-in không cần tài khoản).
/// </summary>
[Table("PlaySessions")]
public class PlaySession
{
    [Key]
    public int Id { get; set; }

    public int TableId { get; set; }

    public int? BookingId { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    [Required, MaxLength(450)]
    public string OpenedById { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ClosedById { get; set; }

    public DateTime StartAtUtc { get; set; }

    public DateTime? EndAtUtc { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyRateSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PlaytimeAmount { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;   // Active | Closed

    // Navigation
    public BilliardTable Table { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

/// <summary>
/// Ánh xạ bảng dbo.ProductCategories.
/// </summary>
[Table("ProductCategories")]
public class ProductCategory
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>
/// Ánh xạ bảng dbo.Products.
/// </summary>
[Table("Products")]
public class Product
{
    [Key]
    public int Id { get; set; }

    public int CategoryId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public ProductCategory Category { get; set; } = null!;
}

/// <summary>
/// Ánh xạ bảng dbo.Orders.
/// </summary>
[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    public int SessionId { get; set; }

    public DateTime OrderTimeUtc { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending"; // Pending | Preparing | Served | Cancelled

    // Navigation
    public PlaySession Session { get; set; } = null!;
    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}

/// <summary>
/// Ánh xạ bảng dbo.OrderDetails.
/// </summary>
[Table("OrderDetails")]
public class OrderDetail
{
    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Subtotal { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

/// <summary>
/// Ánh xạ bảng dbo.Invoices.
/// </summary>
[Table("Invoices")]
public class Invoice
{
    [Key]
    public int Id { get; set; }

    public int SessionId { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPlaytime { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalFandB { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(20,2)")]
    public decimal? FinalAmount { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Unpaid"; // Unpaid | Paid | Cancelled

    public DateTime CreatedAtUtc { get; set; }

    // Navigation
    public PlaySession Session { get; set; } = null!;
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}

/// <summary>
/// Ánh xạ bảng dbo.PaymentTransactions.
/// </summary>
[Table("PaymentTransactions")]
public class PaymentTransaction
{
    [Key]
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    [Required, MaxLength(20)]
    public string PaymentMethod { get; set; } = string.Empty; // Cash | VNPay

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountPaid { get; set; }

    [MaxLength(100)]
    public string? GatewayRefCode { get; set; }

    public DateTime TransactionTimeUtc { get; set; }

    // Navigation
    public Invoice Invoice { get; set; } = null!;
}

/// <summary>
/// Ánh xạ bảng dbo.MembershipTiers.
/// </summary>
[Table("MembershipTiers")]
public class MembershipTier
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string TierName { get; set; } = string.Empty;

    public int MinPoints { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPercent { get; set; }
}

/// <summary>
/// Ánh xạ bảng dbo.Combos.
/// </summary>
[Table("Combos")]
public class Combo
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlaytimeHours { get; set; }

    public bool IsActive { get; set; } = true;
}
