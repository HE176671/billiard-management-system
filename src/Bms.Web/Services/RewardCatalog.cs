namespace Bms.Web.Services;

/// <summary>Một mục trong danh mục đổi điểm lấy quà / dịch vụ.</summary>
public record RewardItem(string Code, string Name, string Description, int Points, string Icon);

/// <summary>Danh mục đổi điểm theo spec: 20 điểm = nước, 50 điểm = đồ ăn, 80 điểm = 1 giờ chơi.</summary>
public static class RewardCatalog
{
    public static readonly IReadOnlyList<RewardItem> Items = new[]
    {
        new RewardItem("DRINK",    "Nước uống",           "Nước ngọt / nước suối / Bò húc",          20, "bi-cup-straw"),
        new RewardItem("SNACK",    "Đồ ăn nhẹ",           "Đĩa trái cây / hướng dương / khô gà",     50, "bi-basket2"),
        new RewardItem("FREEHOUR", "1 giờ chơi miễn phí", "Tặng 1 giờ chơi tại bàn bất kỳ",          80, "bi-stopwatch")
    };

    public static RewardItem? Find(string? code) =>
        Items.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
}
