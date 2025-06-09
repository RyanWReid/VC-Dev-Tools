using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using VCDevTool.API.Data;
using VCDevTool.API.Services;
using VCDevTool.Shared;
using Xunit;
using Xunit.Abstractions;

namespace VCDevTool.API.Tests.Performance
{
    /// <summary>
    /// Performance benchmark tests for measuring system performance under various conditions
    /// </summary>
    public class PerformanceBenchmarkTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _context;
        private readonly IPerformanceMonitoringService _performanceService;
        private readonly ITestOutputHelper _output;

        public PerformanceBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;

            var services = new ServiceCollection();
            
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            services.AddLogging(builder => builder.AddConsole());
            services.AddScoped<IPerformanceMonitoringService, PerformanceMonitoringService>();

            _serviceProvider = services.BuildServiceProvider();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
            _performanceService = _serviceProvider.GetRequiredService<IPerformanceMonitoringService>();
        }

        [Fact]
        public async Task BenchmarkDatabaseOperations_ShouldMeetPerformanceThresholds()
        {
            // Arrange
            const int recordCount = 1000;
            await SeedTestData(recordCount);

            var stopwatch = Stopwatch.StartNew();
            var operations = new List<(string Operation, long ElapsedMs)>();

            // Act & Measure - Basic CRUD operations
            
            // 1. Bulk Insert Performance
            stopwatch.Restart();
            var newTasks = Enumerable.Range(recordCount + 1, 100).Select(i => new BatchTask
            {
                Name = $"Bulk Insert Task {i}",
                Type = TaskType.FileProcessing,
                Status = BatchTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _context.Tasks.AddRange(newTasks);
            await _context.SaveChangesOptimizedAsync();
            stopwatch.Stop();
            operations.Add(("Bulk Insert (100 records)", stopwatch.ElapsedMilliseconds));

            // 2. Indexed Query Performance
            stopwatch.Restart();
            var pendingTasks = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Status == BatchTaskStatus.Pending)
                .Take(50)
                .ToListAsync();
            stopwatch.Stop();
            operations.Add(("Indexed Query (Status)", stopwatch.ElapsedMilliseconds));

            // 3. Complex Query Performance
            stopwatch.Restart();
            var complexQuery = await _context.Tasks
                .WithPerformanceOptimizations()
                .Where(t => t.Status == BatchTaskStatus.Pending && 
                           t.CreatedAt >= DateTime.UtcNow.AddDays(-1) &&
                           t.Type == TaskType.FileProcessing)
                .OrderBy(t => t.CreatedAt)
                .Take(20)
                .ToListAsync();
            stopwatch.Stop();
            operations.Add(("Complex Query", stopwatch.ElapsedMilliseconds));

            // 4. Aggregation Performance
            stopwatch.Restart();
            var stats = await _context.Tasks
                .WithPerformanceOptimizations()
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            stopwatch.Stop();
            operations.Add(("Aggregation Query", stopwatch.ElapsedMilliseconds));

            // 5. Update Performance
            stopwatch.Restart();
            var tasksToUpdate = await _context.Tasks
                .Where(t => t.Status == BatchTaskStatus.Pending)
                .Take(10)
                .ToListAsync();

            foreach (var task in tasksToUpdate)
            {
                task.Status = BatchTaskStatus.Running;
                task.StartedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesOptimizedAsync();
            stopwatch.Stop();
            operations.Add(("Bulk Update (10 records)", stopwatch.ElapsedMilliseconds));

            // Assert - Performance thresholds
            _output.WriteLine("Database Operation Performance Results:");
            foreach (var (operation, elapsedMs) in operations)
            {
                _output.WriteLine($"{operation}: {elapsedMs}ms");
                
                // Performance assertions (adjust thresholds based on requirements)
                switch (operation)
                {
                    case var op when op.Contains("Bulk Insert"):
                        Assert.True(elapsedMs < 1000, $"Bulk insert took {elapsedMs}ms, expected < 1000ms");
                        break;
                    case var op when op.Contains("Indexed Query"):
                        Assert.True(elapsedMs < 100, $"Indexed query took {elapsedMs}ms, expected < 100ms");
                        break;
                    case var op when op.Contains("Complex Query"):
                        Assert.True(elapsedMs < 200, $"Complex query took {elapsedMs}ms, expected < 200ms");
                        break;
                    case var op when op.Contains("Aggregation"):
                        Assert.True(elapsedMs < 150, $"Aggregation query took {elapsedMs}ms, expected < 150ms");
                        break;
                    case var op when op.Contains("Bulk Update"):
                        Assert.True(elapsedMs < 500, $"Bulk update took {elapsedMs}ms, expected < 500ms");
                        break;
                }
            }
        }

        [Fact]
        public async Task BenchmarkPerformanceService_ShouldMeetResponseTimeTargets()
        {
            // Arrange
            await SeedPerformanceTestData();
            var measurements = new List<(string Method, long ElapsedMs)>();

            // Act & Measure - Performance service methods
            
            // 1. Performance Metrics Calculation
            var stopwatch = Stopwatch.StartNew();
            var metrics = await _performanceService.GetPerformanceMetricsAsync(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
            stopwatch.Stop();
            measurements.Add(("GetPerformanceMetrics", stopwatch.ElapsedMilliseconds));

            // 2. Database Performance Check
            stopwatch.Restart();
            var dbMetrics = await _performanceService.GetDatabasePerformanceAsync();
            stopwatch.Stop();
            measurements.Add(("GetDatabasePerformance", stopwatch.ElapsedMilliseconds));

            // 3. System Health Check
            stopwatch.Restart();
            var health = await _performanceService.GetSystemHealthAsync();
            stopwatch.Stop();
            measurements.Add(("GetSystemHealth", stopwatch.ElapsedMilliseconds));

            // 4. Node Performance Metrics
            stopwatch.Restart();
            var nodeMetrics = await _performanceService.GetNodePerformanceMetricsAsync(DateTime.UtcNow.AddDays(-1));
            stopwatch.Stop();
            measurements.Add(("GetNodePerformanceMetrics", stopwatch.ElapsedMilliseconds));

            // Assert - Response time targets
            _output.WriteLine("Performance Service Response Times:");
            foreach (var (method, elapsedMs) in measurements)
            {
                _output.WriteLine($"{method}: {elapsedMs}ms");
                
                // All performance service methods should respond within 2 seconds
                Assert.True(elapsedMs < 2000, $"{method} took {elapsedMs}ms, expected < 2000ms");
                
                // Critical methods should be even faster
                if (method == "GetSystemHealth")
                {
                    Assert.True(elapsedMs < 500, $"{method} took {elapsedMs}ms, expected < 500ms for health checks");
                }
            }

            // Assert - Data quality
            Assert.True(metrics.TotalTasks > 0, "Performance metrics should include task data");
            Assert.True(dbMetrics.TasksCount > 0, "Database metrics should include table counts");
            Assert.True(health.Timestamp != default, "Health timestamp should be set");
        }

        [Fact]
        public async Task BenchmarkConcurrentOperations_ShouldMaintainPerformance()
        {
            // Arrange
            const int concurrentOperations = 20;
            const int operationsPerTask = 10;
            var results = new List<long>();

            // Act - Simulate concurrent database operations
            var tasks = Enumerable.Range(0, concurrentOperations).Select(async taskIndex =>
            {
                var taskResults = new List<long>();
                
                for (int i = 0; i < operationsPerTask; i++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    
                    // Simulate typical operations
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // Create task
                    var task = new BatchTask
                    {
                        Name = $"Concurrent Task {taskIndex}-{i}",
                        Type = TaskType.FileProcessing,
                        Status = BatchTaskStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    context.Tasks.Add(task);
                    await context.SaveChangesOptimizedAsync();
                    
                    // Read tasks
                    var recentTasks = await context.Tasks
                        .WithPerformanceOptimizations()
                        .Where(t => t.Status == BatchTaskStatus.Pending)
                        .Take(5)
                        .ToListAsync();
                    
                    stopwatch.Stop();
                    taskResults.Add(stopwatch.ElapsedMilliseconds);
                }
                
                return taskResults;
            });

            var allResults = await Task.WhenAll(tasks);
            
            // Flatten results
            foreach (var taskResults in allResults)
            {
                results.AddRange(taskResults);
            }

            // Assert - Concurrent performance
            var averageTime = results.Average();
            var maxTime = results.Max();
            var minTime = results.Min();

            _output.WriteLine($"Concurrent Operations Performance:");
            _output.WriteLine($"Operations: {results.Count}");
            _output.WriteLine($"Average Time: {averageTime:F2}ms");
            _output.WriteLine($"Min Time: {minTime}ms");
            _output.WriteLine($"Max Time: {maxTime}ms");

            // Performance assertions for concurrent operations
            Assert.True(averageTime < 500, $"Average concurrent operation time {averageTime:F2}ms, expected < 500ms");
            Assert.True(maxTime < 2000, $"Max concurrent operation time {maxTime}ms, expected < 2000ms");
            
            // Ensure we completed all operations
            Assert.Equal(concurrentOperations * operationsPerTask, results.Count);
        }

        [Fact]
        public async Task BenchmarkLargeDatasetQueries_ShouldScaleLinear()
        {
            // Arrange - Test with different dataset sizes
            var dataSizes = new[] { 100, 500, 1000, 2000 };
            var results = new List<(int Size, long QueryTimeMs)>();

            foreach (var size in dataSizes)
            {
                // Clean database for each test
                _context.Tasks.RemoveRange(_context.Tasks);
                await _context.SaveChangesAsync();
                
                // Seed data
                await SeedTestData(size);

                // Measure query performance
                var stopwatch = Stopwatch.StartNew();
                
                var query = await _context.Tasks
                    .WithPerformanceOptimizations()
                    .Where(t => t.Status == BatchTaskStatus.Pending || t.Status == BatchTaskStatus.Running)
                    .OrderBy(t => t.CreatedAt)
                    .Take(50)
                    .ToListAsync();
                
                stopwatch.Stop();
                results.Add((size, stopwatch.ElapsedMilliseconds));
            }

            // Assert - Performance should scale reasonably with data size
            _output.WriteLine("Large Dataset Query Performance:");
            
            for (int i = 0; i < results.Count; i++)
            {
                var (size, queryTime) = results[i];
                _output.WriteLine($"Dataset Size: {size}, Query Time: {queryTime}ms");
                
                // Query time should not grow exponentially with data size
                Assert.True(queryTime < 1000, $"Query on {size} records took {queryTime}ms, expected < 1000ms");
                
                // For larger datasets, performance should still be reasonable
                if (size >= 2000)
                {
                    Assert.True(queryTime < 500, $"Query on large dataset ({size} records) took {queryTime}ms, expected < 500ms with proper indexing");
                }
            }

            // Check that performance doesn't degrade dramatically
            if (results.Count >= 2)
            {
                var smallDatasetTime = results[0].QueryTimeMs;
                var largeDatasetTime = results[^1].QueryTimeMs;
                var performanceRatio = (double)largeDatasetTime / smallDatasetTime;
                
                Assert.True(performanceRatio < 10, 
                    $"Performance degradation ratio {performanceRatio:F2} is too high, indicating poor scaling");
            }
        }

        [Fact]
        public async Task BenchmarkMemoryUsage_ShouldBeEfficient()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(false);
            
            // Act - Perform memory-intensive operations
            await SeedTestData(1000);
            
            // Perform multiple queries to test memory efficiency
            for (int i = 0; i < 10; i++)
            {
                var tasks = await _context.Tasks
                    .WithPerformanceOptimizations() // Should use AsNoTracking
                    .Take(100)
                    .ToListAsync();
                
                // Process tasks (simulate business logic)
                foreach (var task in tasks)
                {
                    _ = task.Name.Length + task.CreatedAt.Ticks;
                }
            }
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;
            
            // Assert - Memory usage should be reasonable
            _output.WriteLine($"Memory Usage:");
            _output.WriteLine($"Initial: {initialMemory / 1024 / 1024:F2} MB");
            _output.WriteLine($"Final: {finalMemory / 1024 / 1024:F2} MB");
            _output.WriteLine($"Increase: {memoryIncrease / 1024 / 1024:F2} MB");
            
            // Memory increase should be reasonable (less than 50MB for this test)
            Assert.True(memoryIncrease < 50 * 1024 * 1024, 
                $"Memory increase {memoryIncrease / 1024 / 1024:F2} MB is too high, indicating memory leak");
        }

        private async Task SeedTestData(int count)
        {
            var random = new Random(42); // Fixed seed for consistent tests
            var tasks = Enumerable.Range(1, count).Select(i => new BatchTask
            {
                Name = $"Test Task {i}",
                Type = (TaskType)(random.Next(1, 8)),
                Status = (BatchTaskStatus)(random.Next(0, 5)),
                CreatedAt = DateTime.UtcNow.AddDays(-random.Next(0, 30)),
                StartedAt = random.NextDouble() > 0.5 ? DateTime.UtcNow.AddDays(-random.Next(0, 29)) : null,
                CompletedAt = random.NextDouble() > 0.7 ? DateTime.UtcNow.AddDays(-random.Next(0, 28)) : null
            }).ToList();

            // Add in batches for better performance
            const int batchSize = 100;
            for (int i = 0; i < tasks.Count; i += batchSize)
            {
                var batch = tasks.Skip(i).Take(batchSize);
                _context.Tasks.AddRange(batch);
                await _context.SaveChangesOptimizedAsync();
            }
        }

        private async Task SeedPerformanceTestData()
        {
            // Create test data with known patterns for performance testing
            var nodes = new[]
            {
                new ComputerNode { Id = "node-1", Name = "Test Node 1", IpAddress = "192.168.1.100", IsAvailable = true, LastHeartbeat = DateTime.UtcNow },
                new ComputerNode { Id = "node-2", Name = "Test Node 2", IpAddress = "192.168.1.101", IsAvailable = true, LastHeartbeat = DateTime.UtcNow }
            };

            _context.Nodes.AddRange(nodes);
            await _context.SaveChangesAsync();

            // Create tasks with specific patterns
            var tasks = new List<BatchTask>();
            for (int i = 0; i < 100; i++)
            {
                var task = new BatchTask
                {
                    Name = $"Performance Test Task {i}",
                    Type = TaskType.FileProcessing,
                    Status = i < 70 ? BatchTaskStatus.Completed : 
                            i < 85 ? BatchTaskStatus.Running : BatchTaskStatus.Pending,
                    AssignedNodeId = i % 2 == 0 ? "node-1" : "node-2",
                    CreatedAt = DateTime.UtcNow.AddHours(-i),
                    StartedAt = i < 85 ? DateTime.UtcNow.AddHours(-i + 1) : null,
                    CompletedAt = i < 70 ? DateTime.UtcNow.AddHours(-i + 2) : null
                };
                tasks.Add(task);
            }

            _context.Tasks.AddRange(tasks);
            await _context.SaveChangesOptimizedAsync();
        }

        public void Dispose()
        {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
} 