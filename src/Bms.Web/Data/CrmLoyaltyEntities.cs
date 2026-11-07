namespace Bms.Web.Data;

/// <summary>Danh mục quà/ưu đãi đổi điểm (thay thế RewardCatalog.cs) - bảng RewardItems.</summary>
public class RewardItemEntity
{
    public const string TypeGift = "Gift";
    public const string TypeFreeHour = "FreeHour";
    public const string TypeBillDiscount = "BillDiscount";

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PointCost { get; set; }
    public string RewardType { get; set; } = TypeGift;
    public decimal? DiscountValue { get; set; }
    public int ValidDays { get; set; } = 30;
    public int? MinTierId { get; set; }
    public MembershipTier? MinTier { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Voucher đã phát cho khách (đổi điểm, sinh nhật, chiến dịch) - bảng CustomerVouchers.</summary>
public class CustomerVoucher
{
    public const string StatusActive = "Active";
    public const string StatusUsed = "Used";
    public const string StatusExpired = "Expired";
    public const string StatusCancelled = "Cancelled";

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }
    public int? RewardItemId { get; set; }
    public RewardItemEntity? RewardItem { get; set; }
    public int? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public string Title { get; set; } = string.Empty;
    public string VoucherType { get; set; } = RewardItemEntity.TypeGift;
    public decimal? DiscountValue { get; set; }
    public string Status { get; set; } = StatusActive;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string? UsedById { get; set; }
    public ApplicationUser? UsedBy { get; set; }
    public string? UsedReferenceCode { get; set; }
    public int? PointTransactionId { get; set; }
    public PointTransaction? PointTransaction { get; set; }
}

/// <summary>Yêu cầu điều chỉnh điểm thủ công (maker-checker) - bảng PointAdjustmentRequests.</summary>
public class PointAdjustmentRequest
{
    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }
    public int Points { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public string RequestedById { get; set; } = string.Empty;
    public ApplicationUser? RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedById { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public int? PointTransactionId { get; set; }
    public PointTransaction? PointTransaction { get; set; }
}

/// <summary>Ghi chú / timeline chăm sóc khách hàng - bảng CustomerNotes.</summary>
public class CustomerNote
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public ApplicationUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Chiến dịch marketing - bảng Campaigns.</summary>
public class Campaign
{
    public const string TypeManual = "Manual";
    public const string TypeBirthday = "Birthday";
    public const string TypeWinBack = "WinBack";

    public const string StatusDraft = "Draft";
    public const string StatusSent = "Sent";
    public const string StatusCancelled = "Cancelled";

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CampaignType { get; set; } = TypeManual;
    public string? TargetSegment { get; set; }
    public int? TargetTierId { get; set; }
    public MembershipTier? TargetTier { get; set; }
    public int? RewardItemId { get; set; }
    public RewardItemEntity? RewardItem { get; set; }
    public int BonusPoints { get; set; }
    public string MessageTitle { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public string Status { get; set; } = StatusDraft;
    public string CreatedById { get; set; } = string.Empty;
    public ApplicationUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public int RecipientCount { get; set; }
}

/// <summary>Nhật ký thay đổi hồ sơ khách hàng - bảng CustomerAuditLogs.</summary>
public class CustomerAuditLog
{
    public long Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? ActorId { get; set; }
    public ApplicationUser? Actor { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

