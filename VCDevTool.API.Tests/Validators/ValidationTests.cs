using FluentValidation.TestHelper;
using VCDevTool.API.Models;
using VCDevTool.API.Validators;
using VCDevTool.Shared;
using Xunit;

namespace VCDevTool.API.Tests.Validators
{
    /// <summary>
    /// Comprehensive tests for validation rules and security checks
    /// </summary>
    public class ValidationTests
    {
        [Fact]
        public void ValidationHelpers_IsSafeString_DetectsSqlInjection()
        {
            // Arrange
            var maliciousInputs = new[]
            {
                "'; DROP TABLE Users; --",
                "1' OR '1'='1",
                "UNION SELECT * FROM passwords",
                "INSERT INTO users VALUES ('admin', 'password')"
            };

            // Act & Assert
            foreach (var input in maliciousInputs)
            {
                Assert.False(ValidationHelpers.IsSafeString(input), $"Failed to detect SQL injection: {input}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsSafeString_DetectsXss()
        {
            // Arrange
            var maliciousInputs = new[]
            {
                "<script>alert('xss')</script>",
                "javascript:alert('xss')",
                "<img onerror='alert(1)' src='x'>",
                "vbscript:msgbox('xss')"
            };

            // Act & Assert
            foreach (var input in maliciousInputs)
            {
                Assert.False(ValidationHelpers.IsSafeString(input), $"Failed to detect XSS: {input}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsSafeString_DetectsPathTraversal()
        {
            // Arrange
            var maliciousInputs = new[]
            {
                "../../../etc/passwd",
                "..\\..\\windows\\system32\\config\\sam",
                "%2E%2E%2Fetc%2Fpasswd",
                "..%2Fetc%2Fpasswd"
            };

            // Act & Assert
            foreach (var input in maliciousInputs)
            {
                Assert.False(ValidationHelpers.IsSafeString(input), $"Failed to detect path traversal: {input}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsSafeString_AllowsSafeInputs()
        {
            // Arrange
            var safeInputs = new[]
            {
                "Normal task name",
                "Task-123_Test",
                "file.txt",
                "Some regular text with numbers 123"
            };

            // Act & Assert
            foreach (var input in safeInputs)
            {
                Assert.True(ValidationHelpers.IsSafeString(input), $"Incorrectly flagged safe input: {input}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsValidFilePath_RejectsInvalidPaths()
        {
            // Arrange - Focus on actual security threats rather than platform-specific paths
            var invalidPaths = new[]
            {
                "",
                null,
                "../../../etc/passwd",  // Path traversal attack
                "..\\..\\..\\windows\\system32\\config", // Path traversal attack  
                "con:",  // Windows reserved name
                "aux:",  // Windows reserved name
                "path|with|pipes" // Invalid characters
            };

            // Act & Assert
            foreach (var path in invalidPaths)
            {
                Assert.False(ValidationHelpers.IsValidFilePath(path), $"Failed to reject invalid path: {path}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsValidFilePath_AllowsValidPaths()
        {
            // Arrange - Test that legitimate paths are allowed
            var validPaths = new[]
            {
                "C:\\temp\\file.txt",
                "D:\\Projects\\VCDevTool\\data.db",
                "/home/user/documents/file.txt",
                "\\\\server\\share\\file.txt"
            };

            // Act & Assert
            foreach (var path in validPaths)
            {
                Assert.True(ValidationHelpers.IsValidFilePath(path), $"Incorrectly rejected valid path: {path}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsValidIpAddress_ValidatesCorrectly()
        {
            // Arrange
            var validIps = new[] { "192.168.1.1", "127.0.0.1", "::1", "2001:db8::1" };
            var invalidIps = new[] { "", null, "999.999.999.999", "not.an.ip", "192.168.1" };

            // Act & Assert
            foreach (var ip in validIps)
            {
                Assert.True(ValidationHelpers.IsValidIpAddress(ip), $"Failed to validate valid IP: {ip}");
            }

            foreach (var ip in invalidIps)
            {
                Assert.False(ValidationHelpers.IsValidIpAddress(ip), $"Failed to reject invalid IP: {ip}");
            }
        }

        [Fact]
        public void ValidationHelpers_IsValidJsonParameters_ValidatesJsonSafety()
        {
            // Arrange
            var validJson = new[]
            {
                "{}",
                "{\"key\": \"value\"}",
                "{\"number\": 123, \"boolean\": true}",
                "[1, 2, 3]"
            };

            var invalidJson = new[]
            {
                "{\"key\": \"<script>alert('xss')</script>\"}",
                "{\"sql\": \"'; DROP TABLE Users; --\"}",
                "{\"path\": \"../../../etc/passwd\"}",
                "invalid json"
            };

            // Act & Assert
            foreach (var json in validJson)
            {
                Assert.True(ValidationHelpers.IsValidJsonParameters(json), $"Failed to validate safe JSON: {json}");
            }

            foreach (var json in invalidJson)
            {
                Assert.False(ValidationHelpers.IsValidJsonParameters(json), $"Failed to reject unsafe JSON: {json}");
            }
        }
    }

    public class EnhancedCreateTaskRequestValidatorTests
    {
        private readonly EnhancedCreateTaskRequestValidator _validator = new();

        [Fact]
        public void Should_Have_Error_When_Name_Is_Empty()
        {
            var model = new CreateTaskRequest { Name = "" };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public void Should_Have_Error_When_Name_Contains_Malicious_Content()
        {
            var model = new CreateTaskRequest { Name = "<script>alert('xss')</script>" };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public void Should_Have_Error_When_Type_Is_Unknown()
        {
            var model = new CreateTaskRequest 
            { 
                Name = "Valid Name",
                Type = TaskType.Unknown 
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Type);
        }

        [Fact]
        public void Should_Have_Error_When_Parameters_Contain_Malicious_Json()
        {
            var model = new CreateTaskRequest
            {
                Name = "Valid Name",
                Type = TaskType.FileProcessing,
                Parameters = "{\"script\": \"<script>alert('xss')</script>\"}"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Parameters);
        }

        [Fact]
        public void Should_Not_Have_Error_When_All_Fields_Are_Valid()
        {
            var model = new CreateTaskRequest
            {
                Name = "Valid Task Name",
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"C:\\\\valid\\\\path\", \"outputPath\": \"C:\\\\output\"}"
            };
            var result = _validator.TestValidate(model);
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Theory]
        [InlineData(TaskType.RealityCapture, "{\"inputPath\": \"../../../etc/passwd\"}", false)]
        [InlineData(TaskType.VolumeCompression, "{\"compressionLevel\": 15}", false)]
        [InlineData(TaskType.FileProcessing, "{\"maxFileSize\": -1}", false)]
        [InlineData(TaskType.RealityCapture, "{\"inputPath\": \"C:\\\\valid\\\\path\"}", true)]
        [InlineData(TaskType.VolumeCompression, "{\"compressionLevel\": 5}", true)]
        [InlineData(TaskType.FileProcessing, "{\"maxFileSize\": 1048576}", true)]
        public void Should_Validate_Task_Type_Specific_Parameters(TaskType taskType, string parameters, bool shouldBeValid)
        {
            var model = new CreateTaskRequest
            {
                Name = "Test Task",
                Type = taskType,
                Parameters = parameters
            };
            var result = _validator.TestValidate(model);

            if (shouldBeValid)
            {
                result.ShouldNotHaveValidationErrorFor(x => x);
            }
            else
            {
                result.ShouldHaveValidationErrorFor(x => x);
            }
        }
    }

    public class EnhancedRegisterNodeRequestValidatorTests
    {
        private readonly EnhancedRegisterNodeRequestValidator _validator = new();

        [Fact]
        public void Should_Have_Error_When_Id_Contains_Invalid_Characters()
        {
            var model = new RegisterNodeRequest 
            { 
                Id = "node@123!",
                Name = "Valid Name",
                IpAddress = "192.168.1.1",
                HardwareFingerprint = "valid-fingerprint"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Id);
        }

        [Fact]
        public void Should_Have_Error_When_Name_Contains_Malicious_Content()
        {
            var model = new RegisterNodeRequest
            {
                Id = "node-123",
                Name = "<script>alert('xss')</script>",
                IpAddress = "192.168.1.1",
                HardwareFingerprint = "valid-fingerprint"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Theory]
        [InlineData("192.168.1.1", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("::1", true)]
        [InlineData("2001:db8::1", true)]
        [InlineData("999.999.999.999", false)]
        [InlineData("not.an.ip", false)]
        [InlineData("", false)]
        public void Should_Validate_Ip_Address_Format(string ipAddress, bool shouldBeValid)
        {
            var model = new RegisterNodeRequest
            {
                Id = "node-123",
                Name = "Valid Name",
                IpAddress = ipAddress,
                HardwareFingerprint = "valid-fingerprint"
            };
            var result = _validator.TestValidate(model);

            if (shouldBeValid)
            {
                result.ShouldNotHaveValidationErrorFor(x => x.IpAddress);
            }
            else
            {
                result.ShouldHaveValidationErrorFor(x => x.IpAddress);
            }
        }

        [Fact]
        public void Should_Not_Have_Error_When_All_Fields_Are_Valid()
        {
            var model = new RegisterNodeRequest
            {
                Id = "node-123",
                Name = "Test Node",
                IpAddress = "192.168.1.100",
                HardwareFingerprint = "12345678-abcd-efgh-ijkl-123456789012"
            };
            var result = _validator.TestValidate(model);
            result.ShouldNotHaveAnyValidationErrors();
        }
    }

    public class EnhancedCreateFileLockRequestValidatorTests
    {
        private readonly EnhancedCreateFileLockRequestValidator _validator = new();

        [Fact]
        public void Should_Have_Error_When_FilePath_Is_Empty()
        {
            var model = new CreateFileLockRequest
            {
                FilePath = "",
                LockingNodeId = "node-123"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.FilePath);
        }

        [Fact]
        public void Should_Have_Error_When_FilePath_Contains_Path_Traversal()
        {
            var model = new CreateFileLockRequest
            {
                FilePath = "../../../etc/passwd",
                LockingNodeId = "node-123"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.FilePath);
        }

        [Fact]
        public void Should_Have_Error_When_LockingNodeId_Is_Invalid()
        {
            var model = new CreateFileLockRequest
            {
                FilePath = "C:\\temp\\file.txt",
                LockingNodeId = "node@123!"
            };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.LockingNodeId);
        }

        [Fact]
        public void Should_Not_Have_Error_When_All_Fields_Are_Valid()
        {
            var model = new CreateFileLockRequest
            {
                FilePath = "C:\\temp\\file.txt",
                LockingNodeId = "node-123"
            };
            var result = _validator.TestValidate(model);
            result.ShouldNotHaveAnyValidationErrors();
        }
    }

    public class InputSanitizationServiceTests
    {
        [Fact]
        public void SanitizeString_RemovesControlCharacters()
        {
            // Arrange
            var input = "Hello\0World\x1F";
            
            // Act
            var result = InputSanitizationService.SanitizeString(input);
            
            // Assert - The method should filter out control characters before HTML encoding
            // After filtering, we should have "HelloWorld" which gets HTML encoded
            Assert.Equal("HelloWorld", result);
            
            // Verify that result doesn't contain raw control characters
            Assert.DoesNotContain("\0", result);
            Assert.DoesNotContain("\x1F", result);
        }

        [Fact]
        public void SanitizeString_TruncatesLongInput()
        {
            // Arrange
            var input = new string('A', 2000);
            var maxLength = 100;
            
            // Act
            var result = InputSanitizationService.SanitizeString(input, maxLength);
            
            // Assert
            Assert.True(result.Length <= maxLength);
        }

        [Fact]
        public void SanitizeFilePath_RemovesPathTraversal()
        {
            // Arrange
            var input = "../../etc/passwd";
            
            // Act
            var result = InputSanitizationService.SanitizeFilePath(input);
            
            // Assert
            Assert.DoesNotContain("..", result);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null!, "")]
        [InlineData("normal-text", "normal-text")]
        public void SanitizeString_HandlesEdgeCases(string? input, string expected)
        {
            // Act
            var result = InputSanitizationService.SanitizeString(input);
            
            // Assert
            Assert.Equal(expected, result);
        }
    }
} 