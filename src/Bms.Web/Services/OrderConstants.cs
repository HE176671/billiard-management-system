namespace Bms.Web.Services;

/// <summary>
/// Hằng số và quy tắc nghiệp vụ cho UC07 — Khách gọi đồ ăn/uống.
/// </summary>
public static class OrderConstants
{
    // ── Giới hạn số lượng món (FR-07.4) ──
    public const int MinQuantityPerItem = 1;
    public const int MaxQuantityPerItem = 20;

    // ── Trạng thái đơn hàng trong DB ──
    public const string StatusPending   = "Pending";
    public const string StatusPreparing = "Preparing";
    public const string StatusServed    = "Served";
    public const string StatusCancelled = "Cancelled";

    // ── Tên hiển thị trạng thái tiếng Việt ──
    public const string DisplayPending   = "Chờ xử lý";
    public const string DisplayPreparing = "Đang chuẩn bị";
    public const string DisplayServed    = "Đã phục vụ";
    public const string DisplayCancelled = "Đã hủy";

    // ── Thông báo chuẩn theo Spec ──
    public const string MsgSessionRequired = "Bạn cần có phiên chơi đang diễn ra để gọi đồ.";
    public const string MsgSessionEnded    = "Phiên chơi đã kết thúc, không thể gọi thêm đồ.";
    public const string MsgEmptyCart       = "Giỏ hàng rỗng. Vui lòng chọn món trước khi gửi đơn.";
    public const string MsgInvalidQuantity = "Số lượng mỗi món phải từ 1 đến 20.";
    public const string MsgOrderSuccess    = "Đã gửi đơn. Nhân viên sẽ phục vụ ngay.";

    public static string MsgProductUnavailable(string productName) 
        => $"Món {productName} hiện không còn. Vui lòng bỏ món này và gửi lại.";

    public static string GetStatusDisplay(string status) => status switch
    {
        StatusPending   => DisplayPending,
        StatusPreparing => DisplayPreparing,
        StatusServed    => DisplayServed,
        StatusCancelled => DisplayCancelled,
        _               => status
    };

    public static bool IsQuantityValid(int quantity)
        => quantity >= MinQuantityPerItem && quantity <= MaxQuantityPerItem;

    public static bool IsProductAvailable(bool isActive, int stockQuantity, int requestedQuantity = 1)
        => isActive && stockQuantity >= requestedQuantity;

    public static decimal CalcItemSubtotal(int quantity, decimal unitPriceSnapshot)
        => quantity * unitPriceSnapshot;

    public static decimal CalcTotalBill(decimal? playtimeAmount, decimal foodAmount)
        => (playtimeAmount ?? 0m) + foodAmount;

    public static bool IsDuplicateOrder(
        IEnumerable<(int ProductId, int Quantity)> recentItems,
        IEnumerable<(int ProductId, int Quantity)> currentItems,
        DateTime recentOrderUtc,
        DateTime currentOrderUtc,
        int windowSeconds = 5)
    {
        if (currentOrderUtc < recentOrderUtc || (currentOrderUtc - recentOrderUtc).TotalSeconds > windowSeconds)
            return false;

        var recentList = recentItems.OrderBy(i => i.ProductId).ToList();
        var currentList = currentItems.OrderBy(i => i.ProductId).ToList();

        return recentList.SequenceEqual(currentList);
    }
}
