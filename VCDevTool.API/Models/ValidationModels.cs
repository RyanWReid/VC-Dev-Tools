using System.ComponentModel.DataAnnotations;
using VCDevTool.Shared;

namespace VCDevTool.API.Models
{
    // DTOs for API input validation
    public class CreateTaskRequest
    {
        public string Name { get; set; } = string.Empty;
        public TaskType Type { get; set; }
        public string? Parameters { get; set; }
    }

    public class UpdateTaskRequest
    {
        public string? Name { get; set; }
        public BatchTaskStatus? Status { get; set; }
        public string? AssignedNodeId { get; set; }
        public string? Parameters { get; set; }
        public string? ResultMessage { get; set; }
    }

    public class RegisterNodeRequest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HardwareFingerprint { get; set; } = string.Empty;
    }

    public class UpdateNodeRequest
    {
        public string? Name { get; set; }
        public string? IpAddress { get; set; }
        public bool? IsAvailable { get; set; }
        public string? HardwareFingerprint { get; set; }
    }

    public class CreateFileLockRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string LockingNodeId { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string NodeId { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string HardwareFingerprint { get; set; } = string.Empty;
    }

    public class CreateTaskFolderProgressRequest
    {
        public int TaskId { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string? AssignedNodeId { get; set; }
        public string? AssignedNodeName { get; set; }
    }

    public class UpdateTaskFolderProgressRequest
    {
        public TaskFolderStatus? Status { get; set; }
        public string? AssignedNodeId { get; set; }
        public string? AssignedNodeName { get; set; }
        public string? ErrorMessage { get; set; }
        public double? Progress { get; set; }
        public string? OutputPath { get; set; }
    }

    // Query parameter models
    public class TaskQueryParameters
    {
        public BatchTaskStatus? Status { get; set; }
        public TaskType? Type { get; set; }
        public string? AssignedNodeId { get; set; }
        public DateTime? CreatedAfter { get; set; }
        public DateTime? CreatedBefore { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class NodeQueryParameters
    {
        public bool? IsAvailable { get; set; }
        public DateTime? HeartbeatAfter { get; set; }
        public string? NameFilter { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
} 