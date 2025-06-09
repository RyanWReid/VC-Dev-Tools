using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCDevTool.Shared;

namespace VCDevTool.Client.Services
{
    public interface IApiClient : IDisposable
    {
        // Connection Management
        Task<bool> TestConnectionAsync();
        string GetBaseUrl();

        // Task Management
        Task<List<BatchTask>> GetTasksAsync();
        Task<BatchTask?> GetTaskByIdAsync(int taskId);
        Task<BatchTask?> CreateTaskAsync(BatchTask task);
        Task<BatchTask?> UpdateTaskStatusAsync(int taskId, BatchTaskStatus status, string? resultMessage = null, byte[]? rowVersion = null);
        Task<bool> AssignTaskToNodeAsync(int taskId, string nodeId);

        // Node Management
        Task<List<ComputerNode>> GetAvailableNodesAsync();
        Task<List<ComputerNode>> GetAllNodesAsync();
        Task<ComputerNode> RegisterNodeAsync(ComputerNode node);
        Task<bool> SendHeartbeatAsync(string nodeId);
        Task<bool> UpdateNodeAsync(ComputerNode node);

        // File Locking
        Task<bool> TryAcquireFileLockAsync(string filePath, string nodeId);
        Task<bool> ReleaseFileLockAsync(string filePath, string nodeId);
        Task<List<FileLock>> GetActiveLocksAsync();
        Task<bool> ResetFileLocksAsync();

        // Task Folder Progress Management
        Task<List<TaskFolderProgress>> GetTaskFoldersAsync(int taskId);
        Task<TaskFolderProgress?> CreateTaskFolderAsync(int taskId, TaskFolderProgress folderProgress);
        Task<TaskFolderProgress?> UpdateTaskFolderStatusAsync(int folderId, TaskFolderStatus status, string? nodeId = null, string? nodeName = null, double progress = 0.0, string? errorMessage = null, string? outputPath = null);
        Task<bool> DeleteTaskFoldersAsync(int taskId);

        // Debug Management
        Task<bool> SendDebugMessageAsync(string source, string message, string? nodeId = null);

        // Health and Status
        Task<ConnectionHealthStatus> GetConnectionHealthAsync();
    }

    public class ConnectionHealthStatus
    {
        public bool IsHealthy { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string? LastError { get; set; }
        public DateTime LastChecked { get; set; }
        public bool CircuitBreakerOpen { get; set; }
        public int FailureCount { get; set; }
        public string ServerVersion { get; set; } = string.Empty;
    }
} 