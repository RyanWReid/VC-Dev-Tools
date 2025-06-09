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
        Task<BatchTask> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null);
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
        Task<bool> ResetAllFileLocksAsync();

        // Task Folder Progress Management
        Task<List<TaskFolderProgress>> GetTaskFolderProgressAsync(int taskId);
        Task<TaskFolderProgress> CreateTaskFolderProgressAsync(TaskFolderProgress folderProgress);
        Task<TaskFolderProgress> UpdateTaskFolderProgressAsync(int id, TaskFolderStatus status, string? nodeId = null, string? nodeName = null, double progress = 0.0, string? errorMessage = null, string? outputPath = null);
        Task<bool> DeleteTaskFolderProgressAsync(int taskId);
        
        // Task Completion Management
        Task<bool> CheckAndCompleteVolumeCompressionTaskAsync(int taskId);
    }
} 