namespace Bms.Web.Models;

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
    public string? CustomerFullName { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public class OpenSessionRequest
{
    public int TableId { get; set; }
}

public class CloseSessionRequest
{
    public int SessionId { get; set; }
}

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
