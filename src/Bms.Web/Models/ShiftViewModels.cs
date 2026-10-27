using System.ComponentModel.DataAnnotations;
using Bms.Web.Services;

namespace Bms.Web.Models;

public class OpenShiftViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tiền đầu ca.")]
    [Range(0, 1_000_000_000, ErrorMessage = "Tiền đầu ca phải từ 0 đến 1.000.000.000 ₫.")]
    [Display(Name = "Tiền mặt đầu ca (₫)")]
    public decimal? StartingCash { get; set; }

    [StringLength(500, ErrorMessage = "Tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }
}

public class CloseShiftViewModel
{
    public int ShiftId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số tiền đếm thực tế.")]
    [Range(0, 1_000_000_000, ErrorMessage = "Số tiền phải từ 0 đến 1.000.000.000 ₫.")]
    [Display(Name = "Tiền mặt đếm thực tế (₫)")]
    public decimal? EndingCash { get; set; }

    [StringLength(500, ErrorMessage = "Tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú / lý do chênh lệch")]
    public string? Notes { get; set; }
}

/// <summary>Trang "Ca của tôi": chưa mở ca thì hiện form mở, đang mở thì hiện số liệu + form đóng.</summary>
public class ShiftHomeViewModel
{
    public ShiftSummary? Active { get; set; }
    public OpenShiftViewModel Open { get; set; } = new();
    public CloseShiftViewModel Close { get; set; } = new();
    public List<ShiftRowViewModel> Recent { get; set; } = new();
}

public class ShiftDetailsViewModel
{
    public ShiftSummary Summary { get; set; } = null!;
    public bool CanClose { get; set; }
    public CloseShiftViewModel Close { get; set; } = new();
    public List<ShiftPaymentRowViewModel> Payments { get; set; } = new();
}

public class ShiftPaymentRowViewModel
{
    public int      Id            { get; set; }
    public int      InvoiceId     { get; set; }
    public string   PaymentMethod { get; set; } = "";
    public decimal  AmountPaid    { get; set; }
    public DateTime TimeUtc       { get; set; }
}

public class ShiftRowViewModel
{
    public int       Id            { get; set; }
    public string    StaffName     { get; set; } = "";
    public string?   EmployeeCode  { get; set; }
    public DateTime  StartTimeUtc  { get; set; }
    public DateTime? EndTimeUtc    { get; set; }
    public string    Status        { get; set; } = "";
    public decimal   StartingCash  { get; set; }
    public decimal   CashCollected { get; set; }
    public decimal   ExpectedCash  { get; set; }
    public decimal?  EndingCash    { get; set; }
    public decimal?  Difference    => EndingCash - ExpectedCash;
}

public class ShiftListViewModel
{
    public List<ShiftRowViewModel> Shifts { get; set; } = new();
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? StaffId { get; set; }
    public string? Status { get; set; }
    public bool OnlyDifference { get; set; }
    public List<StaffOptionViewModel> StaffOptions { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalCashCollected { get; set; }
    public decimal TotalDifference { get; set; }
    public int ActiveCount { get; set; }
    /// <summary>true = trang Admin xem mọi nhân viên; false = lịch sử của tôi.</summary>
    public bool IsManage { get; set; }
}

public class StaffOptionViewModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
