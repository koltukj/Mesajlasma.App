using System.Net.Mail;
using MesajlasmaApp.Data;
using MesajlasmaApp.Infrastructure;
using MesajlasmaApp.Models;
using MesajlasmaApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimits.Auth);
        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Auth);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/me", MeAsync).RequireAuthorization();
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization().RequireRateLimiting(RateLimits.Auth);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext httpContext)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var email = NormalizeEmail(request.Email);
        var password = request.Password ?? string.Empty;

        if (displayName.Length is < 2 or > Limits.DisplayName)
            return Results.BadRequest(new ApiError($"Ad 2-{Limits.DisplayName} karakter olmalı"));
        if (!IsValidEmail(email))
            return Results.BadRequest(new ApiError("Geçerli bir e-posta adresi girin"));
        var passwordError = ValidatePassword(password);
        if (passwordError is not null)
            return Results.BadRequest(new ApiError(passwordError));
        if (await db.Users.AnyAsync(user => user.Email == email))
            return Results.Conflict(new ApiError("Bu e-posta adresi zaten kayıtlı"));

        var newUser = new User { Email = email };
        newUser.SetDisplayName(displayName);
        newUser.PasswordHash = hasher.HashPassword(newUser, password);
        db.Users.Add(newUser);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Aynı anda aynı e-posta ile iki kayıt gelirse benzersiz indeks yakalar.
            return Results.Conflict(new ApiError("Bu e-posta adresi zaten kayıtlı"));
        }

        await httpContext.SignInUserAsync(newUser);
        return Results.Ok(DtoMapper.ToMe(newUser));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext httpContext)
    {
        var email = NormalizeEmail(request.Email);
        var password = request.Password ?? string.Empty;
        if (email.Length == 0 || password.Length == 0)
            return Results.BadRequest(new ApiError("E-posta ve şifre zorunlu"));

        var user = await db.Users.FirstOrDefaultAsync(item => item.Email == email);
        var result = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (user is null || result == PasswordVerificationResult.Failed)
            return Results.Json(new ApiError("E-posta veya şifre hatalı"), statusCode: StatusCodes.Status401Unauthorized);

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        user.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await httpContext.SignInUserAsync(user);
        return Results.Ok(DtoMapper.ToMe(user));
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext, CancellationToken _)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext httpContext, AppDbContext db)
    {
        var userId = httpContext.User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId);
        return user is null ? Results.Unauthorized() : Results.Ok(DtoMapper.ToMe(user));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, AppDbContext db, IPasswordHasher<User> hasher, HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId);
        if (user is null) return Results.Unauthorized();

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
            return Results.BadRequest(new ApiError("Mevcut şifre hatalı"));

        var passwordError = ValidatePassword(request.NewPassword ?? string.Empty);
        if (passwordError is not null) return Results.BadRequest(new ApiError(passwordError));

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.SecurityVersion++; // Diğer cihazlardaki oturumlar kapanır.
        await db.SaveChangesAsync();
        await httpContext.SignInUserAsync(user);
        return Results.NoContent();
    }

    private static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsValidEmail(string email)
    {
        if (email.Length is < 5 or > 256 || email.Contains(' ')) return false;
        return MailAddress.TryCreate(email, out var address) &&
               address.Address == email &&
               address.Host.Contains('.');
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < Limits.PasswordMin) return $"Şifre en az {Limits.PasswordMin} karakter olmalı";
        if (password.Length > Limits.PasswordMax) return $"Şifre en fazla {Limits.PasswordMax} karakter olabilir";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Şifre en az bir harf ve bir rakam içermeli";
        return null;
    }
}

