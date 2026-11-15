using System.Text.RegularExpressions;

namespace MesajlasmaApp.Services;

public enum FileKind
{
    Attachment,
    Avatar,
}

public sealed record StoredFile(string FileName, string ContentType, long Size);

/// <summary>
/// Yüklenen dosyaları doğrular (uzantı + dosya imzası/magic bytes) ve wwwroot DIŞINDA saklar.
/// Dosyalar yalnızca yetki kontrolü yapan endpoint'ler üzerinden sunulur.
/// </summary>
public sealed partial class FileStorage(AppPaths paths, ILogger<FileStorage> logger)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif = "GIF8"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly Dictionary<string, (string ContentType, Func<byte[], bool> Check)> ImageTypes = new()
    {
        [".jpg"] = ("image/jpeg", header => StartsWith(header, Jpeg)),
        [".jpeg"] = ("image/jpeg", header => StartsWith(header, Jpeg)),
        [".png"] = ("image/png", header => StartsWith(header, Png)),
        [".gif"] = ("image/gif", header => StartsWith(header, Gif)),
        [".webp"] = ("image/webp", header => StartsWith(header, Riff) && header.Length >= 12 && header.AsSpan(8, 4).SequenceEqual(Webp)),
    };

    private static readonly Dictionary<string, (string ContentType, Func<byte[], bool> Check)> DocumentTypes = new()
    {
        [".pdf"] = ("application/pdf", header => StartsWith(header, Pdf)),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", header => StartsWith(header, Zip)),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", header => StartsWith(header, Zip)),
        [".doc"] = ("application/msword", header => StartsWith(header, Ole)),
        [".xls"] = ("application/vnd.ms-excel", header => StartsWith(header, Ole)),
        [".txt"] = ("text/plain; charset=utf-8", header => !header.Contains((byte)0)),
    };

    public static bool IsImageContentType(string? contentType) =>
        contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public async Task<(StoredFile? File, string? Error)> SaveAsync(IFormFile file, FileKind kind, CancellationToken cancellationToken)
    {
        var maxBytes = kind == FileKind.Avatar ? Data.Limits.AvatarMaxBytes : Data.Limits.AttachmentMaxBytes;
        if (file.Length == 0)
        {
            return (null, "Dosya boş");
        }

        if (file.Length > maxBytes)
        {
            return (null, $"Dosya en fazla {maxBytes / 1024 / 1024} MB olabilir");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = ImageTypes.TryGetValue(extension, out var type) ||
                      (kind == FileKind.Attachment && DocumentTypes.TryGetValue(extension, out type));
        if (!allowed)
        {
            return (null, kind == FileKind.Avatar
                ? "Sadece JPG, PNG, GIF veya WEBP yükleyebilirsiniz"
                : "Desteklenmeyen dosya türü");
        }

        await using var input = file.OpenReadStream();
        var header = new byte[512];
        var read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        Array.Resize(ref header, read);
        if (!type.Check(header))
        {
            return (null, "Dosya içeriği uzantısıyla uyuşmuyor");
        }

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(Folder(kind), storedName);
        try
        {
            await using var output = File.Create(fullPath);
            await output.WriteAsync(header, cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }

        return (new StoredFile(storedName, type.ContentType, file.Length), null);
    }

    /// <summary>Saklanan dosyanın tam yolunu döndürür; geçersiz/yol aşımı içeren adlarda null.</summary>
    public string? GetPath(FileKind kind, string? storedName)
    {
        if (storedName is null || !StoredNamePattern().IsMatch(storedName))
        {
            return null;
        }

        var fullPath = Path.Combine(Folder(kind), storedName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    public static string GetContentType(string storedName)
    {
        var extension = Path.GetExtension(storedName).ToLowerInvariant();
        if (ImageTypes.TryGetValue(extension, out var image)) return image.ContentType;
        if (DocumentTypes.TryGetValue(extension, out var document)) return document.ContentType;
        return "application/octet-stream";
    }

    public void Delete(FileKind kind, string? storedName)
    {
        var path = GetPath(kind, storedName);
        if (path is not null)
        {
            TryDelete(path);
        }
    }

    private string Folder(FileKind kind) => kind == FileKind.Avatar ? paths.Avatars : paths.Attachments;

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Dosya silinemedi: {Path}", path);
        }
    }

    private static bool StartsWith(byte[] data, byte[] prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    [GeneratedRegex("^[a-f0-9]{32}\\.[a-z0-9]{2,5}$")]
    private static partial Regex StoredNamePattern();
}

