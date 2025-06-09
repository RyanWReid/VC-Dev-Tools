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
    /// Test-specific TaskService that implements SQLite-compatible file locking
    /// without SQL Server-specific locking syntax
    /// </summary>
    public class TestTaskService : ITaskService
    {
        protected readonly AppDbContext _dbContext;
        protected readonly ILogger<TaskService> _logger;
        private readonly FileLockOptions _lockOptions;

        public TestTaskService(AppDbContext dbContext, ILogger<TaskService> logger, IOptions<FileLockOptions>? lockOptions = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lockOptions = lockOptions?.Value ?? new FileLockOptions();
        }

        // Task Management - delegate to the original TaskService logic
        public async Task<List<BatchTask>> GetAllTasksAsync()
        {
            return await _dbContext.Tasks.ToListAsync();
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

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                return false;
            }

            task.AssignedNodeId = nodeId;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        // Node Management
        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
            return await _dbContext.Nodes
                .Where(n => n.IsAvailable && n.LastHeartbeat > cutoffTime)
                .ToListAsync();
        }

        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
            return await _dbContext.Nodes.ToListAsync();
        }

        public async Task<ComputerNode> RegisterNodeAsync(ComputerNode node)
        {
            var existingNode = await _dbContext.Nodes.FindAsync(node.Id);
            
            if (existingNode != null)
            {
                existingNode.Name = node.Name;
                existingNode.IpAddress = node.IpAddress;
                existingNode.LastHeartbeat = DateTime.UtcNow;
                existingNode.IsAvailable = true;
                existingNode.HardwareFingerprint = node.HardwareFingerprint;
                await _dbContext.SaveChangesAsync();
                return existingNode;
            }
            else
            {
                node.LastHeartbeat = DateTime.UtcNow;
                node.IsAvailable = true;
                _dbContext.Nodes.Add(node);
                await _dbContext.SaveChangesAsync();
                return node;
            }
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
            return true;
        }

        // SQLite-compatible file locking methods
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
                    // Check if lock exists - SQLite compatible query
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
                _logger?.LogWarning("Failed to acquire lock for file: {FilePath} by node: {NodeId}: {Error}", 
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
                    // Get the lock - SQLite compatible query
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
                        
                        _logger?.LogInformation("Released lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
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
                return await _dbContext.FileLocks.ToListAsync();
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
                _dbContext.FileLocks.RemoveRange(allLocks);
                await _dbContext.SaveChangesAsync();
                _logger?.LogInformation("Reset all file locks - removed {Count} locks", allLocks.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error resetting all file locks: {Message}", ex.Message);
                return false;
            }
        }

        // Task Folder Progress Management - basic implementation for tests
        public async Task<List<TaskFolderProgress>> GetTaskFolderProgressAsync(int taskId)
        {
            return await _dbContext.TaskFolderProgress
                .Where(tfp => tfp.TaskId == taskId)
                .ToListAsync();
        }

        public async Task<TaskFolderProgress> CreateTaskFolderProgressAsync(TaskFolderProgress folderProgress)
        {
            folderProgress.CreatedAt = DateTime.UtcNow;
            _dbContext.TaskFolderProgress.Add(folderProgress);
            await _dbContext.SaveChangesAsync();
            return folderProgress;
        }

        public async Task<TaskFolderProgress> UpdateTaskFolderProgressAsync(int id, TaskFolderStatus status, 
            string? nodeId = null, string? nodeName = null, double progress = 0.0, 
            string? errorMessage = null, string? outputPath = null)
        {
            var folderProgress = await _dbContext.TaskFolderProgress.FindAsync(id);
            if (folderProgress == null)
            {
                return new TaskFolderProgress { Id = -1 };
            }

            folderProgress.Status = status;
            folderProgress.Progress = progress;
            
            if (!string.IsNullOrEmpty(nodeId))
                folderProgress.AssignedNodeId = nodeId;
            if (!string.IsNullOrEmpty(nodeName))
                folderProgress.AssignedNodeName = nodeName;
            if (!string.IsNullOrEmpty(errorMessage))
                folderProgress.ErrorMessage = errorMessage;
            if (!string.IsNullOrEmpty(outputPath))
                folderProgress.OutputPath = outputPath;

            if (status == TaskFolderStatus.Completed || status == TaskFolderStatus.Failed)
            {
                folderProgress.CompletedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
            return folderProgress;
        }

        public async Task<bool> DeleteTaskFolderProgressAsync(int taskId)
        {
            var folderProgressList = await _dbContext.TaskFolderProgress
                .Where(tfp => tfp.TaskId == taskId)
                .ToListAsync();
            
            _dbContext.TaskFolderProgress.RemoveRange(folderProgressList);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CheckAndCompleteVolumeCompressionTaskAsync(int taskId)
        {
            // Basic implementation for tests
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task != null && task.Type == TaskType.VolumeCompression)
            {
                task.Status = BatchTaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return true;
            }
            return false;
        }
    }
} 