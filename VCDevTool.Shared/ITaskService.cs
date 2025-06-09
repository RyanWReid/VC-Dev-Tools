using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VCDevTool.Shared
{
    public interface ITaskService
    {
        // Task Management
        Task<List<BatchTask>> GetAllTasksAsync();
        Task<BatchTask> GetTaskByIdAsync(int taskId);
        Task<BatchTask> CreateTaskAsync(BatchTask task);
        Task<BatchTask> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null);
        Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId);
        
        // Node Management
        Task<List<ComputerNode>> GetAvailableNodesAsync();
        Task<ComputerNode> RegisterNodeAsync(ComputerNode node);
        Task<bool> UpdateNodeHeartbeatAsync(string nodeId);
        Task<List<ComputerNode>> GetAllNodesAsync();
        
        // File Locking
        Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId);
        Task<bool> ReleaseFileLockAsync(string filePath, string nodeId);
        Task<List<FileLock>> GetActiveLocksAsync();
    }
} 