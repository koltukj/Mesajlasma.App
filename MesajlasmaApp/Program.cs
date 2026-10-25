using MesajlasmaApp.Models;
using MesajlasmaApp.Services;
using Microsoft.AspNetCore.Identity;
using static MesajlasmaApp.Models.ApiModels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<JsonDataStore>();

var app = builder.Build();

var store = app.Services.GetRequiredService<JsonDataStore>();
await store.InitializeAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/auth/register", async (RegisterRequest request, JsonDataStore dataStore) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) ||
        string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "Tum alanlar zorunlu" });
    }

    var (user, error) = await dataStore.CreateUserAsync(
        request.DisplayName,
        request.Email,
        request.Password);

    if (error is not null)
    {
        return Results.Conflict(new { error });
    }

    return Results.Ok(new
    {
        id = user!.Id,
        displayName = user.DisplayName,
        email = user.Email
    });
});
//Kayit olduktan sonra tekrar giriş yapmak için login endppointi
app.MapPost("/api/auth/login", async (
    LoginRequest request,
    JsonDataStore dataStore,
    PasswordHasher<AppUser> passwordHasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "E-posta ve sifre zorunlu" });
    }

    var user = await dataStore.FindUserByEmailAsync(request.Email);
    if (user is null)
    {
        return Results.Json(new { error = "E-posta veya sifre hatali" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (verification == PasswordVerificationResult.Failed)
    {
        return Results.Json(new { error = "E-posta veya sifre hatali" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        id = user.Id,
        displayName = user.DisplayName,
        email = user.Email
    });
});

app.Run();
