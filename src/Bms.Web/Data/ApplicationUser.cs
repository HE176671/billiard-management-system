using Microsoft.AspNetCore.Identity;

namespace Bms.Web.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? EmployeeCode { get; set; }
    public DateOnly? HireDate { get; set; }
    // ---- Loyalty (TV2) ----
    public int? TierId { get; set; }
    public MembershipTier? Tier { get; set; }
    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }
    public DateTime? LastPaidDate { get; set; }

    public ICollection<PointTransaction> PointTransactions { get; set; } = new List<PointTransaction>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
