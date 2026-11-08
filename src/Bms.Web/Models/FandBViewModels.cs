using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class ProductViewModel
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public int PriceChangeCount { get; set; }
}

public class ProductInputModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn danh mục")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên món không được để trống")]
    [MaxLength(100, ErrorMessage = "Tên món tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000000, ErrorMessage = "Giá bán phải từ 0 đ trở lên")]
    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(255)]
    public string? PriceChangeReason { get; set; }
}

public class ProductPriceHistoryViewModel
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime EffectiveDateUtc { get; set; }
    public string? ChangedByName { get; set; }
    public string? Note { get; set; }
}

public class ProductCategoryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProductCount { get; set; }
}

public class StockReceiptInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập số hóa đơn / mã phiếu")]
    [MaxLength(50)]
    public string ReceiptCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp")]
    [MaxLength(100)]
    public string SupplierName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; set; }

    public List<StockReceiptItemInputModel> Items { get; set; } = new();
}

public class StockReceiptItemInputModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal CostPrice { get; set; }
}

public class StockReceiptViewModel
{
    public int Id { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public DateTime ReceiptDateUtc { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public List<StockReceiptDetailViewModel> Details { get; set; } = new();
}

public class StockReceiptDetailViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal Subtotal => Quantity * CostPrice;
}

public class StockMovementViewModel
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public int BalanceAfter { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
}

public class ComboInputModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên Combo không được để trống")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000000, ErrorMessage = "Giá gói phải từ 0 đ trở lên")]
    public decimal Price { get; set; }

    [Range(0, 24, ErrorMessage = "Số giờ chơi phải từ 0 đến 24")]
    public decimal PlaytimeHours { get; set; }

    public int? TableTypeId { get; set; }

    public bool IsActive { get; set; } = true;

    public List<ComboItemInputModel> Items { get; set; } = new();
}

public class ComboItemInputModel
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
}

public class ComboViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal PlaytimeHours { get; set; }
    public int? TableTypeId { get; set; }
    public string? TableTypeName { get; set; }
    public bool IsActive { get; set; }
    public List<ComboItemViewModel> Items { get; set; } = new();
}

public class ComboItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int CurrentStock { get; set; }
}

public class OrderItemInputModel
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class QrOrderRequest
{
    public string TableCode { get; set; } = string.Empty;
    public int TableId { get; set; }
    public int ClientSessionId { get; set; }
    public List<OrderItemInputModel> Items { get; set; } = new();
    public string? Note { get; set; }
}

public class OrderViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableStatus { get; set; } = string.Empty;
    public DateTime OrderTimeUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderSource { get; set; } = string.Empty;
    public int? ClientSessionId { get; set; }
    public string? RejectReason { get; set; }
    public string? Note { get; set; }
    public string? CreatedByName { get; set; }
    public decimal TotalAmount => Details.Where(d => d.Status == "Active").Sum(d => (d.Quantity - d.CancelledQuantity) * d.UnitPrice);
    public List<OrderDetailViewModel> Details { get; set; } = new();
}

public class OrderDetailViewModel
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => (Quantity - CancelledQuantity) * UnitPrice;
    public string Status { get; set; } = "Active";
    public int CancelledQuantity { get; set; }
    public int CurrentStock { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancelReason { get; set; }
}

public class StockOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int BalanceAfter { get; set; }

    public static StockOperationResult Ok(int balanceAfter, string msg = "Thành công") =>
        new() { Success = true, BalanceAfter = balanceAfter, Message = msg };

    public static StockOperationResult Fail(string msg) =>
        new() { Success = false, Message = msg };
}

public class OrderOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? OrderId { get; set; }

    public static OrderOperationResult Ok(int? orderId = null, string msg = "Thành công") =>
        new() { Success = true, OrderId = orderId, Message = msg };

    public static OrderOperationResult Fail(string msg) =>
        new() { Success = false, Message = msg };
}

public class FandBServiceResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public static FandBServiceResult Ok(string msg = "Thành công") =>
        new() { Success = true, Message = msg };

    public static FandBServiceResult Fail(string msg) =>
        new() { Success = false, Message = msg };
}

public class QrMenuViewModel
{
    public string TableCode { get; set; } = string.Empty;
    public int TableId { get; set; }
    public string TableTypeName { get; set; } = string.Empty;
    public decimal TableHourlyRate { get; set; }
    public bool HasActiveSession { get; set; }
    public int? SessionId { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public string? StartAtVnDisplay { get; set; }
    public string? CustomerName { get; set; }

    // Tạm tính WF3
    public decimal PlaytimeAmount { get; set; }
    public decimal FandBAmount { get; set; }
    public decimal GrandTotal => PlaytimeAmount + FandBAmount;
    public int PlayedMinutes { get; set; }
    public string? ComboName { get; set; }

    // Thực đơn (Món hết hàng tự ẩn)
    public List<QrCategoryItem> Categories { get; set; } = new();

    // Các đơn đã gọi của phiên
    public List<OrderViewModel> SessionOrders { get; set; } = new();

    public string? ErrorMessage { get; set; }
}

public class QrCategoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<QrProductItem> Products { get; set; } = new();
}

public class QrProductItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Unit { get; set; }
    public string? ImageUrl { get; set; }
    public int StockQuantity { get; set; }
    public string? Description { get; set; }
}
