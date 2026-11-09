using System.ComponentModel.DataAnnotations;
using Bms.Web.Services;

namespace Bms.Web.Models;

// ══════════════════════════════════════════════════════
// F&B Ordering ViewModels
// ══════════════════════════════════════════════════════

/// <summary>View model cho trang gọi đồ (/Order).</summary>
public class OrderMenuViewModel
{
    public bool HasActiveSession { get; set; }
    public int? ActiveSessionId  { get; set; }
    public string? TableCode     { get; set; }
    public string? TableTypeName { get; set; }
    public int? FloorNumber      { get; set; }
    public DateTime? StartAtUtc  { get; set; }

    public List<CategoryMenuViewModel> Categories { get; set; } = new();
    public List<CustomerOrderSummaryViewModel> MyOrders { get; set; } = new();

    public decimal TotalFoodAmount => MyOrders
        .Where(o => o.Status == OrderConstants.StatusServed)
        .Sum(o => o.TotalAmount);
}

public class CategoryMenuViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ProductMenuItemViewModel> Products { get; set; } = new();
}

public class ProductMenuItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }

    public bool IsAvailable => IsActive && StockQuantity > 0;
}

public class CustomerOrderSummaryViewModel
{
    public int OrderId { get; set; }
    public DateTime OrderTimeUtc { get; set; }
    public string Status { get; set; } = string.Empty; // Pending | Preparing | Served | Cancelled
    public string StatusDisplay => Status switch
    {
        "Pending"   => "Chờ xử lý",
        "Preparing" => "Đang chuẩn bị",
        "Served"    => "Đã phục vụ",
        "Cancelled" => "Đã hủy",
        _           => Status
    };
    public string StatusBadgeClass => Status switch
    {
        "Pending"   => "badge-warning-bms",
        "Preparing" => "badge-info-bms",
        "Served"    => "badge-active",
        "Cancelled" => "badge-inactive",
        _           => "badge-secondary"
    };

    public List<CustomerOrderItemViewModel> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.Subtotal);
}

public class CustomerOrderItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}

public class SubmitOrderInputModel
{
    public string? IdempotencyToken { get; set; }
    public List<OrderItemInputModel> Items { get; set; } = new();
}
