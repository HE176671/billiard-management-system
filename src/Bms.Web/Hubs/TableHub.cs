using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bms.Web.Hubs;

/// <summary>
/// TV5 — kênh realtime cho sơ đồ bàn. Server phát "TablesChanged" mỗi khi có thao tác bàn thành công;
/// mọi màn Quản lý bàn đang mở sẽ tải lại lưới bàn ngay, không phải chờ vòng polling 10 giây.
/// </summary>
[Authorize(Roles = "Staff,Manager")]
public class TableHub : Hub
{
    public const string Path = "/hubs/table";
    public const string TablesChangedEvent = "TablesChanged";
}
