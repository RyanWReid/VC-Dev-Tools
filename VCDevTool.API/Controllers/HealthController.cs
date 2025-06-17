using Microsoft.AspNetCore.Mvc;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
} 