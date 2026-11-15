using MesajlasmaApp.Data;
using MesajlasmaApp.Infrastructure;
using MesajlasmaApp.Models;
using MesajlasmaApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var conversations = app.MapGroup("/api/conversations").RequireAuthorization();
        conversations.MapGet("/", GetConversationsAsync);
        conversations.MapPost("/", CreateConversationAsync);
        conversations.MapGet("/{conversationId:guid}/messages", GetMessagesAsync);
        conversations.MapGet("/{conversationId:guid}/messages/search", SearchMessagesAsync);
        conversations.MapPost("/{conversationId:guid}/messages", SendMessageAsync).RequireRateLimiting(RateLimits.Messages);
        conversations.MapPost("/{conversationId:guid}/attachments", SendAttachmentAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimits.Uploads);
        conversations.MapPost("/{conversationId:guid}/read", MarkReadAsync);

        var messages = app.MapGroup("/api/messages").RequireAuthorization();
        messages.MapPut("/{messageId:guid}", EditMessageAsync).RequireRateLimiting(RateLimits.Messages);
        messages.MapDelete("/{messageId:guid}", DeleteMessageAsync);

        app.MapGet("/api/files/{messageId:guid}", GetAttachmentAsync).RequireAuthorization();
    }

    // ------------------------------------------------------------------ Sohbetler

    private static async Task<IResult> GetConversationsAsync(HttpContext httpContext, AppDbContext db, PresenceTracker presence)
    {
        var userId = httpContext.User.GetUserId();

        var mine = await db.ConversationParticipants.AsNoTracking()
            .Where(participant => participant.UserId == userId)
            .Select(participant => new { participant.ConversationId, participant.Conversation.UpdatedAt })
            .ToListAsync();
        if (mine.Count == 0) return Results.Ok(Array.Empty<ConversationDto>());

        var conversationIds = mine.Select(item => item.ConversationId).ToList();

        var others = await db.ConversationParticipants.AsNoTracking()
            .Include(participant => participant.User)
            .Where(participant => conversationIds.Contains(participant.ConversationId) && participant.UserId != userId)
            .ToListAsync();

        var lastMessages = await db.Messages.AsNoTracking()
            .Where(message => conversationIds.Contains(message.ConversationId) &&
                              !message.HiddenFor.Any(hidden => hidden.UserId == userId))
            .GroupBy(message => message.ConversationId)
            .Select(group => group.OrderByDescending(message => message.SentAt).First())
            .ToListAsync();

        var unreadCounts = await (
                from message in db.Messages
                join participant in db.ConversationParticipants on message.ConversationId equals participant.ConversationId
                where participant.UserId == userId &&
                      message.SenderId != userId &&
                      !message.IsDeleted &&
                      (participant.LastReadAt == null || message.SentAt > participant.LastReadAt) &&
                      !message.HiddenFor.Any(hidden => hidden.UserId == userId)
                group message by message.ConversationId into grouped
                select new { ConversationId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(item => item.ConversationId, item => item.Count);

        var result = new List<ConversationDto>();
        foreach (var item in mine.OrderByDescending(entry => entry.UpdatedAt))
        {
            var other = others.FirstOrDefault(participant => participant.ConversationId == item.ConversationId);
            if (other is null) continue;

            var last = lastMessages.FirstOrDefault(message => message.ConversationId == item.ConversationId);
            result.Add(new ConversationDto(
                item.ConversationId,
                DtoMapper.ToUser(other.User, presence),
                last is null ? null : DtoMapper.ToMessage(last, other.LastDeliveredAt, other.LastReadAt),
                unreadCounts.GetValueOrDefault(item.ConversationId),
                item.UpdatedAt));
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateConversationAsync(
        CreateConversationRequest request, HttpContext httpContext, AppDbContext db, PresenceTracker presence)
    {
        var userId = httpContext.User.GetUserId();
        if (request.UserId == userId)
            return Results.BadRequest(new ApiError("Kendinizle sohbet başlatamazsınız"));

        var otherUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.UserId);
        if (otherUser is null)
            return Results.NotFound(new ApiError("Kullanıcı bulunamadı"));

        var conversationId = await db.Conversations
            .Where(conversation => conversation.Participants.Count == 2 &&
                                   conversation.Participants.Any(participant => participant.UserId == userId) &&
                                   conversation.Participants.Any(participant => participant.UserId == request.UserId))
            .Select(conversation => (Guid?)conversation.Id)
            .FirstOrDefaultAsync();

        if (conversationId is null)
        {
            var conversation = new Conversation();
            conversation.Participants.Add(new ConversationParticipant { UserId = userId });
            conversation.Participants.Add(new ConversationParticipant { UserId = request.UserId });
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            conversationId = conversation.Id;
        }

        var participants = await db.ConversationParticipants.AsNoTracking()
            .Where(participant => participant.ConversationId == conversationId)
            .ToListAsync();
        var other = participants.First(participant => participant.UserId == request.UserId);

        var lastMessage = await db.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId &&
                              !message.HiddenFor.Any(hidden => hidden.UserId == userId))
            .OrderByDescending(message => message.SentAt)
            .FirstOrDefaultAsync();
        var updatedAt = await db.Conversations.Where(item => item.Id == conversationId).Select(item => item.UpdatedAt).FirstAsync();

        return Results.Ok(new ConversationDto(
            conversationId.Value,
            DtoMapper.ToUser(otherUser, presence),
            lastMessage is null ? null : DtoMapper.ToMessage(lastMessage, other.LastDeliveredAt, other.LastReadAt),
            0,
            updatedAt));
    }

    // ------------------------------------------------------------------ Mesajlar

    private static async Task<IResult> GetMessagesAsync(
        Guid conversationId, DateTime? before, int? take, HttpContext httpContext, AppDbContext db)
    {
        var userId = httpContext.User.GetUserId();
        var participants = await LoadParticipantsAsync(db, conversationId);
        if (!participants.Any(participant => participant.UserId == userId))
            return Results.NotFound(new ApiError("Sohbet bulunamadı"));

        var limit = Math.Clamp(take ?? 50, 1, 100);
        var beforeUtc = before?.ToUniversalTime();
        var query = db.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId &&
                              !message.HiddenFor.Any(hidden => hidden.UserId == userId));
        if (beforeUtc is not null)
        {
            query = query.Where(message => message.SentAt < beforeUtc);
        }

        var page = await query.OrderByDescending(message => message.SentAt).Take(limit).ToListAsync();
        page.Reverse();

        var other = participants.FirstOrDefault(participant => participant.UserId != userId);
        return Results.Ok(page.Select(message => DtoMapper.ToMessage(message, other?.LastDeliveredAt, other?.LastReadAt)));
    }

    private static async Task<IResult> SearchMessagesAsync(Guid conversationId, string? q, HttpContext httpContext, AppDbContext db)
    {
        var userId = httpContext.User.GetUserId();
        var term = q?.Trim().ToLower() ?? string.Empty;
        if (term.Length == 0) return Results.BadRequest(new ApiError("Arama metni gerekli"));
        if (term.Length > 100) term = term[..100];

        var participants = await LoadParticipantsAsync(db, conversationId);
        if (!participants.Any(participant => participant.UserId == userId))
            return Results.NotFound(new ApiError("Sohbet bulunamadı"));

        var results = await db.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId &&
                              !message.IsDeleted &&
                              !message.HiddenFor.Any(hidden => hidden.UserId == userId) &&
                              (message.Content.ToLower().Contains(term) ||
                               (message.AttachmentName != null && message.AttachmentName.ToLower().Contains(term))))
            .OrderByDescending(message => message.SentAt)
            .Take(50)
            .ToListAsync();
        results.Reverse();

        var other = participants.FirstOrDefault(participant => participant.UserId != userId);
        return Results.Ok(results.Select(message => DtoMapper.ToMessage(message, other?.LastDeliveredAt, other?.LastReadAt)));
    }

    private static async Task<IResult> SendMessageAsync(
        Guid conversationId, SendMessageRequest request, HttpContext httpContext,
        AppDbContext db, PresenceTracker presence, ChatNotifier notifier)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0) return Results.BadRequest(new ApiError("Mesaj boş olamaz"));
        if (content.Length > Limits.MessageContent)
            return Results.BadRequest(new ApiError($"Mesaj en fazla {Limits.MessageContent} karakter olabilir"));

        var (message, error) = await CreateMessageAsync(db, presence, notifier, conversationId, httpContext.User.GetUserId(), content, null);
        return message is null ? error! : Results.Ok(message);
    }

    private static async Task<IResult> SendAttachmentAsync(
        Guid conversationId, IFormFile file, [FromForm] string? caption, HttpContext httpContext,
        AppDbContext db, PresenceTracker presence, ChatNotifier notifier, FileStorage storage, CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        var text = caption?.Trim() ?? string.Empty;
        if (text.Length > Limits.MessageContent)
            return Results.BadRequest(new ApiError($"Açıklama en fazla {Limits.MessageContent} karakter olabilir"));

        // Önce yetki kontrolü, sonra dosyayı diske yaz (sahipsiz dosya kalmasın).
        if (await ChatQueries.GetParticipantIdsAsync(db, conversationId, userId) is null)
            return Results.NotFound(new ApiError("Sohbet bulunamadı"));

        var (stored, saveError) = await storage.SaveAsync(file, FileKind.Attachment, cancellationToken);
        if (stored is null) return Results.BadRequest(new ApiError(saveError!));

        var originalName = Path.GetFileName(file.FileName);
        if (originalName.Length > 255) originalName = originalName[^255..];

        try
        {
            var (message, error) = await CreateMessageAsync(db, presence, notifier, conversationId, userId, text,
                new AttachmentInfo(originalName, stored));
            if (message is null)
            {
                storage.Delete(FileKind.Attachment, stored.FileName);
                return error!;
            }

            return Results.Ok(message);
        }
        catch
        {
            storage.Delete(FileKind.Attachment, stored.FileName);
            throw;
        }
    }

    private sealed record AttachmentInfo(string OriginalName, StoredFile File);

    private static async Task<(MessageDto? Message, IResult? Error)> CreateMessageAsync(
        AppDbContext db, PresenceTracker presence, ChatNotifier notifier,
        Guid conversationId, Guid senderId, string content, AttachmentInfo? attachment)
    {
        var conversation = await db.Conversations
            .Include(item => item.Participants)
            .FirstOrDefaultAsync(item => item.Id == conversationId);
        if (conversation is null || conversation.Participants.All(participant => participant.UserId != senderId))
            return (null, Results.NotFound(new ApiError("Sohbet bulunamadı")));

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = senderId,
            Content = content,
            SentAt = now,
            AttachmentName = attachment?.OriginalName,
            AttachmentFileName = attachment?.File.FileName,
            AttachmentContentType = attachment?.File.ContentType,
            AttachmentSize = attachment?.File.Size,
        };
        db.Messages.Add(message);
        conversation.UpdatedAt = now;

        // Alıcı o an bağlıysa mesaj anında teslim edilmiş sayılır.
        foreach (var participant in conversation.Participants.Where(item => item.UserId != senderId))
        {
            if (presence.IsOnline(participant.UserId))
            {
                participant.LastDeliveredAt = now;
            }
        }

        await db.SaveChangesAsync();

        var other = conversation.Participants.FirstOrDefault(participant => participant.UserId != senderId);
        var dto = DtoMapper.ToMessage(message, other?.LastDeliveredAt, other?.LastReadAt);
        await notifier.SendToUsersAsync(conversation.Participants.Select(participant => participant.UserId), "MessageCreated", dto);
        return (dto, null);
    }

    private static async Task<IResult> MarkReadAsync(Guid conversationId, HttpContext httpContext, AppDbContext db, ChatNotifier notifier)
    {
        var userId = httpContext.User.GetUserId();
        var participants = await db.ConversationParticipants
            .Where(participant => participant.ConversationId == conversationId)
            .ToListAsync();
        var me = participants.FirstOrDefault(participant => participant.UserId == userId);
        if (me is null) return Results.NotFound(new ApiError("Sohbet bulunamadı"));

        var now = DateTime.UtcNow;
        me.LastReadAt = now;
        me.LastDeliveredAt = now;
        await db.SaveChangesAsync();

        // Karşı taraf mavi tiki, kendi diğer sekmelerimiz okunmamış sayısının sıfırlanmasını görür.
        await notifier.SendToUsersAsync(participants.Select(participant => participant.UserId), "ConversationRead",
            new { conversationId, userId, readAt = now });
        return Results.NoContent();
    }

    private static async Task<IResult> EditMessageAsync(
        Guid messageId, EditMessageRequest request, HttpContext httpContext, AppDbContext db, ChatNotifier notifier)
    {
        var userId = httpContext.User.GetUserId();
        var message = await db.Messages.FirstOrDefaultAsync(item => item.Id == messageId);
        if (message is null || message.SenderId != userId)
            return Results.NotFound(new ApiError("Mesaj bulunamadı"));
        if (message.IsDeleted)
            return Results.BadRequest(new ApiError("Silinmiş mesaj düzenlenemez"));

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0 && message.AttachmentFileName is null)
            return Results.BadRequest(new ApiError("Mesaj boş olamaz"));
        if (content.Length > Limits.MessageContent)
            return Results.BadRequest(new ApiError($"Mesaj en fazla {Limits.MessageContent} karakter olabilir"));

        if (content != message.Content)
        {
            message.Content = content;
            message.EditedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return Results.Ok(await BroadcastUpdatedAsync(db, notifier, message));
    }

    private static async Task<IResult> DeleteMessageAsync(
        Guid messageId, string? scope, HttpContext httpContext, AppDbContext db, ChatNotifier notifier, FileStorage storage)
    {
        var userId = httpContext.User.GetUserId();
        var message = await db.Messages.FirstOrDefaultAsync(item => item.Id == messageId);
        if (message is null || await ChatQueries.GetParticipantIdsAsync(db, message.ConversationId, userId) is null)
            return Results.NotFound(new ApiError("Mesaj bulunamadı"));

        if (string.Equals(scope, "everyone", StringComparison.OrdinalIgnoreCase))
        {
            if (message.SenderId != userId)
                return Results.Json(new ApiError("Sadece kendi mesajlarınızı herkesten silebilirsiniz"), statusCode: StatusCodes.Status403Forbidden);

            var attachmentFile = message.AttachmentFileName;
            message.IsDeleted = true;
            message.Content = string.Empty;
            message.AttachmentName = null;
            message.AttachmentFileName = null;
            message.AttachmentContentType = null;
            message.AttachmentSize = null;
            await db.SaveChangesAsync();
            storage.Delete(FileKind.Attachment, attachmentFile);

            return Results.Ok(await BroadcastUpdatedAsync(db, notifier, message));
        }

        // Varsayılan: sadece benden sil (gönderen ya da alıcı yapabilir).
        if (!await db.HiddenMessages.AnyAsync(hidden => hidden.MessageId == messageId && hidden.UserId == userId))
        {
            db.HiddenMessages.Add(new HiddenMessage { MessageId = messageId, UserId = userId });
            await db.SaveChangesAsync();
        }

        await notifier.SendToUsersAsync([userId], "MessageHidden", new { conversationId = message.ConversationId, messageId });
        return Results.NoContent();
    }

    private static async Task<MessageDto> BroadcastUpdatedAsync(AppDbContext db, ChatNotifier notifier, Message message)
    {
        var participants = await LoadParticipantsAsync(db, message.ConversationId);
        var other = participants.FirstOrDefault(participant => participant.UserId != message.SenderId);
        var dto = DtoMapper.ToMessage(message, other?.LastDeliveredAt, other?.LastReadAt);
        await notifier.SendToUsersAsync(participants.Select(participant => participant.UserId), "MessageUpdated", dto);
        return dto;
    }

    // ------------------------------------------------------------------ Dosyalar

    private static async Task<IResult> GetAttachmentAsync(
        Guid messageId, bool? download, HttpContext httpContext, AppDbContext db, FileStorage storage)
    {
        var userId = httpContext.User.GetUserId();
        var message = await db.Messages.AsNoTracking()
            .Where(item => item.Id == messageId &&
                           !item.IsDeleted &&
                           item.Conversation.Participants.Any(participant => participant.UserId == userId))
            .Select(item => new { item.AttachmentFileName, item.AttachmentName, item.AttachmentContentType })
            .FirstOrDefaultAsync();

        var path = storage.GetPath(FileKind.Attachment, message?.AttachmentFileName);
        if (message is null || path is null) return Results.NotFound();

        var contentType = message.AttachmentContentType ?? FileStorage.GetContentType(path);
        httpContext.Response.Headers.CacheControl = "private, max-age=86400";

        // Resimler sayfada gösterilir; diğer tüm dosyalar (txt dahil) indirme olarak sunulur.
        return FileStorage.IsImageContentType(contentType) && download != true
            ? Results.File(path, contentType)
            : Results.File(path, contentType, fileDownloadName: message.AttachmentName ?? Path.GetFileName(path));
    }

    private static Task<List<ConversationParticipant>> LoadParticipantsAsync(AppDbContext db, Guid conversationId) =>
        db.ConversationParticipants.AsNoTracking()
            .Where(participant => participant.ConversationId == conversationId)
            .ToListAsync();
}

