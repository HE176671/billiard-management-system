using Microsoft.AspNetCore.Identity;

namespace Bms.Web.Data;

public class ApplicationUser : IdentityUser
{
    public const string SourceSelf = "Self";
    public const string SourceCounter = "Counter";

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

    // ---- CRM & Loyalty V2 ----
    public DateOnly? DateOfBirth { get; set; }
    public bool MarketingOptIn { get; set; } = true;
    public string AccountSource { get; set; } = SourceSelf;
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? TierAchievedAtUtc { get; set; }
    public DateTime? TierReviewAtUtc { get; set; }
    public DateTime? TierGraceUntilUtc { get; set; }

    public ICollection<PointTransaction> PointTransactions { get; set; } = new List<PointTransaction>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<CustomerVoucher> Vouchers { get; set; } = new List<CustomerVoucher>();
    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
    public ICollection<CustomerAuditLog> AuditLogs { get; set; } = new List<CustomerAuditLog>();
}
