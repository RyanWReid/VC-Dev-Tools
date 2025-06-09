using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;
using VCDevTool.Shared;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NodesController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<NodesController> _logger;

        public NodesController(ITaskService taskService, ILogger<NodesController> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<ComputerNode>>> GetAvailableNodes()
        {
            var nodes = await _taskService.GetAvailableNodesAsync();
            return Ok(nodes);
        }

        [HttpPost("register")]
        public async Task<ActionResult<ComputerNode>> RegisterNode(ComputerNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Id))
            {
                return BadRequest("Invalid node data");
            }

            var registeredNode = await _taskService.RegisterNodeAsync(node);
            return Ok(registeredNode);
        }

        [HttpPost("{nodeId}/heartbeat")]
        public async Task<ActionResult> UpdateHeartbeat(string nodeId)
        {
            var success = await _taskService.UpdateNodeHeartbeatAsync(nodeId);
            if (!success)
            {
                return NotFound("Node not found");
            }
            
            return NoContent();
        }

        [HttpGet("all")]
        public async Task<ActionResult<List<ComputerNode>>> GetAllNodes()
        {
            var nodes = await _taskService.GetAllNodesAsync();
            return Ok(nodes);
        }
    }
} 