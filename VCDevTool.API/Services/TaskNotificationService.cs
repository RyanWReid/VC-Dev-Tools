using Microsoft.AspNetCore.SignalR;
using VCDevTool.API.Hubs;
using VCDevTool.Shared;

namespace VCDevTool.API.Services
{
    public class TaskNotificationService
    {
        private readonly IHubContext<TaskHub> _hubContext;
        private readonly ILogger<TaskNotificationService> _logger;

        public TaskNotificationService(IHubContext<TaskHub> hubContext, ILogger<TaskNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task BroadcastTaskNotificationAsync(
            int taskId, 
            string? nodeId, 
            TaskType taskType, 
            BatchTaskStatus status, 
            string? message = null)
        {
            try
            {
                var notification = new
                {
                    TaskId = taskId,
                    NodeId = nodeId,
                    TaskType = (int)taskType,
                    Status = (int)status,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };

                // Broadcast to all connected clients
                await _hubContext.Clients.All.SendAsync("ReceiveTaskNotification", notification);

                // Also log to server logs
                _logger.LogDebug(
                    "Task notification: Id={TaskId}, Node={NodeId}, Type={TaskType}, Status={Status}, Message={Message}", 
                    taskId, nodeId, taskType, status, message ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting task notification: {Message}", ex.Message);
            }
        }
    }
} 