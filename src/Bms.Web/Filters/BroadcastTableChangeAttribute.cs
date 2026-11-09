using Bms.Web.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;

namespace Bms.Web.Filters;

/// <summary>
/// TV5 — gắn lên controller thao tác bàn: sau mỗi request POST chạy xong không lỗi,
/// phát sự kiện "TablesChanged" qua <see cref="TableHub"/> để các màn sơ đồ bàn tự làm mới.
/// </summary>
public sealed class BroadcastTableChangeAttribute : TypeFilterAttribute
{
    public BroadcastTableChangeAttribute() : base(typeof(BroadcastTableChangeFilter)) { }
}

internal sealed class BroadcastTableChangeFilter : IAsyncActionFilter
{
    private readonly IHubContext<TableHub> _hub;
    private readonly ILogger<BroadcastTableChangeFilter> _logger;

    public BroadcastTableChangeFilter(IHubContext<TableHub> hub, ILogger<BroadcastTableChangeFilter> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method) || executed.Exception != null)
            return;

        try
        {
            await _hub.Clients.All.SendAsync(TableHub.TablesChangedEvent,
                new { action = context.ActionDescriptor.RouteValues["action"], atUtc = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            // Realtime chỉ là tiện ích; lỗi phát sự kiện không được làm hỏng thao tác bàn.
            _logger.LogWarning(ex, "Không phát được sự kiện TablesChanged.");
        }
    }
}
