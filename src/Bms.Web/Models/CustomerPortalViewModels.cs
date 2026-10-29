namespace Bms.Web.Models;

// ── Hồ sơ + Điểm + Hạng ──
public class CustomerProfileViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? CardCode { get; set; }

    public string? TierName { get; set; }
    public decimal DiscountPercent { get; set; }
    public int PointMultiplier { get; set; } = 1;

    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }

    /// <summary>null = đã ở hạng cao nhất.</summary>
    public string? NextTierName { get; set; }
    public decimal AmountToNextTier { get; set; }
    public int ProgressPercent { get; set; }

    public DateTime? PointsExpireAtUtc { get; set; }
    public List<PointTransactionRowViewModel> Transactions { get; set; } = new();
}

public class PointTransactionRowViewModel
{
    public DateTime CreatedAt { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int Amount { get; set; }
}

// ── Lịch sử chơi & hóa đơn ──
public class HistoryViewModel
{
    public List<HistoryItemViewModel> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    /// <summary>true = đang hiển thị dữ liệu mẫu vì chưa có dữ liệu thật.</summary>
    public bool IsMock { get; set; }
}

public class HistoryItemViewModel
{
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public string SessionStatus { get; set; } = string.Empty;   // Active | Closed

    public int? InvoiceId { get; set; }
    public decimal? TotalPlaytime { get; set; }
    public decimal? TotalFandB { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? InvoiceStatus { get; set; }                  // Paid | Unpaid | null
}

/// <summary>Kết quả map trực tiếp từ truy vấn SQL (PlaySessions LEFT JOIN Invoices) - thuộc phạm vi thành viên khác.</summary>
public class HistoryRow
{
    public int SessionId { get; set; }
    public string? TableCode { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public string SessionStatus { get; set; } = string.Empty;
    public int? InvoiceId { get; set; }
    public decimal? TotalPlaytime { get; set; }
    public decimal? TotalFandB { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? InvoiceStatus { get; set; }
}

// ── Khuyến mãi & đổi điểm ──
public class PromotionsViewModel
{
    public int RewardPoints { get; set; }
    public List<ComboCardViewModel> Combos { get; set; } = new();
    public bool IsMockCombos { get; set; }
}

public class ComboCardViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal PlaytimeHours { get; set; }
}
