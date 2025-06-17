using FluentValidation;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using VCDevTool.API.Models;
using VCDevTool.Shared;

namespace VCDevTool.API.Validators
{
    public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
    {
        public CreateTaskRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("Task name is required")
                .MaximumLength(200)
                .WithMessage("Task name cannot exceed 200 characters")
                .Matches(@"^[a-zA-Z0-9\s\-_\.]+$")
                .WithMessage("Task name can only contain alphanumeric characters, spaces, hyphens, underscores, and periods");

            RuleFor(x => x.Type)
                .IsInEnum()
                .WithMessage("Invalid task type");

            RuleFor(x => x.Parameters)
                .MaximumLength(4000)
                .WithMessage("Parameters cannot exceed 4000 characters")
                .Must(BeValidJsonOrNull)
                .WithMessage("Parameters must be valid JSON format");
        }

        private bool BeValidJsonOrNull(string? json)
        {
            if (string.IsNullOrEmpty(json)) return true;

            try
            {
                JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
    {
        public UpdateTaskRequestValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(200)
                .WithMessage("Task name cannot exceed 200 characters")
                .Matches(@"^[a-zA-Z0-9\s\-_\.]+$")
                .WithMessage("Task name can only contain alphanumeric characters, spaces, hyphens, underscores, and periods")
                .When(x => !string.IsNullOrEmpty(x.Name));

            RuleFor(x => x.Status)
                .IsInEnum()
                .WithMessage("Invalid task status")
                .When(x => x.Status.HasValue);

            RuleFor(x => x.AssignedNodeId)
                .MaximumLength(50)
                .WithMessage("Assigned node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Assigned node ID can only contain alphanumeric characters, hyphens, and underscores")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeId));

            RuleFor(x => x.Parameters)
                .MaximumLength(4000)
                .WithMessage("Parameters cannot exceed 4000 characters")
                .Must(BeValidJsonOrNull)
                .WithMessage("Parameters must be valid JSON format")
                .When(x => !string.IsNullOrEmpty(x.Parameters));

            RuleFor(x => x.ResultMessage)
                .MaximumLength(2000)
                .WithMessage("Result message cannot exceed 2000 characters")
                .When(x => !string.IsNullOrEmpty(x.ResultMessage));
        }

        private bool BeValidJsonOrNull(string? json)
        {
            if (string.IsNullOrEmpty(json)) return true;

            try
            {
                JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class RegisterNodeRequestValidator : AbstractValidator<RegisterNodeRequest>
    {
        public RegisterNodeRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage("Node ID is required")
                .MaximumLength(50)
                .WithMessage("Node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Node ID can only contain alphanumeric characters, hyphens, and underscores");

            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("Node name is required")
                .MaximumLength(100)
                .WithMessage("Node name cannot exceed 100 characters")
                .Matches(@"^[a-zA-Z0-9\s\-_\.]+$")
                .WithMessage("Node name can only contain alphanumeric characters, spaces, hyphens, underscores, and periods");

            RuleFor(x => x.IpAddress)
                .NotEmpty()
                .WithMessage("IP address is required")
                .MaximumLength(100) // Increased to accommodate node identifiers
                .WithMessage("IP address cannot exceed 100 characters")
                .Must(BeValidIpAddressOrNodeId)
                .WithMessage("Invalid IP address or node identifier format");

            RuleFor(x => x.HardwareFingerprint)
                .NotEmpty()
                .WithMessage("Hardware fingerprint is required")
                .MaximumLength(256)
                .WithMessage("Hardware fingerprint cannot exceed 256 characters");
        }

        private bool BeValidIpAddressOrNodeId(string ipAddress)
        {
            // Allow valid IP addresses
            if (IPAddress.TryParse(ipAddress, out _))
                return true;
            
            // Allow node identifiers in format: NODE-{machineName}-{hash/timestamp}
            // This accommodates fallback identifiers when real IP detection fails
            if (ipAddress.StartsWith("NODE-") && ipAddress.Length <= 100)
            {
                // Ensure it contains only alphanumeric characters, hyphens, and underscores
                return Regex.IsMatch(ipAddress, @"^NODE-[a-zA-Z0-9\-_]+$");
            }
            
            return false;
        }
    }

    public class UpdateNodeRequestValidator : AbstractValidator<UpdateNodeRequest>
    {
        public UpdateNodeRequestValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(100)
                .WithMessage("Node name cannot exceed 100 characters")
                .Matches(@"^[a-zA-Z0-9\s\-_\.]+$")
                .WithMessage("Node name can only contain alphanumeric characters, spaces, hyphens, underscores, and periods")
                .When(x => !string.IsNullOrEmpty(x.Name));

            RuleFor(x => x.IpAddress)
                .MaximumLength(45)
                .WithMessage("IP address cannot exceed 45 characters")
                .Must(BeValidIpAddress)
                .WithMessage("Invalid IP address format")
                .When(x => !string.IsNullOrEmpty(x.IpAddress));

            RuleFor(x => x.HardwareFingerprint)
                .MaximumLength(256)
                .WithMessage("Hardware fingerprint cannot exceed 256 characters")
                .When(x => !string.IsNullOrEmpty(x.HardwareFingerprint));
        }

        private bool BeValidIpAddress(string ipAddress)
        {
            return IPAddress.TryParse(ipAddress, out _);
        }
    }

    public class CreateFileLockRequestValidator : AbstractValidator<CreateFileLockRequest>
    {
        public CreateFileLockRequestValidator()
        {
            RuleFor(x => x.FilePath)
                .NotEmpty()
                .WithMessage("File path is required")
                .MaximumLength(500)
                .WithMessage("File path cannot exceed 500 characters")
                .Must(BeValidFilePath)
                .WithMessage("Invalid file path format");

            RuleFor(x => x.LockingNodeId)
                .NotEmpty()
                .WithMessage("Locking node ID is required")
                .MaximumLength(50)
                .WithMessage("Locking node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Locking node ID can only contain alphanumeric characters, hyphens, and underscores");
        }

        private bool BeValidFilePath(string filePath)
        {
            // Basic validation for file path - could be expanded based on requirements
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            
            var invalidChars = Path.GetInvalidPathChars();
            return !filePath.Any(c => invalidChars.Contains(c)) && filePath.Length > 0;
        }
    }

    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.NodeId)
                .NotEmpty()
                .WithMessage("Node ID is required")
                .MaximumLength(50)
                .WithMessage("Node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Node ID can only contain alphanumeric characters, hyphens, and underscores");

            RuleFor(x => x.ApiKey)
                .MinimumLength(10)
                .WithMessage("API key must be at least 10 characters long")
                .When(x => !string.IsNullOrEmpty(x.ApiKey));

            RuleFor(x => x.HardwareFingerprint)
                .NotEmpty()
                .WithMessage("Hardware fingerprint is required")
                .MaximumLength(256)
                .WithMessage("Hardware fingerprint cannot exceed 256 characters");
        }
    }

    public class CreateTaskFolderProgressRequestValidator : AbstractValidator<CreateTaskFolderProgressRequest>
    {
        public CreateTaskFolderProgressRequestValidator()
        {
            RuleFor(x => x.TaskId)
                .GreaterThan(0)
                .WithMessage("Task ID must be greater than 0");

            RuleFor(x => x.FolderPath)
                .NotEmpty()
                .WithMessage("Folder path is required")
                .MaximumLength(500)
                .WithMessage("Folder path cannot exceed 500 characters")
                .Must(BeValidFilePath)
                .WithMessage("Invalid folder path format");

            RuleFor(x => x.FolderName)
                .NotEmpty()
                .WithMessage("Folder name is required")
                .MaximumLength(255)
                .WithMessage("Folder name cannot exceed 255 characters")
                .Matches(@"^[a-zA-Z0-9\s\-_\.]+$")
                .WithMessage("Folder name can only contain alphanumeric characters, spaces, hyphens, underscores, and periods");

            RuleFor(x => x.AssignedNodeId)
                .MaximumLength(50)
                .WithMessage("Assigned node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Assigned node ID can only contain alphanumeric characters, hyphens, and underscores")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeId));

            RuleFor(x => x.AssignedNodeName)
                .MaximumLength(100)
                .WithMessage("Assigned node name cannot exceed 100 characters")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeName));
        }

        private bool BeValidFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            
            var invalidChars = Path.GetInvalidPathChars();
            return !filePath.Any(c => invalidChars.Contains(c)) && filePath.Length > 0;
        }
    }

    public class UpdateTaskFolderProgressRequestValidator : AbstractValidator<UpdateTaskFolderProgressRequest>
    {
        public UpdateTaskFolderProgressRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum()
                .WithMessage("Invalid task folder status")
                .When(x => x.Status.HasValue);

            RuleFor(x => x.AssignedNodeId)
                .MaximumLength(50)
                .WithMessage("Assigned node ID cannot exceed 50 characters")
                .Matches(@"^[a-zA-Z0-9\-_]+$")
                .WithMessage("Assigned node ID can only contain alphanumeric characters, hyphens, and underscores")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeId));

            RuleFor(x => x.AssignedNodeName)
                .MaximumLength(100)
                .WithMessage("Assigned node name cannot exceed 100 characters")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeName));

            RuleFor(x => x.ErrorMessage)
                .MaximumLength(2000)
                .WithMessage("Error message cannot exceed 2000 characters")
                .When(x => !string.IsNullOrEmpty(x.ErrorMessage));

            RuleFor(x => x.Progress)
                .InclusiveBetween(0.0, 100.0)
                .WithMessage("Progress must be between 0.0 and 100.0")
                .When(x => x.Progress.HasValue);

            RuleFor(x => x.OutputPath)
                .MaximumLength(500)
                .WithMessage("Output path cannot exceed 500 characters")
                .Must(BeValidFilePath)
                .WithMessage("Invalid output path format")
                .When(x => !string.IsNullOrEmpty(x.OutputPath));
        }

        private bool BeValidFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            
            var invalidChars = Path.GetInvalidPathChars();
            return !filePath.Any(c => invalidChars.Contains(c)) && filePath.Length > 0;
        }
    }

    public class TaskQueryParametersValidator : AbstractValidator<TaskQueryParameters>
    {
        public TaskQueryParametersValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum()
                .WithMessage("Invalid task status")
                .When(x => x.Status.HasValue);

            RuleFor(x => x.Type)
                .IsInEnum()
                .WithMessage("Invalid task type")
                .When(x => x.Type.HasValue);

            RuleFor(x => x.AssignedNodeId)
                .MaximumLength(50)
                .WithMessage("Assigned node ID cannot exceed 50 characters")
                .When(x => !string.IsNullOrEmpty(x.AssignedNodeId));

            RuleFor(x => x.CreatedAfter)
                .LessThan(DateTime.UtcNow.AddDays(1))
                .WithMessage("Created after date cannot be in the future")
                .When(x => x.CreatedAfter.HasValue);

            RuleFor(x => x.CreatedBefore)
                .LessThan(DateTime.UtcNow.AddDays(1))
                .WithMessage("Created before date cannot be in the future")
                .When(x => x.CreatedBefore.HasValue);

            RuleFor(x => x.Page)
                .GreaterThan(0)
                .WithMessage("Page must be greater than 0");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 1000)
                .WithMessage("Page size must be between 1 and 1000");
        }
    }

    public class NodeQueryParametersValidator : AbstractValidator<NodeQueryParameters>
    {
        public NodeQueryParametersValidator()
        {
            RuleFor(x => x.HeartbeatAfter)
                .LessThan(DateTime.UtcNow.AddDays(1))
                .WithMessage("Heartbeat after date cannot be in the future")
                .When(x => x.HeartbeatAfter.HasValue);

            RuleFor(x => x.NameFilter)
                .MaximumLength(100)
                .WithMessage("Name filter cannot exceed 100 characters")
                .When(x => !string.IsNullOrEmpty(x.NameFilter));

            RuleFor(x => x.Page)
                .GreaterThan(0)
                .WithMessage("Page must be greater than 0");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 1000)
                .WithMessage("Page size must be between 1 and 1000");
        }
    }
} 