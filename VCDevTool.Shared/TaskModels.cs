using System;
using System.Collections.Generic;

namespace VCDevTool.Shared
{
    public enum BatchTaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum TaskType
    {
        Unknown = 0,
        HelloWorld = 1,
        TestMessage = 2,
        RenderThumbnails = 3,
        FileProcessing = 4,
        RealityCapture = 5,
        PackageTask = 6,
        VolumeCompression = 7
        // Add more task types as needed
    }

    public enum TaskFolderStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    public class ComputerNode
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public string HardwareFingerprint { get; set; } = string.Empty;
        
        // Active Directory Properties
        public string ActiveDirectoryName { get; set; } = string.Empty;
        public string DomainController { get; set; } = string.Empty;
        public string OrganizationalUnit { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string DnsHostName { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public DateTime LastAdLogon { get; set; }
        public bool IsAdEnabled { get; set; } = true;
        public string AdGroups { get; set; } = "[]"; // JSON array of AD group memberships
        public DateTime LastAdSync { get; set; }
        public string ServicePrincipalName { get; set; } = string.Empty;
    }

    public class BatchTask
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public TaskType Type { get; set; }
        public BatchTaskStatus Status { get; set; }
        public string? AssignedNodeId { get; set; }
        public string AssignedNodeIds { get; set; } = "[]"; // JSON array of assigned node IDs
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Parameters { get; set; } // JSON serialized parameters
        public string? ResultMessage { get; set; }
        public byte[]? RowVersion { get; set; } // Added for optimistic concurrency control
    }

    public class FileLock
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string LockingNodeId { get; set; } = string.Empty;
        public DateTime AcquiredAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }

    public class TaskFolderProgress
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public TaskFolderStatus Status { get; set; }
        public string? AssignedNodeId { get; set; }
        public string? AssignedNodeName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public double Progress { get; set; } = 0.0;
        public string? OutputPath { get; set; }
    }
} 