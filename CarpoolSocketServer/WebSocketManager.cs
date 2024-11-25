﻿using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace CarpoolSocketServer
{
    public class WebSocketManager
    {
        private static ConcurrentDictionary<string, ConcurrentBag<WebSocket>> _groups = new ConcurrentDictionary<string, ConcurrentBag<WebSocket>>();
        private static ConcurrentDictionary<WebSocket, (string Name, double Latitude, double Longitude)> _clientLocations = new ConcurrentDictionary<WebSocket, (string, double, double)>();
        private readonly ConcurrentDictionary<WebSocket, DateTime> _connectionTimestamps = new ConcurrentDictionary<WebSocket, DateTime>();
        private readonly WebSocketCleanupService _cleanupService;

        public WebSocketManager(TimeSpan cleanupInterval, TimeSpan connectionTimeout)
        {
            _cleanupService = new WebSocketCleanupService(_connectionTimestamps, cleanupInterval, connectionTimeout);
        }

        public void AddClientToGroup(string groupId, WebSocket webSocket)
        {
            _groups.AddOrUpdate(groupId, new ConcurrentBag<WebSocket> { webSocket }, (key, existingBag) =>
            {
                existingBag.Add(webSocket);
                return existingBag;
            });

            // Track initial connection time
            _connectionTimestamps[webSocket] = DateTime.UtcNow;
        }

        public async Task HandleWebSocketAsync(WebSocket webSocket, string groupId, CancellationToken cancellationToken)
        {
            AddClientToGroup(groupId, webSocket);
            var buffer = new byte[1024 * 4];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.CloseStatus.HasValue) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var locationData = ParseLocationData(message);

                    if (locationData.HasValue)
                    {
                        // Update the client's location
                        _clientLocations[webSocket] = locationData.Value;

                        // Update the connection timestamp
                        _cleanupService.UpdateConnection(webSocket);

                        // Broadcast updated locations to all group members
                        await BroadcastGroupLocationsAsync(groupId, cancellationToken);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"WebSocket exception: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("WebSocket connection canceled.");
            }
            finally
            {
                RemoveClientFromGroup(groupId, webSocket);
                _clientLocations.TryRemove(webSocket, out _);
                _connectionTimestamps.TryRemove(webSocket, out _);
                await CloseWebSocketAsync(webSocket);
            }
        }

        private (string Name, double Latitude, double Longitude)? ParseLocationData(string message)
        {
            try
            {
                var locationData = JsonConvert.DeserializeObject<LocationData>(message);
                if (locationData != null && !string.IsNullOrWhiteSpace(locationData.Name) &&
                    locationData.Latitude >= -90 && locationData.Latitude <= 90 &&
                    locationData.Longitude >= -180 && locationData.Longitude <= 180)
                {
                    return (locationData.Name, locationData.Latitude, locationData.Longitude);
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing location data: {ex.Message}");
            }

            return null;
        }

        public async Task BroadcastGroupLocationsAsync(string groupId, CancellationToken cancellationToken)
        {
            if (!_groups.TryGetValue(groupId, out var clients)) return;

            var locationUpdates = _clientLocations
                .Where(c => clients.Contains(c.Key))
                .Select(c => new { c.Value.Name, Latitude = c.Value.Latitude, Longitude = c.Value.Longitude })
                .ToList();

            var updateMessage = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(locationUpdates));

            var tasks = clients
                .Where(c => c.State == WebSocketState.Open)
                .Select(client => client.SendAsync(new ArraySegment<byte>(updateMessage), WebSocketMessageType.Text, true, cancellationToken));

            await Task.WhenAll(tasks);
        }

        private async Task CloseWebSocketAsync(WebSocket webSocket)
        {
            if (webSocket.State == WebSocketState.Open ||
                webSocket.State == WebSocketState.CloseReceived ||
                webSocket.State == WebSocketState.CloseSent)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
            }
        }

        private void RemoveClientFromGroup(string groupId, WebSocket webSocket)
        {
            if (_groups.TryGetValue(groupId, out var clients))
            {
                clients.TryTake(out webSocket); // Remove the client safely

                if (clients.IsEmpty)
                {
                    _groups.TryRemove(groupId, out _);
                }
            }
        }
    }
}
