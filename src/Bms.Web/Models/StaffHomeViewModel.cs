using Bms.Web.Services;

namespace Bms.Web.Models;

public class StaffHomeViewModel
{
    public string FullName { get; set; } = "";
    public ShiftSummary? ActiveShift { get; set; }
    /// <summary>Số bàn theo trạng thái: Available (Còn trống), InUse (Đang sử dụng), Maintenance (Bảo trì).</summary>
    public Dictionary<string, int> TableCounts { get; set; } = new();
    public int OverdueComboCount { get; set; }
    public int Count(string status) => TableCounts.GetValueOrDefault(status);
}
