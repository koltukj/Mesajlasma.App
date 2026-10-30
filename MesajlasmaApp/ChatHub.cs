using Microsoft.AspNetCore.SignalR;

namespace MesajlasmaApp;

public sealed class ChatHub : Hub
{
    public Task JoinUser(string userId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
    }

    public Task SendTypingToUser(string recipientId, string senderName)
    {
        return Clients.Group(UserGroup(recipientId)).SendAsync("UserTyping", senderName);
    }

    public Task StopTypingToUser(string recipientId)
    {
        return Clients.Group(UserGroup(recipientId)).SendAsync("UserStoppedTyping");
    }

    private static string UserGroup(string userId) => $"user:{userId}";
}
