using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCDevTool.API.Data;
using VCDevTool.Shared;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace VCDevTool.API.Services
{
    public class FileLockOptions
    {
        public int LockTimeoutMinutes { get; set; } = 10;
        public int MaxLockAttempts { get; set; } = 3;
        public int RetryDelayBaseMs { get; set; } = 500;
        public int RetryDelayRandomMs { get; set; } = 500;
    }

    public class TaskService : ITaskService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<TaskService> _logger;
        private readonly FileLockOptions _lockOptions;

        public TaskService(
            AppDbContext dbContext, 
            ILogger<TaskService> logger,
            IOptions<FileLockOptions>? lockOptions = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lockOptions = lockOptions?.Value ?? new FileLockOptions();
        }

        // Task Management
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
            
            _logger.LogInformation("Created new task: {TaskId} - {TaskName}", task.Id, task.Name);
            return task;
        }

        public async Task<BatchTask> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                _logger.LogWarning("Task not found for status update: {TaskId}", taskId);
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
            _logger.LogInformation("Updated task status: {TaskId} to {Status}", taskId, status);
            return task;
        }

        public async Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                _logger.LogWarning("Task not found for assignment: {TaskId}", taskId);
                return false;
            }

            var node = await _dbContext.Nodes.FindAsync(nodeId);
            if (node == null || !node.IsAvailable)
            {
                _logger.LogWarning("Node not found or unavailable: {NodeId}", nodeId);
                return false;
            }

            // Parse existing assigned node IDs
            List<string> assignedNodeIds;
            try
            {
                assignedNodeIds = JsonSerializer.Deserialize<List<string>>(task.AssignedNodeIds) ?? new List<string>();
            }
            catch
            {
                assignedNodeIds = new List<string>();
            }

            // Add the new node if not already assigned
            if (!assignedNodeIds.Contains(nodeId))
            {
                assignedNodeIds.Add(nodeId);
                task.AssignedNodeIds = JsonSerializer.Serialize(assignedNodeIds);
                
                // Keep the first assigned node in AssignedNodeId for backward compatibility
                if (string.IsNullOrEmpty(task.AssignedNodeId))
                {
                    task.AssignedNodeId = nodeId;
                }
                
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Assigned task {TaskId} to node {NodeId}. Total assigned nodes: {Count}", 
                    taskId, nodeId, assignedNodeIds.Count);
            }
            else
            {
                _logger.LogInformation("Task {TaskId} already assigned to node {NodeId}", taskId, nodeId);
            }
            
            return true;
        }

        // Node Management
        public async Task<List<ComputerNode>> GetAvailableNodesAsync()
        {
            // Consider a node available if it had a heartbeat in the last minute
            var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
            return await _dbContext.Nodes
                .Where(n => n.IsAvailable && n.LastHeartbeat > cutoffTime)
                .ToListAsync();
        }

        // Retrieve all nodes, including offline ones
        public async Task<List<ComputerNode>> GetAllNodesAsync()
        {
            return await _dbContext.Nodes.ToListAsync();
        }

        public async Task<ComputerNode> RegisterNodeAsync(ComputerNode node)
        {
            // Validate input
            if (node == null || string.IsNullOrEmpty(node.Id))
            {
                throw new ArgumentException("Invalid node data");
            }

            try
            {
                ComputerNode? existingNode = null;
                
                // STEP 1: Try to find node by unique device ID (most reliable)
                existingNode = await _dbContext.Nodes.FindAsync(node.Id);
                
                // STEP 2: If not found by ID, try to find by hardware fingerprint (next most reliable)
                if (existingNode == null && !string.IsNullOrEmpty(node.HardwareFingerprint))
                {
                    existingNode = await _dbContext.Nodes
                        .FirstOrDefaultAsync(n => n.HardwareFingerprint == node.HardwareFingerprint);
                    
                    // If found by fingerprint but IDs don't match, update the ID (hardware is same)
                    if (existingNode != null && existingNode.Id != node.Id)
                    {
                        _logger.LogWarning("Node with same hardware fingerprint found but different ID. " +
                                         "Old ID: {OldId}, New ID: {NewId}", existingNode.Id, node.Id);
                        
                        // Update the existing node's ID
                        await UpdateNodeIdentifierAsync(existingNode, node.Id);
                    }
                }
                
                // STEP 3: Finally try by IP address (least reliable, but still useful)
                if (existingNode == null)
                {
                    existingNode = await _dbContext.Nodes
                        .FirstOrDefaultAsync(n => n.IpAddress == node.IpAddress);
                    
                    // If found by IP address, log this as potential duplicate node
                    if (existingNode != null)
                    {
                        _logger.LogWarning("Node with same IP address found. This could be a different machine. " +
                                          "Examining hardware fingerprints for verification.");
                        
                        // If hardware fingerprints exist and are different, treat as a new node
                        if (!string.IsNullOrEmpty(existingNode.HardwareFingerprint) && 
                            !string.IsNullOrEmpty(node.HardwareFingerprint) && 
                            existingNode.HardwareFingerprint != node.HardwareFingerprint)
                        {
                            _logger.LogInformation("Hardware fingerprints don't match. Treating as new node.");
                            existingNode = null;
                        }
                    }
                }
                
                if (existingNode != null)
                {
                    // Update existing node properties
                    existingNode.Name = node.Name;
                    existingNode.IpAddress = node.IpAddress;
                    existingNode.IsAvailable = true;
                    existingNode.LastHeartbeat = DateTime.UtcNow;
                    
                    // Update hardware fingerprint if provided
                    if (!string.IsNullOrEmpty(node.HardwareFingerprint))
                    {
                        existingNode.HardwareFingerprint = node.HardwareFingerprint;
                    }
                    
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Updated existing node: {NodeId} - {NodeName}", existingNode.Id, existingNode.Name);
                    return existingNode;
                }
                else
                {
                    // Create new node
                    var newNode = new ComputerNode
                    {
                        Id = node.Id,
                        Name = node.Name,
                        IpAddress = node.IpAddress,
                        HardwareFingerprint = node.HardwareFingerprint,
                        IsAvailable = true,
                        LastHeartbeat = DateTime.UtcNow,
                        // Copy any AD properties
                        ActiveDirectoryName = node.ActiveDirectoryName,
                        DomainController = node.DomainController,
                        OrganizationalUnit = node.OrganizationalUnit,
                        DistinguishedName = node.DistinguishedName,
                        DnsHostName = node.DnsHostName,
                        OperatingSystem = node.OperatingSystem,
                        LastAdLogon = node.LastAdLogon,
                        IsAdEnabled = node.IsAdEnabled,
                        AdGroups = node.AdGroups,
                        LastAdSync = node.LastAdSync,
                        ServicePrincipalName = node.ServicePrincipalName
                    };
                    
                    _dbContext.Nodes.Add(newNode);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Registered new node: {NodeId} - {NodeName}", newNode.Id, newNode.Name);
                    return newNode;
                }
            }
            catch (Exception ex) when (ex.InnerException?.Message.Contains("duplicate key") == true || 
                                      ex.Message.Contains("already being tracked") ||
                                      ex.InnerException?.Message.Contains("constraint") == true)
            {
                _logger.LogWarning(ex, "Entity tracking or constraint violation detected. Attempting cleanup and retry for node {NodeId}", node.Id);
                
                // Clear the context state to avoid tracking conflicts
                _dbContext.ChangeTracker.Clear();
                
                // Try cleaning up the database first
                await CleanupStaleNodesAsync();
                
                // Retry with a fresh query and simple registration
                var existingNodeRetry = await _dbContext.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == node.Id);
                
                if (existingNodeRetry != null)
                {
                    // Node exists, update it
                    _dbContext.Attach(existingNodeRetry);
                    existingNodeRetry.Name = node.Name;
                    existingNodeRetry.IpAddress = node.IpAddress;
                    existingNodeRetry.IsAvailable = true;
                    existingNodeRetry.LastHeartbeat = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(node.HardwareFingerprint))
                    {
                        existingNodeRetry.HardwareFingerprint = node.HardwareFingerprint;
                    }
                    
                    _dbContext.Entry(existingNodeRetry).State = EntityState.Modified;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Updated node on retry: {NodeId} - {NodeName}", existingNodeRetry.Id, existingNodeRetry.Name);
                    return existingNodeRetry;
                }
                else
                {
                    // Create new node
                    var retryNode = new ComputerNode
                    {
                        Id = node.Id,
                        Name = node.Name,
                        IpAddress = node.IpAddress,
                        HardwareFingerprint = node.HardwareFingerprint,
                        IsAvailable = true,
                        LastHeartbeat = DateTime.UtcNow
                    };
                    
                    _dbContext.Nodes.Add(retryNode);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Registered new node on retry: {NodeId} - {NodeName}", retryNode.Id, retryNode.Name);
                    return retryNode;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering node {NodeId} with IP {IpAddress}", node.Id, node.IpAddress);
                throw;
            }
        }
        
        // Helper method to update a node's identifier while maintaining relationships
        private async Task UpdateNodeIdentifierAsync(ComputerNode node, string newId)
        {
            // Update all tasks assigned to the old node to use the new node ID
            var tasksToUpdate = await _dbContext.Tasks
                .Where(t => t.AssignedNodeId == node.Id)
                .ToListAsync();
            
            foreach (var task in tasksToUpdate)
            {
                task.AssignedNodeId = newId;
            }
            
            // Update file locks too
            var locksToUpdate = await _dbContext.FileLocks
                .Where(l => l.LockingNodeId == node.Id)
                .ToListAsync();
            
            foreach (var fileLock in locksToUpdate)
            {
                fileLock.LockingNodeId = newId;
            }
            
            // Update the node ID
            node.Id = newId;
        }
        
        // Helper method to clean up stale nodes
        private async Task CleanupStaleNodesAsync()
        {
            try
            {
                // Find and remove nodes that haven't sent a heartbeat in the last hour
                var staleTime = DateTime.UtcNow.AddHours(-1);
                var staleNodes = await _dbContext.Nodes
                    .Where(n => n.LastHeartbeat < staleTime)
                    .ToListAsync();
                
                if (staleNodes.Any())
                {
                    _logger.LogInformation("Removing {Count} stale nodes from the database", staleNodes.Count);
                    _dbContext.Nodes.RemoveRange(staleNodes);
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up stale nodes");
            }
        }

        public async Task<bool> UpdateNodeHeartbeatAsync(string nodeId)
        {
            var node = await _dbContext.Nodes.FindAsync(nodeId);
            if (node == null)
            {
                _logger.LogWarning("Node not found for heartbeat update: {NodeId}", nodeId);
                return false;
            }

            node.LastHeartbeat = DateTime.UtcNow;
            node.IsAvailable = true;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        // File Locking
        public async Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId)
        {
            // Normalize the path using shared helper so API & client match
            filePath = PathUtils.NormalizePath(filePath);
            
            _logger.LogInformation("Starting lock acquisition for file: {FilePath}, node: {NodeId}", filePath, nodeId);
            
            // Get lock options from configuration
            var lockTimeout = TimeSpan.FromMinutes(_lockOptions.LockTimeoutMinutes);
            
            try
            {
                // Use SQL Server execution strategy to handle retries with transactions
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _dbContext.Database.BeginTransactionAsync();
                    try
                    {
                        // First check if lock exists
                        var existingLock = await _dbContext.FileLocks
                            .Where(fl => fl.FilePath == filePath)
                            .FirstOrDefaultAsync();
                            
                        if (existingLock == null)
                        {
                            _logger.LogInformation("No existing lock found for file: {FilePath}, creating new lock", filePath);
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
                            _logger.LogInformation("Acquired new lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                            return true;
                        }
                        else
                        {
                            _logger.LogInformation("Found existing lock for file: {FilePath}, owned by node: {OwnerNodeId}", filePath, existingLock.LockingNodeId);
                            // Lock exists - check if it's stale or belongs to this node
                            if (DateTime.UtcNow - existingLock.LastUpdatedAt > lockTimeout)
                            {
                                // Remove stale lock
                                _logger.LogWarning("Found stale lock for file: {FilePath} (was held by {NodeId} for {Duration} minutes)", 
                                    filePath, existingLock.LockingNodeId, 
                                    (DateTime.UtcNow - existingLock.AcquiredAt).TotalMinutes.ToString("F1"));
                                
                                _dbContext.FileLocks.Remove(existingLock);
                                
                                // Create a new lock for this node
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
                                _logger.LogInformation("Acquired lock after clearing stale lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                                return true;
                            }
                            else if (existingLock.LockingNodeId == nodeId)
                            {
                                // Update existing lock's timestamp if it belongs to this node
                                existingLock.LastUpdatedAt = DateTime.UtcNow;
                                await _dbContext.SaveChangesAsync();
                                await transaction.CommitAsync();
                                _logger.LogInformation("Updated existing lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                                return true;
                            }
                            else
                            {
                                // Lock belongs to another node and is not stale
                                var lockingNode = await _dbContext.Nodes.FindAsync(existingLock.LockingNodeId);
                                string lockingNodeName = lockingNode?.Name ?? "unknown";
                                var lockDuration = DateTime.UtcNow - existingLock.AcquiredAt;
                                
                                await transaction.CommitAsync();
                                _logger.LogInformation("Lock acquisition failed for file {FilePath} - already locked by node {OtherNodeId} ({NodeName}) for {Duration:F1} minutes", 
                                    filePath, existingLock.LockingNodeId, lockingNodeName, lockDuration.TotalMinutes);
                                
                                return false;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to acquire lock for file: {FilePath} by node: {NodeId}: {Error}", 
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
                // Use SQL Server execution strategy to handle retries with transactions
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _dbContext.Database.BeginTransactionAsync();
                    try
                    {
                        // Get the lock
                        var fileLock = await _dbContext.FileLocks
                            .Where(fl => fl.FilePath == filePath)
                            .FirstOrDefaultAsync();
        
                        if (fileLock == null)
                        {
                            // Lock doesn't exist, nothing to release
                            _logger.LogInformation("No lock found to release for file: {FilePath} by node: {NodeId}", 
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
                                _logger.LogWarning("Node {NodeId} is releasing a stale lock owned by {OwnerNodeId} for file {FilePath}", 
                                    nodeId, fileLock.LockingNodeId, filePath);
                            }
                            
                            _dbContext.FileLocks.Remove(fileLock);
                            await _dbContext.SaveChangesAsync();
                            await transaction.CommitAsync();
                            
                            _logger.LogInformation("Released lock for file: {FilePath} by node: {NodeId} (held for {Duration} seconds)", 
                                filePath, nodeId, (DateTime.UtcNow - fileLock.AcquiredAt).TotalSeconds.ToString("F1"));
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning("Node {NodeId} attempted to release lock owned by {OwnerNodeId} for file {FilePath}", 
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
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing lock for file {FilePath} by node {NodeId}: {Message}", 
                    filePath, nodeId, ex.Message);
                return false;
            }
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            try
            {
                var activeLocks = await _dbContext.FileLocks.ToListAsync();
                
                // Add diagnostic info
                foreach (var fileLock in activeLocks)
                {
                    // Calculate lock duration
                    var duration = DateTime.UtcNow - fileLock.AcquiredAt;
                    _logger.LogDebug("Found active lock: File={FilePath}, Node={NodeId}, Duration={Duration:F1} minutes", 
                        fileLock.FilePath, fileLock.LockingNodeId, duration.TotalMinutes);
                    
                    // Check if the locking node still exists
                    var node = await _dbContext.Nodes.FindAsync(fileLock.LockingNodeId);
                    if (node == null)
                    {
                        _logger.LogWarning("Lock owned by non-existent node: {NodeId} for file {FilePath}", 
                            fileLock.LockingNodeId, fileLock.FilePath);
                    }
                }
                
                return activeLocks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active locks: {Message}", ex.Message);
                
                // Try a more robust approach with direct SQL if Entity Framework approach failed
                try
                {
                    _logger.LogInformation("Attempting to retrieve locks using direct SQL");
                    var locks = new List<FileLock>();
                    
                    // Using a raw SQL query to get locks
                    var lockEntities = await _dbContext.FileLocks
                        .FromSqlRaw("SELECT * FROM FileLocks")
                        .AsNoTracking()
                        .ToListAsync();
                    
                    _logger.LogInformation("Successfully retrieved {Count} locks with direct SQL", lockEntities.Count);
                    return lockEntities;
                }
                catch (Exception sqlEx)
                {
                    _logger.LogError(sqlEx, "Failed to retrieve locks with direct SQL: {Message}", sqlEx.Message);
                    return new List<FileLock>();
                }
            }
        }

        public async Task<bool> ResetAllFileLocksAsync()
        {
            try
            {
                _logger.LogInformation("Starting direct deletion of all file locks");
                
                // Get current locks without locking
                var locks = await _dbContext.FileLocks.AsNoTracking().ToListAsync();
                
                if (!locks.Any())
                {
                    _logger.LogInformation("No locks found to reset");
                    return true;
                }
                
                _logger.LogInformation("Found {Count} locks to reset", locks.Count);
                
                // Log details of each lock being removed
                foreach (var fileLock in locks)
                {
                    var duration = (DateTime.UtcNow - fileLock.AcquiredAt).TotalMinutes;
                    _logger.LogInformation("Removing lock: File={FilePath}, Node={NodeId}, Duration={Duration:F1} minutes", 
                        fileLock.FilePath, fileLock.LockingNodeId, duration);
                }
                
                // Direct deletion using a SQL command to bypass locking issues
                int rowsAffected = await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM FileLocks");
                
                _logger.LogInformation("Successfully deleted {Count} locks", rowsAffected);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while directly deleting file locks: {Message}", ex.Message);
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
                _logger.LogError(ex, "Error retrieving task folder progress for task {TaskId}: {Message}", taskId, ex.Message);
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
                
                _logger.LogInformation("Created folder progress: TaskId={TaskId}, Folder={FolderName}", 
                    folderProgress.TaskId, folderProgress.FolderName);
                
                return folderProgress;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task folder progress: {Message}", ex.Message);
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
                    _logger.LogWarning("Task folder progress not found: {Id}", id);
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
                
                _logger.LogInformation("Updated folder progress: Id={Id}, Status={Status}, Progress={Progress:P0}", 
                    id, status, progress);
                
                return folderProgress;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task folder progress {Id}: {Message}", id, ex.Message);
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
                    
                    _logger.LogInformation("Deleted {Count} folder progress records for task {TaskId}", 
                        folderProgressList.Count, taskId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting task folder progress for task {TaskId}: {Message}", taskId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Checks if a volume compression task should be marked as completed
        /// This should be called periodically to detect when all nodes have finished processing
        /// </summary>
        public async Task<bool> CheckAndCompleteVolumeCompressionTaskAsync(int taskId)
        {
            try
            {
                var task = await _dbContext.Tasks.FindAsync(taskId);
                if (task == null || task.Type != TaskType.VolumeCompression || task.Status != BatchTaskStatus.Running)
                {
                    return false;
                }

                // Get all folder progress records for this task
                var folderProgress = await _dbContext.TaskFolderProgress
                    .Where(tf => tf.TaskId == taskId)
                    .ToListAsync();

                if (!folderProgress.Any())
                {
                    // No folders to process, mark as completed
                    task.Status = BatchTaskStatus.Completed;
                    task.CompletedAt = DateTime.UtcNow;
                    task.ResultMessage = "No folders found to process";
                    await _dbContext.SaveChangesAsync();
                    return true;
                }

                // Check if all folders are in a final state (completed or failed)
                var pendingFolders = folderProgress.Where(f => f.Status == TaskFolderStatus.Pending).Count();
                var inProgressFolders = folderProgress.Where(f => f.Status == TaskFolderStatus.InProgress).Count();
                var completedFolders = folderProgress.Where(f => f.Status == TaskFolderStatus.Completed).Count();
                var failedFolders = folderProgress.Where(f => f.Status == TaskFolderStatus.Failed).Count();
                var totalFolders = folderProgress.Count();

                // If there are still pending or in-progress folders, task is not complete
                if (pendingFolders > 0 || inProgressFolders > 0)
                {
                    return false;
                }

                // All folders are in final state - mark task as completed
                task.Status = BatchTaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;
                task.ResultMessage = $"Volume compression completed. {completedFolders} folders completed, {failedFolders} folders failed out of {totalFolders} total folders";
                
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Volume compression task {TaskId} marked as completed: {CompletedFolders} completed, {FailedFolders} failed", 
                    taskId, completedFolders, failedFolders);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking completion status for volume compression task {TaskId}", taskId);
                return false;
            }
        }
    }
} 