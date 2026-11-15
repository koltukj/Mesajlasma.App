using MesajlasmaApp.Data;
using MesajlasmaApp.Services;

namespace MesajlasmaApp.Models;

// ---- İstekler ----
public sealed record RegisterRequest(string? DisplayName, string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record UpdateProfileRequest(string? DisplayName, string? StatusMessage, bool IsInvisible);
public sealed record CreateConversationRequest(Guid UserId);
public sealed record SendMessageRequest(string? Content);
public sealed record EditMessageRequest(string? Content);

// ---- Yanıtlar ----
public sealed record MeDto(Guid Id, string DisplayName, string Email, string? AvatarUrl, string StatusMessage, bool IsInvisible);

/// <summary>Diğer kullanıcılar için görünen bilgiler. E-posta adresi gizlilik nedeniyle paylaşılmaz.</summary>
public sealed record UserDto(Guid Id, string DisplayName, string? AvatarUrl, string StatusMessage, bool IsOnline, DateTime? LastSeenAt);

public sealed record AttachmentDto(string Name, string Url, string ContentType, long Size, bool IsImage);

/// <param name="Status">Sadece kendi mesajlarınız için anlamlıdır: sent | delivered | read</param>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string Content,
    AttachmentDto? Attachment,
    DateTime SentAt,
    DateTime? EditedAt,
    bool IsDeleted,
    string Status);

public sealed record ConversationDto(Guid Id, UserDto OtherUser, MessageDto? LastMessage, int UnreadCount, DateTime UpdatedAt);

public sealed record ApiError(string Error);

public static class DtoMapper
{
    public static MeDto ToMe(User user) =>
        new(user.Id, user.DisplayName, user.Email, AvatarUrl(user), user.StatusMessage, user.IsInvisible);

    public static UserDto ToUser(User user, PresenceTracker presence)
    {
        // Görünmez moddaki kullanıcının çevrimiçi durumu ve son görülmesi gizlenir.
        var visible = !user.IsInvisible;
        return new UserDto(
            user.Id,
            user.DisplayName,
            AvatarUrl(user),
            user.StatusMessage,
            visible && presence.IsOnline(user.Id),
            visible ? user.LastSeenAt : null);
    }

    /// <param name="otherDeliveredAt">Karşı tarafın teslim aldığı son mesaj zamanı</param>
    /// <param name="otherReadAt">Karşı tarafın okuduğu son mesaj zamanı</param>
    public static MessageDto ToMessage(Message message, DateTime? otherDeliveredAt, DateTime? otherReadAt)
    {
        var status = otherReadAt >= message.SentAt ? "read"
            : otherDeliveredAt >= message.SentAt ? "delivered"
            : "sent";

        AttachmentDto? attachment = null;
        if (!message.IsDeleted && message.AttachmentFileName is not null)
        {
            attachment = new AttachmentDto(
                message.AttachmentName ?? "dosya",
                $"/api/files/{message.Id}",
                message.AttachmentContentType ?? "application/octet-stream",
                message.AttachmentSize ?? 0,
                FileStorage.IsImageContentType(message.AttachmentContentType));
        }

        return new MessageDto(
            message.Id,
            message.ConversationId,
            message.SenderId,
            message.IsDeleted ? string.Empty : message.Content,
            attachment,
            message.SentAt,
            message.EditedAt,
            message.IsDeleted,
            status);
    }

    private static string? AvatarUrl(User user) =>
        user.AvatarFileName is null ? null : $"/api/avatars/{user.AvatarFileName}";
}

