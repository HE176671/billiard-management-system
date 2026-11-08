namespace Bms.Web.Models;

// =========================================================================
// PHẦN 1: VIEWMODELS HIỂN THỊ SƠ ĐỒ BÀN, COMBO & CHI TIẾT PHIÊN CHƠI
// =========================================================================

public class TableManagementViewModel
{
    public List<TableCardViewModel> Tables { get; set; } = new();
    public TableDetailViewModel? SelectedTable { get; set; }
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
}

public class TableCardViewModel
{
    public int Id { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public int FloorNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public int? ActiveSessionId { get; set; }
    public DateTime? SessionStartUtc { get; set; }
    public bool HasCombo { get; set; }
    public int TotalComboMinutes { get; set; }
    public string? ComboSummaryText { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public class TableDetailViewModel
{
    public int TableId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public int? SessionId { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public decimal? HourlyRateSnapshot { get; set; }
    public decimal? PlaytimeAmount { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public decimal ComboAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public int TotalComboMinutes { get; set; }
    public bool HasCombo { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerFullName { get; set; }
    public string? CustomerPhone { get; set; }
    public List<SessionSegmentViewModel> Segments { get; set; } = new();
    public List<SessionComboPurchaseViewModel> Combos { get; set; } = new();
    public List<SessionCueRentalViewModel> CueRentals { get; set; } = new();
    public decimal CueRentalAmount { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public class SessionSegmentViewModel
{
    public string TableCode { get; set; } = string.Empty;
    public string TableTypeName { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
}

public class SessionComboPurchaseViewModel
{
    public int SessionComboId { get; set; }
    public int ComboId { get; set; }
    public string ComboName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
    public decimal PlaytimeHoursPerCombo { get; set; }
    public int TotalMinutes => (int)Math.Round(Quantity * PlaytimeHoursPerCombo * 60m);
    public DateTime PurchasedAtUtc { get; set; }
    public List<SessionComboItemViewModel> Items { get; set; } = new();
}

public class SessionComboItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantityPerCombo { get; set; }
    public int TotalQuantity { get; set; }
}

public class SessionCueRentalViewModel
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
    public DateTime RentedAtUtc { get; set; }
}

public class RentalCueOptionViewModel
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}

public class ActiveComboOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal PlaytimeHours { get; set; }
    public int PlaytimeMinutes => (int)Math.Round(PlaytimeHours * 60m);
    public List<ActiveComboItemOptionViewModel> Items { get; set; } = new();
}

public class ActiveComboItemOptionViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class CustomerLookupItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? UserName { get; set; }
}

// =========================================================================
// PHẦN 2: REQUEST MODELS NHẬN DỮ LIỆU TỪ FORM POST (MỞ, ĐÓNG, CHUYỂN BÀN, COMBO)
// =========================================================================

public class OpenSessionRequest
{
    public int TableId { get; set; }
    /// <summary>
    /// Chế độ mở bàn: "hourly" (Mở bàn tính tiền theo giờ đến khi đóng bàn) hoặc "combo" (Mở bàn áp dụng 1 gói Combo).
    /// </summary>
    public string BillingMode { get; set; } = "hourly";
    public int? ComboId { get; set; }

    /// <summary>
    /// Danh sách gậy thuê theo lượt chọn ngay lúc mở bàn (tuỳ chọn, có thể rỗng).
    /// </summary>
    public List<CueRentalLineRequest> CueRentals { get; set; } = new();
}

public class CueRentalLineRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class AddCueRentalRequest
{
    public int SessionId { get; set; }
    public List<CueRentalLineRequest> CueRentals { get; set; } = new();
}

public class CloseSessionRequest
{
    public int SessionId { get; set; }
}

public class TransferSessionRequest
{
    public int SessionId { get; set; }
    public int NewTableId { get; set; }
}

public class AddSessionComboRequest
{
    public int SessionId { get; set; }
    public int ComboId { get; set; }
    public int Quantity { get; set; } = 1;
}

// =========================================================================
// PHẦN 3: RESULT & SUMMARY MODELS TRẢ KẾT QUẢ VỀ CLIENT
// =========================================================================

public class TableOperationResult
{
    public bool Success { get; set; }
    public int? ErrorCode { get; set; }
    public string? Message { get; set; }
    public bool AutoReload { get; set; }
    public object? Data { get; set; }

    public static TableOperationResult Ok(object? data = null, string? message = null) =>
        new()
        {
            Success = true,
            Data = data,
            Message = message
        };

    public static TableOperationResult Fail(int? errorCode, string? message, bool autoReload = false) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            AutoReload = autoReload
        };
}

public class CloseSummaryViewModel
{
    public int SessionId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public DateTime StartAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public int BilledMinutes { get; set; }
    public bool HasCombo { get; set; }
    public int TotalComboMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public decimal ComboAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public decimal CueRentalAmount { get; set; }
    public List<SessionCueRentalViewModel> CueRentals { get; set; } = new();
}
