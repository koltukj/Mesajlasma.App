# Kozmik Sohbet (MesajlasmaApp)

ASP.NET Core 10 + SignalR + EF Core (SQLite / PostgreSQL) ile gerçek zamanlı birebir mesajlaşma uygulaması.

## Yerelde Çalıştırma

`Baslat.bat` dosyasına çift tıklayın veya terminalde:

```powershell
dotnet run --project MesajlasmaApp
```

Adres: `http://localhost:5144`

Geliştirme modunda ilk açılışta 3 demo hesap otomatik oluşturulur (şifreleri: `Demo123!`):
- `ayse@example.com`
- `mehmet@example.com`
- `zeynep@example.com`

Karşılıklı yazışmayı tek bilgisayarda denemek için bir normal tarayıcı penceresi, bir de **Gizli Sekme (Incognito)** açıp iki farklı hesapla giriş yapın.

---

## Canlıya Alma ve Domain Bağlama

> **Önemli Not (Vercel hakkında):** Vercel yalnızca statik siteler ve kısa ömürlü serverless fonksiyonlar çalıştırır; bu uygulamanın ihtiyaç duyduğu **kalıcı WebSocket (SignalR) bağlantısını ve .NET sunucusunu desteklemez**.
>
> Uygulamayı tek parça olarak **Railway**, **Render** veya **Fly.io** üzerine kurup kendi domain'inizi (Cloudflare, Namecheap, Google/Squarespace, Vercel Domains vb. nereden aldıysanız) doğrudan oraya yönlendirebilirsiniz. Projedeki `Dockerfile` bunun için hazırdır.

### Adım 1 – Projeyi GitHub'a Yükleyin
Repository'yi GitHub hesabınıza gönderin (`git push`).

### Adım 2 – Sunucu Kurulumu (Örnek: Railway veya Render)
1. **Railway.app** veya **Render.com** üzerinde **New → Web Service → Deploy from GitHub repo** seçin; `Dockerfile` otomatik algılanır.
2. **Kalıcı Disk (Volume) ekleyin:** Bağlama yolu (Mount Path) olarak `/data` verin. Yüklenen dosyalar, oturum anahtarları ve (PostgreSQL kullanmıyorsanız) SQLite veritabanı burada saklanır.
3. **(Önerilen) PostgreSQL ekleyin:** Panelden bir PostgreSQL veritabanı eklediğinizde `DATABASE_URL` ortam değişkeni otomatik tanımlanır; uygulama bunu görünce SQLite yerine PostgreSQL kullanır.

### Adım 3 – Ortam Değişkenleri

| Değişken | Açıklama |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (demo kullanıcıları kapatır) |
| `DATA_PATH` | Kalıcı disk klasörü (Docker'da varsayılan `/data`) |
| `DATABASE_URL` | Opsiyonel. `postgres://...` verilirse PostgreSQL kullanılır; verilmezse `DATA_PATH/mesajlasma.db` (SQLite) kullanılır |
| `ALLOWED_ORIGINS` | Opsiyonel. Arayüzü ayrı bir domain'de barındıracaksanız izin verilen adresler (`https://sohbet.site.com`) |

### Adım 4 – Kendi Domain'inizi Bağlama
1. Sunucu panelinde **Settings → Custom Domains** bölümüne gidin ve domain'inizi yazın (ör. `sohbet.siteniz.com`).
2. Panel size bir **CNAME** hedefi verir (ör. `xxxx.up.railway.app` veya `xxxx.onrender.com`).
3. Domain'i aldığınız sitenin (Vercel Domains, Cloudflare, Namecheap vb.) **DNS ayarlarına** gidip bu CNAME kaydını ekleyin.
4. SSL (HTTPS) sertifikası otomatik oluşturulur; güvenli çerezler ve `wss://` (SignalR) ek ayar gerektirmeden çalışır.

