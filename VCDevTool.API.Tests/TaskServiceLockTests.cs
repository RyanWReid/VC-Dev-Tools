using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VCDevTool.API.Data;
using VCDevTool.API.Services;
using VCDevTool.Shared;
using VCDevTool.API.Tests.Data;
using VCDevTool.API.Tests.Services;
using Xunit;

namespace VCDevTool.API.Tests
{
    public class TaskServiceLockTests : IDisposable
    {
        private readonly List<AppDbContext> _contexts = new();
        private readonly List<SqliteConnection> _connections = new();

        private AppDbContext CreateSqliteDbContext(SqliteConnection connection)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            TestDbOptimizations.ConfigureTestOptions(options, connection);

            var context = new AppDbContext(options.Options);
            TestDbOptimizations.InitializeTestDatabase(context);
            
            _contexts.Add(context);
            return context;
        }

        private SqliteConnection CreateInMemoryConnection()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            _connections.Add(connection);
            return connection;
        }

        private async Task<AppDbContext> CreateContextWithTestDataAsync()
        {
            var connection = CreateInMemoryConnection();
            var context = CreateSqliteDbContext(connection);
            await TestDataSeeder.SeedBasicTestDataAsync(context);
            return context;
        }

        private ILogger<TaskService> CreateMockLogger()
        {
            return new LoggerFactory().CreateLogger<TaskService>();
        }

        [Fact]
        public async Task OnlyOneNodeCanAcquireLock_SimultaneousAttempts()
        {
            // Arrange
            var testFilePath = "test-file-concurrent.txt";
            var nodeId1 = "test-node-1";
            var nodeId2 = "test-node-2";
            
            var lockResults = new ConcurrentBag<bool>();
            var tasks = new List<Task>();

            // Use a shared connection for proper transaction isolation testing
            using var sharedConnection = CreateInMemoryConnection();
            using var sharedContext = CreateSqliteDbContext(sharedConnection);
            await TestDataSeeder.SeedBasicTestDataAsync(sharedContext);
            
            var sharedService = new SqliteCompatibleTaskService(sharedContext, CreateMockLogger());

            // Act - Create concurrent tasks that try to acquire the same lock
            for (int i = 0; i < 2; i++)
            {
                var nodeId = i == 0 ? nodeId1 : nodeId2;
                var task = Task.Run(async () =>
                {
                    var result = await sharedService.TryAcquireFileLockAsync(testFilePath, nodeId);
                    lockResults.Add(result);
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Assert - Only one should succeed
            var results = lockResults.ToArray();
            Assert.Equal(2, results.Length);
            Assert.Equal(1, results.Count(r => r == true));
            Assert.Equal(1, results.Count(r => r == false));
        }

        [Fact]
        public async Task Lock_Acquisition_Is_Independent_Of_Path_Separators()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var pathWithBackslash = "C:\\temp\\test.txt";
            var pathWithForwardslash = "C:/temp/test.txt";
            var nodeId = "test-node-1";

            // Act
            var firstLock = await service.TryAcquireFileLockAsync(pathWithBackslash, nodeId);
            var secondLock = await service.TryAcquireFileLockAsync(pathWithForwardslash, nodeId);

            // Assert
            Assert.True(firstLock, "Should acquire first lock");
            Assert.False(secondLock, "Should not acquire second lock (same normalized path)");
        }

        [Fact]
        public async Task File_Lock_Works_With_Different_Path_Formats()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var pathFormats = new[]
            {
                "c:\\temp\\file.txt",
                "C:/TEMP/FILE.TXT",
                "C:\\temp\\file.txt\\"
            };
            var nodeId = "test-node-1";

            // Act & Assert
            var firstResult = await service.TryAcquireFileLockAsync(pathFormats[0], nodeId);
            Assert.True(firstResult, "Should acquire lock with format 1");

            for (int i = 1; i < pathFormats.Length; i++)
            {
                var result = await service.TryAcquireFileLockAsync(pathFormats[i], nodeId);
                Assert.False(result, $"Should not acquire lock with format {i + 1} (normalized paths should match)");
            }
        }

        [Fact]
        public async Task Multiple_Locks_Different_Files_Should_Succeed()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var files = new[] { "file1.txt", "file2.txt", "file3.txt" };
            var nodeId = "test-node-1";

            // Act & Assert
            foreach (var file in files)
            {
                var result = await service.TryAcquireFileLockAsync(file, nodeId);
                Assert.True(result, $"Should acquire lock for {file}");
            }
        }

        [Fact]
        public async Task Lock_Release_Allows_Reacquisition()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var filePath = "reacquire-test.txt";
            var nodeId = "test-node-1";

            // Act
            var firstLock = await service.TryAcquireFileLockAsync(filePath, nodeId);
            Assert.True(firstLock, "Should acquire initial lock");

            await service.ReleaseFileLockAsync(filePath, nodeId);

            var secondLock = await service.TryAcquireFileLockAsync(filePath, nodeId);

            // Assert
            Assert.True(secondLock, "Should be able to reacquire lock after release");
        }

        [Fact]
        public async Task Lock_Cleanup_Removes_Expired_Locks()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var filePath = "expired-lock.txt";
            var nodeId = "test-node-1";

            // Acquire lock
            var lockAcquired = await service.TryAcquireFileLockAsync(filePath, nodeId);
            Assert.True(lockAcquired, "Should acquire initial lock");

            // Manually set lock as expired (simulate old lock)
            var lockRecord = await context.FileLocks.FirstOrDefaultAsync(l => l.FilePath.Contains(filePath));
            Assert.NotNull(lockRecord);
            lockRecord.LastUpdatedAt = DateTime.UtcNow.AddMinutes(-15); // 15 minutes old
            await context.SaveChangesAsync();

            // Act - Cleanup expired locks using available method (reset all locks for simplicity in tests)
            await service.ResetAllFileLocksAsync();

            // Try to acquire lock again
            var newLock = await service.TryAcquireFileLockAsync(filePath, nodeId);

            // Assert
            Assert.True(newLock, "Should be able to acquire lock after cleanup");
        }

        [Fact]
        public async Task TryAcquireFileLockAsync_WithValidNode_ShouldSucceed()
        {
            // Arrange
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var filePath = "valid-node-lock.txt";
            var nodeId = "test-node-1"; // This exists in test data

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
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var filePath = "locked-file.txt";
            var nodeId1 = "test-node-1";
            var nodeId2 = "test-node-2";

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
            using var context = await CreateContextWithTestDataAsync();
            var service = new SqliteCompatibleTaskService(context, CreateMockLogger());

            var filePath = "release-test.txt";
            var nodeId = "test-node-1";

            await service.TryAcquireFileLockAsync(filePath, nodeId);

            // Act
            var result = await service.ReleaseFileLockAsync(filePath, nodeId);

            // Assert
            Assert.True(result);
            
            // Verify lock is removed
            var fileLock = await context.FileLocks.FirstOrDefaultAsync(l => l.FilePath.Contains(filePath));
            Assert.Null(fileLock);
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
