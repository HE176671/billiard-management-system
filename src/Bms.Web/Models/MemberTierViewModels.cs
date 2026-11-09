using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class MemberTierListViewModel
{
    public List<MemberTierRowViewModel> Tiers { get; set; } = new();
}

public class MemberTierRowViewModel
{
    public int     Id              { get; set; }
    public string  TierName        { get; set; } = "";
    public int     MinPoints       { get; set; }
    public decimal DiscountPercent { get; set; }
    public int     UserCount       { get; set; }
}

public class CreateEditTierViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên hạng không được để trống")]
    [MaxLength(50, ErrorMessage = "Tối đa 50 ký tự")]
    public string TierName { get; set; } = "";

    [Range(0, int.MaxValue, ErrorMessage = "Điểm tối thiểu phải >= 0")]
    public int MinPoints { get; set; }

    [Range(0, 100, ErrorMessage = "% giảm giá phải từ 0 đến 100")]
    public decimal DiscountPercent { get; set; }
}
