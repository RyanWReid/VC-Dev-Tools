using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCDevTool.API.Services;
using VCDevTool.Shared;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // // [Authorize(Policy = "NodePolicy")] // AUTHENTICATION DISABLED
    public class TasksController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<TasksController> _logger;
        private readonly TaskNotificationService _taskNotificationService;

        public TasksController(
            ITaskService taskService, 
            ILogger<TasksController> logger,
            TaskNotificationService taskNotificationService)
        {
            _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
            _logger = logger;
            _taskNotificationService = taskNotificationService ?? throw new ArgumentNullException(nameof(taskNotificationService));
        }

        [HttpGet]
        public async Task<ActionResult<List<BatchTask>>> GetAllTasks()
        {
            var tasks = await _taskService.GetAllTasksAsync();
            return Ok(tasks);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BatchTask>> GetTask(int id)
        {
            var task = await _taskService.GetTaskByIdAsync(id);
            if (task == null)
            {
                return NotFound();
            }
            
            return Ok(task);
        }

        [HttpPost]
        // [Authorize(Policy = "AdminPolicy")] // Only admins can create tasks
        public async Task<ActionResult<BatchTask>> CreateTask(BatchTask task)
        {
            if (task == null)
            {
                return BadRequest();
            }

            var createdTask = await _taskService.CreateTaskAsync(task);
            
            _logger.LogInformation("Task created by {NodeId}: {TaskId} - {TaskName}", 
                User.FindFirst("node_id")?.Value, createdTask.Id, createdTask.Name);
            
            return CreatedAtAction(nameof(GetTask), new { id = createdTask.Id }, createdTask);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateTaskStatus(int id, [FromBody] TaskStatusUpdateRequest request)
        {
            try
            {
                var task = await _taskService.GetTaskByIdAsync(id);
                if (task == null)
                {
                    return NotFound($"Task with ID {id} not found");
                }

                // Verify the requesting node is assigned to this task or is an admin
                var authenticatedNodeId = User.FindFirst("node_id")?.Value;
                var isAdmin = User.IsInRole("Admin");
                
                if (!isAdmin && task.AssignedNodeId != authenticatedNodeId)
                {
                    // Check if node is in the assigned nodes list
                    var assignedNodeIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(task.AssignedNodeIds) ?? new List<string>();
                    if (!string.IsNullOrEmpty(authenticatedNodeId) && !assignedNodeIds.Contains(authenticatedNodeId))
                    {
                        return Forbid("Cannot update status for task not assigned to this node");
                    }
                }

                // Update the task status
                task.Status = request.Status;
                if (!string.IsNullOrEmpty(request.ResultMessage))
                {
                    task.ResultMessage = request.ResultMessage;
                }

                // Set completed time if the task is being completed or failed
                if (request.Status == BatchTaskStatus.Completed || request.Status == BatchTaskStatus.Failed)
                {
                    task.CompletedAt = DateTime.UtcNow;
                }

                // Update the task using the existing method, including the row version for concurrency check
                try
                {
                    var updatedTask = await _taskService.UpdateTaskStatusAsync(id, request.Status, request.ResultMessage, request.RowVersion);
                    
                    // Broadcast the task status change notification
                    await _taskNotificationService.BroadcastTaskNotificationAsync(
                        updatedTask.Id,
                        updatedTask.AssignedNodeId,
                        updatedTask.Type,
                        updatedTask.Status,
                        request.ResultMessage);

                    return Ok(updatedTask);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Get the current version of the task
                    var currentTask = await _taskService.GetTaskByIdAsync(id);
                    if (currentTask == null)
                    {
                        return NotFound($"Task with ID {id} was deleted by another process");
                    }
                    
                    // Return a conflict response with the current version of the task
                    return Conflict(new
                    {
                        Message = "The task was modified by another process. Please refresh and try again.",
                        CurrentTask = currentTask
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task status for task {TaskId}", id);
                return StatusCode(500, $"Error updating task status: {ex.Message}");
            }
        }

        [HttpPut("{id}/assign/{nodeId}")]
        // [Authorize(Policy = "AdminPolicy")] // Only admins can assign tasks
        public async Task<ActionResult> AssignTask(int id, string nodeId)
        {
            var success = await _taskService.AssignTaskToNodeAsync(id, nodeId);
            if (!success)
            {
                return BadRequest("Failed to assign task to node. Task or node may not exist.");
            }
            
            _logger.LogInformation("Task {TaskId} assigned to node {NodeId} by {AdminNodeId}", 
                id, nodeId, User.FindFirst("node_id")?.Value);
            
            return NoContent();
        }

        // Task Folder Progress endpoints
        [HttpGet("{taskId}/folders")]
        public async Task<ActionResult<List<TaskFolderProgress>>> GetTaskFolders(int taskId)
        {
            var folderProgress = await _taskService.GetTaskFolderProgressAsync(taskId);
            return Ok(folderProgress);
        }

        [HttpPost("{taskId}/folders")]
        public async Task<ActionResult<TaskFolderProgress>> CreateTaskFolder(int taskId, TaskFolderProgress folderProgress)
        {
            if (folderProgress == null)
            {
                return BadRequest("Folder progress data is required");
            }

            // Ensure the task ID matches
            folderProgress.TaskId = taskId;

            try
            {
                var createdFolderProgress = await _taskService.CreateTaskFolderProgressAsync(folderProgress);
                return CreatedAtAction(nameof(GetTaskFolders), new { taskId = taskId }, createdFolderProgress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task folder progress for task {TaskId}", taskId);
                return StatusCode(500, $"Error creating folder progress: {ex.Message}");
            }
        }

        [HttpPut("folders/{folderId}/status")]
        public async Task<ActionResult<TaskFolderProgress>> UpdateTaskFolderStatus(int folderId, [FromBody] TaskFolderStatusUpdateRequest request)
        {
            try
            {
                var updatedFolderProgress = await _taskService.UpdateTaskFolderProgressAsync(
                    folderId,
                    request.Status,
                    request.NodeId,
                    request.NodeName,
                    request.Progress,
                    request.ErrorMessage,
                    request.OutputPath);

                if (updatedFolderProgress.Id == -1)
                {
                    return NotFound($"Task folder progress with ID {folderId} not found");
                }

                return Ok(updatedFolderProgress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task folder progress {FolderId}", folderId);
                return StatusCode(500, $"Error updating folder progress: {ex.Message}");
            }
        }

        [HttpDelete("{taskId}/folders")]
        public async Task<ActionResult> DeleteTaskFolders(int taskId)
        {
            try
            {
                var success = await _taskService.DeleteTaskFolderProgressAsync(taskId);
                if (!success)
                {
                    return StatusCode(500, "Failed to delete task folder progress");
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting task folder progress for task {TaskId}", taskId);
                return StatusCode(500, $"Error deleting folder progress: {ex.Message}");
            }
        }
    }

    public class TaskStatusUpdateRequest
    {
        public BatchTaskStatus Status { get; set; }
        public string? ResultMessage { get; set; }
        public byte[]? RowVersion { get; set; }
    }

    public class TaskFolderStatusUpdateRequest
    {
        public TaskFolderStatus Status { get; set; }
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }
        public double Progress { get; set; } = 0.0;
        public string? ErrorMessage { get; set; }
        public string? OutputPath { get; set; }
    }
} 