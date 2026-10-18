using Microsoft.VisualBasic;

namespace MesajlasmaApp.Models
{
    public sealed class AppData
    {
        public List<AppUser> Users { get; set; } = [];
        public List<Conversation> Conversations { get; set; } = [];
        public List<ChatMessage> Messages { get; set; } = [];
    }

    public sealed class AppUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreateAt {  get; set; } = DateTime.UtcNow; 
        public DateTime LastSeenAt { get; set; }
    }

    public sealed class Conversation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public List<Guid> ParticipantIds { get; set; } = [];
        public DateTime CreateAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public List<Guid> ReadByUserIds { get; set; } = [];
    }
}
