using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class CheckoutSessionViewModel
{
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public string Status { get; set; } = string.Empty; // Active | Closed

    // Tiền giờ
    public decimal HourlyRate { get; set; }
    public TimeSpan Duration { get; set; }
    public decimal TotalPlaytime { get; set; }

    // Khách hàng & Hạng thành viên
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string TierName { get; set; } = "Đồng";
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    // Món F&B
    public List<CheckoutFandBItemViewModel> FandBItems { get; set; } = new();
    public decimal TotalFandB { get; set; }

    // Tổng thanh toán
    public decimal FinalAmount => Math.Max(0, (TotalPlaytime + TotalFandB) - DiscountAmount);

    // Trạng thái hóa đơn đã có sẵn (nếu có)
    public int? ExistingInvoiceId { get; set; }
    public string? ExistingInvoiceStatus { get; set; }
}

public class CheckoutFandBItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
    public string OrderStatus { get; set; } = string.Empty;
}

public class InvoiceListViewModel
{
    public List<InvoiceItemViewModel> Invoices { get; set; } = new();
    public string? SearchKeyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

public class InvoiceItemViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal TotalPlaytime { get; set; }
    public decimal TotalFandB { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? PaymentMethod { get; set; }
    public string? GatewayRefCode { get; set; }
}

public class InvoiceDetailViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TotalPlaytime { get; set; }
    public decimal TotalFandB { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public List<CheckoutFandBItemViewModel> FandBItems { get; set; } = new();
    public List<PaymentTransactionViewModel> Transactions { get; set; } = new();
}

public class PaymentTransactionViewModel
{
    public int Id { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string? GatewayRefCode { get; set; }
    public DateTime TransactionTimeUtc { get; set; }
}

