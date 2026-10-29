namespace Bms.Web.Models;

public class NotificationListViewModel
{
    public List<NotificationItemViewModel> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
}

public class NotificationItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? Type { get; set; }
    public DateTime CreatedAt { get; set; }
}
