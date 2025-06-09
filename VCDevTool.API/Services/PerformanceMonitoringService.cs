using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VCDevTool.API.Data;
using VCDevTool.Shared;

namespace VCDevTool.API.Services
{
    /// <summary>
    /// Service for monitoring application performance and database metrics
    /// </summary>
    public interface IPerformanceMonitoringService
    {
        Task<PerformanceMetrics> GetPerformanceMetricsAsync(DateTime fromDate, DateTime toDate);
        Task<DatabasePerformanceMetrics> GetDatabasePerformanceAsync();
        Task<NodePerformanceMetrics[]> GetNodePerformanceMetricsAsync(DateTime fromDate);
        Task<SystemHealthStatus> GetSystemHealthAsync();
        Task LogPerformanceEventAsync(string eventType, TimeSpan duration, Dictionary<string, object>? metadata = null);
    }

    public class PerformanceMonitoringService : IPerformanceMonitoringService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PerformanceMonitoringService> _logger;

        public PerformanceMonitoringService(AppDbContext context, ILogger<PerformanceMonitoringService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PerformanceMetrics> GetPerformanceMetricsAsync(DateTime fromDate, DateTime toDate)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var tasks = await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.CreatedAt >= fromDate && t.CreatedAt <= toDate)
                    .ToListAsync();

                var metrics = new PerformanceMetrics
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    TotalTasks = tasks.Count,
                    CompletedTasks = tasks.Count(t => t.Status == BatchTaskStatus.Completed),
                    FailedTasks = tasks.Count(t => t.Status == BatchTaskStatus.Failed),
                    RunningTasks = tasks.Count(t => t.Status == BatchTaskStatus.Running),
                    PendingTasks = tasks.Count(t => t.Status == BatchTaskStatus.Pending),
                    CancelledTasks = tasks.Count(t => t.Status == BatchTaskStatus.Cancelled)
                };

                // Calculate processing times for completed tasks
                var completedTasksWithTimes = tasks
                    .Where(t => t.Status == BatchTaskStatus.Completed && 
                               t.StartedAt.HasValue && 
                               t.CompletedAt.HasValue)
                    .Select(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalSeconds)
                    .ToList();

                if (completedTasksWithTimes.Any())
                {
                    metrics.AverageProcessingTimeSeconds = completedTasksWithTimes.Average();
                    metrics.MinProcessingTimeSeconds = completedTasksWithTimes.Min();
                    metrics.MaxProcessingTimeSeconds = completedTasksWithTimes.Max();
                }

                // Task type distribution
                metrics.TaskTypeDistribution = tasks
                    .GroupBy(t => t.Type)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count());

                // Throughput calculation (tasks completed per hour)
                var timeSpanHours = (toDate - fromDate).TotalHours;
                metrics.ThroughputTasksPerHour = timeSpanHours > 0 ? metrics.CompletedTasks / timeSpanHours : 0;

                return metrics;
            }
            finally
            {
                stopwatch.Stop();
                await LogPerformanceEventAsync("GetPerformanceMetrics", stopwatch.Elapsed);
            }
        }

        public async Task<DatabasePerformanceMetrics> GetDatabasePerformanceAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var metrics = new DatabasePerformanceMetrics
                {
                    Timestamp = DateTime.UtcNow
                };

                // Get table sizes and counts
                metrics.TasksCount = await _context.Tasks.CountAsync();
                metrics.NodesCount = await _context.Nodes.CountAsync();
                metrics.FileLocksCount = await _context.FileLocks.CountAsync();
                metrics.TaskFolderProgressCount = await _context.TaskFolderProgress.CountAsync();

                // Test query performance for common operations
                var queryStopwatch = Stopwatch.StartNew();
                
                // Test indexed query performance
                await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.Status == BatchTaskStatus.Pending)
                    .Take(1)
                    .FirstOrDefaultAsync();
                
                queryStopwatch.Stop();
                metrics.IndexedQueryPerformanceMs = queryStopwatch.ElapsedMilliseconds;

                // Test complex query performance
                queryStopwatch.Restart();
                
                await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.Status == BatchTaskStatus.Running && 
                               t.CreatedAt >= DateTime.UtcNow.AddDays(-1))
                    .Take(10)
                    .ToListAsync();
                
                queryStopwatch.Stop();
                metrics.ComplexQueryPerformanceMs = queryStopwatch.ElapsedMilliseconds;

                // Check for old file locks (potential cleanup needed)
                metrics.ExpiredFileLocksCount = await _context.FileLocks
                    .WithPerformanceOptimizations()
                    .Where(fl => fl.LastUpdatedAt < DateTime.UtcNow.AddHours(-1))
                    .CountAsync();

                return metrics;
            }
            finally
            {
                stopwatch.Stop();
                await LogPerformanceEventAsync("GetDatabasePerformance", stopwatch.Elapsed);
            }
        }

        public async Task<NodePerformanceMetrics[]> GetNodePerformanceMetricsAsync(DateTime fromDate)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // First get the basic grouped data
                var nodeTaskData = await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.AssignedNodeId != null && t.CreatedAt >= fromDate)
                    .Select(t => new 
                    {
                        t.AssignedNodeId,
                        t.Status,
                        t.StartedAt,
                        t.CompletedAt
                    })
                    .ToListAsync();

                // Group and calculate metrics in memory
                var nodeMetrics = nodeTaskData
                    .GroupBy(t => t.AssignedNodeId)
                    .Select(g => new NodePerformanceMetrics
                    {
                        NodeId = g.Key!,
                        TasksProcessed = g.Count(),
                        TasksCompleted = g.Count(t => t.Status == BatchTaskStatus.Completed),
                        TasksFailed = g.Count(t => t.Status == BatchTaskStatus.Failed),
                        AverageProcessingTimeSeconds = g
                            .Where(t => t.Status == BatchTaskStatus.Completed && 
                                       t.StartedAt.HasValue && 
                                       t.CompletedAt.HasValue)
                            .Select(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalSeconds)
                            .DefaultIfEmpty(0)
                            .Average()
                    })
                    .ToArray();

                // Enrich with node information
                var nodeIds = nodeMetrics.Select(nm => nm.NodeId).ToList();
                var nodes = await _context.Nodes
                    .WithPerformanceOptimizations()
                    .Where(n => nodeIds.Contains(n.Id))
                    .ToDictionaryAsync(n => n.Id, n => n);

                foreach (var metric in nodeMetrics)
                {
                    if (nodes.TryGetValue(metric.NodeId, out var node))
                    {
                        metric.NodeName = node.Name;
                        metric.IsAvailable = node.IsAvailable;
                        metric.LastHeartbeat = node.LastHeartbeat;
                    }
                }

                return nodeMetrics;
            }
            finally
            {
                stopwatch.Stop();
                await LogPerformanceEventAsync("GetNodePerformanceMetrics", stopwatch.Elapsed);
            }
        }

        public async Task<SystemHealthStatus> GetSystemHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var health = new SystemHealthStatus
                {
                    Timestamp = DateTime.UtcNow,
                    IsHealthy = true,
                    Issues = new List<string>()
                };

                // Check database connectivity
                try
                {
                    var dbStopwatch = Stopwatch.StartNew();
                    await _context.Database.ExecuteSqlRawAsync("SELECT 1");
                    dbStopwatch.Stop();
                    
                    health.DatabaseResponseTimeMs = dbStopwatch.ElapsedMilliseconds;
                    
                    if (health.DatabaseResponseTimeMs > 5000) // 5 seconds threshold
                    {
                        health.IsHealthy = false;
                        health.Issues.Add($"Database response time is slow: {health.DatabaseResponseTimeMs}ms");
                    }
                }
                catch (Exception ex)
                {
                    health.IsHealthy = false;
                    health.Issues.Add($"Database connectivity issue: {ex.Message}");
                    health.DatabaseResponseTimeMs = -1;
                }

                // Check for stuck tasks
                var stuckTasks = await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.Status == BatchTaskStatus.Running && 
                               t.StartedAt.HasValue && 
                               t.StartedAt < DateTime.UtcNow.AddHours(-24))
                    .CountAsync();

                health.StuckTasksCount = stuckTasks;
                if (stuckTasks > 0)
                {
                    health.Issues.Add($"Found {stuckTasks} tasks running for more than 24 hours");
                }

                // Check for expired file locks
                var expiredLocks = await _context.FileLocks
                    .WithPerformanceOptimizations()
                    .Where(fl => fl.LastUpdatedAt < DateTime.UtcNow.AddHours(-2))
                    .CountAsync();

                health.ExpiredFileLocksCount = expiredLocks;
                if (expiredLocks > 100) // Threshold for concern
                {
                    health.Issues.Add($"High number of expired file locks: {expiredLocks}");
                }

                // Check node availability
                var totalNodes = await _context.Nodes.CountAsync();
                var availableNodes = await _context.Nodes
                    .WithPerformanceOptimizations()
                    .Where(n => n.IsAvailable && n.LastHeartbeat >= DateTime.UtcNow.AddMinutes(-5))
                    .CountAsync();

                health.TotalNodes = totalNodes;
                health.AvailableNodes = availableNodes;

                if (totalNodes > 0 && (double)availableNodes / totalNodes < 0.5) // Less than 50% nodes available
                {
                    health.IsHealthy = false;
                    health.Issues.Add($"Low node availability: {availableNodes}/{totalNodes} nodes available");
                }

                return health;
            }
            finally
            {
                stopwatch.Stop();
                await LogPerformanceEventAsync("GetSystemHealth", stopwatch.Elapsed);
            }
        }

        public async Task LogPerformanceEventAsync(string eventType, TimeSpan duration, Dictionary<string, object>? metadata = null)
        {
            try
            {
                var logData = new
                {
                    EventType = eventType,
                    DurationMs = duration.TotalMilliseconds,
                    Timestamp = DateTime.UtcNow,
                    Metadata = metadata
                };

                _logger.LogInformation("Performance Event: {EventType} took {DurationMs}ms at {Timestamp}. Metadata: {@Metadata}",
                    eventType, duration.TotalMilliseconds, DateTime.UtcNow, metadata);

                // In a production system, you might also want to:
                // - Store to a time-series database
                // - Send to application monitoring service (Application Insights, etc.)
                // - Trigger alerts if duration exceeds thresholds

                await Task.CompletedTask; // Placeholder for async operations
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log performance event: {EventType}", eventType);
            }
        }
    }

    /// <summary>
    /// Performance metrics data models
    /// </summary>
    public class PerformanceMetrics
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int FailedTasks { get; set; }
        public int RunningTasks { get; set; }
        public int PendingTasks { get; set; }
        public int CancelledTasks { get; set; }
        public double AverageProcessingTimeSeconds { get; set; }
        public double MinProcessingTimeSeconds { get; set; }
        public double MaxProcessingTimeSeconds { get; set; }
        public double ThroughputTasksPerHour { get; set; }
        public Dictionary<string, int> TaskTypeDistribution { get; set; } = new();
    }

    public class DatabasePerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public long TasksCount { get; set; }
        public long NodesCount { get; set; }
        public long FileLocksCount { get; set; }
        public long TaskFolderProgressCount { get; set; }
        public long IndexedQueryPerformanceMs { get; set; }
        public long ComplexQueryPerformanceMs { get; set; }
        public long ExpiredFileLocksCount { get; set; }
    }

    public class NodePerformanceMetrics
    {
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public int TasksProcessed { get; set; }
        public int TasksCompleted { get; set; }
        public int TasksFailed { get; set; }
        public double AverageProcessingTimeSeconds { get; set; }
        public double SuccessRate => TasksProcessed > 0 ? (double)TasksCompleted / TasksProcessed : 0;
    }

    public class SystemHealthStatus
    {
        public DateTime Timestamp { get; set; }
        public bool IsHealthy { get; set; }
        public List<string> Issues { get; set; } = new();
        public long DatabaseResponseTimeMs { get; set; }
        public int StuckTasksCount { get; set; }
        public int ExpiredFileLocksCount { get; set; }
        public int TotalNodes { get; set; }
        public int AvailableNodes { get; set; }
    }
} 