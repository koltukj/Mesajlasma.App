using MesajlasmaApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MesajlasmaApp.Data;

public static class DatabaseSetup
{
    /// <summary>
    /// Veritabanı sağlayıcısını ortama göre seçer:
    /// 1) DATABASE_URL (postgres://...) ortam değişkeni varsa PostgreSQL (Railway/Render/Neon bunu verir)
    /// 2) ConnectionStrings:Postgres ayarı varsa PostgreSQL
    /// 3) Hiçbiri yoksa veri klasöründe SQLite dosyası
    /// </summary>
    public static IServiceCollection AddAppDatabase(this IServiceCollection services, IConfiguration configuration, AppPaths paths)
    {
        var rawPostgres = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(rawPostgres)){ 
            rawPostgres = configuration.GetConnectionString("Postgres");
        }
        var postgresConnection = !string.IsNullOrWhiteSpace(rawPostgres)
            ? ConvertPostgresUrl(rawPostgres)
            : null;

        if (!string.IsNullOrWhiteSpace(postgresConnection))
        {
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgresConnection));
        }
        else
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
            var sqlitePath = Path.Combine(paths.DataRoot, "mesajlasma.db");
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={sqlitePath}"));
        }

        return services;
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        // Not: Şema ilk çalıştırmada oluşturulur. İleride şema değişikliği gerekirse
        // EF Core Migrations'a geçilmelidir (README'ye bakın).
        await db.Database.EnsureCreatedAsync();
        logger.LogInformation("Veritabanı hazır ({Provider})", db.Database.ProviderName);

        if (app.Configuration.GetValue<bool>("Seed:DemoUsers") &&
            !await db.Users.AnyAsync(u => u.Email == "test-528ffc85@example.com"))
        {
            await SeedDemoDataAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
            logger.LogInformation("Önceki kullanıcılar ve sohbet verileri yüklendi.");
        }
    }

    private static async Task SeedDemoDataAsync(AppDbContext db, IPasswordHasher<User> _)
    {
        // Mevcut otomatik demo verisi varsa temizle ve önceki data.json verilerini orijinal ID/şifreleriyle aktar
        await db.Messages.ExecuteDeleteAsync();
        await db.ConversationParticipants.ExecuteDeleteAsync();
        await db.Conversations.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();

        User CreateExisting(Guid id, string name, string email, string passwordHash, DateTime createdAt, DateTime lastSeenAt)
        {
            var user = new User
            {
                Id = id,
                Email = email,
                PasswordHash = passwordHash,
                CreatedAt = createdAt,
                LastSeenAt = lastSeenAt
            };
            user.SetDisplayName(name);
            return user;
        }

        var ayseId = Guid.Parse("7de85405-d3c2-4531-a785-377cbc1365bc");
        var mehmetId = Guid.Parse("1d27422d-b6b3-41bf-9f87-95f96cb9973b");
        var zeynepId = Guid.Parse("330ac0a3-8e24-4c9c-a98f-49aeea6ad121");
        var test1Id = Guid.Parse("3eef2be1-1832-4b28-a2f5-f5b23119c7a8");
        var test2Id = Guid.Parse("709ec24a-5837-41c9-928b-9b27d634db19");
        var silaId = Guid.Parse("6e314054-61b6-4a23-a5e5-823a1659915c");

        db.Users.AddRange(
            CreateExisting(ayseId, "Ayşe Yılmaz", "ayse@example.com",
                "AQAAAAIAAYagAAAAELMFU7+r6FtXk6ekotLH9wcmrtYZAz9tIYtFLhd+gmXJS99ttLzVgk+WWT7Gy1BkIw==",
                DateTime.Parse("2026-08-05T13:30:35.5046689Z").ToUniversalTime(),
                DateTime.Parse("2026-08-06T13:34:46.341994Z").ToUniversalTime()),
            CreateExisting(mehmetId, "Mehmet Demir", "mehmet@example.com",
                "AQAAAAIAAYagAAAAEOWmDl5/PDauOA6I2MqNWAmfnaieg/x7cbeVKXfQgKjZf6wz/EkSgqP1WiqlhSMMHQ==",
                DateTime.Parse("2026-08-05T13:30:35.5466109Z").ToUniversalTime(),
                DateTime.Parse("2026-08-06T13:20:35.5466112Z").ToUniversalTime()),
            CreateExisting(zeynepId, "Zeynep Kaya", "zeynep@example.com",
                "AQAAAAIAAYagAAAAEHG9c9Wk6wibjlqn0VvqK+hnIGvy2mIisvvfu4AWjMmv0zSMsszhej8JU4NW/2BcXQ==",
                DateTime.Parse("2026-08-05T13:30:35.5864146Z").ToUniversalTime(),
                DateTime.Parse("2026-08-06T13:20:35.586415Z").ToUniversalTime()),
            CreateExisting(test1Id, "Test Kullanici", "test-528ffc85@example.com",
                "AQAAAAIAAYagAAAAEI4qGcmkg2JP2ZRNN1PgvI5M2h0+JhSmVVBwV3L5OpjqALavSKnKPFPxV8/q4cGp3Q==",
                DateTime.Parse("2026-10-25T07:15:02.4108441Z").ToUniversalTime(),
                DateTime.Parse("2026-10-25T07:15:02.4108443Z").ToUniversalTime()),
            CreateExisting(test2Id, "Test Kullanici", "test-fix@example.com",
                "AQAAAAIAAYagAAAAEEB86CeMXaR+LD4emUDP1gH9N8ZEW6P3VIAWCttnyQUnPsD+cze8NqEMGhZmGQsrPQ==",
                DateTime.Parse("2026-10-25T07:15:26.1779708Z").ToUniversalTime(),
                DateTime.Parse("2026-10-25T07:15:26.1779711Z").ToUniversalTime()),
            CreateExisting(silaId, "sıla ildeniz", "silaildeniz10@gmail.com",
                "AQAAAAIAAYagAAAAELkKFlb287gexWPFfYIOXVnBzpEQtgpKJoljWM1gL0wTTjYgLPbi0oUnJO4HPIH8sw==",
                DateTime.Parse("2026-10-25T07:16:48.3085796Z").ToUniversalTime(),
                DateTime.Parse("2026-10-25T07:16:48.3085799Z").ToUniversalTime())
        );

        Conversation MakeConv(string id, Guid u1, Guid u2, string createdIso, string updatedIso, DateTime? u1Read = null, DateTime? u2Read = null)
        {
            var created = DateTime.Parse(createdIso).ToUniversalTime();
            var updated = DateTime.Parse(updatedIso).ToUniversalTime();
            var conv = new Conversation { Id = Guid.Parse(id), CreatedAt = created, UpdatedAt = updated };
            conv.Participants.Add(new ConversationParticipant { ConversationId = conv.Id, UserId = u1, LastDeliveredAt = u1Read ?? created, LastReadAt = u1Read });
            conv.Participants.Add(new ConversationParticipant { ConversationId = conv.Id, UserId = u2, LastDeliveredAt = u2Read ?? created, LastReadAt = u2Read });
            return conv;
        }

        var c1 = MakeConv("15c82e7c-c78f-4ed6-8fb5-d078fce169c2", ayseId, mehmetId,
            "2026-10-25T07:14:57.7254877Z", "2026-08-06T13:06:35.6337325Z",
            DateTime.Parse("2026-08-06T13:06:35.6337325Z").ToUniversalTime(),
            DateTime.Parse("2026-08-06T13:06:35.6337325Z").ToUniversalTime());
        c1.Messages.Add(new Message
        {
            Id = Guid.Parse("3431202d-1428-4a7d-9dc6-32e82614a931"),
            ConversationId = c1.Id,
            SenderId = ayseId,
            Content = "Merhaba Mehmet, mesajlaşma uygulamasına hoş geldin.",
            SentAt = DateTime.Parse("2026-08-06T13:05:35.6337058Z").ToUniversalTime()
        });
        c1.Messages.Add(new Message
        {
            Id = Guid.Parse("05b74165-b0f1-47b0-9cc1-d026c4fa414d"),
            ConversationId = c1.Id,
            SenderId = mehmetId,
            Content = "Merhaba Ayşe! Gerçek zamanlı mesajlaşma çalışıyor.",
            SentAt = DateTime.Parse("2026-08-06T13:06:35.6337325Z").ToUniversalTime()
        });

        var c2 = MakeConv("9271b804-9247-4636-9fd8-ab300ed0c6cb", silaId, ayseId,
            "2026-10-30T08:58:25.230812Z", "2026-10-30T08:58:29.4338492Z",
            DateTime.Parse("2026-10-30T08:58:29.4338492Z").ToUniversalTime(), null);
        c2.Messages.Add(new Message
        {
            Id = Guid.Parse("63197741-988d-4a31-9aa3-bf41154f9a19"),
            ConversationId = c2.Id,
            SenderId = silaId,
            Content = "merhaba nasılsın",
            SentAt = DateTime.Parse("2026-10-30T08:58:29.4338492Z").ToUniversalTime()
        });

        var c3 = MakeConv("b63737f9-18f6-4565-8af1-5189bd2f1784", silaId, mehmetId,
            "2026-10-30T09:01:41.9973366Z", "2026-10-30T09:01:41.9973366Z");

        var c4 = MakeConv("85a8705f-221c-4d5e-b00c-cfd5a02d81eb", silaId, test1Id,
            "2026-10-30T09:13:30.0051263Z", "2026-10-30T09:13:45.4673707Z",
            DateTime.Parse("2026-10-30T09:13:45.4673707Z").ToUniversalTime(), null);
        c4.Messages.Add(new Message
        {
            Id = Guid.Parse("79c95816-d03b-40ed-892c-86f4747b0dd1"),
            ConversationId = c4.Id,
            SenderId = silaId,
            Content = "merhaba nasılsın",
            SentAt = DateTime.Parse("2026-10-30T09:13:45.4673707Z").ToUniversalTime()
        });

        var c5 = MakeConv("c198beba-e24b-4719-a12f-6268ba10eb90", silaId, test2Id,
            "2026-10-30T09:13:33.155157Z", "2026-10-30T09:40:48.0178935Z",
            DateTime.Parse("2026-10-30T09:40:48.0178935Z").ToUniversalTime(), null);
        c5.Messages.Add(new Message
        {
            Id = Guid.Parse("4e60c046-8414-47dd-bc37-cd3a0fa8accc"),
            ConversationId = c5.Id,
            SenderId = silaId,
            Content = "selam",
            SentAt = DateTime.Parse("2026-10-30T09:40:48.0178935Z").ToUniversalTime()
        });

        var c6 = MakeConv("0bcaae3e-3f8e-48e2-a7bf-e9392a023337", silaId, zeynepId,
            "2026-10-30T09:13:34.8109447Z", "2026-10-30T09:13:34.8109447Z");

        db.Conversations.AddRange(c1, c2, c3, c4, c5, c6);

        await db.SaveChangesAsync();
    }

    /// <summary>postgres://kullanici:sifre@host:5432/veritabani?sslmode=require → Npgsql bağlantı dizesi</summary>
    public static string ConvertPostgresUrl(string url)
    {
        if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return url; // Zaten "Host=...;Username=..." biçiminde
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = uri.AbsolutePath.Trim('/'),
        };

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sslMode = query["sslmode"];
        if (!string.IsNullOrWhiteSpace(sslMode) && Enum.TryParse<SslMode>(sslMode.Replace("-", ""), true, out var parsed))
        {
            builder.SslMode = parsed;
        }

        return builder.ConnectionString;
    }
}
