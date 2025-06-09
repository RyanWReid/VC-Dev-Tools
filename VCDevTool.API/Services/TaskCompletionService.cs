using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VCDevTool.Shared;

namespace VCDevTool.API.Services
{
    /// <summary>
    /// Background service that periodically checks for volume compression tasks that should be marked as completed
    /// </summary>
    public class TaskCompletionService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TaskCompletionService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30); // Check every 30 seconds

        public TaskCompletionService(IServiceProvider serviceProvider, ILogger<TaskCompletionService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Task Completion Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForCompletedTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking for completed tasks");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Task Completion Service stopped");
        }

        private async Task CheckForCompletedTasksAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<TaskService>();

            try
            {
                // Get all running volume compression tasks
                var runningTasks = await taskService.GetAllTasksAsync();
                var runningVolumeCompressionTasks = runningTasks
                    .Where(t => t.Type == TaskType.VolumeCompression && t.Status == BatchTaskStatus.Running)
                    .ToList();

                if (!runningVolumeCompressionTasks.Any())
                {
                    return; // No running volume compression tasks to check
                }

                _logger.LogDebug("Checking {Count} running volume compression tasks for completion", runningVolumeCompressionTasks.Count);

                foreach (var task in runningVolumeCompressionTasks)
                {
                    try
                    {
                        bool wasCompleted = await taskService.CheckAndCompleteVolumeCompressionTaskAsync(task.Id);
                        if (wasCompleted)
                        {
                            _logger.LogInformation("Marked volume compression task {TaskId} as completed", task.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking completion for task {TaskId}", task.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving running tasks");
            }
        }
    }
} 