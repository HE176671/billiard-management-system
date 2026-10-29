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

    /// <summary>Ngưỡng TotalSpend tối thiểu để đạt hạng này (decimal(18,2)).</summary>
    public decimal SpendThreshold { get; set; }

    /// <summary>Hệ số nhân điểm thưởng.</summary>
    public int PointMultiplier { get; set; } = 1;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
