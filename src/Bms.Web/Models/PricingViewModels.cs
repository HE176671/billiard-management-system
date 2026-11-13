using System.ComponentModel.DataAnnotations;
using Bms.Web.Data;

namespace Bms.Web.Models;

public class PricingListViewModel
{
    public string ActiveTab { get; set; } = "tables";
    public List<PricingRowViewModel> Configs { get; set; } = new();
    public List<TableTypeOptionViewModel> TableTypes { get; set; } = new();

    // 1. Giá giờ chuẩn theo từng loại bàn (Pool, Carom...)
    public List<TableType> TableTypeRates { get; set; } = new();

    // 2. Giá Gậy cho thuê theo lượt
    public List<Product> RentalCues { get; set; } = new();
    public Product? EditCue { get; set; }

    // 3. Khuyến mãi Combo Giờ chơi tặng kèm Nước / Đồ ăn
    public List<ComboRowViewModel> Combos { get; set; } = new();
    public List<Product> AvailableBonusProducts { get; set; } = new();
    public SaveComboInputModel? EditCombo { get; set; }

    // 4. Quản lý Danh sách Bàn Bi-a (Manager tạo thêm bàn & điều chỉnh trạng thái bàn)
    public List<BilliardTableRowViewModel> Tables { get; set; } = new();
    public SaveBilliardTableInputModel? EditTable { get; set; }
}

public class BilliardTableRowViewModel
{
    public int Id { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public int TableTypeId { get; set; }
    public string TableTypeName { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public int FloorNumber { get; set; } = 1;
    public string Status { get; set; } = "Available";
    public bool IsComboOverdue { get; set; }
    public bool CanModifyStatus => Status is "Available" or "Maintenance";
}

public class SaveBilliardTableInputModel
{
    public int Id { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public int TableTypeId { get; set; }
    public int FloorNumber { get; set; } = 1;
    public string Status { get; set; } = "Available";
}

public class PricingRowViewModel
{
    public int Id { get; set; }
    public string TableTypeName { get; set; } = "";
    public decimal HourlyRate { get; set; }
    public string StartHour { get; set; } = "";
    public string EndHour { get; set; } = "";
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
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ComboRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PlaytimeHours { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public string IncludedItemsSummary { get; set; } = string.Empty;
    public int? PrimaryProductId { get; set; }
    public int PrimaryQuantity { get; set; } = 1;
}

public class SaveComboInputModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PlaytimeHours { get; set; } = 2.0m;
    public decimal Price { get; set; } = 190000m;
    public int? BonusProductId { get; set; }
    public int BonusQuantity { get; set; } = 2;
    public bool IsActive { get; set; } = true;
}
