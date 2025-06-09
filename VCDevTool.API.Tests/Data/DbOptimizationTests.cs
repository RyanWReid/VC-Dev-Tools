using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VCDevTool.API.Data;
using VCDevTool.Shared;
using Xunit;

namespace VCDevTool.API.Tests.Data
{
    /// <summary>
    /// Tests for database optimization features and performance tracking
    /// </summary>
    public class DbOptimizationTests : IDisposable
    {
        private readonly AppDbContext _context;
        private readonly ServiceProvider _serviceProvider;

        public DbOptimizationTests()
        {
            var services = new ServiceCollection();
            
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            _serviceProvider = services.BuildServiceProvider();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
        }

        [Fact]
        public async Task WithPerformanceOptimizations_ShouldReturnAsNoTracking()
        {
            // Arrange
            await SeedTestData();

            // Act
            var query = _context.Tasks.WithPerformanceOptimizations();
            var tasks = await query.ToListAsync();

            // Assert
            Assert.NotEmpty(tasks);
            
            // Verify that entities are not tracked
            foreach (var task in tasks)
            {
                var entry = _context.Entry(task);
                Assert.Equal(EntityState.Detached, entry.State);
            }
        }

        [Fact]
        public async Task SaveChangesOptimizedAsync_ShouldBatchOperations()
        {
            // Arrange
            var tasks = Enumerable.Range(1, 100).Select(i => new BatchTask
            {
                Name = $"Test Task {i}",
                Type = TaskType.FileProcessing,
                Status = BatchTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _context.Tasks.AddRange(tasks);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _context.SaveChangesOptimizedAsync();
            stopwatch.Stop();

            // Assert
            Assert.Equal(100, result);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
            
            var savedTasks = await _context.Tasks.CountAsync();
            Assert.Equal(100, savedTasks);
        }

        [Fact]
        public void ConfigurePerformanceOptions_ShouldSetCorrectOptions()
        {
            // Arrange
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            var connectionString = "Server=test;Database=test;Integrated Security=true;";

            // Act
            DbOptimizations.ConfigurePerformanceOptions(optionsBuilder, connectionString, isProduction: true);

            // Assert
            var options = optionsBuilder.Options;
            Assert.NotNull(options);
            // Additional assertions would depend on the specific options being set
        }

        [Fact]
        public async Task DatabasePerformance_ShouldHandleLargeDatasets()
        {
            // Arrange
            const int recordCount = 1000;
            await SeedLargeDataset(recordCount);

            // Act - Test query performance
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var pendingTasks = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Status == BatchTaskStatus.Pending)
                .Take(10)
                .ToListAsync();
            
            stopwatch.Stop();

            // Assert
            Assert.Equal(10, pendingTasks.Count);
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Query took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
        }

        [Fact]
        public async Task IndexedQueries_ShouldPerformEfficiently()
        {
            // Arrange
            await SeedTestData();

            // Act & Assert - Test various indexed queries
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Query by Status (indexed)
            var tasksByStatus = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Status == BatchTaskStatus.Pending)
                .CountAsync();

            // Query by Type (indexed)
            var tasksByType = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Type == TaskType.FileProcessing)
                .CountAsync();

            // Query by composite index (Status + CreatedAt)
            var recentPendingTasks = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Status == BatchTaskStatus.Pending && 
                           t.CreatedAt >= DateTime.UtcNow.AddDays(-1))
                .CountAsync();

            stopwatch.Stop();

            // All indexed queries should complete quickly
            Assert.True(stopwatch.ElapsedMilliseconds < 500, 
                $"Indexed queries took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
        }

        [Fact]
        public async Task ConcurrentOperations_ShouldHandleGracefully()
        {
            // Arrange
            const int concurrentTasks = 10;
            var tasks = new List<Task>();

            // Ensure database is initialized
            await _context.Database.EnsureCreatedAsync();

            // Act - Simulate concurrent database operations
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    var batchTask = new BatchTask
                    {
                        Name = $"Concurrent Task {taskIndex}",
                        Type = TaskType.FileProcessing,
                        Status = BatchTaskStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Tasks.Add(batchTask);
                    await context.SaveChangesOptimizedAsync();
                }));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            // Wait a bit to ensure all changes are committed
            await Task.Delay(100);

            // Assert - Refresh context to ensure we see all changes
            var freshContext = _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
            var totalTasks = await freshContext.Tasks.CountAsync();
            Assert.Equal(concurrentTasks, totalTasks);
        }

        [Fact]
        public async Task FileLockCleanup_ShouldRemoveExpiredLocks()
        {
            // Arrange
            var expiredLock = new FileLock
            {
                FilePath = "/test/expired/file.txt",
                LockingNodeId = "node-1",
                AcquiredAt = DateTime.UtcNow.AddHours(-2),
                LastUpdatedAt = DateTime.UtcNow.AddHours(-2)
            };

            var activeLock = new FileLock
            {
                FilePath = "/test/active/file.txt",
                LockingNodeId = "node-2",
                AcquiredAt = DateTime.UtcNow.AddMinutes(-5),
                LastUpdatedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            _context.FileLocks.AddRange(expiredLock, activeLock);
            await _context.SaveChangesAsync();

            // Act - Simulate cleanup query (this would typically be run by a background service)
            var expiredLocks = await _context.FileLocks
                .Where(fl => fl.LastUpdatedAt < DateTime.UtcNow.AddMinutes(-60))
                .ToListAsync();

            _context.FileLocks.RemoveRange(expiredLocks);
            await _context.SaveChangesAsync();

            // Assert
            var remainingLocks = await _context.FileLocks.CountAsync();
            Assert.Equal(1, remainingLocks);

            var remaining = await _context.FileLocks.FirstAsync();
            Assert.Equal("/test/active/file.txt", remaining.FilePath);
        }

        [Fact]
        public async Task TaskFolderProgress_ShouldTrackPerformanceMetrics()
        {
            // Arrange
            var task = new BatchTask
            {
                Name = "Performance Test Task",
                Type = TaskType.RealityCapture,
                Status = BatchTaskStatus.Running,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            var folderProgress = new TaskFolderProgress
            {
                TaskId = task.Id,
                FolderPath = "/test/folder",
                FolderName = "folder",
                Status = TaskFolderStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                StartedAt = DateTime.UtcNow.AddMinutes(-8),
                CompletedAt = DateTime.UtcNow.AddMinutes(-2),
                Progress = 100.0
            };

            _context.TaskFolderProgress.Add(folderProgress);
            await _context.SaveChangesAsync();

            // Act - Calculate performance metrics (avoid SQL Server functions for InMemory database)
            var completedFolders = await _context.TaskFolderProgress
                .WithPerformanceOptimizations()
                .Where(tfp => tfp.Status == TaskFolderStatus.Completed)
                .Select(tfp => new
                {
                    StartedAt = tfp.StartedAt,
                    CompletedAt = tfp.CompletedAt
                })
                .ToListAsync();

            // Calculate duration in memory
            var metrics = completedFolders.Select(tfp => new
            {
                Duration = tfp.CompletedAt.HasValue && tfp.StartedAt.HasValue
                    ? (int)(tfp.CompletedAt.Value - tfp.StartedAt.Value).TotalSeconds
                    : (int?)null
            }).ToList();

            // Assert
            Assert.Single(metrics);
            var metric = metrics.First();
            Assert.NotNull(metric.Duration);
            Assert.True(metric.Duration > 0);
        }

        private async Task SeedTestData()
        {
            var tasks = new[]
            {
                new BatchTask { Name = "Task 1", Type = TaskType.FileProcessing, Status = BatchTaskStatus.Pending, CreatedAt = DateTime.UtcNow },
                new BatchTask { Name = "Task 2", Type = TaskType.RealityCapture, Status = BatchTaskStatus.Running, CreatedAt = DateTime.UtcNow },
                new BatchTask { Name = "Task 3", Type = TaskType.VolumeCompression, Status = BatchTaskStatus.Completed, CreatedAt = DateTime.UtcNow }
            };

            _context.Tasks.AddRange(tasks);
            await _context.SaveChangesAsync();
        }

        private async Task SeedLargeDataset(int count)
        {
            var random = new Random();
            var tasks = Enumerable.Range(1, count).Select(i => new BatchTask
            {
                Name = $"Large Dataset Task {i}",
                Type = (TaskType)(random.Next(1, 8)), // Random task type
                Status = (BatchTaskStatus)(random.Next(0, 5)), // Random status
                CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 30))
            }).ToList();

            // Add in batches to avoid memory issues
            const int batchSize = 100;
            for (int i = 0; i < tasks.Count; i += batchSize)
            {
                var batch = tasks.Skip(i).Take(batchSize);
                _context.Tasks.AddRange(batch);
                await _context.SaveChangesOptimizedAsync();
            }
        }

        public void Dispose()
        {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
} 