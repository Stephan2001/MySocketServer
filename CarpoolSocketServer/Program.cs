using System.Net.WebSockets;

namespace CarpoolSocketServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSingleton<WebSocketManager>();

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
