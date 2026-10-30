using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class PricingListViewModel
{
    public List<PricingRowViewModel>    Configs    { get; set; } = new();
    public List<TableTypeOptionViewModel> TableTypes { get; set; } = new();
}

public class PricingRowViewModel
{
    public int     Id            { get; set; }
    public string  TableTypeName { get; set; } = "";
    public decimal HourlyRate    { get; set; }
    public string  StartHour     { get; set; } = "";
    public string  EndHour       { get; set; } = "";
}

public class CreateEditPricingViewModel
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn loại bàn")]
    public int TableTypeId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu")]
    public string StartHour { get; set; } = "08:00";

    [Required(ErrorMessage = "Vui lòng nhập giờ kết thúc")]
    public string EndHour { get; set; } = "22:00";

    [Range(1000, 10_000_000, ErrorMessage = "Giá phải lớn hơn 0")]
    public decimal HourlyRate { get; set; }

    public List<TableTypeOptionViewModel> TableTypes { get; set; } = new();
}

public class TableTypeOptionViewModel
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
