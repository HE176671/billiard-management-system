namespace Bms.Web.Data;

/// <summary>
/// Nhật ký thao tác nhạy cảm (bảng dbo.AuditLogs). TV5 sở hữu, cả nhóm ghi vào qua IAuditLogService.
/// Chỉ thêm, không sửa, không xóa.
/// </summary>
public class AuditLog
{
    public long     Id           { get; set; }
    /// <summary>Mã thao tác, xem <see cref="Bms.Web.Services.AuditActions"/>.</summary>
    public string   ActionType   { get; set; } = "";
    /// <summary>Loại đối tượng bị tác động: WorkShift, TableType, Product, Invoice...</summary>
    public string   EntityType   { get; set; } = "";
    public string?  EntityId     { get; set; }
    /// <summary>Mô tả ngắn bằng tiếng Việt để Manager đọc.</summary>
    public string   Description  { get; set; } = "";
    public string?  OldValue     { get; set; }
    public string?  NewValue     { get; set; }
    public string?  Reason       { get; set; }
    public string?  ActorId      { get; set; }
    /// <summary>Tên người thao tác tại thời điểm ghi, để log vẫn đọc được nếu tài khoản bị đổi tên.</summary>
    public string?  ActorName    { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
