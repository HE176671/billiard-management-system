namespace Bms.Web.Data;

/// <summary>
/// Sổ cái chi phí vận hành &amp; giá vốn quán Bida (bảng dbo.OperatingExpenses).
/// Phục vụ tính toán Báo cáo Lãi/Lỗ (P&amp;L), kiểm soát 4 luồng mất tiền cốt lõi (BR-FIN-03)
/// và quy trình Lập phiếu -> Chỉnh sửa -> Phê duyệt (Pending -> Approved / Rejected).
/// </summary>
public class OperatingExpense
{
    public const string CatMaintenance   = "Maintenance";   // Bảo trì bàn, vải nỉ, gậy, đầu cơ, lơ, bi, điều hòa
    public const string CatInventoryCogs = "InventoryCOGS"; // Giá vốn nhập bia, bò húc, trà đào, nước ngọt, đồ ăn
    public const string CatUtilities     = "Utilities";     // Tiền điện điều hòa/đèn bàn, nước sinh hoạt, internet
    public const string CatPayroll       = "Payroll";       // Lương nhân viên ca ngày / ca đêm / phụ cấp

    public const string ShiftDay   = "Day";
    public const string ShiftNight = "Night";
    public const string ShiftAll   = "All";

    public const string StatusPending  = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    public int       Id            { get; set; }
    public string    ExpenseCode   { get; set; } = string.Empty;
    public DateOnly  ExpenseDate   { get; set; }
    public string    Category      { get; set; } = CatMaintenance;
    public string    Title         { get; set; } = string.Empty;
    public decimal   Amount        { get; set; }
    public int?      TableId       { get; set; }
    public string    ShiftPeriod   { get; set; } = ShiftAll;
    public string?   VendorName    { get; set; }
    public string    Status        { get; set; } = StatusApproved;
    public string?   Notes         { get; set; }
    public string?   CreatedById   { get; set; }
    public DateTime  CreatedAtUtc  { get; set; } = DateTime.UtcNow;
    public string?   ReviewedById  { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string?   ReviewNote    { get; set; }
    public DateTime? UpdatedAtUtc  { get; set; }

    public BilliardTable?   Table      { get; set; }
    public ApplicationUser? CreatedBy  { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }

    public static string GetCategoryDisplayName(string category) => category switch
    {
        CatMaintenance   => "Bảo trì bàn, gậy, vải nỉ & bi",
        CatInventoryCogs => "Giá vốn bia, nước giải khát & F&B",
        CatUtilities     => "Tiền điện điều hòa, đèn bàn & nước",
        CatPayroll       => "Lương nhân sự theo ca (Ngày / Đêm)",
        _                => category
    };

    public static string GetShiftDisplayName(string shift) => shift switch
    {
        ShiftDay   => "Ca Ban Ngày (08:00–18:00)",
        ShiftNight => "Ca Ban Đêm (18:00–02:00)",
        _          => "Toàn thời gian (Cả ngày & đêm)"
    };

    public static string GetStatusDisplayName(string status) => status switch
    {
        StatusPending  => "Chờ CFO phê duyệt",
        StatusApproved => "Đã duyệt (Ghi nhận vào P&L)",
        StatusRejected => "Từ chối thanh toán",
        _              => status
    };
}
