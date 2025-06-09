using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;

namespace VCDevTool.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly DebugBroadcastService _debugBroadcastService;

        public DebugController(DebugBroadcastService debugBroadcastService)
        {
            _debugBroadcastService = debugBroadcastService; 
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendDebugMessage([FromBody] DebugMessageRequest request)
        {
            if (string.IsNullOrEmpty(request.Message))
            {
                return BadRequest("Message is required");
            }

            await _debugBroadcastService.BroadcastDebugMessageAsync(
                request.Source ?? "Unknown",
                request.Message,
                request.NodeId
            );

            return Ok();
        }
    }

    public class DebugMessageRequest
    {
        public string? Source { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? NodeId { get; set; }
    }
} 