using System.Collections.Concurrent;

namespace MesajlasmaApp.Services;

/// <summary>
/// Hangi kullanıcının o an kaç açık SignalR bağlantısı olduğunu tutar (çoklu sekme desteği).
/// Not: Bellekte tutulduğu için tek sunucu örneğinde (instance) çalışır. Yatay ölçekleme
/// gerekirse Redis backplane + Redis tabanlı presence kullanılmalıdır.
/// </summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    /// <returns>Kullanıcının ilk bağlantısıysa true (çevrimiçi oldu).</returns>
    public bool Connected(Guid userId)
    {
        return _connections.AddOrUpdate(userId, 1, (_, count) => count + 1) == 1;
    }

    /// <returns>Kullanıcının son bağlantısı kapandıysa true (çevrimdışı oldu).</returns>
    public bool Disconnected(Guid userId)
    {
        while (true)
        {
            if (!_connections.TryGetValue(userId, out var count))
            {
                return false;
            }

            if (count <= 1)
            {
                if (_connections.TryRemove(new KeyValuePair<Guid, int>(userId, count)))
                {
                    return true;
                }
            }
            else if (_connections.TryUpdate(userId, count - 1, count))
            {
                return false;
            }
        }
    }

    public bool IsOnline(Guid userId) => _connections.ContainsKey(userId);
}

