namespace Bms.Web.Data;

public class PricingConfig
{
    public int       Id          { get; set; }
    public int       TableTypeId { get; set; }
    public decimal   HourlyRate  { get; set; }
    public TimeSpan  StartHour   { get; set; }
    public TimeSpan  EndHour     { get; set; }
    public TableType? TableType  { get; set; }
}
