using System.ComponentModel.DataAnnotations;
using Bms.Web.Data;
using Bms.Web.Services;

namespace Bms.Web.Models;

// ── Cấu hình hạng thành viên (F5) ──
public class TierConfigViewModel
{
    public List<TierEditItem> Tiers { get; set; } = new();
    public List<TierImpactItem>? ImpactPreview { get; set; }
}

public class TierEditItem
{
    public int Id { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string SpendThreshold { get; set; } = "0";
    public string DiscountPercent { get; set; } = "0";
    public int PointMultiplier { get; set; } = 1;
    public string BirthdayVoucherValue { get; set; } = "0";
    public int CustomerCount { get; set; }
}

// ── Danh sách khách hàng (Admin) ──
public class CustomerManagementViewModel
{
    public List<CustomerRowViewModel> Customers { get; set; } = new();
    public List<TierOptionViewModel> Tiers { get; set; } = new();
    public string? Search { get; set; }
    public int? TierId { get; set; }
    public string? Status { get; set; }     // "active" | "locked" | null
    public string? Segment { get; set; }    // New | Champion | Loyal | AtRisk | Lost | Regular | null
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public CustomerFormViewModel? Form { get; set; }
}

public class CustomerFormViewModel
{
    public string? Id { get; set; }
    public bool IsNew => string.IsNullOrEmpty(Id);

    [Required(ErrorMessage = "Vui lòng nhập họ và tên khách hàng.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    [RegularExpression(@"^[\p{L}\p{M}]+(\s+[\p{L}\p{M}]+)*$", ErrorMessage = "Họ và tên không được chứa số hoặc ký tự đặc biệt. Vui lòng nhập theo định dạng: Nguyễn Văn A.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm đúng 10 chữ số, bắt đầu bằng số 0 và không chứa chữ cái hay ký tự đặc biệt (Ví dụ: 0901234567).")]
    [Display(Name = "Số điện thoại")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không đúng định dạng cú pháp (Ví dụ: khachhang@gmail.com).")]
    [RegularExpression(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$", ErrorMessage = "Email không đúng định dạng cú pháp (Ví dụ: khachhang@gmail.com).")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public bool MarketingOptIn { get; set; } = true;
    public bool IsActive { get; set; } = true;

    // ── Chỉ đọc ──
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
    public decimal TotalSpend { get; set; }
    public string? CardCode { get; set; }
}

// ── Ghi danh nhanh tại quầy (F1) ──
public class QuickEnrollInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên khách hàng.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    [RegularExpression(@"^[\p{L}\p{M}]+(\s+[\p{L}\p{M}]+)*$", ErrorMessage = "Họ và tên không được chứa số hoặc ký tự đặc biệt. Vui lòng nhập theo định dạng: Nguyễn Văn A.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm đúng 10 chữ số, bắt đầu bằng số 0 và không chứa chữ cái hay ký tự đặc biệt (Ví dụ: 0901234567).")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ Email của khách hàng.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng cú pháp (Ví dụ: khachhang@gmail.com).")]
    [RegularExpression(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$", ErrorMessage = "Email không đúng định dạng cú pháp (Ví dụ: khachhang@gmail.com).")]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ngày tháng năm sinh để đối chiếu CCCD (từ 18 tuổi trở lên).")]
    public DateOnly? DateOfBirth { get; set; }

    public bool IsCccdVerified { get; set; }
    public bool MarketingOptIn { get; set; } = true;
    public string? ReturnTo { get; set; }
    public int? SessionId { get; set; }
}

// ── Customer 360 ViewModel (F1) ──
public class Customer360ViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? CardCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool IsBirthdaySoon { get; set; }
    public int? DaysUntilBirthday { get; set; }
    public bool IsActive { get; set; }
    public bool MarketingOptIn { get; set; }
    public string AccountSource { get; set; } = "Self";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }

    // Tier & Lifecycle
    public string TierName { get; set; } = "Member";
    public decimal TierDiscountPercent { get; set; }
    public int TierMultiplier { get; set; } = 1;
    public DateTime? TierAchievedAtUtc { get; set; }
    public DateTime? TierReviewAtUtc { get; set; }
    public DateTime? TierGraceUntilUtc { get; set; }
    public decimal CurrentTierThreshold { get; set; }
    public string? NextTierName { get; set; }
    public decimal AmountToNextTier { get; set; }
    public decimal AmountToKeepTier { get; set; }
    public int TierProgressPercent { get; set; }

    // RFM & Stats
    public int RewardPoints { get; set; }
    public decimal Spend12Months { get; set; }
    public decimal TotalSpendLifetime { get; set; }
    public int Visits12Months { get; set; }
    public DateTime? LastPaidDate { get; set; }
    public int? RecencyDays { get; set; }
    public string Segment { get; set; } = "Regular";
    public string SegmentBadgeStyle { get; set; } = string.Empty;
    public string SegmentSuggestedAction { get; set; } = string.Empty;

    // Tabs data
    public List<CustomerNote> PinnedNotes { get; set; } = new();
    public List<CustomerNote> Notes { get; set; } = new();
    public List<CustomerVoucher> ActiveVouchers { get; set; } = new();
    public List<CustomerVoucher> AllVouchers { get; set; } = new();
    public List<RewardItemEntity> AvailableRewards { get; set; } = new();
    public List<PointTransaction> LedgerEntries { get; set; } = new();
    public List<HistoryItemViewModel> VisitHistory { get; set; } = new();
    public List<CustomerAuditLog> AuditLogs { get; set; } = new();
    public List<CustomerSelectItemViewModel> MergeCandidates { get; set; } = new();
}

// ── Điều chỉnh điểm thủ công (F2) ──
public class PointAdjustmentIndexViewModel
{
    public List<PointAdjustmentRequest> PendingRequests { get; set; } = new();
    public List<PointAdjustmentRequest> RecentHistory { get; set; } = new();
    public List<CustomerSelectItemViewModel> Customers { get; set; } = new();
    public string? SelectedCustomerId { get; set; }
}

// ── CRM Dashboard (F6) ──
public class CrmDashboardViewModel
{
    public int TotalMembers { get; set; }
    public int NewMembersThisMonth { get; set; }
    public decimal RepeatRate30DaysPercent { get; set; }
    public int RepeatCustomers30Days { get; set; }
    public int ActiveCustomers30Days { get; set; }
    public int TotalCirculatingPoints { get; set; }
    public decimal LiabilityVnd => TotalCirculatingPoints * 1000m;

    public Dictionary<string, int> SegmentCounts { get; set; } = new();
    public List<TierDistributionPoint> TierCounts { get; set; } = new();
    public List<MonthlyPointsFlowPoint> MonthlyPointsFlow { get; set; } = new();
    public List<CustomerRfmProfile> TopSpenders12M { get; set; } = new();
    public List<CustomerRfmProfile> HighValueAtRisk { get; set; } = new();
}

public class MonthlyPointsFlowPoint
{
    public string MonthLabel { get; set; } = string.Empty;
    public int EarnedPoints { get; set; }
    public int RedeemedPoints { get; set; }
}

// ── Chiến dịch (F7) ──
public class CampaignIndexViewModel
{
    public List<Campaign> Campaigns { get; set; } = new();
    public List<TierOptionViewModel> Tiers { get; set; } = new();
    public List<RewardItemEntity> RewardItems { get; set; } = new();
    public CampaignCreateInputModel Form { get; set; } = new();
    public CampaignPreviewResult? Preview { get; set; }
}

public class CampaignCreateInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên chiến dịch.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public string CampaignType { get; set; } = Campaign.TypeManual;
    public string? TargetSegment { get; set; } = "AtRisk";
    public int? TargetTierId { get; set; }
    public int? RewardItemId { get; set; }
    public int BonusPoints { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tiêu đề thông báo.")]
    [StringLength(200)]
    public string MessageTitle { get; set; } = "Ưu đãi đặc biệt dành riêng cho {FullName}!";

    [Required(ErrorMessage = "Vui lòng nhập nội dung thông báo.")]
    [StringLength(1000)]
    public string MessageContent { get; set; } = "Xin chào {FullName} (Hạng {TierName}), quán gửi tặng bạn ưu đãi đặc biệt. Số điểm hiện tại của bạn: {Points} điểm. Hẹn gặp lại bạn tại quán!";
}
