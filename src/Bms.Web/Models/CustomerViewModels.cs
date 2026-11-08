using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class CustomerListViewModel
{
    public List<CustomerRowViewModel> Customers  { get; set; } = new();
    public string?                    Search      { get; set; }
    public string?                    StatusFilter{ get; set; }
    public int?                       TierFilter  { get; set; }
    public int                        Page        { get; set; } = 1;
    public int                        TotalPages  { get; set; }
    public int                        TotalCount  { get; set; }
    public List<TierOptionViewModel>  TierOptions { get; set; } = new();
}

public class CustomerRowViewModel
{
    public string   Id           { get; set; } = "";
    public string   FullName     { get; set; } = "";
    public string?  UserName     { get; set; }
    public string?  Email        { get; set; }
    public string?  PhoneNumber  { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string?  CardCode     { get; set; }
    public bool     IsActive     { get; set; }
    public int      RewardPoints { get; set; }
    public string?  TierName     { get; set; }
    public string?  Segment      { get; set; }
    public string?  SegmentStyle { get; set; }
    public decimal  Spend12M     { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class TierOptionViewModel
{
    public int    Id       { get; set; }
    public string TierName { get; set; } = "";
    public string Name     { get => TierName; set => TierName = value; }
}

/// <summary>Admin sửa thông tin liên hệ của khách. Điểm/hạng do TV2 quản lý nên chỉ hiển thị.</summary>
public class EditCustomerViewModel
{
    public string Id { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Tối đa 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20, ErrorMessage = "Tối đa 20 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    // Chỉ hiển thị
    public string? UserName { get; set; }
    public string? TierName { get; set; }
    public int RewardPoints { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// ══════════════════════════════════════════════════════
// UC08 — Hồ sơ cá nhân (Khách hàng tự quản lý)
// ══════════════════════════════════════════════════════

/// <summary>View model cho trang xem hồ sơ (GET /CustomerProfile).</summary>
public class ProfileViewModel
{
    public string FullName     { get; set; } = string.Empty;
    public string? UserName    { get; set; }
    public string? Email       { get; set; }
    public string? PhoneNumber { get; set; }
    public string RankName     { get; set; } = string.Empty;
    public int    TotalPoints  { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? PointsToNextRank { get; set; }   // null = đã hạng cao nhất
}

/// <summary>View model cho form sửa hồ sơ (GET/POST /CustomerProfile/Edit).</summary>
public class CustomerEditProfileViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên từ 2–100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Tối đa 100 ký tự.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải đúng 10 chữ số và bắt đầu bằng 0.")]
    [Display(Name = "Số điện thoại")]
    public string PhoneNumber { get; set; } = string.Empty;

    // Chỉ đọc — hiển thị trên form nhưng không cho sửa
    public string? UserName   { get; set; }
    public string RankName    { get; set; } = string.Empty;
    public int    TotalPoints { get; set; }
}

// ══════════════════════════════════════════════════════
// UC10 — Lịch sử phiên chơi
// ══════════════════════════════════════════════════════

public class PlayHistoryListViewModel
{
    public List<PlayHistoryRowViewModel> Sessions { get; set; } = new();
    public int Page       { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public class PlayHistoryRowViewModel
{
    public int     Id           { get; set; }
    public string  TableCode    { get; set; } = string.Empty;
    public string  TableTypeName{ get; set; } = string.Empty;
    public DateTime StartAtUtc  { get; set; }
    public DateTime? EndAtUtc   { get; set; }
    public string  Status       { get; set; } = string.Empty;   // Active | Closed
    public decimal? PlaytimeAmount { get; set; }
    public decimal FoodAmount   { get; set; }
    public decimal? TotalAmount => PlaytimeAmount.HasValue ? PlaytimeAmount.Value + FoodAmount : (FoodAmount > 0 ? FoodAmount : null);

    public string DurationDisplay => EndAtUtc.HasValue
        ? $"{(int)(EndAtUtc.Value - StartAtUtc).TotalHours:D2}:{(EndAtUtc.Value - StartAtUtc).Minutes:D2}"
        : "—";

    public bool IsActive => Status == "Active";
}

public class PlayHistoryDetailViewModel
{
    public int     Id            { get; set; }
    public string  TableCode     { get; set; } = string.Empty;
    public string  TableTypeName { get; set; } = string.Empty;
    public int     FloorNumber   { get; set; }
    public DateTime StartAtUtc   { get; set; }
    public DateTime? EndAtUtc    { get; set; }
    public decimal HourlyRateSnapshot { get; set; }
    public decimal? PlaytimeAmount    { get; set; }
    public string  Status        { get; set; } = string.Empty;

    public bool IsActive => Status == "Active";

    public string DurationDisplay => EndAtUtc.HasValue
        ? $"{(int)(EndAtUtc.Value - StartAtUtc).TotalHours:D2}:{(EndAtUtc.Value - StartAtUtc).Minutes:D2}"
        : "—";

    public int SessionPoints { get; set; }
    public List<CustomerOrderItemViewModel> OrderedItems { get; set; } = new();
    public decimal FoodAmount => OrderedItems.Sum(x => x.Subtotal);
    public decimal TotalBillAmount => (PlaytimeAmount ?? 0) + FoodAmount;
}

// ══════════════════════════════════════════════════════
// UC09/15 — Điểm thưởng & Hạng thành viên
// ══════════════════════════════════════════════════════

public class LoyaltyViewModel
{
    public int    TotalPoints      { get; set; }
    public string RankName         { get; set; } = string.Empty;
    public int?   PointsToNextRank { get; set; }

    public List<LoyaltyHistoryRowViewModel> History { get; set; } = new();
    public int Page       { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public class LoyaltyHistoryRowViewModel
{
    public int     SessionId     { get; set; }
    public string  TableCode     { get; set; } = string.Empty;
    public DateTime StartAtUtc   { get; set; }
    public DateTime EndAtUtc     { get; set; }
    public decimal PlaytimeAmount { get; set; }
    public int     Points        { get; set; }
}
