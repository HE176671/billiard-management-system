using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ══════════════════════════════════════════════════════
// UC08 — Hồ sơ cá nhân
// ══════════════════════════════════════════════════════

/// <summary>View model cho trang xem hồ sơ (GET /Profile).</summary>
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

/// <summary>View model cho form sửa hồ sơ (GET/POST /Profile/Edit).</summary>
public class EditProfileViewModel
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

/// <summary>View model cho danh sách phiên chơi (GET /PlayHistory).</summary>
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

    // Computed
    public string DurationDisplay => EndAtUtc.HasValue
        ? $"{(int)(EndAtUtc.Value - StartAtUtc).TotalHours:D2}:{(EndAtUtc.Value - StartAtUtc).Minutes:D2}"
        : "—";

    public bool IsActive => Status == "Active";
}

/// <summary>View model cho trang chi tiết phiên chơi (GET /PlayHistory/Detail/id).</summary>
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

    // Điểm nhận được từ phiên này (0 nếu chưa Closed)
    public int SessionPoints { get; set; }

    // Đồ ăn / uống đã gọi trong phiên (UC07)
    public List<CustomerOrderItemViewModel> OrderedItems { get; set; } = new();
    public decimal FoodAmount => OrderedItems.Sum(x => x.Subtotal);
    public decimal TotalBillAmount => (PlaytimeAmount ?? 0) + FoodAmount;
}

// ══════════════════════════════════════════════════════
// UC09/15 — Điểm thưởng & Hạng thành viên
// ══════════════════════════════════════════════════════

/// <summary>View model cho trang /Loyalty.</summary>
public class LoyaltyViewModel
{
    public int    TotalPoints      { get; set; }
    public string RankName         { get; set; } = string.Empty;
    public int?   PointsToNextRank { get; set; }   // null = hạng cao nhất

    public List<LoyaltyHistoryRowViewModel> History { get; set; } = new();
    public int Page       { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>Một dòng lịch sử điểm (= một phiên Closed có điểm).</summary>
public class LoyaltyHistoryRowViewModel
{
    public int     SessionId     { get; set; }
    public string  TableCode     { get; set; } = string.Empty;
    public DateTime StartAtUtc   { get; set; }
    public DateTime EndAtUtc     { get; set; }
    public decimal PlaytimeAmount { get; set; }
    public int     Points        { get; set; }   // FLOOR(PlaytimeAmount / 10000)
}

// ══════════════════════════════════════════════════════
// UC07 — Khách gọi đồ ăn/uống (Iter 2)
// ══════════════════════════════════════════════════════

/// <summary>View model cho trang gọi đồ (/Order).</summary>
public class OrderMenuViewModel
{
    public bool HasActiveSession { get; set; }
    public int? ActiveSessionId  { get; set; }
    public string? TableCode     { get; set; }
    public string? TableTypeName { get; set; }
    public int? FloorNumber      { get; set; }
    public DateTime? StartAtUtc  { get; set; }

    public List<CategoryMenuViewModel> Categories { get; set; } = new();
    public List<CustomerOrderSummaryViewModel> MyOrders { get; set; } = new();

    public decimal TotalFoodAmount => MyOrders
        .Where(o => o.Status != "Cancelled")
        .Sum(o => o.TotalAmount);
}

public class CategoryMenuViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ProductMenuItemViewModel> Products { get; set; } = new();
}

public class ProductMenuItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }

    public bool IsAvailable => IsActive && StockQuantity > 0;
}

public class CustomerOrderSummaryViewModel
{
    public int OrderId { get; set; }
    public DateTime OrderTimeUtc { get; set; }
    public string Status { get; set; } = string.Empty; // Pending | Preparing | Served | Cancelled
    public string StatusDisplay => Status switch
    {
        "Pending"   => "Chờ xử lý",
        "Preparing" => "Đang chuẩn bị",
        "Served"    => "Đã phục vụ",
        "Cancelled" => "Đã hủy",
        _           => Status
    };
    public string StatusBadgeClass => Status switch
    {
        "Pending"   => "badge-warning-bms",
        "Preparing" => "badge-info-bms",
        "Served"    => "badge-active",
        "Cancelled" => "badge-inactive",
        _           => "badge-secondary"
    };

    public List<CustomerOrderItemViewModel> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.Subtotal);
}

public class CustomerOrderItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}

public class SubmitOrderInputModel
{
    public string? IdempotencyToken { get; set; }
    public List<OrderItemInputModel> Items { get; set; } = new();
}

public class OrderItemInputModel
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

