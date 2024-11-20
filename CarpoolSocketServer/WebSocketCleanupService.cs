using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace CarpoolSocketServer
{
    public class WebSocketCleanupService
    {
        private readonly ConcurrentDictionary<WebSocket, DateTime> _clientConnections;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _connectionTimeout;

        public WebSocketCleanupService(ConcurrentDictionary<WebSocket, DateTime> clientConnections, TimeSpan cleanupInterval, TimeSpan connectionTimeout)
        {
            _clientConnections = clientConnections;
            _cleanupInterval = cleanupInterval;
            _connectionTimeout = connectionTimeout;
            StartCleanupTask();
        }

        private void StartCleanupTask()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    Console.WriteLine($"[Cleanup Task] Running cleanup at {DateTime.UtcNow}");
                    await Task.Delay(_cleanupInterval);
                    await CleanupStaleConnections();
                }
            });
        }

        private async Task CleanupStaleConnections()
        {
            var staleConnections = _clientConnections
                .Where(kvp => DateTime.UtcNow - kvp.Value > _connectionTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connection in staleConnections)
            {
                if (_clientConnections.TryRemove(connection, out _))
                {
                    Console.WriteLine($"[Cleanup Task] Removing stale connection at {DateTime.UtcNow}");
                    try
                    {
                        await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed due to inactivity", CancellationToken.None);
                        Console.WriteLine($"[Cleanup Task] Successfully closed stale WebSocket connection.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error closing WebSocket: {ex.Message}");
                    }
                }
            }
        }

        public void UpdateConnection(WebSocket webSocket)
        {
            _clientConnections[webSocket] = DateTime.UtcNow;
            Console.WriteLine($"[Cleanup Task] Updated connection timestamp for WebSocket.");
        }
    }
}
