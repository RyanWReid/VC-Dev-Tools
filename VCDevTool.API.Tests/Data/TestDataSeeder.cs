using VCDevTool.API.Data;
using VCDevTool.Shared;

namespace VCDevTool.API.Tests.Data
{
    /// <summary>
    /// Centralized test data seeding utility that properly handles foreign key relationships
    /// </summary>
    public static class TestDataSeeder
    {
        /// <summary>
        /// Seed minimal required data for basic tests
        /// </summary>
        public static async Task SeedBasicTestDataAsync(AppDbContext context)
        {
            await context.Database.EnsureCreatedAsync();
            
            // Seed nodes first (referenced by foreign keys)
            if (!context.Nodes.Any())
            {
                var testNodes = GetTestNodes();
                context.Nodes.AddRange(testNodes);
                await context.SaveChangesAsync();
            }

            // Seed basic tasks
            if (!context.Tasks.Any())
            {
                var testTasks = GetTestTasks();
                context.Tasks.AddRange(testTasks);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Seed comprehensive test data including relationships
        /// </summary>
        public static async Task SeedFullTestDataAsync(AppDbContext context)
        {
            await SeedBasicTestDataAsync(context);

            // Seed task folder progress (requires existing tasks)
            if (!context.TaskFolderProgress.Any())
            {
                var firstTask = context.Tasks.First();
                var testFolderProgresses = GetTestTaskFolderProgresses(firstTask.Id);
                context.TaskFolderProgress.AddRange(testFolderProgresses);
                await context.SaveChangesAsync();
            }

            // Seed file locks (requires existing nodes)
            if (!context.FileLocks.Any())
            {
                var firstNode = context.Nodes.First();
                var testFileLocks = GetTestFileLocks(firstNode.Id);
                context.FileLocks.AddRange(testFileLocks);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Get test computer nodes
        /// </summary>
        public static List<ComputerNode> GetTestNodes()
        {
            return new List<ComputerNode>
            {
                new ComputerNode
                {
                    Id = "test-node-1",
                    Name = "Test Node 1",
                    IpAddress = "192.168.1.100",
                    HardwareFingerprint = "TEST-FINGERPRINT-001",
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-1)
                },
                new ComputerNode
                {
                    Id = "test-node-2",
                    Name = "Test Node 2",
                    IpAddress = "192.168.1.101",
                    HardwareFingerprint = "TEST-FINGERPRINT-002",
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-2)
                },
                new ComputerNode
                {
                    Id = "test-node-3",
                    Name = "Test Node 3",
                    IpAddress = "192.168.1.102",
                    HardwareFingerprint = "TEST-FINGERPRINT-003",
                    IsAvailable = false,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-10)
                }
            };
        }

        /// <summary>
        /// Get test batch tasks
        /// </summary>
        public static List<BatchTask> GetTestTasks()
        {
            return new List<BatchTask>
            {
                new BatchTask
                {
                    Name = "Test Task 1 - File Processing",
                    Type = TaskType.FileProcessing,
                    Status = BatchTaskStatus.Pending,
                    AssignedNodeId = "test-node-1",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    Parameters = "{\"inputPath\": \"/test/input\", \"outputPath\": \"/test/output\"}"
                },
                new BatchTask
                {
                    Name = "Test Task 2 - Volume Compression",
                    Type = TaskType.VolumeCompression,
                    Status = BatchTaskStatus.Running,
                    AssignedNodeId = "test-node-2",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    StartedAt = DateTime.UtcNow.AddMinutes(-30),
                    Parameters = "{\"compressionLevel\": 5}"
                },
                new BatchTask
                {
                    Name = "Test Task 3 - Reality Capture",
                    Type = TaskType.RealityCapture,
                    Status = BatchTaskStatus.Completed,
                    AssignedNodeId = "test-node-1",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    StartedAt = DateTime.UtcNow.AddDays(-1).AddMinutes(5),
                    CompletedAt = DateTime.UtcNow.AddDays(-1).AddHours(2),
                    ResultMessage = "Task completed successfully"
                },
                new BatchTask
                {
                    Name = "Test Task 4 - Failed Task",
                    Type = TaskType.FileProcessing,
                    Status = BatchTaskStatus.Failed,
                    AssignedNodeId = "test-node-3",
                    CreatedAt = DateTime.UtcNow.AddHours(-3),
                    StartedAt = DateTime.UtcNow.AddHours(-3).AddMinutes(2),
                    CompletedAt = DateTime.UtcNow.AddHours(-3).AddMinutes(5),
                    ResultMessage = "Test failure for validation"
                }
            };
        }

        /// <summary>
        /// Get test task folder progresses for a given task ID
        /// </summary>
        public static List<TaskFolderProgress> GetTestTaskFolderProgresses(int taskId)
        {
            return new List<TaskFolderProgress>
            {
                new TaskFolderProgress
                {
                    TaskId = taskId,
                    FolderPath = "/test/folder1",
                    FolderName = "folder1",
                    Status = TaskFolderStatus.InProgress,
                    AssignedNodeId = "test-node-1",
                    AssignedNodeName = "Test Node 1",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                    Progress = 0.45
                },
                new TaskFolderProgress
                {
                    TaskId = taskId,
                    FolderPath = "/test/folder2",
                    FolderName = "folder2",
                    Status = TaskFolderStatus.Completed,
                    AssignedNodeId = "test-node-2",
                    AssignedNodeName = "Test Node 2",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    CompletedAt = DateTime.UtcNow.AddMinutes(-10),
                    Progress = 1.0,
                    OutputPath = "/test/output/folder2"
                }
            };
        }

        /// <summary>
        /// Get test file locks for a given node ID
        /// </summary>
        public static List<FileLock> GetTestFileLocks(string nodeId)
        {
            return new List<FileLock>
            {
                new FileLock
                {
                    FilePath = "/test/locked/file1.txt",
                    LockingNodeId = nodeId,
                    AcquiredAt = DateTime.UtcNow.AddMinutes(-15),
                    LastUpdatedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new FileLock
                {
                    FilePath = "/test/locked/file2.txt",
                    LockingNodeId = nodeId,
                    AcquiredAt = DateTime.UtcNow.AddMinutes(-10),
                    LastUpdatedAt = DateTime.UtcNow.AddMinutes(-2)
                }
            };
        }

        /// <summary>
        /// Clear all test data from the database
        /// </summary>
        public static async Task ClearAllTestDataAsync(AppDbContext context)
        {
            // Delete in correct order to respect foreign key constraints
            context.FileLocks.RemoveRange(context.FileLocks);
            context.TaskFolderProgress.RemoveRange(context.TaskFolderProgress);
            context.Tasks.RemoveRange(context.Tasks);
            context.Nodes.RemoveRange(context.Nodes);
            
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Create a specific test node if it doesn't exist
        /// </summary>
        public static async Task EnsureTestNodeExistsAsync(AppDbContext context, string nodeId, string nodeName = null, string ipAddress = null)
        {
            var existingNode = await context.Nodes.FindAsync(nodeId);
            if (existingNode == null)
            {
                var testNode = new ComputerNode
                {
                    Id = nodeId,
                    Name = nodeName ?? $"Test Node {nodeId}",
                    IpAddress = ipAddress ?? $"192.168.1.{100 + nodeId.GetHashCode() % 150}",
                    HardwareFingerprint = $"TEST-FINGERPRINT-{nodeId}",
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow
                };
                
                context.Nodes.Add(testNode);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Create a test task with proper relationships
        /// </summary>
        public static async Task<BatchTask> CreateTestTaskAsync(AppDbContext context, string taskName = null, TaskType taskType = TaskType.FileProcessing, string assignedNodeId = null)
        {
            // Ensure the assigned node exists
            if (!string.IsNullOrEmpty(assignedNodeId))
            {
                await EnsureTestNodeExistsAsync(context, assignedNodeId);
            }

            var task = new BatchTask
            {
                Name = taskName ?? $"Test Task {Guid.NewGuid().ToString()[..8]}",
                Type = taskType,
                Status = BatchTaskStatus.Pending,
                AssignedNodeId = assignedNodeId,
                CreatedAt = DateTime.UtcNow,
                Parameters = "{\"test\": true}"
            };

            context.Tasks.Add(task);
            await context.SaveChangesAsync();
            return task;
        }
    }
} 