using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;
using VCDevTool.Shared;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileLocksController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<FileLocksController> _logger;

        public FileLocksController(ITaskService taskService, ILogger<FileLocksController> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<FileLock>>> GetActiveLocks()
        {
            var locks = await _taskService.GetActiveLocksAsync();
            return Ok(locks);
        }

        [HttpPost("acquire")]
        public async Task<ActionResult> AcquireLock(FileLockRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FilePath) || string.IsNullOrWhiteSpace(request.NodeId))
            {
                return BadRequest("Invalid lock request");
            }

            var success = await _taskService.TryAcquireFileLockAsync(request.FilePath, request.NodeId);
            if (!success)
            {
                return Conflict("File is locked by another node");
            }
            
            return NoContent();
        }

        [HttpPost("release")]
        public async Task<ActionResult> ReleaseLock(FileLockRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FilePath) || string.IsNullOrWhiteSpace(request.NodeId))
            {
                return BadRequest("Invalid lock request");
            }

            var success = await _taskService.ReleaseFileLockAsync(request.FilePath, request.NodeId);
            if (!success)
            {
                return BadRequest("Lock cannot be released. It may be owned by another node.");
            }
            
            return NoContent();
        }
    }

    public class FileLockRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
    }
} 