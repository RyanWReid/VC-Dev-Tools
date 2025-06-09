using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VCDevTool.API.Data;
using VCDevTool.API.Services;
using VCDevTool.Shared;
using System.Text.Json;

namespace VCDevTool.API.Tests.Services
{
    /// <summary>
    /// SQLite-compatible task service that implements proper file locking for SQLite databases
    /// This service avoids SQL Server-specific syntax and uses SQLite-compatible operations
    /// </summary>
    public class SqliteCompatibleTaskService : ITaskService
    {
        protected readonly AppDbContext _dbContext;
        protected readonly ILogger<TaskService> _logger;
        private readonly FileLockOptions _lockOptions;

        public SqliteCompatibleTaskService(AppDbContext dbContext, ILogger<TaskService> logger, IOptions<FileLockOptions>? lockOptions = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lockOptions = lockOptions?.Value ?? new FileLockOptions
            {
                LockTimeoutMinutes = 10,
                MaxLockAttempts = 3,
                RetryDelayBaseMs = 100,
                RetryDelayRandomMs = 200
            };
        }

        // Task Management Methods
        public async Task<List<BatchTask>> GetAllTasksAsync()
        {
            return await _dbContext.Tasks.OrderBy(t => t.CreatedAt).ToListAsync();
        }

        public async Task<BatchTask> GetTaskByIdAsync(int taskId)
        {
            return await _dbContext.Tasks.FindAsync(taskId) ?? new BatchTask { Id = -1 };
        }

        public async Task<BatchTask> CreateTaskAsync(BatchTask task)
        {
            task.CreatedAt = DateTime.UtcNow;
            task.Status = BatchTaskStatus.Pending;
            
            _dbContext.Tasks.Add(task);
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Created new task: {TaskId} - {TaskName}", task.Id, task.Name);
            return task;
        }

        public async Task<BatchTask> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                _logger?.LogWarning("Task not found for status update: {TaskId}", taskId);
                return new BatchTask { Id = -1 };
            }

            // If rowVersion is provided, set it for optimistic concurrency check
            if (rowVersion != null)
            {
                _dbContext.Entry(task).Property("RowVersion").OriginalValue = rowVersion;
            }

            task.Status = status;
            
            if (status == BatchTaskStatus.Running && !task.StartedAt.HasValue)
            {
                task.StartedAt = DateTime.UtcNow;
            }
            else if ((status == BatchTaskStatus.Completed || status == BatchTaskStatus.Failed) && !task.CompletedAt.HasValue)
            {
                task.CompletedAt = DateTime.UtcNow;
                task.ResultMessage = resultMessage;
            }

            await _dbContext.SaveChangesAsync();
            _logger?.LogInformation("Updated task status: {TaskId} to {Status}", taskId, status);
            return task;
        }

        public async Task<BatchTask> UpdateTaskAsync(BatchTask task)
        {
            var existingTask = await _dbContext.Tasks.FindAsync(task.Id);
            if (existingTask == null)
            {
                throw new InvalidOperationException($"Task with ID {task.Id} not found");
            }

            // Update properties
            existingTask.Name = task.Name;
            existingTask.Type = task.Type;
            existingTask.Status = task.Status;
            existingTask.AssignedNodeId = task.AssignedNodeId;
            existingTask.Parameters = task.Parameters;
            existingTask.ResultMessage = task.ResultMessage;
            existingTask.StartedAt = task.StartedAt;
            existingTask.CompletedAt = task.CompletedAt;

            await _dbContext.SaveChangesAsync();
            _logger?.LogInformation("Updated task: {TaskId}", task.Id);
            return existingTask;
        }

        public async Task<bool> DeleteTaskAsync(int taskId)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                return false;
            }

            _dbContext.Tasks.Remove(task);
            await _dbContext.SaveChangesAsync();
            _logger?.LogInformation("Deleted task: {TaskId}", taskId);
            return true;
        }

        public async Task<List<BatchTask>> GetTasksByStatusAsync(BatchTaskStatus status)
        {
            return await _dbContext.Tasks
                .Where(t => t.Status == status)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<BatchTask>> GetTasksByNodeAsync(string nodeId)
        {
            return await _dbContext.Tasks
                .Where(t => t.AssignedNodeId == nodeId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null || task.Status != BatchTaskStatus.Pending)
            {
                return false;
            }

            task.AssignedNodeId = nodeId;
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Assigned task {TaskId} to node {NodeId}", taskId, nodeId);
            return true;
        }

        // Node Management Methods
        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
            return await _dbContext.Nodes
                .Where(n => n.IsAvailable && n.LastHeartbeat > cutoffTime)
                .OrderBy(n => n.Name)
                .ToListAsync();
        }

        public async Task<ComputerNode> RegisterNodeAsync(ComputerNode node)
        {
            var existingNode = await _dbContext.Nodes.FindAsync(node.Id);
            if (existingNode == null)
            {
                node.LastHeartbeat = DateTime.UtcNow;
                _dbContext.Nodes.Add(node);
                _logger?.LogInformation("Registered new node: {NodeId} - {NodeName}", node.Id, node.Name);
            }
            else
            {
                existingNode.Name = node.Name;
                existingNode.IpAddress = node.IpAddress;
                existingNode.HardwareFingerprint = node.HardwareFingerprint;
                existingNode.IsAvailable = node.IsAvailable;
                existingNode.LastHeartbeat = DateTime.UtcNow;
                _logger?.LogInformation("Updated existing node: {NodeId} - {NodeName}", node.Id, node.Name);
            }

            await _dbContext.SaveChangesAsync();
            return existingNode ?? node;
        }

        public async Task<bool> UpdateNodeHeartbeatAsync(string nodeId)
        {
            var node = await _dbContext.Nodes.FindAsync(nodeId);
            if (node == null)
            {
                return false;
            }

            node.LastHeartbeat = DateTime.UtcNow;
            node.IsAvailable = true;
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Updated heartbeat for node: {NodeId}", nodeId);
            return true;
        }

        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
            return await _dbContext.Nodes.OrderBy(n => n.Name).ToListAsync();
        }

        // SQLite-compatible file locking implementation
        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
            // Normalize the path using shared helper so API & client match
            filePath = PathUtils.NormalizePath(filePath);
            
            var lockTimeout = TimeSpan.FromMinutes(_lockOptions.LockTimeoutMinutes);
            
            try
            {
                // Use a transaction to ensure atomic operation
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                try
                {
                    // Check if lock exists - SQLite compatible query (no WITH syntax)
                    var existingLock = await _dbContext.FileLocks
                        .FirstOrDefaultAsync(l => l.FilePath == filePath);
                        
                    if (existingLock == null)
                    {
                        // No lock exists - create a new one
                        var newLock = new FileLock
                        {
                            FilePath = filePath,
                            LockingNodeId = nodeId,
                            AcquiredAt = DateTime.UtcNow,
                            LastUpdatedAt = DateTime.UtcNow
                        };
                        _dbContext.FileLocks.Add(newLock);
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                        _logger?.LogInformation("Acquired new lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                        return true;
                    }
                    else
                    {
                        // Lock exists - check if it's stale or belongs to this node
                        if (DateTime.UtcNow - existingLock.LastUpdatedAt > lockTimeout)
                        {
                            // Remove stale lock and create new one
                            _dbContext.FileLocks.Remove(existingLock);
                            
                            var newLock = new FileLock
                            {
                                FilePath = filePath,
                                LockingNodeId = nodeId,
                                AcquiredAt = DateTime.UtcNow,
                                LastUpdatedAt = DateTime.UtcNow
                            };
                            _dbContext.FileLocks.Add(newLock);
                            await _dbContext.SaveChangesAsync();
                            await transaction.CommitAsync();
                            _logger?.LogInformation("Acquired lock after clearing stale lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                            return true;
                        }
                        else if (existingLock.LockingNodeId == nodeId)
                        {
                            // Update existing lock's timestamp if it belongs to this node
                            existingLock.LastUpdatedAt = DateTime.UtcNow;
                            await _dbContext.SaveChangesAsync();
                            await transaction.CommitAsync();
                            _logger?.LogInformation("Updated existing lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                            return true;
                        }
                        else
                        {
                            // Lock belongs to another node and is not stale
                            await transaction.CommitAsync();
                            _logger?.LogInformation("Lock acquisition failed for file {FilePath} - already locked by node {OtherNodeId}", 
                                filePath, existingLock.LockingNodeId);
                            return false;
                        }
                    }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error acquiring lock for file {FilePath} by node {NodeId}: {Message}", 
                    filePath, nodeId, ex.Message);
                return false;
            }
        }

        public async Task<bool> ReleaseFileLockAsync(string filePath, string nodeId)
        {
            // Normalize the path using shared helper so API & client match
            filePath = PathUtils.NormalizePath(filePath);
            
            try
            {
                // Use a transaction to ensure atomic operation
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                try
                {
                    // Get the lock - SQLite compatible query (no WITH syntax)
                    var fileLock = await _dbContext.FileLocks
                        .FirstOrDefaultAsync(l => l.FilePath == filePath);
    
                    if (fileLock == null)
                    {
                        // Lock doesn't exist, nothing to release
                        _logger?.LogInformation("No lock found to release for file: {FilePath} by node: {NodeId}", 
                            filePath, nodeId);
                        await transaction.CommitAsync();
                        return true;
                    }
    
                    // Allow release by the owner node or if lock is stale
                    bool isOwner = fileLock.LockingNodeId == nodeId;
                    bool isStale = DateTime.UtcNow - fileLock.LastUpdatedAt > TimeSpan.FromMinutes(_lockOptions.LockTimeoutMinutes);
                    
                    if (isOwner || isStale)
                    {
                        if (!isOwner && isStale)
                        {
                            _logger?.LogWarning("Node {NodeId} is releasing a stale lock owned by {OwnerNodeId} for file {FilePath}", 
                                nodeId, fileLock.LockingNodeId, filePath);
                        }
                        
                        _dbContext.FileLocks.Remove(fileLock);
                        await _dbContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                        
                        var duration = (DateTime.UtcNow - fileLock.AcquiredAt).TotalSeconds;
                        _logger?.LogInformation("Released lock for file: {FilePath} by node: {NodeId} (held for {Duration:F1} seconds)", 
                            filePath, nodeId, duration);
                        return true;
                    }
                    else
                    {
                        _logger?.LogWarning("Node {NodeId} attempted to release lock owned by {OwnerNodeId} for file {FilePath}", 
                            nodeId, fileLock.LockingNodeId, filePath);
                        await transaction.CommitAsync();
                        return false;
                    }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error releasing lock for file {FilePath} by node {NodeId}: {Message}", 
                    filePath, nodeId, ex.Message);
                return false;
            }
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            try
            {
                return await _dbContext.FileLocks
                    .OrderBy(l => l.AcquiredAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving active locks: {Message}", ex.Message);
                return new List<FileLock>();
            }
        }

        public async Task<bool> ResetAllFileLocksAsync()
        {
            try
            {
                var allLocks = await _dbContext.FileLocks.ToListAsync();
                if (allLocks.Any())
                {
                    _dbContext.FileLocks.RemoveRange(allLocks);
                    await _dbContext.SaveChangesAsync();
                    _logger?.LogInformation("Reset {Count} file locks", allLocks.Count);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error resetting file locks: {Message}", ex.Message);
                return false;
            }
        }

        // Task Folder Progress Management
        public async Task<List<TaskFolderProgress>> GetTaskFolderProgressAsync(int taskId)
        {
            try
            {
                return await _dbContext.TaskFolderProgress
                    .Where(tfp => tfp.TaskId == taskId)
                    .OrderBy(tfp => tfp.FolderName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving task folder progress for task {TaskId}: {Message}", taskId, ex.Message);
                return new List<TaskFolderProgress>();
            }
        }

        public async Task<TaskFolderProgress> CreateTaskFolderProgressAsync(TaskFolderProgress folderProgress)
        {
            try
            {
                folderProgress.CreatedAt = DateTime.UtcNow;
                _dbContext.TaskFolderProgress.Add(folderProgress);
                await _dbContext.SaveChangesAsync();
                
                _logger?.LogInformation("Created folder progress: TaskId={TaskId}, Folder={FolderName}", 
                    folderProgress.TaskId, folderProgress.FolderName);
                
                return folderProgress;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating task folder progress: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<TaskFolderProgress> UpdateTaskFolderProgressAsync(int id, TaskFolderStatus status, string? nodeId = null, string? nodeName = null, double progress = 0.0, string? errorMessage = null, string? outputPath = null)
        {
            try
            {
                var folderProgress = await _dbContext.TaskFolderProgress.FindAsync(id);
                if (folderProgress == null)
                {
                    _logger?.LogWarning("Task folder progress not found: {Id}", id);
                    return new TaskFolderProgress { Id = -1 };
                }

                folderProgress.Status = status;
                folderProgress.Progress = Math.Max(0, Math.Min(1, progress)); // Clamp between 0 and 1

                if (!string.IsNullOrEmpty(nodeId))
                {
                    folderProgress.AssignedNodeId = nodeId;
                }

                if (!string.IsNullOrEmpty(nodeName))
                {
                    folderProgress.AssignedNodeName = nodeName;
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    folderProgress.ErrorMessage = errorMessage;
                }

                if (!string.IsNullOrEmpty(outputPath))
                {
                    folderProgress.OutputPath = outputPath;
                }

                if (status == TaskFolderStatus.InProgress && !folderProgress.StartedAt.HasValue)
                {
                    folderProgress.StartedAt = DateTime.UtcNow;
                }
                else if ((status == TaskFolderStatus.Completed || status == TaskFolderStatus.Failed) && !folderProgress.CompletedAt.HasValue)
                {
                    folderProgress.CompletedAt = DateTime.UtcNow;
                    if (status == TaskFolderStatus.Completed)
                    {
                        folderProgress.Progress = 1.0; // Ensure completed tasks show 100%
                    }
                }

                await _dbContext.SaveChangesAsync();
                
                _logger?.LogInformation("Updated folder progress: Id={Id}, Status={Status}, Progress={Progress:P0}", 
                    id, status, progress);
                
                return folderProgress;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating task folder progress {Id}: {Message}", id, ex.Message);
                throw;
            }
        }

        public async Task<bool> DeleteTaskFolderProgressAsync(int taskId)
        {
            try
            {
                var folderProgressList = await _dbContext.TaskFolderProgress
                    .Where(tfp => tfp.TaskId == taskId)
                    .ToListAsync();

                if (folderProgressList.Any())
                {
                    _dbContext.TaskFolderProgress.RemoveRange(folderProgressList);
                    await _dbContext.SaveChangesAsync();
                    
                    _logger?.LogInformation("Deleted {Count} folder progress records for task {TaskId}", 
                        folderProgressList.Count, taskId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting task folder progress for task {TaskId}: {Message}", taskId, ex.Message);
                return false;
            }
        }

        public async Task<bool> CheckAndCompleteVolumeCompressionTaskAsync(int taskId)
        {
            try
            {
                var task = await _dbContext.Tasks.FindAsync(taskId);
                if (task == null || task.Type != TaskType.VolumeCompression)
                {
                    return false;
                }

                // For test purposes, simply mark the task as completed
                if (task.Status == BatchTaskStatus.Running)
                {
                    task.Status = BatchTaskStatus.Completed;
                    task.CompletedAt = DateTime.UtcNow;
                    task.ResultMessage = "Volume compression completed (test mode)";
                    await _dbContext.SaveChangesAsync();
                    
                    _logger?.LogInformation("Marked volume compression task {TaskId} as completed", taskId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking volume compression task {TaskId}: {Message}", taskId, ex.Message);
                return false;
            }
        }

        public async Task<List<BatchTask>> GetPendingTasksForNodeAsync(string nodeId)
        {
            return await _dbContext.Tasks
                .Where(t => t.Status == BatchTaskStatus.Pending && 
                           (t.AssignedNodeId == nodeId || t.AssignedNodeId == null))
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> StartTaskAsync(int taskId, string nodeId)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null || task.AssignedNodeId != nodeId || task.Status != BatchTaskStatus.Pending)
            {
                return false;
            }

            task.Status = BatchTaskStatus.Running;
            task.StartedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Started task {TaskId} on node {NodeId}", taskId, nodeId);
            return true;
        }

        public async Task<bool> CompleteTaskAsync(int taskId, string nodeId, string? resultMessage = null)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null || task.AssignedNodeId != nodeId || task.Status != BatchTaskStatus.Running)
            {
                return false;
            }

            task.Status = BatchTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.ResultMessage = resultMessage;
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Completed task {TaskId} on node {NodeId}", taskId, nodeId);
            return true;
        }

        public async Task<bool> FailTaskAsync(int taskId, string nodeId, string? errorMessage = null)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null || task.AssignedNodeId != nodeId)
            {
                return false;
            }

            task.Status = BatchTaskStatus.Failed;
            task.CompletedAt = DateTime.UtcNow;
            task.ResultMessage = errorMessage;
            await _dbContext.SaveChangesAsync();
            
            _logger?.LogInformation("Failed task {TaskId} on node {NodeId}: {ErrorMessage}", taskId, nodeId, errorMessage);
            return true;
        }
    }
} 