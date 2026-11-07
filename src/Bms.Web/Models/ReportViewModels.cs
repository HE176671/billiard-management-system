namespace Bms.Web.Models;

public class RevenueReportViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string GroupBy { get; set; } = "day";
    public decimal TotalRevenue { get; set; }
    public int PaymentCount { get; set; }
    public int InvoiceCount { get; set; }
    public List<RevenuePeriodRow> Periods { get; set; } = new();
    public List<RevenueMethodRow> Methods { get; set; } = new();
}
public record RevenuePeriodRow(string Period, decimal Revenue, int PaymentCount);
public record RevenueMethodRow(string Method, decimal Revenue, int PaymentCount);
