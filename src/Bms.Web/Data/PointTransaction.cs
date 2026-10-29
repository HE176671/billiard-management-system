namespace Bms.Web.Data;

/// <summary>Lịch sử biến động điểm thưởng - bảng PointTransactions.</summary>
public class PointTransaction
{
    public const string Earn = "Earn";
    public const string Redeem = "Redeem";
    public const string Expire = "Expire";

    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    /// <summary>Số điểm thay đổi (Redeem/Expire lưu số âm).</summary>
    public int Amount { get; set; }

    /// <summary>'Earn', 'Redeem' hoặc 'Expire' (varchar(20)).</summary>
    public string TransactionType { get; set; } = Earn;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
