using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;
using VCDevTool.API.Hubs;

namespace VCDevTool.API.Services
{
    public class DebugBroadcastService
    {
        private readonly IHubContext<DebugHub> _hubContext;
        private readonly ILogger<DebugBroadcastService> _logger;

        public DebugBroadcastService(IHubContext<DebugHub> hubContext, ILogger<DebugBroadcastService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task BroadcastDebugMessageAsync(string source, string message, string? nodeId = null)
        {
            try
            {
                var debugMessage = new
                {
                    Source = source,
                    Message = message,
                    NodeId = nodeId,
                    Timestamp = DateTime.UtcNow
                };

                // Broadcast to all connected clients
                await _hubContext.Clients.All.SendAsync("ReceiveDebugMessage", debugMessage);

                // Also log to server logs
                _logger.LogDebug("[{Source}] {Message}", source, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting debug message: {Message}", ex.Message);
            }
        }
    }
} 