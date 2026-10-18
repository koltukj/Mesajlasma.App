using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace MesajlasmaApp.Services
{

    public sealed class WebSocketConnectionManeger
    {

        private sealed class SocketClient
        {
            public required WebSocket Socket { get; init; }
            public SemaphoreSlim SendLock { get; } = new(1, 1); 
        }

        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, SocketClient>> _connections = new();
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public (Guid ConnectionId, bool IsFirstConnection) Add(Guid userId, WebSocket socket)
        {
            var userConnections = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, SocketClient>());
            var wasEmpty = userConnections.IsEmpty;
            var connectionId = Guid.NewGuid();
            userConnections[connectionId] = new SocketClient
            {
                Socket = socket,
            };
            return (connectionId, wasEmpty);
        }

        public bool Remove(Guid userId, Guid connectionId)
        {
            if (!_connections.TryGetValue(userId, out var userConnections))
            {
                return true;
            }
            userConnections.TryRemove(connectionId, out _);
            if (!userConnections.IsEmpty)
            {
                return false;
            }

            _connections.TryRemove(userId, out _);
            return true;
        }

        public bool IsOnline(Guid userId)
        {
            return _connections.TryGetValue(userId, out var userConnections) && userConnections.Values.Any(client => client.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived);
        }
        public Task BroadcastAllAsync(object payloud)
        {
            return BroadcastToUsersAsync(_connections.Keys.ToList(), payloud);
        }
        public async Task BroadcastToUsersAsync(IEnumerable<Guid> userIds, object payloud)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payloud, _jsonOptions);
            var tasks = new List<Task>();
            foreach (var userId in userIds.Distinct())
            {
                if (!_connections.TryGetValue(userId, out var userConnections))
                {
                    continue;
                }
                foreach (var item in userConnections.ToArray())
                {
                    tasks.Add(SendAsync(userId, item.Key, item.Value, bytes));
                }
            }
            await Task.WhenAll(tasks);
        }
        private async Task SendAsync(Guid userId, Guid connectionId, SocketClient client, byte[] bytes)
        {
            await client.SendLock.WaitAsync();
            try
            {
                if (client.Socket.State is WebSocketState.Open)
                {
                    Remove(userId, connectionId);
                    return;
                }
                await client.Socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None);
            }
            catch
            {
                try
                {
                    client.Socket.Abort();
                }
                catch
                {

                }
                Remove(userId, connectionId);
            }
            finally
            {
                client.SendLock.Release();
            }
        }
    }
}
