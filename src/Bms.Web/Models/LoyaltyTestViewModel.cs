namespace Bms.Web.Models;

public class LoyaltyTestViewModel
{
    public string? SelectedCustomerId { get; set; }
    public List<CustomerSelectItemViewModel> Customers { get; set; } = new();

    // Thông tin chi tiết khách hàng đang chọn
    public SelectedCustomerDetailViewModel? CustomerDetail { get; set; }

    // Dữ liệu form giả lập thanh toán
    public decimal PaymentFinalAmount { get; set; } = 100000;

    // Dữ liệu form giả lập đổi điểm hóa đơn
    public decimal BillAmount { get; set; } = 200000;
    public int PointsToRedeem { get; set; } = 50;

    // Dữ liệu form giả lập khách hàng gửi đánh giá
    public int ReviewSessionId { get; set; } = 999;
    public byte ReviewRating { get; set; } = 5;
    public string ReviewComment { get; set; } = "Bàn chơi rất êm, nhân viên nhiệt tình, giá hợp lý!";

    // Danh sách đánh giá gần đây trong hệ thống
    public List<SimulatedReviewItemViewModel> RecentReviews { get; set; } = new();

    // Hộp thư thông báo của khách hàng đang chọn
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
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
}

public class SelectedCustomerDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? TierName { get; set; }
    public int PointMultiplier { get; set; }
    public decimal DiscountPercent { get; set; }
    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }
    public DateTime? LastPaidDate { get; set; }
    public List<PointTransactionRowViewModel> RecentTransactions { get; set; } = new();
}
