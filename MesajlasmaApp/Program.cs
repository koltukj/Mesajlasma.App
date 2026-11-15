using MesajlasmaApp.Data;
using MesajlasmaApp.Endpoints;
using MesajlasmaApp.Hubs;
using MesajlasmaApp.Infrastructure;
using MesajlasmaApp.Models;
using MesajlasmaApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Bulut sağlayıcıların (Render, Railway, Fly.io vb.) verdiği PORT değişkenine uyum sağla.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var portNumber))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portNumber}");
}

var paths = new AppPaths(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<ChatNotifier>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Oturum çerezi şifreleme anahtarlarını kalıcı klasörde tut (sunucu yeniden başlayınca çıkış yapılmasın).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(paths.Keys))
    .SetApplicationName("MesajlasmaApp");

builder.Services.AddAppDatabase(builder.Configuration, paths);
builder.Services.AddAppAuthentication();
builder.Services.AddAppRateLimiting();
builder.Services.AddSignalR();

// Yükleme boyutu sınırı (10 MB ek dosya + form alanı payı)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = Limits.AttachmentMaxBytes + (512 * 1024);
});

// Ters vekil (Cloudflare, Nginx, Render, Railway vb.) arkasında gerçek istemci IP'sini ve HTTPS bilgisini al.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Arayüz farklı bir domain'de (ör. Vercel) barındırılıyorsa CORS gerekir.
var allowedOrigins = (builder.Configuration["ALLOWED_ORIGINS"] ?? builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("GlobalException")
        .LogError(exception, "Beklenmeyen sunucu hatası");

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiError("Sunucuda beklenmeyen bir hata oluştu"));
}));

// Güvenlik başlıkları
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self' ws: wss:;";
    await next();
});

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapChatEndpoints();
app.MapHub<ChatHub>("/hubs/chat");

await app.InitializeDatabaseAsync();
app.Run();
