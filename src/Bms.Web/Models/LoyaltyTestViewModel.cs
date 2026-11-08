using Bms.Web.Data;
using Bms.Web.Services;

namespace Bms.Web.Models;

public class LoyaltyTestViewModel
{
    // Chế độ khách hàng: "member" (Khách hội viên) | "guest" (Khách lẻ)
    public string CustomerMode { get; set; } = "member";
    public bool IsGuestMode => string.Equals(CustomerMode, "guest", StringComparison.OrdinalIgnoreCase);

    public string? SelectedCustomerId { get; set; }
    public List<CustomerSelectItemViewModel> Customers { get; set; } = new();
    public SelectedCustomerDetailViewModel? CustomerDetail { get; set; }

    // Tích hợp Vận hành bàn (PlaySessions) & Thực đơn F&B (Orders/OrderDetails)
    public int? SelectedSessionId { get; set; }
    public List<CheckoutSessionItemViewModel> ActiveSessions { get; set; } = new();
    public CheckoutSessionItemViewModel? SelectedSession { get; set; }
    public List<SessionOrderLineViewModel> SessionOrderLines { get; set; } = new();

    // Dữ liệu form Quầy thu ngân & Thanh toán (F3)
    public string ReferenceCode { get; set; } = $"HD-{DateTime.UtcNow:yyyyMMddHHmmss}";
    public decimal PlaytimeAmount { get; set; } = 300000;
    public decimal FandBAmount { get; set; } = 80000;
    public int PointsToRedeem { get; set; } = 0;
    public string? VoucherCode { get; set; }
    public List<CustomerVoucher> AvailableVouchers { get; set; } = new();
    public CheckoutLoyaltyPreview? Preview { get; set; }

    // Hình thức thanh toán: "VietQR" (Chuyển khoản qua VietQR) | "Cash" (Tiền mặt)
    public string PaymentMethod { get; set; } = "VietQR";
    public decimal CashReceived { get; set; } = 0;
    public decimal FinalAmount => Preview?.FinalAmount ?? Math.Max(0m, PlaytimeAmount + FandBAmount);
    public decimal ChangeAmount => Math.Max(0m, CashReceived - FinalAmount);
    public bool HasPendingTransfer { get; set; }
    public DateTime? PendingTransferExpiresAtUtc { get; set; }

    // Dữ liệu form cũ để tương thích
    public decimal PaymentFinalAmount { get; set; } = 100000;
    public decimal BillAmount { get; set; } = 200000;

    public List<SimulatedNotificationItemViewModel> CustomerNotifications { get; set; } = new();
}

public class CheckoutSessionItemViewModel
{
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public decimal HourlyRateSnapshot { get; set; }
    public decimal PlaytimeAmount { get; set; }
    public decimal FandBAmount { get; set; }
    public string Status { get; set; } = string.Empty;

    public int ElapsedMinutes
    {
        get
        {
            var end = EndAtUtc ?? DateTime.UtcNow;
            int diff = (int)Math.Round((end - StartAtUtc).TotalMinutes);
            if (diff <= 1 && PlaytimeAmount > 0 && HourlyRateSnapshot > 0)
            {
                int est = (int)Math.Round((PlaytimeAmount / HourlyRateSnapshot) * 60m);
                if (est > diff) return est;
            }
            return Math.Max(1, diff);
        }
    }

    public string PlayDurationText
    {
        get
        {
            int totalMins = ElapsedMinutes;
            int hours = totalMins / 60;
            int mins = totalMins % 60;
            return hours > 0
                ? $"{hours} giờ {mins:00} phút"
                : $"{mins} phút";
        }
    }
}

public class SessionOrderLineViewModel
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class SimulatedNotificationItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerSelectItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? CardCode { get; set; }
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
}

public class SelectedCustomerDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? CardCode { get; set; }
    public string? TierName { get; set; }
    public int PointMultiplier { get; set; }
    public decimal DiscountPercent { get; set; }
    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal Spend12Months { get; set; }
    public DateTime? LastPaidDate { get; set; }
    public List<PointTransactionRowViewModel> RecentTransactions { get; set; } = new();
}
