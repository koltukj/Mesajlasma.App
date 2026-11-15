using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<HiddenMessage> HiddenMessages => Set<HiddenMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(Limits.DisplayName).IsRequired();
            entity.Property(user => user.SearchName).HasMaxLength(Limits.DisplayName).IsRequired();
            entity.HasIndex(user => user.SearchName);
            entity.Property(user => user.StatusMessage).HasMaxLength(Limits.StatusMessage);
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.AvatarFileName).HasMaxLength(100);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(conversation => conversation.Id);
            entity.HasIndex(conversation => conversation.UpdatedAt);
        });

        modelBuilder.Entity<ConversationParticipant>(entity =>
        {
            entity.HasKey(participant => new { participant.ConversationId, participant.UserId });
            entity.HasIndex(participant => participant.UserId);
            entity.HasOne(participant => participant.Conversation)
                .WithMany(conversation => conversation.Participants)
                .HasForeignKey(participant => participant.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(participant => participant.User)
                .WithMany()
                .HasForeignKey(participant => participant.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.ConversationId, message.SentAt });
            entity.Property(message => message.Content).HasMaxLength(Limits.MessageContent).IsRequired();
            entity.Property(message => message.AttachmentName).HasMaxLength(255);
            entity.Property(message => message.AttachmentFileName).HasMaxLength(100);
            entity.Property(message => message.AttachmentContentType).HasMaxLength(100);
            entity.HasIndex(message => message.AttachmentFileName);
            entity.HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(message => message.Sender)
                .WithMany()
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HiddenMessage>(entity =>
        {
            entity.HasKey(hidden => new { hidden.MessageId, hidden.UserId });
            entity.HasOne(hidden => hidden.Message)
                .WithMany(message => message.HiddenFor)
                .HasForeignKey(hidden => hidden.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Tüm tarihler UTC saklanır. SQLite tarih türünü (Kind) kaybettiği için okurken
        // tekrar UTC olarak işaretliyoruz; aksi halde tarayıcı saati yerel saat sanar.
        var utcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcConverter);
                }
            }
        }
    }
}

public static class Limits
{
    public const int DisplayName = 50;
    public const int StatusMessage = 140;
    public const int MessageContent = 4000;
    public const int PasswordMin = 8;
    public const int PasswordMax = 128;
    public const long AttachmentMaxBytes = 10 * 1024 * 1024;
    public const long AvatarMaxBytes = 5 * 1024 * 1024;
}
