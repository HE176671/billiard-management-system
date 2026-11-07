using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Data;

/// <summary>Hạng thành viên (Member / Silver / Gold) - bảng MembershipTiers.</summary>
public class MembershipTier
{
    public int Id { get; set; }
    [MaxLength(50)]
    public string TierName { get; set; } = string.Empty;

    /// <summary>% giảm tiền giờ chơi (decimal(5,2)).</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Số điểm tối thiểu (dùng cho MemberTierController của Hung).</summary>
    public int MinPoints { get; set; }

    /// <summary>Ngưỡng chi tiêu 12 tháng gần nhất để đạt/giữ hạng này (decimal(18,2)).</summary>
    public decimal SpendThreshold { get; set; }

    /// <summary>Hệ số nhân điểm thưởng.</summary>
    public int PointMultiplier { get; set; } = 1;

    /// <summary>Giá trị voucher sinh nhật (VND).</summary>
    public decimal BirthdayVoucherValue { get; set; }

    /// <summary>Thứ tự sắp xếp hạng tăng dần.</summary>
    public int SortOrder { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
