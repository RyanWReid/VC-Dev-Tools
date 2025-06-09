using FluentValidation;
using VCDevTool.API.Models;
using VCDevTool.API.Validators;
using VCDevTool.Shared;
using Xunit;

namespace VCDevTool.API.Tests.Services
{
    public class ValidationTests
    {
        [Fact]
        public void CreateTaskRequestValidator_ValidInput_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = new CreateTaskRequestValidator();
            var request = new CreateTaskRequest
            {
                Name = "Valid Task Name",
                Type = TaskType.FileProcessing,
                Parameters = "{\"key\": \"value\"}"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void CreateTaskRequestValidator_EmptyName_ShouldHaveValidationError()
        {
            // Arrange
            var validator = new CreateTaskRequestValidator();
            var request = new CreateTaskRequest
            {
                Name = "",
                Type = TaskType.FileProcessing
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Name));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Task name is required");
        }

        [Fact]
        public void CreateTaskRequestValidator_InvalidJsonParameters_ShouldHaveValidationError()
        {
            // Arrange
            var validator = new CreateTaskRequestValidator();
            var request = new CreateTaskRequest
            {
                Name = "Test Task",
                Type = TaskType.FileProcessing,
                Parameters = "invalid json {"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Parameters));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Parameters must be valid JSON format");
        }

        [Fact]
        public void RegisterNodeRequestValidator_ValidInput_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = new RegisterNodeRequestValidator();
            var request = new RegisterNodeRequest
            {
                Id = "node-001",
                Name = "Test Node",
                IpAddress = "192.168.1.100",
                HardwareFingerprint = "ABC123DEF456"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void RegisterNodeRequestValidator_InvalidIpAddress_ShouldHaveValidationError()
        {
            // Arrange
            var validator = new RegisterNodeRequestValidator();
            var request = new RegisterNodeRequest
            {
                Id = "node-001",
                Name = "Test Node",
                IpAddress = "invalid.ip.address",
                HardwareFingerprint = "ABC123DEF456"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterNodeRequest.IpAddress));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Invalid IP address format");
        }

        [Fact]
        public void CreateFileLockRequestValidator_ValidInput_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = new CreateFileLockRequestValidator();
            var request = new CreateFileLockRequest
            {
                FilePath = "C:\\temp\\test.txt",
                LockingNodeId = "node-001"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void TaskQueryParametersValidator_ValidPagination_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = new TaskQueryParametersValidator();
            var request = new TaskQueryParameters
            {
                Status = BatchTaskStatus.Running,
                Type = TaskType.FileProcessing,
                Page = 1,
                PageSize = 50
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void TaskQueryParametersValidator_InvalidPageSize_ShouldHaveValidationError()
        {
            // Arrange
            var validator = new TaskQueryParametersValidator();
            var request = new TaskQueryParameters
            {
                Page = 1,
                PageSize = 1001 // Exceeds maximum
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(TaskQueryParameters.PageSize));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Page size must be between 1 and 1000");
        }

        [Fact]
        public void UpdateTaskFolderProgressRequestValidator_ValidProgress_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = new UpdateTaskFolderProgressRequestValidator();
            var request = new UpdateTaskFolderProgressRequest
            {
                Status = TaskFolderStatus.InProgress,
                Progress = 50.5,
                AssignedNodeId = "node-001"
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void UpdateTaskFolderProgressRequestValidator_InvalidProgress_ShouldHaveValidationError()
        {
            // Arrange
            var validator = new UpdateTaskFolderProgressRequestValidator();
            var request = new UpdateTaskFolderProgressRequest
            {
                Progress = 150.0 // Exceeds maximum
            };

            // Act
            var result = validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateTaskFolderProgressRequest.Progress));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Progress must be between 0.0 and 100.0");
        }
    }
} 