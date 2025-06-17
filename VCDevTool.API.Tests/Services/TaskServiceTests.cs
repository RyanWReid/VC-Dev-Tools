using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VCDevTool.API.Data;
using VCDevTool.API.Services;
using VCDevTool.API.Tests.Data;
using VCDevTool.API.Tests.Services;
using VCDevTool.Shared;
using Xunit;

namespace VCDevTool.API.Tests.Services
{
    public class TaskServiceTests : IDisposable
    {
        private readonly List<SqliteConnection> _connections = new();
        private readonly List<AppDbContext> _contexts = new();

        private AppDbContext CreateSqliteDbContext()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            _connections.Add(connection);

            var options = new DbContextOptionsBuilder<AppDbContext>();
            TestDbOptimizations.ConfigureTestOptions(options, connection);

            var context = new AppDbContext(options.Options);
            TestDbOptimizations.InitializeTestDatabase(context);
            _contexts.Add(context);
            
            return context;
        }

        private async Task<AppDbContext> CreateContextWithTestDataAsync()
        {
            var context = CreateSqliteDbContext();
            await TestDataSeeder.SeedBasicTestDataAsync(context);
            return context;
        }

        [Fact]
        public async Task CreateTaskAsync_ValidTask_ShouldCreateSuccessfully()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            var task = new BatchTask
            {
                Name = "Test Task",
                Type = TaskType.FileProcessing,
                Parameters = "{\"inputPath\": \"C:\\\\test\\\\input\"}"
            };

            // Act
            var result = await service.CreateTaskAsync(task);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Task", result.Name);
            Assert.Equal(TaskType.FileProcessing, result.Type);
            Assert.Equal(BatchTaskStatus.Pending, result.Status);
            Assert.True(result.Id > 0);
        }

        [Fact]
        public async Task GetTaskByIdAsync_ExistingTask_ShouldReturnTask()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            // Get an existing task from test data
            var existingTask = context.Tasks.First();

            // Act
            var result = await service.GetTaskByIdAsync(existingTask.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(existingTask.Id, result.Id);
            Assert.Equal(existingTask.Name, result.Name);
        }

        [Fact]
        public async Task GetTaskByIdAsync_NonExistentTask_ShouldReturnEmptyTask()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            // Act
            var result = await service.GetTaskByIdAsync(999);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(-1, result.Id); // Service returns empty task with Id = -1
        }

        [Fact]
        public async Task GetAllTasksAsync_WithTasks_ShouldReturnAllTasks()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            // Act
            var result = await service.GetAllTasksAsync();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count >= 4); // Test data includes at least 4 tasks
        }

        [Fact]
        public async Task UpdateTaskAsync_ValidUpdate_ShouldUpdateSuccessfully()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            var existingTask = context.Tasks.First();
            existingTask.Status = BatchTaskStatus.Running;
            existingTask.ResultMessage = "Task started";

            // Act
            var result = await service.UpdateTaskAsync(existingTask);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(BatchTaskStatus.Running, result.Status);
            Assert.Equal("Task started", result.ResultMessage);
        }

        [Fact]
        public async Task AssignTaskToNodeAsync_ValidAssignment_ShouldSucceed()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            // Get an existing node and pending task from test data
            var node = context.Nodes.First(n => n.IsAvailable);
            var task = context.Tasks.First(t => t.Status == BatchTaskStatus.Pending);

            // Act
            var result = await service.AssignTaskToNodeAsync(task.Id, node.Id);

            // Assert
            Assert.True(result);
            
            // Verify assignment
            var updatedTask = await context.Tasks.FindAsync(task.Id);
            Assert.NotNull(updatedTask);
            Assert.Equal(node.Id, updatedTask.AssignedNodeId);
        }

        [Fact]
        public async Task StartTaskAsync_ValidTask_ShouldStartSuccessfully()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new SqliteCompatibleTaskService(context, mockLogger.Object);

            // Get a pending task with assigned node
            var node = context.Nodes.First(n => n.IsAvailable);
            var task = context.Tasks.First(t => t.Status == BatchTaskStatus.Pending);
            task.AssignedNodeId = node.Id;
            await context.SaveChangesAsync();

            // Act
            var result = await service.StartTaskAsync(task.Id, node.Id);

            // Assert
            Assert.True(result);
            
            // Verify task started
            var updatedTask = await context.Tasks.FindAsync(task.Id);
            Assert.NotNull(updatedTask);
            Assert.Equal(BatchTaskStatus.Running, updatedTask.Status);
            Assert.NotNull(updatedTask.StartedAt);
        }

        [Fact]
        public async Task GetAvailableNodesAsync_WithAvailableNodes_ShouldReturnOnlyAvailableNodes()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            var nodes = new[]
            {
                new ComputerNode { Id = "node-1", Name = "Available Node", IpAddress = "192.168.1.1", IsAvailable = true, LastHeartbeat = DateTime.UtcNow, HardwareFingerprint = "FP1" },
                new ComputerNode { Id = "node-2", Name = "Unavailable Node", IpAddress = "192.168.1.2", IsAvailable = false, LastHeartbeat = DateTime.UtcNow.AddMinutes(-5), HardwareFingerprint = "FP2" },
                new ComputerNode { Id = "node-3", Name = "Available Node 2", IpAddress = "192.168.1.3", IsAvailable = true, LastHeartbeat = DateTime.UtcNow, HardwareFingerprint = "FP3" }
            };

            context.Nodes.AddRange(nodes);
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetAvailableNodesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.All(result, node => Assert.True(node.IsAvailable));
        }

        [Fact]
        public async Task TryAcquireFileLockAsync_NewLock_ShouldSucceed()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            var filePath = "test-file.txt";
            var nodeId = "test-node";

            // Act
            var result = await service.TryAcquireFileLockAsync(filePath, nodeId);

            // Assert
            Assert.True(result);
            
            // Verify lock exists in database
            var fileLock = await context.FileLocks.FirstOrDefaultAsync(l => l.FilePath.Contains(filePath));
            Assert.NotNull(fileLock);
            Assert.Equal(nodeId, fileLock.LockingNodeId);
        }

        [Fact]
        public async Task TryAcquireFileLockAsync_ExistingLock_ShouldFail()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            var filePath = "locked-file.txt";
            var nodeId1 = "node-1";
            var nodeId2 = "node-2";

            // First lock
            await service.TryAcquireFileLockAsync(filePath, nodeId1);

            // Act - Try to acquire same lock with different node
            var result = await service.TryAcquireFileLockAsync(filePath, nodeId2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReleaseFileLockAsync_ExistingLock_ShouldSucceed()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            var filePath = "release-test.txt";
            var nodeId = "test-node";

            await service.TryAcquireFileLockAsync(filePath, nodeId);

            // Act
            var result = await service.ReleaseFileLockAsync(filePath, nodeId);

            // Assert
            Assert.True(result);
            
            // Verify lock is removed
            var fileLock = await context.FileLocks.FirstOrDefaultAsync(l => l.FilePath.Contains(filePath));
            Assert.Null(fileLock);
        }

        [Fact]
        public async Task GetActiveLocksAsync_ShouldReturnActiveLocks()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            // Create some locks
            await service.TryAcquireFileLockAsync("file1.txt", "node-1");
            await service.TryAcquireFileLockAsync("file2.txt", "node-2");
            await service.TryAcquireFileLockAsync("file3.txt", "node-1");

            // Act
            var result = await service.GetActiveLocksAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task CheckAndCompleteVolumeCompressionTaskAsync_ShouldCheckCompletion()
        {
            // Arrange
            using var context = CreateSqliteDbContext();
            var mockLogger = new Mock<ILogger<TaskService>>();
            var service = new TestTaskService(context, mockLogger.Object);

            var task = new BatchTask
            {
                Name = "Volume Compression Test",
                Type = TaskType.VolumeCompression,
                Status = BatchTaskStatus.Running,
                CreatedAt = DateTime.UtcNow
            };

            context.Tasks.Add(task);
            await context.SaveChangesAsync();

            // Act
            var result = await service.CheckAndCompleteVolumeCompressionTaskAsync(task.Id);

            // Assert
            Assert.True(result);
            
            // Verify task is completed
            var updatedTask = await context.Tasks.FindAsync(task.Id);
            Assert.NotNull(updatedTask);
            Assert.Equal(BatchTaskStatus.Completed, updatedTask.Status);
            Assert.NotNull(updatedTask.CompletedAt);
        }

        public void Dispose()
        {
            foreach (var context in _contexts)
            {
                try
                {
                    context?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors in tests
                }
            }

            foreach (var connection in _connections)
            {
                try
                {
                    connection?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors in tests
                }
            }

            _contexts.Clear();
            _connections.Clear();
        }
    }
} 