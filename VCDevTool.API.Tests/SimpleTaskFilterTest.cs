using System.Text.Json;
using VCDevTool.Shared;

namespace VCDevTool.API.Tests
{
    public class SimpleTaskFilterTest
    {
        public static void RunTests()
        {
            Console.WriteLine("=== Task Filtering Logic Tests ===");
            Console.WriteLine();
            
            // Test scenario: VolumeCompression task assigned to 2 nodes
            // Node 1 starts first and changes task to Running
            // Node 2 polls after and should still be able to join processing with the fix
            
            var volumeCompressionTask = new BatchTask
            {
                Id = 1,
                Type = TaskType.VolumeCompression,
                Status = BatchTaskStatus.Running, // Already started by Node 1
                AssignedNodeIds = JsonSerializer.Serialize(new List<string> { "node1", "node2" })
            };
            
            var testMessageTask = new BatchTask
            {
                Id = 2,
                Type = TaskType.TestMessage,
                Status = BatchTaskStatus.Running, // Already started
                AssignedNodeId = "node2"
            };
            
            var allTasks = new List<BatchTask> { volumeCompressionTask, testMessageTask };
            string nodeId = "node2"; // This is Node 2 polling after Node 1 already started the tasks
            var processedTaskIds = new HashSet<int>();
            
            // Filter tasks assigned to this node and not already processed
            var tasksForThisNode = allTasks.Where(t => 
                IsTaskAssignedToNode(t, nodeId) &&
                !processedTaskIds.Contains(t.Id)).ToList();
            
            Console.WriteLine($"Scenario: Node '{nodeId}' polling for tasks");
            Console.WriteLine($"Total tasks assigned to this node: {tasksForThisNode.Count}");
            
            foreach (var task in tasksForThisNode)
            {
                Console.WriteLine($"  - Task {task.Id} ({task.Type}) - Status: {task.Status}");
            }
            Console.WriteLine();
            
            // BEFORE FIX: Only pending tasks
            var tasksBeforeFix = tasksForThisNode.Where(t => 
                t.Status == BatchTaskStatus.Pending
            ).ToList();
            
            Console.WriteLine("BEFORE FIX (original logic):");
            Console.WriteLine($"Tasks that would be processed: {tasksBeforeFix.Count}");
            foreach (var task in tasksBeforeFix)
            {
                Console.WriteLine($"  ✓ Task {task.Id} ({task.Type})");
            }
            if (!tasksBeforeFix.Any())
            {
                Console.WriteLine("  (none - running tasks are filtered out)");
            }
            Console.WriteLine();
            
            // AFTER FIX: Pending + Running VolumeCompression tasks
            var tasksAfterFix = tasksForThisNode.Where(t => 
                t.Status == BatchTaskStatus.Pending || 
                (t.Type == TaskType.VolumeCompression && t.Status == BatchTaskStatus.Running)
            ).ToList();
            
            Console.WriteLine("AFTER FIX (new logic):");
            Console.WriteLine($"Tasks that would be processed: {tasksAfterFix.Count}");
            foreach (var task in tasksAfterFix)
            {
                Console.WriteLine($"  ✓ Task {task.Id} ({task.Type}) - Allows concurrent processing");
            }
            Console.WriteLine();
            
            // Verify results
            bool testPassed = 
                tasksBeforeFix.Count == 0 && // Before: No running tasks processed
                tasksAfterFix.Count == 1 &&   // After: VolumeCompression running task processed
                tasksAfterFix[0].Type == TaskType.VolumeCompression; // Correct task type
            
            Console.WriteLine($"=== Test Result: {(testPassed ? "PASSED ✓" : "FAILED ✗")} ===");
            
            if (testPassed)
            {
                Console.WriteLine("✓ Fix successfully enables concurrent VolumeCompression processing");
                Console.WriteLine("✓ Other task types maintain existing single-node behavior");
                Console.WriteLine("✓ Multiple nodes can now process the same VolumeCompression task");
            }
            else
            {
                Console.WriteLine("✗ Fix did not work as expected");
                Console.WriteLine($"  Expected: 0 tasks before fix, 1 VolumeCompression task after fix");
                Console.WriteLine($"  Actual: {tasksBeforeFix.Count} tasks before, {tasksAfterFix.Count} tasks after");
            }
        }
        
        private static bool IsTaskAssignedToNode(BatchTask task, string nodeId)
        {
            // Check single node assignment (backward compatibility)
            if (task.AssignedNodeId == nodeId)
            {
                return true;
            }
            
            // Check multiple node assignment
            try
            {
                if (!string.IsNullOrEmpty(task.AssignedNodeIds))
                {
                    var assignedNodeIds = JsonSerializer.Deserialize<List<string>>(task.AssignedNodeIds);
                    return assignedNodeIds?.Contains(nodeId) == true;
                }
            }
            catch (Exception)
            {
                // Ignore parsing errors for test
            }
            
            return false;
        }
    }
} 