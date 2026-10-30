namespace MesajlasmaApp.Models
{
    public class ApiModels
    {
        public sealed record RegisterRequest(string DisplayName, string Email, string Password);
        public sealed record LoginRequest(string Email, string Password);
        public sealed record CreateConversationRequest(Guid UserId);
        public sealed record SendMessageRequest(string Content);
        public sealed record EditMessageRequest(string Content);
        public sealed record DeleteMessageRequest(bool ForEveryone);
        public sealed record UpdateProfileRequest(string DisplayName, string StatusMessage, bool IsInvisible);
        public sealed record UserDto(
            Guid Id,
            string DisplayName,
            string Email,
            bool IsOnline,
            DateTime? LastSeenAt,
            string? ProfileImageUrl,
            string StatusMessage);
        public sealed record MeDto(
            Guid Id,
            string DisplayName,
            string Email);
        public sealed record MessageDto(
            Guid Id,
            Guid ConversationId,
            Guid SenderId,
            string SenderName,
            string Content,
            string? AttachmentName,
            string? AttachmentUrl,
            DateTime SentAt,
            int ReadByCount,
            int DeliveredToCount,
            bool IsEdited,
            bool IsDeleted);
        public sealed record ConversationSummaryDto(
            Guid Id,
            UserDto OtherUser,
            MessageDto? LastMessage,
            int UnreadCount,
            DateTime UpdateAt);
        public sealed record ConversationCreatedDto(
            Guid Id,
            UserDto OtherUser);
    }
}
