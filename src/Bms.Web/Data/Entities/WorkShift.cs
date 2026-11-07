namespace Bms.Web.Data;

/// <summary>Ca làm của nhân viên (bảng dbo.WorkShifts). TV5 sở hữu.</summary>
public class WorkShift
{
    public const string StatusActive = "Active";
    public const string StatusClosed = "Closed";

    public int       Id            { get; set; }
    public string    StaffId       { get; set; } = "";
    public DateTime  StartTimeUtc  { get; set; }
    public DateTime? EndTimeUtc    { get; set; }
    /// <summary>Tiền mặt có trong két lúc mở ca.</summary>
    public decimal   StartingCash  { get; set; }
    /// <summary>Tiền mặt nhân viên đếm thực tế lúc đóng ca.</summary>
    public decimal?  EndingCash    { get; set; }
    /// <summary>Snapshot lúc đóng ca: tổng giao dịch Cash gắn với ca.</summary>
    public decimal?  CashCollected { get; set; }
    /// <summary>Snapshot lúc đóng ca: StartingCash + CashCollected.</summary>
    public decimal?  ExpectedCash  { get; set; }
    public string    Status        { get; set; } = StatusActive;
    public string?   Notes         { get; set; }
    public string?   ClosedById    { get; set; }

    public ApplicationUser? Staff    { get; set; }
    public ApplicationUser? ClosedBy { get; set; }

    /// <summary>Chênh lệch = thực tế - kỳ vọng. Dương là thừa, âm là thiếu.</summary>
    public decimal? CashDifference => EndingCash - ExpectedCash;
}
