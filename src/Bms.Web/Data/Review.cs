using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Data;

/// <summary>Đánh giá của khách cho một phiên chơi - bảng Reviews.</summary>
public class Review
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    // TODO: Connect real data from Member X - chưa có entity PlaySession trong code,
    // nên SessionId chỉ là cột FK thường (không có navigation).
    public int SessionId { get; set; }

    /// <summary>Số sao 1-5.</summary>
    [Range(1, 5)]
    public byte Rating { get; set; } = 5;

    [MaxLength(500)]
    public string? Comment { get; set; }

    public string? AdminReply { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
