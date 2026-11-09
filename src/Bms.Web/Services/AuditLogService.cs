using System.Security.Claims;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Mã thao tác ghi vào nhật ký. Dùng chung cho cả nhóm để lọc được trên màn hình Nhật ký.</summary>
public static class AuditActions
{
    public const string ShiftOpen          = "ShiftOpen";
    public const string ShiftClose         = "ShiftClose";
    public const string ShiftCashDiff      = "ShiftCashDifference";
    public const string TableRateChange    = "TableRateChange";
    public const string TableTypeChange    = "TableTypeChange";
    public const string TierReviewRun      = "TierReviewRun";
    public const string StockCount         = "StockCount";
    public const string ProductPriceChange = "ProductPriceChange";
    public const string ItemCancel         = "ItemCancel";
    public const string BillCancel         = "BillCancel";
    public const string PointAdjust        = "PointAdjust";
    public const string TierChange         = "TierChange";
    public const string AccountChange      = "AccountChange";

    /// <summary>Tên hiển thị tiếng Việt cho bộ lọc và bảng.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [ShiftOpen]          = "Mở ca",
        [ShiftClose]         = "Đóng ca",
        [ShiftCashDiff]      = "Lệch tiền cuối ca",
        [TableRateChange]    = "Đổi giá giờ bàn",
        [TableTypeChange]    = "Thêm/đổi tên loại bàn",
        [TierReviewRun]      = "Chạy xét hạng",
        [StockCount]         = "Kiểm kê kho",
        [ProductPriceChange] = "Đổi giá món",
        [ItemCancel]         = "Hủy món",
        [BillCancel]         = "Hủy hóa đơn",
        [PointAdjust]        = "Điều chỉnh điểm",
        [TierChange]         = "Thay đổi hạng",
        [AccountChange]      = "Thay đổi tài khoản"
    };

    public static string Label(string action) => Labels.TryGetValue(action, out var l) ? l : action;
}

public interface IAuditLogService
{
    /// <summary>
    /// Thêm một dòng nhật ký vào DbContext nhưng KHÔNG lưu. Dùng khi đang trong transaction/SaveChanges
    /// của bên gọi để nhật ký và dữ liệu nghiệp vụ được lưu cùng lúc.
    /// </summary>
    Task AddAsync(string actionType, string entityType, string? entityId, string description,
        string? oldValue = null, string? newValue = null, string? reason = null, string? actorId = null,
        CancellationToken ct = default);

    /// <summary>Thêm một dòng nhật ký và lưu ngay.</summary>
    Task LogAsync(string actionType, string entityType, string? entityId, string description,
        string? oldValue = null, string? newValue = null, string? reason = null, string? actorId = null,
        CancellationToken ct = default);
}

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditLogService(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task AddAsync(string actionType, string entityType, string? entityId, string description,
        string? oldValue = null, string? newValue = null, string? reason = null, string? actorId = null,
        CancellationToken ct = default)
    {
        actorId ??= _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? actorName = null;
        if (!string.IsNullOrEmpty(actorId))
        {
            actorName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            ActionType   = Cut(actionType, 50)!,
            EntityType   = Cut(entityType, 50)!,
            EntityId     = Cut(entityId, 100),
            Description  = Cut(description, 1000)!,
            OldValue     = Cut(oldValue, 1000),
            NewValue     = Cut(newValue, 1000),
            Reason       = Cut(reason?.Trim(), 500),
            ActorId      = string.IsNullOrEmpty(actorId) ? null : actorId,
            ActorName    = Cut(actorName, 200),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public async Task LogAsync(string actionType, string entityType, string? entityId, string description,
        string? oldValue = null, string? newValue = null, string? reason = null, string? actorId = null,
        CancellationToken ct = default)
    {
        await AddAsync(actionType, entityType, entityId, description, oldValue, newValue, reason, actorId, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Cut(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
