using Bms.Web.Data;
using Bms.Web.Services;

namespace Bms.Web.Models;

public class LoyaltyTestViewModel
{
    public string? SelectedCustomerId { get; set; }
    public List<CustomerSelectItemViewModel> Customers { get; set; } = new();
    public SelectedCustomerDetailViewModel? CustomerDetail { get; set; }

    // Dữ liệu form Mô phỏng quầy thu ngân (F3)
    public string ReferenceCode { get; set; } = $"SIM-{DateTime.UtcNow:yyyyMMddHHmmss}";
    public decimal PlaytimeAmount { get; set; } = 300000;
    public decimal FandBAmount { get; set; } = 80000;
    public int PointsToRedeem { get; set; } = 0;
    public string? VoucherCode { get; set; }
    public List<CustomerVoucher> AvailableVouchers { get; set; } = new();
    public CheckoutLoyaltyPreview? Preview { get; set; }

    // Dữ liệu form cũ để tương thích
    public decimal PaymentFinalAmount { get; set; } = 100000;
    public decimal BillAmount { get; set; } = 200000;

    // Dữ liệu form giả lập khách hàng gửi đánh giá
    public int ReviewSessionId { get; set; } = 999;
    public byte ReviewRating { get; set; } = 5;
    public string ReviewComment { get; set; } = "Bàn chơi rất êm, nhân viên nhiệt tình, giá hợp lý!";

    public List<SimulatedReviewItemViewModel> RecentReviews { get; set; } = new();
    public List<SimulatedNotificationItemViewModel> CustomerNotifications { get; set; } = new();
}

public class SimulatedReviewItemViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public byte Rating { get; set; }
    public string? Comment { get; set; }
    public string? AdminReply { get; set; }
    public DateTime CreatedAtUtc { get; set; }
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
    public string? CardCode { get; set; }
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
}

public class SelectedCustomerDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
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
