namespace MesajlasmaApp.Services;

/// <summary>
/// Kalıcı verilerin (SQLite dosyası, yüklenen dosyalar, oturum anahtarları) tutulduğu klasörler.
/// Sunucuya kurulumda bu klasör kalıcı bir diske (volume) bağlanmalıdır.
/// Varsayılan: &lt;proje&gt;/App_Data — DATA_PATH ortam değişkeni ile değiştirilebilir.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["DATA_PATH"] ?? configuration["Storage:DataPath"];
        DataRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data")
            : Path.GetFullPath(configured);

        Attachments = Path.Combine(DataRoot, "uploads", "attachments");
        Avatars = Path.Combine(DataRoot, "uploads", "avatars");
        Keys = Path.Combine(DataRoot, "keys");

        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Attachments);
        Directory.CreateDirectory(Avatars);
        Directory.CreateDirectory(Keys);
    }

    public string DataRoot { get; }
    public string Attachments { get; }
    public string Avatars { get; }
    public string Keys { get; }
}

