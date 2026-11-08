using Microsoft.AspNetCore.SignalR;

namespace Bms.Web.Hubs;

public class OrderHub : Hub
{
    public const string GroupStaff = "StaffGroup";

    public async Task JoinStaffGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupStaff);
    }

    public async Task LeaveStaffGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupStaff);
    }
}
