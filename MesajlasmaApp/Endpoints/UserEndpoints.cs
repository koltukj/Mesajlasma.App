using MesajlasmaApp.Data;
using MesajlasmaApp.Infrastructure;
using MesajlasmaApp.Models;
using MesajlasmaApp.Services;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/profile").RequireAuthorization();
        profile.MapPut("/", UpdateProfileAsync);
        profile.MapPost("/avatar", UploadAvatarAsync).DisableAntiforgery().RequireRateLimiting(RateLimits.Uploads);
        profile.MapDelete("/avatar", DeleteAvatarAsync);

        var users = app.MapGroup("/api/users").RequireAuthorization();
        users.MapGet("/search", SearchUsersAsync);

        app.MapGet("/api/avatars/{fileName}", GetAvatar).RequireAuthorization();
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request, HttpContext httpContext, AppDbContext db, ChatNotifier notifier, PresenceTracker presence)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == httpContext.User.GetUserId());
        if (user is null) return Results.Unauthorized();

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var statusMessage = request.StatusMessage?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > Limits.DisplayName)
            return Results.BadRequest(new ApiError($"Ad 2-{Limits.DisplayName} karakter olmalı"));
        if (statusMessage.Length > Limits.StatusMessage)
            return Results.BadRequest(new ApiError($"Durum mesajı en fazla {Limits.StatusMessage} karakter olabilir"));

        user.SetDisplayName(displayName);
        user.StatusMessage = statusMessage;
        user.IsInvisible = request.IsInvisible;
        await db.SaveChangesAsync();

        await NotifyUserUpdatedAsync(db, notifier, presence, user);
        return Results.Ok(DtoMapper.ToMe(user));
    }

    private static async Task<IResult> UploadAvatarAsync(
        IFormFile file, HttpContext httpContext, AppDbContext db, FileStorage storage,
        ChatNotifier notifier, PresenceTracker presence, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == httpContext.User.GetUserId(), cancellationToken);
        if (user is null) return Results.Unauthorized();

        var (stored, error) = await storage.SaveAsync(file, FileKind.Avatar, cancellationToken);
        if (stored is null) return Results.BadRequest(new ApiError(error!));

        var oldAvatar = user.AvatarFileName;
        user.AvatarFileName = stored.FileName;
        await db.SaveChangesAsync(cancellationToken);
        storage.Delete(FileKind.Avatar, oldAvatar);

        await NotifyUserUpdatedAsync(db, notifier, presence, user);
        return Results.Ok(DtoMapper.ToMe(user));
    }

    private static async Task<IResult> DeleteAvatarAsync(
        HttpContext httpContext, AppDbContext db, FileStorage storage, ChatNotifier notifier, PresenceTracker presence)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == httpContext.User.GetUserId());
        if (user is null) return Results.Unauthorized();

        var oldAvatar = user.AvatarFileName;
        user.AvatarFileName = null;
        await db.SaveChangesAsync();
        storage.Delete(FileKind.Avatar, oldAvatar);

        await NotifyUserUpdatedAsync(db, notifier, presence, user);
        return Results.Ok(DtoMapper.ToMe(user));
    }

    /// <summary>
    /// Görünen adla (parça eşleşme) veya tam e-posta adresiyle kullanıcı arar.
    /// Tüm kullanıcıları listelemek gizlilik nedeniyle mümkün değildir.
    /// </summary>
    private static async Task<IResult> SearchUsersAsync(string? q, HttpContext httpContext, AppDbContext db, PresenceTracker presence)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2) return Results.Ok(Array.Empty<UserDto>());

        var userId = httpContext.User.GetUserId();
        var normalized = User.NormalizeForSearch(query);
        var email = query.ToLowerInvariant();

        var users = await db.Users.AsNoTracking()
            .Where(user => user.Id != userId && (user.Email == email || user.SearchName.Contains(normalized)))
            .OrderBy(user => user.DisplayName)
            .Take(20)
            .ToListAsync();

        return Results.Ok(users.Select(user => DtoMapper.ToUser(user, presence)));
    }

    private static IResult GetAvatar(string fileName, FileStorage storage)
    {
        var path = storage.GetPath(FileKind.Avatar, fileName);
        if (path is null) return Results.NotFound();

        // Dosya adı her yüklemede değiştiği için uzun süre önbelleğe alınabilir.
        return Results.File(path, FileStorage.GetContentType(fileName), enableRangeProcessing: false,
            lastModified: File.GetLastWriteTimeUtc(path));
    }

    private static async Task NotifyUserUpdatedAsync(AppDbContext db, ChatNotifier notifier, PresenceTracker presence, User user)
    {
        var contacts = await ChatQueries.GetContactIdsAsync(db, user.Id);
        await notifier.SendToUsersAsync(contacts, "UserUpdated", DtoMapper.ToUser(user, presence));
        await notifier.SendToUsersAsync([user.Id], "ProfileUpdated", DtoMapper.ToMe(user));
    }
}

