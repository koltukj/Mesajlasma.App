using MesajlasmaApp.Data;
using MesajlasmaApp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Services;

/// <summary>Sunucudan istemcilere gerçek zamanlı olay gönderen yardımcı.</summary>
public sealed class ChatNotifier(IHubContext<ChatHub> hubContext)
{
    public Task SendToUsersAsync(IEnumerable<Guid> userIds, string method, object payload)
    {
        var ids = userIds.Distinct().Select(id => id.ToString()).ToList();
        return ids.Count == 0 ? Task.CompletedTask : hubContext.Clients.Users(ids).SendAsync(method, payload);
    }
}

public static class ChatQueries
{
    /// <summary>Kullanıcıyla en az bir sohbeti olan diğer kullanıcılar.</summary>
    public static Task<List<Guid>> GetContactIdsAsync(AppDbContext db, Guid userId) =>
        db.ConversationParticipants
            .Where(participant => participant.UserId != userId &&
                                  participant.Conversation.Participants.Any(other => other.UserId == userId))
            .Select(participant => participant.UserId)
            .Distinct()
            .ToListAsync();

    /// <summary>Kullanıcı sohbetin katılımcısıysa tüm katılımcıların ID'lerini, değilse null döndürür.</summary>
    public static async Task<List<Guid>?> GetParticipantIdsAsync(AppDbContext db, Guid conversationId, Guid userId)
    {
        var ids = await db.ConversationParticipants
            .Where(participant => participant.ConversationId == conversationId)
            .Select(participant => participant.UserId)
            .ToListAsync();
        return ids.Contains(userId) ? ids : null;
    }
}

