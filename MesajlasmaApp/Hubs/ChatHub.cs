using MesajlasmaApp.Data;
using MesajlasmaApp.Infrastructure;
using MesajlasmaApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Hubs;

/// <summary>
/// Gerçek zamanlı bağlantı noktası. Kimlik oturum çerezinden alınır; istemci başka bir
/// kullanıcı adına bağlanamaz. Mesajlar Clients.User(id) ile sadece ilgili kişilere gider.
/// </summary>
[Authorize]
public sealed class ChatHub(AppDbContext db, PresenceTracker presence, ILogger<ChatHub> logger) : Hub
{
    private Guid CurrentUserId => Context.User!.GetUserId();

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;
        var becameOnline = presence.Connected(userId);
        var now = DateTime.UtcNow;

        try
        {
            var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId);
            if (user is null)
            {
                Context.Abort();
                return;
            }

            user.LastSeenAt = now;

            // Kullanıcı bağlandığında bekleyen mesajlar "teslim edildi" olur.
            var pending = await db.ConversationParticipants
                .Where(participant => participant.UserId == userId &&
                                      (participant.LastDeliveredAt == null ||
                                       participant.Conversation.UpdatedAt > participant.LastDeliveredAt))
                .ToListAsync();
            foreach (var participant in pending)
            {
                participant.LastDeliveredAt = now;
            }

            await db.SaveChangesAsync();

            foreach (var participant in pending)
            {
                var others = await db.ConversationParticipants
                    .Where(item => item.ConversationId == participant.ConversationId && item.UserId != userId)
                    .Select(item => item.UserId.ToString())
                    .ToListAsync();
                await Clients.Users(others).SendAsync("ConversationDelivered", new
                {
                    conversationId = participant.ConversationId,
                    userId,
                    deliveredAt = now,
                });
            }

            if (becameOnline && !user.IsInvisible)
            {
                await NotifyPresenceAsync(userId, isOnline: true, now);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bağlantı açılırken hata");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId;
        if (presence.Disconnected(userId))
        {
            try
            {
                var now = DateTime.UtcNow;
                var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId);
                if (user is not null)
                {
                    user.LastSeenAt = now;
                    await db.SaveChangesAsync();
                    if (!user.IsInvisible)
                    {
                        await NotifyPresenceAsync(userId, isOnline: false, now);
                    }
                }
            }
            catch (Exception error)
            {
                logger.LogError(error, "Bağlantı kapanırken hata");
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>"Yazıyor..." bilgisini sohbetteki diğer kişiye iletir.</summary>
    public async Task Typing(Guid conversationId, bool isTyping)
    {
        var userId = CurrentUserId;
        var participants = await ChatQueries.GetParticipantIdsAsync(db, conversationId, userId);
        if (participants is null)
        {
            throw new HubException("Bu sohbete erişiminiz yok");
        }

        await Clients.Users(participants.Where(id => id != userId).Select(id => id.ToString()).ToList())
            .SendAsync("Typing", new { conversationId, userId, isTyping });
    }

    private async Task NotifyPresenceAsync(Guid userId, bool isOnline, DateTime lastSeenAt)
    {
        var contacts = await ChatQueries.GetContactIdsAsync(db, userId);
        if (contacts.Count > 0)
        {
            await Clients.Users(contacts.Select(id => id.ToString()).ToList())
                .SendAsync("PresenceChanged", new { userId, isOnline, lastSeenAt });
        }
    }
}

