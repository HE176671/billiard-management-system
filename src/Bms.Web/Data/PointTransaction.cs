namespace Bms.Web.Data;

/// <summary>Sổ cái biến động điểm thưởng - bảng PointTransactions.</summary>
public class PointTransaction
{
    public const string Earn = "Earn";
    public const string Redeem = "Redeem";
    public const string Expire = "Expire";
    public const string Adjust = "Adjust";
    public const string Reverse = "Reverse";
    public const string Bonus = "Bonus";

    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    /// <summary>Số điểm thay đổi (Redeem/Expire/Adjust âm lưu số âm).</summary>
    public int Amount { get; set; }

    /// <summary>'Earn', 'Redeem', 'Expire', 'Adjust', 'Reverse', 'Bonus' (varchar(20)).</summary>
    public string TransactionType { get; set; } = Earn;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- Sổ cái V2 ----
    public int BalanceAfter { get; set; }
    public int? InvoiceId { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Reason { get; set; }
    public string? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
    public decimal? SpendAmount { get; set; }
}
