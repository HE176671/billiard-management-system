using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ── Quản lý đánh giá (Admin & Staff) ──
public class ReviewListAdminViewModel
{
    public List<ReviewItemViewModel> Reviews { get; set; } = new();
    public int? RatingFilter { get; set; }
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }
    public Dictionary<int, int> StarCounts { get; set; } = new();
}

public class ReviewItemViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public byte Rating { get; set; }
    public string? Comment { get; set; }
    public string? AdminReply { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AdminReplyInputModel
{
    public int ReviewId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nội dung phản hồi.")]
    [StringLength(1000, ErrorMessage = "Tối đa 1000 ký tự.")]
    public string Reply { get; set; } = string.Empty;

    public int? RatingFilter { get; set; }
}

// ── Gửi đánh giá (Khách hàng) ──
public class CreateReviewViewModel
{
    public int SessionId { get; set; }
    public string? TableCode { get; set; }

    [Range(1, 5, ErrorMessage = "Đánh giá từ 1 đến 5 sao.")]
    public byte Rating { get; set; } = 5;

    [StringLength(500, ErrorMessage = "Nhận xét tối đa 500 ký tự.")]
    [Display(Name = "Nhận xét")]
    public string? Comment { get; set; }
}
