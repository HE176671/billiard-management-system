namespace Bms.Web.Data;

/// <summary>
/// TV5 — UC40: mục tiêu doanh thu của một tháng (bảng dbo.RevenueTargets).
/// Mỗi tháng tối đa một mục tiêu (unique Year + Month). Doanh thu thực tế không lưu ở đây
/// mà luôn tính lại từ PaymentTransactions, để không bao giờ lệch với Báo cáo doanh thu.
/// </summary>
public class RevenueTarget
{
    public int      Id            { get; set; }
    public int      Year          { get; set; }
    /// <summary>1–12.</summary>
    public int      Month         { get; set; }
    /// <summary>Mục tiêu (VND, làm tròn nghìn).</summary>
    public decimal  TargetAmount  { get; set; }
    public string?  Note          { get; set; }
    public string?  CreatedById   { get; set; }
    public DateTime CreatedAtUtc  { get; set; }
    public string?  UpdatedById   { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
