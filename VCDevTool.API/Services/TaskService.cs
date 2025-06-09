using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCDevTool.API.Data;
using VCDevTool.Shared;

namespace VCDevTool.API.Services
{
    public class TaskService : ITaskService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<TaskService> _logger;

        public TaskService(AppDbContext dbContext, ILogger<TaskService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
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

        public async Task<BatchTask> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null)
        {
            var task = await _dbContext.Tasks.FindAsync(taskId);
            if (task == null)
            {
                _logger.LogWarning("Task not found for status update: {TaskId}", taskId);
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

            task.AssignedNodeId = nodeId;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Assigned task {TaskId} to node {NodeId}", taskId, nodeId);
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

            ComputerNode existingNode = null;
            
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
            }
            else
            {
                // Create new node
                node.IsAvailable = true;
                node.LastHeartbeat = DateTime.UtcNow;
                _dbContext.Nodes.Add(node);
                existingNode = node;
            }
            
            try
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Registered node: {NodeId} - {NodeName}", existingNode.Id, existingNode.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering node {NodeId} with IP {IpAddress}", node.Id, node.IpAddress);
                
                // If we fail due to a unique constraint, try a more thorough cleanup approach
                if (ex.InnerException?.Message.Contains("duplicate key") == true)
                {
                    // Try cleaning up the database first
                    _logger.LogWarning("Attempting to clean up stale nodes and retry registration");
                    await CleanupStaleNodesAsync();
                    
                    // Try registering again with a simple approach
                    var simpleNode = new ComputerNode
                    {
                        Id = node.Id,
                        Name = node.Name,
                        IpAddress = node.IpAddress,
                        HardwareFingerprint = node.HardwareFingerprint,
                        IsAvailable = true,
                        LastHeartbeat = DateTime.UtcNow
                    };
                    
                    _dbContext.Nodes.Add(simpleNode);
                    await _dbContext.SaveChangesAsync();
                    existingNode = simpleNode;
                }
                else
                {
                    // Rethrow any other errors
                    throw;
                }
            }
            
            return existingNode;
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
            // Check if lock exists
            var existingLock = await _dbContext.FileLocks
                .FirstOrDefaultAsync(l => l.FilePath == filePath);

            if (existingLock != null)
            {
                // Check if lock is stale (more than 10 minutes old without updates)
                var lockTimeout = TimeSpan.FromMinutes(10);
                if (DateTime.UtcNow - existingLock.LastUpdatedAt > lockTimeout)
                {
                    // Release stale lock
                    _dbContext.FileLocks.Remove(existingLock);
                    _logger.LogWarning("Released stale lock for file: {FilePath}", filePath);
                }
                else if (existingLock.LockingNodeId != nodeId)
                {
                    // Lock is still valid and owned by another node
                    return false;
                }
                else
                {
                    // Update existing lock's timestamp
                    existingLock.LastUpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    return true;
                }
            }

            // Create new lock
            var fileLock = new FileLock
            {
                FilePath = filePath,
                LockingNodeId = nodeId,
                AcquiredAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            };

            try
            {
                _dbContext.FileLocks.Add(fileLock);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Acquired lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                return true;
            }
            catch (DbUpdateException)
            {
                // Another node might have acquired the lock simultaneously
                _logger.LogWarning("Failed to acquire lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
                return false;
            }
        }

        public async Task<bool> ReleaseFileLockAsync(string filePath, string nodeId)
        {
            var fileLock = await _dbContext.FileLocks
                .FirstOrDefaultAsync(l => l.FilePath == filePath);

            if (fileLock == null)
            {
                // Lock doesn't exist
                return true;
            }

            // Only allow the lock owner to release it
            if (fileLock.LockingNodeId != nodeId)
            {
                _logger.LogWarning("Node {NodeId} attempted to release lock owned by {OwnerNodeId}", nodeId, fileLock.LockingNodeId);
                return false;
            }

            _dbContext.FileLocks.Remove(fileLock);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Released lock for file: {FilePath} by node: {NodeId}", filePath, nodeId);
            return true;
        }

        public async Task<List<FileLock>> GetActiveLocksAsync()
        {
            return await _dbContext.FileLocks.ToListAsync();
        }
    }
} 