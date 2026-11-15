using System.Threading.RateLimiting;
using MesajlasmaApp.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace MesajlasmaApp.Infrastructure;

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Messages = "messages";
    public const string Uploads = "uploads";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError("Çok fazla istek gönderdiniz. Lütfen biraz bekleyin."),
                    cancellationToken);
            };

            // Giriş / kayıt: IP başına dakikada 10 deneme (kaba kuvvet koruması)
            options.AddPolicy(Auth, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                }));

            // Mesaj gönderme: kullanıcı başına 10 saniyede 25 mesaj
            options.AddPolicy(Messages, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 25,
                    Window = TimeSpan.FromSeconds(10),
                }));

            // Dosya yükleme: kullanıcı başına dakikada 15 dosya
            options.AddPolicy(Uploads, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 15,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }
}

