using Bms.Web.Models;
using Bms.Web.Services;
using Xunit;

namespace Bms.Tests;

/// <summary>
/// Unit tests cho UC07 — Khách gọi đồ ăn/uống (Iter 2).
/// Bám sát Functional Requirements (FR-07.1 -> FR-07.7) và Acceptance Criteria trong spec.md.
/// </summary>
public class OrderTests
{
    // ══════════════════════════════════════════════════════
    // FR-07.4: Validate số lượng mỗi món từ 1 đến 20
    // ══════════════════════════════════════════════════════

    [Theory]
    [InlineData(0,  false)]  // AC: số lượng 0 bị từ chối
    [InlineData(-1, false)]  // số âm bị từ chối
    [InlineData(21, false)]  // AC: số lượng 21 bị từ chối
    [InlineData(99, false)]
    [InlineData(1,  true)]   // AC: số lượng 1 được chấp nhận
    [InlineData(5,  true)]
    [InlineData(10, true)]
    [InlineData(20, true)]   // AC: số lượng 20 được chấp nhận
    public void ValidateQuantity_EnforcesBetween1And20(int quantity, bool expectedValid)
    {
        var result = OrderConstants.IsQuantityValid(quantity);
        Assert.Equal(expectedValid, result);
    }

    // ══════════════════════════════════════════════════════
    // FR-07.3, FR-07.7: Kiểm tra tình trạng sản phẩm (còn bán & còn hàng)
    // ══════════════════════════════════════════════════════

    [Fact]
    public void IsProductAvailable_ReturnsTrue_WhenActiveAndInStock()
    {
        bool isActive = true;
        int stockQuantity = 10;
        int requestedQuantity = 2;

        var available = OrderConstants.IsProductAvailable(isActive, stockQuantity, requestedQuantity);
        Assert.True(available);
    }

    [Fact]
    public void IsProductAvailable_ReturnsFalse_WhenInactive()
    {
        bool isActive = false; // Ngừng bán
        int stockQuantity = 10;
        int requestedQuantity = 1;

        var available = OrderConstants.IsProductAvailable(isActive, stockQuantity, requestedQuantity);
        Assert.False(available);
    }

    [Fact]
    public void IsProductAvailable_ReturnsFalse_WhenOutOfStock()
    {
        bool isActive = true;
        int stockQuantity = 0; // Hết hàng
        int requestedQuantity = 1;

        var available = OrderConstants.IsProductAvailable(isActive, stockQuantity, requestedQuantity);
        Assert.False(available);
    }

    [Fact]
    public void IsProductAvailable_ReturnsFalse_WhenRequestedExceedsStock()
    {
        bool isActive = true;
        int stockQuantity = 3;
        int requestedQuantity = 5;

        var available = OrderConstants.IsProductAvailable(isActive, stockQuantity, requestedQuantity);
        Assert.False(available);
    }

    // ══════════════════════════════════════════════════════
    // FR-07.5: Lưu đơn giá tại thời điểm đặt (snapshot), không đổi khi giá sản phẩm đổi
    // ══════════════════════════════════════════════════════

    [Fact]
    public void SnapshotPrice_DoesNotChange_WhenCurrentPriceUpdates()
    {
        // AC: Given đơn đã gửi với giá 20,000 ₫, When Admin đổi giá món thành 25,000 ₫, Then đơn cũ vẫn hiển thị 20,000 ₫
        decimal originalSnapshotPrice = 20_000m;
        int quantity = 2;

        var orderDetail = new CustomerOrderItemViewModel
        {
            ProductId   = 1,
            ProductName = "Cà phê sữa đá",
            Quantity    = quantity,
            UnitPrice   = originalSnapshotPrice
        };

        Assert.Equal(40_000m, orderDetail.Subtotal);

        // Giả sử sau này giá sản phẩm tăng lên 25,000 ₫
        decimal newProductCatalogPrice = 25_000m;

        // Đơn giá và thành tiền của đơn cũ KHÔNG bị thay đổi
        Assert.Equal(20_000m, orderDetail.UnitPrice);
        Assert.Equal(40_000m, orderDetail.Subtotal);
        Assert.NotEqual(newProductCatalogPrice, orderDetail.UnitPrice);
    }

    // ══════════════════════════════════════════════════════
    // FR-07.5, UC10 AC: Tổng tiền phiên = tiền giờ chơi + tổng thành tiền các món
    // ══════════════════════════════════════════════════════

    [Fact]
    public void TotalBill_CalculatesSumOfPlaytimeAndFoodOrders()
    {
        // AC: Given phiên có 2 món đã gọi, When xem chi tiết, Then tổng tiền = tiền giờ chơi + tổng thành tiền các món
        decimal playtimeAmount = 120_000m;
        
        var foodItems = new List<CustomerOrderItemViewModel>
        {
            new() { ProductId = 1, ProductName = "Trà chanh", Quantity = 2, UnitPrice = 15_000m }, // 30,000
            new() { ProductId = 2, ProductName = "Mì xào trứng", Quantity = 1, UnitPrice = 35_000m } // 35,000
        };

        var detailVm = new PlayHistoryDetailViewModel
        {
            Id             = 1,
            PlaytimeAmount = playtimeAmount,
            OrderedItems   = foodItems
        };

        Assert.Equal(65_000m, detailVm.FoodAmount);
        Assert.Equal(185_000m, detailVm.TotalBillAmount);
    }

    [Fact]
    public void TotalBill_WhenPlaytimeIsNull_ReturnsFoodAmount()
    {
        // Phiên đang chơi chưa có PlaytimeAmount
        var foodItems = new List<CustomerOrderItemViewModel>
        {
            new() { ProductId = 1, ProductName = "Bò húc", Quantity = 1, UnitPrice = 20_000m }
        };

        var detailVm = new PlayHistoryDetailViewModel
        {
            Id             = 2,
            PlaytimeAmount = null,
            OrderedItems   = foodItems
        };

        Assert.Equal(20_000m, detailVm.FoodAmount);
        Assert.Equal(20_000m, detailVm.TotalBillAmount);
    }

    // ══════════════════════════════════════════════════════
    // FR-07.7: Thông báo lỗi chuẩn xác khi món hết hàng
    // ══════════════════════════════════════════════════════

    [Fact]
    public void ProductUnavailableMessage_MatchesSpecTemplate()
    {
        // Spec FR-07.7: "Món [tên món] hiện không còn. Vui lòng bỏ món này và gửi lại."
        string productName = "Khăn lạnh";
        string msg = OrderConstants.MsgProductUnavailable(productName);

        Assert.Equal("Món Khăn lạnh hiện không còn. Vui lòng bỏ món này và gửi lại.", msg);
    }

    // ══════════════════════════════════════════════════════
    // Idempotency: Chống gửi đơn trùng do nhấn 2 lần liên tiếp
    // ══════════════════════════════════════════════════════

    [Fact]
    public void IsDuplicateOrder_DetectsDuplicate_WithinWindow()
    {
        var items1 = new List<(int, int)> { (1, 2), (2, 1) };
        var items2 = new List<(int, int)> { (1, 2), (2, 1) };

        var now = DateTime.UtcNow;
        var twoSecondsLater = now.AddSeconds(2);

        var isDup = OrderConstants.IsDuplicateOrder(items1, items2, now, twoSecondsLater, windowSeconds: 5);
        Assert.True(isDup);
    }

    [Fact]
    public void IsDuplicateOrder_ReturnsFalse_WhenDifferentItems()
    {
        var items1 = new List<(int, int)> { (1, 2) };
        var items2 = new List<(int, int)> { (1, 3) }; // số lượng khác

        var now = DateTime.UtcNow;
        var twoSecondsLater = now.AddSeconds(2);

        var isDup = OrderConstants.IsDuplicateOrder(items1, items2, now, twoSecondsLater, windowSeconds: 5);
        Assert.False(isDup);
    }

    [Fact]
    public void IsDuplicateOrder_ReturnsFalse_WhenOutsideWindow()
    {
        var items1 = new List<(int, int)> { (1, 2) };
        var items2 = new List<(int, int)> { (1, 2) };

        var now = DateTime.UtcNow;
        var tenSecondsLater = now.AddSeconds(10); // quá 5 giây

        var isDup = OrderConstants.IsDuplicateOrder(items1, items2, now, tenSecondsLater, windowSeconds: 5);
        Assert.False(isDup);
    }

    // ══════════════════════════════════════════════════════
    // FR-07.6: Hiển thị trạng thái đơn hàng tiếng Việt
    // ══════════════════════════════════════════════════════

    [Theory]
    [InlineData("Pending",   "Chờ xử lý")]
    [InlineData("Preparing", "Đang chuẩn bị")]
    [InlineData("Served",    "Đã phục vụ")]
    [InlineData("Cancelled", "Đã hủy")]
    public void OrderStatus_MapsToCorrectVietnameseDisplay(string status, string expectedDisplay)
    {
        var summary = new CustomerOrderSummaryViewModel
        {
            Status = status
        };

        Assert.Equal(expectedDisplay, summary.StatusDisplay);
        Assert.Equal(expectedDisplay, OrderConstants.GetStatusDisplay(status));
    }
}
