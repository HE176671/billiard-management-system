namespace Bms.Web.Data;

/// <summary>Thông báo gửi tới người dùng - bảng Notifications.</summary>
public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }

    /// <summary>Loại thông báo (varchar(50), nullable). Ví dụ: ReviewReply, TierUpgrade.</summary>
    public string? Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
