namespace Bms.Web.Models;

/// <summary>TV5 — màn hình Nhật ký hệ thống (UC38 View Audit Log).</summary>
public class AuditLogListViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? ActionType { get; set; }
    public string? ActorId { get; set; }
    public string? Search { get; set; }

    public List<AuditLogRowViewModel> Rows { get; set; } = new();
    public List<StaffOptionViewModel> ActorOptions { get; set; } = new();
    public IReadOnlyDictionary<string, string> ActionOptions { get; set; } = new Dictionary<string, string>();

    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}

public class AuditLogRowViewModel
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string ActionType { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string Description { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public string? ActorName { get; set; }
}
