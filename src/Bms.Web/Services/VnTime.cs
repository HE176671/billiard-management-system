namespace Bms.Web.Services;

/// <summary>Quy đổi giờ Việt Nam (UTC+7, không có giờ mùa hè). DB luôn lưu UTC.</summary>
public static class VnTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime ToVn(this DateTime utc) => utc + Offset;
    public static DateTime? ToVn(this DateTime? utc) => utc.HasValue ? utc.Value + Offset : null;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow + Offset);
    /// <summary>Thời điểm UTC tương ứng 00:00 giờ Việt Nam của ngày.</summary>
    public static DateTime StartOfDayUtc(DateOnly vnDate) => vnDate.ToDateTime(TimeOnly.MinValue) - Offset;
}
