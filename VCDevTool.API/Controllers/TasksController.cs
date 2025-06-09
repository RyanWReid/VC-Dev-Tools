using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;
using VCDevTool.Shared;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TasksController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(ITaskService taskService, ILogger<TasksController> logger)
        {
            _taskService = taskService;
            _logger = logger;
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
        public async Task<ActionResult<BatchTask>> CreateTask(BatchTask task)
        {
            if (task == null)
            {
                return BadRequest();
            }

            var createdTask = await _taskService.CreateTaskAsync(task);
            return CreatedAtAction(nameof(GetTask), new { id = createdTask.Id }, createdTask);
        }

        [HttpPut("{id}/status")]
        public async Task<ActionResult<BatchTask>> UpdateTaskStatus(int id, [FromBody] TaskStatusUpdateRequest request)
        {
            var task = await _taskService.UpdateTaskStatusAsync(id, request.Status, request.ResultMessage);
            if (task == null)
            {
                return NotFound();
            }
            
            return Ok(task);
        }

        [HttpPut("{id}/assign/{nodeId}")]
        public async Task<ActionResult> AssignTask(int id, string nodeId)
        {
            var success = await _taskService.AssignTaskToNodeAsync(id, nodeId);
            if (!success)
            {
                return BadRequest("Failed to assign task to node. Task or node may not exist.");
            }
            
            return NoContent();
        }
    }

    public class TaskStatusUpdateRequest
    {
        public BatchTaskStatus Status { get; set; }
        public string? ResultMessage { get; set; }
    }
} 