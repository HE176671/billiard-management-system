using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class CustomerBookingDashboardViewModel
{
    public DateTime DefaultStartLocal { get; set; }
    public DateTime MinStartLocal { get; set; }
    public DateTime MaxStartLocal { get; set; }
    public List<BookableTableOptionViewModel> AvailableTables { get; set; } = new();
    public List<CustomerBookingItemViewModel> MyBookings { get; set; } = new();
}

public class BookableTableOptionViewModel
{
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal HourlyRate { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public bool IsBookableAtSelectedTime { get; set; }
    public string? UnavailableReason { get; set; }
}

public class CustomerCreateBookingRequest
{
    [Required(ErrorMessage = "Vui lòng chọn bàn.")]
    public int TableId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời gian đến.")]
    public DateTime StartTimeLocal { get; set; }

    [MaxLength(300, ErrorMessage = "Ghi chú tối đa 300 ký tự.")]
    public string? Notes { get; set; }
}

public class CustomerBookingItemViewModel
{
    public int BookingId { get; set; }
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal HourlyRate { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime HoldUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
}

public class StaffBookingManagementViewModel
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? DateFilterLocal { get; set; }
    public List<StaffBookingItemViewModel> Bookings { get; set; } = new();
    public List<AssignableTableOptionViewModel> AvailableTables { get; set; } = new();
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
}

public class StaffBookingItemViewModel
{
    public int BookingId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal HourlyRate { get; set; }
    public string TableCurrentStatus { get; set; } = string.Empty;
    public string TableDisplayStatus { get; set; } = string.Empty;
    public DateTime StartAtUtc { get; set; }
    public DateTime HoldUntilUtc { get; set; }
    public DateTime MustVacateByUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public DateTime? CheckedInAtUtc { get; set; }
    public int? LinkedSessionId { get; set; }
    public string? LinkedSessionStatus { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    // Phân loại tình huống nghiệp vụ (8 tình huống)
    public string ScenarioCode { get; set; } = string.Empty;
    public string ScenarioTitle { get; set; } = string.Empty;
    public string ScenarioBadgeClass { get; set; } = string.Empty;
    public string BusinessGuidance { get; set; } = string.Empty;

    // Quyền thao tác trên giao diện nhân viên
    public bool CanCheckInNormal { get; set; }
    public bool CanCheckInEarly { get; set; }
    public bool CanAssignTableAndOpen { get; set; }
    public bool CanCancel { get; set; }
}

public class AssignableTableOptionViewModel
{
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal HourlyRate { get; set; }
}

public class CheckInBookingRequest
{
    public int BookingId { get; set; }
    public bool AllowEarly { get; set; }
}

public class AssignTableSessionRequest
{
    public int BookingId { get; set; }
    public int TableId { get; set; }
}

public class CancelBookingRequest
{
    public int BookingId { get; set; }
    [MaxLength(300)]
    public string? Reason { get; set; }
}
