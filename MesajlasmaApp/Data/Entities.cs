namespace MesajlasmaApp.Data;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Aramalar için küçük harfe çevrilmiş görünen ad (Türkçe karakterler dahil).</summary>
    public string SearchName { get; set; } = string.Empty;

    /// <summary>Küçük harfe çevrilmiş, benzersiz e-posta adresi.</summary>
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sunucuda saklanan profil fotoğrafının dosya adı (yol değil).</summary>
    public string? AvatarFileName { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public bool IsInvisible { get; set; }

    /// <summary>Şifre değişince/çıkışta artırılır; eski oturum çerezlerini geçersiz kılar.</summary>
    public int SecurityVersion { get; set; }

    public void SetDisplayName(string displayName)
    {
        DisplayName = displayName;
        SearchName = NormalizeForSearch(displayName);
    }

    /// <summary>Türkçe I/İ/ı farkını yok sayan, kültürden bağımsız küçük harf dönüşümü.</summary>
    public static string NormalizeForSearch(string value) =>
        value.Trim().Replace('İ', 'i').Replace('I', 'i').ToLowerInvariant().Replace('ı', 'i');
}

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ConversationParticipant> Participants { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
}

public sealed class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Bu tarihe kadar (dahil) gönderilen mesajlar bu kullanıcıya teslim edildi.</summary>
    public DateTime? LastDeliveredAt { get; set; }

    /// <summary>Bu tarihe kadar (dahil) gönderilen mesajlar bu kullanıcı tarafından okundu.</summary>
    public DateTime? LastReadAt { get; set; }
}

public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;
    public string Content { get; set; } = string.Empty;

    /// <summary>Kullanıcının yüklediği orijinal dosya adı (sadece gösterim için).</summary>
    public string? AttachmentName { get; set; }

    /// <summary>Sunucuda saklanan rastgele dosya adı.</summary>
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<HiddenMessage> HiddenFor { get; set; } = [];
}

/// <summary>"Benden sil" ile gizlenen mesajlar.</summary>
public sealed class HiddenMessage
{
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid UserId { get; set; }
}
