using Microsoft.AspNetCore.SignalR;

namespace NotificationHub.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task AddUser(string userId) => await Groups.AddToGroupAsync(Context.ConnectionId, userId);
    }
}
