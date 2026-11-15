using System.Security.Claims;
using MesajlasmaApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace MesajlasmaApp.Infrastructure;

public static class AuthSetup
{
    public const string SecurityVersionClaim = "sv";

    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException();
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "mesajlasma.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // HTTPS üzerinden gelen isteklerde çerez otomatik "Secure" olur (proxy arkasında ForwardedHeaders sayesinde).
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;

                // API olduğu için giriş sayfasına yönlendirmek yerine 401/403 döndür.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };

                // Kullanıcı silindiyse ya da şifre değişip oturumlar kapatıldıysa çerezi reddet.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var principal = context.Principal;
                    if (principal is null ||
                        !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
                        !int.TryParse(principal.FindFirstValue(SecurityVersionClaim), out var version))
                    {
                        context.RejectPrincipal();
                        return;
                    }

                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var currentVersion = await db.Users
                        .Where(user => user.Id == userId)
                        .Select(user => (int?)user.SecurityVersion)
                        .FirstOrDefaultAsync();

                    if (currentVersion != version)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static Task SignInUserAsync(this HttpContext httpContext, User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(SecurityVersionClaim, user.SecurityVersion.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}

