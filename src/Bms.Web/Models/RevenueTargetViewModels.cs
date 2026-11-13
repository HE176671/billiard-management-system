using System.ComponentModel.DataAnnotations;
using Bms.Web.Services;

namespace Bms.Web.Models;

/// <summary>TV5 — UC40: trang danh sách mục tiêu doanh thu.</summary>
public class RevenueTargetIndexViewModel
{
    public RevenueTargetProgress? Current { get; set; }
    public List<RevenueTargetProgress> Items { get; set; } = new();
    public int AchievedCount => Items.Count(i => i.State == TargetMonthState.Ended && i.Achieved);
    public int EndedCount => Items.Count(i => i.State == TargetMonthState.Ended);
}

/// <summary>Form thêm/sửa mục tiêu. Kiểm tra ở 2 lớp: DataAnnotations (client + server) và RevenueTargetController (nghiệp vụ).</summary>
public class RevenueTargetFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn năm.")]
    [Range(2000, 9998, ErrorMessage = "Năm không hợp lệ.")]
    [Display(Name = "Năm")]
    public int? Year { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn tháng.")]
    [Range(1, 12, ErrorMessage = "Tháng phải từ 1 đến 12.")]
    [Display(Name = "Tháng")]
    public int? Month { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mục tiêu doanh thu.")]
    [Range(typeof(decimal), "1000000", "100000000000", ErrorMessage = "Mục tiêu phải từ 1.000.000 đến 100.000.000.000 VND.")]
    [Display(Name = "Mục tiêu doanh thu (VND)")]
    public decimal? TargetAmount { get; set; }

    [StringLength(300, ErrorMessage = "Ghi chú tối đa 300 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    /// <summary>Bắt buộc khi sửa mục tiêu của tháng đang chạy (kiểm tra trong controller).</summary>
    [StringLength(300, ErrorMessage = "Lý do tối đa 300 ký tự.")]
    [Display(Name = "Lý do điều chỉnh")]
    public string? Reason { get; set; }

    // Chỉ để hiển thị khi sửa
    public bool IsEdit => Id > 0;
    public TargetMonthState? State { get; set; }
    public decimal? OldAmount { get; set; }
    public decimal? ActualRevenue { get; set; }
}
