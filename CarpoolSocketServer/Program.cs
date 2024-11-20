using System.Net.WebSockets;

namespace CarpoolSocketServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            TimeSpan cleanupInterval = TimeSpan.FromMinutes(5);  // Cleanup every 5 minutes
            TimeSpan connectionTimeout = TimeSpan.FromMinutes(9);  // Timeout for inactive connections

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register WebSocketManager with intervals
            builder.Services.AddSingleton<WebSocketManager>(sp =>
                new WebSocketManager(cleanupInterval, connectionTimeout));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromMinutes(2) });
            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            // Custom middleware for WebSocket connections
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")  // WebSocket endpoint
                {
                    Console.WriteLine("Attempting to connect...");
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        var groupId = context.Request.Query["groupId"]; // groupId from query parameters
                        var webSocketManager = context.RequestServices.GetRequiredService<WebSocketManager>();

                        // Create a cancellation token for this connection
                        var cancellationTokenSource = new CancellationTokenSource();
                        // Call the WebSocketManager to handle the connection
                        await webSocketManager.HandleWebSocketAsync(webSocket, groupId, cancellationTokenSource.Token);
                    }
                    else
                    {
                        Console.WriteLine("Failed to connect: Not a WebSocket request.");
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });

            app.Run();
        }
    }
}
