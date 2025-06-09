using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCDevTool.API.Services;
using VCDevTool.API.Models;
using VCDevTool.Shared;
using FluentValidation;

namespace VCDevTool.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IJwtService _jwtService;
        private readonly ITaskService _taskService;
        private readonly ILogger<AuthController> _logger;
        private readonly IValidator<RegisterNodeRequest> _registerValidator;
        private readonly IValidator<LoginRequest> _loginValidator;

        public AuthController(
            IJwtService jwtService, 
            ITaskService taskService, 
            ILogger<AuthController> logger,
            IValidator<RegisterNodeRequest> registerValidator,
            IValidator<LoginRequest> loginValidator)
        {
            _jwtService = jwtService;
            _taskService = taskService;
            _logger = logger;
            _registerValidator = registerValidator;
            _loginValidator = loginValidator;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> RegisterNode([FromBody] RegisterNodeRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest("Invalid registration request");
                }

                // Validate the request using FluentValidation
                var validationResult = await _registerValidator.ValidateAsync(request);
                if (!validationResult.IsValid)
                {
                    var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                    return BadRequest(new { message = "Validation failed", errors });
                }

                // Check if node already exists
                var existingNodes = await _taskService.GetAllNodesAsync();
                var existingNode = existingNodes.FirstOrDefault(n => n.Id == request.Id);
                
                if (existingNode != null)
                {
                    return Conflict("Node already registered");
                }

                // Create the node
                var node = new ComputerNode
                {
                    Id = request.Id,
                    Name = request.Name,
                    IpAddress = request.IpAddress ?? GetClientIpAddress(),
                    HardwareFingerprint = request.HardwareFingerprint ?? string.Empty,
                    IsAvailable = true,
                    LastHeartbeat = DateTime.UtcNow
                };

                var registeredNode = await _taskService.RegisterNodeAsync(node);

                // Determine roles based on node type or configuration
                var roles = new List<string> { "Node" };

                // Generate JWT token
                var token = _jwtService.GenerateToken(registeredNode.Id, registeredNode.Name, roles);

                // Generate API key for simplified authentication
                var apiKey = _jwtService.GenerateApiKey(registeredNode.Id);

                _logger.LogInformation("Node registered successfully: {NodeId} - {NodeName} from IP {IpAddress}", 
                    registeredNode.Id, registeredNode.Name, node.IpAddress);

                return Created($"/api/nodes/{registeredNode.Id}", new AuthResponse
                {
                    Token = token,
                    ApiKey = apiKey,
                    NodeId = registeredNode.Id,
                    NodeName = registeredNode.Name,
                    ExpiresAt = DateTime.UtcNow.AddHours(24),
                    Roles = roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering node: {NodeId}", request?.Id);
                return StatusCode(500, "Error registering node");
            }
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest("Invalid login request");
                }

                // Validate the request using FluentValidation
                var validationResult = await _loginValidator.ValidateAsync(request);
                if (!validationResult.IsValid)
                {
                    var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                    return BadRequest(new { message = "Validation failed", errors });
                }

                // Verify the node exists and validate hardware fingerprint
                var nodes = await _taskService.GetAllNodesAsync();
                var node = nodes.FirstOrDefault(n => n.Id == request.NodeId);

                if (node == null)
                {
                    _logger.LogWarning("Login attempt for non-existent node: {NodeId}", request.NodeId);
                    return Unauthorized("Invalid node credentials");
                }

                // Validate hardware fingerprint if provided
                if (!string.IsNullOrEmpty(request.HardwareFingerprint) && 
                    !string.IsNullOrEmpty(node.HardwareFingerprint) && 
                    node.HardwareFingerprint != request.HardwareFingerprint)
                {
                    _logger.LogWarning("Login attempt with invalid hardware fingerprint for node: {NodeId}", request.NodeId);
                    return Unauthorized("Invalid node credentials");
                }

                // Update last heartbeat
                node.LastHeartbeat = DateTime.UtcNow;
                node.IsAvailable = true;
                await _taskService.RegisterNodeAsync(node); // This will update existing node

                // Determine roles
                var roles = new List<string> { "Node" };

                // Generate new token
                var token = _jwtService.GenerateToken(node.Id, node.Name, roles);
                var apiKey = _jwtService.GenerateApiKey(node.Id);

                _logger.LogInformation("Node logged in successfully: {NodeId} - {NodeName}", node.Id, node.Name);

                return Created($"/api/auth/session/{node.Id}", new AuthResponse
                {
                    Token = token,
                    ApiKey = apiKey,
                    NodeId = node.Id,
                    NodeName = node.Name,
                    ExpiresAt = DateTime.UtcNow.AddHours(24),
                    Roles = roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging in node: {NodeId}", request?.NodeId);
                return StatusCode(500, "Error logging in");
            }
        }

        [HttpPost("refresh")]
        [Authorize]
        public ActionResult<AuthResponse> RefreshToken()
        {
            try
            {
                var nodeId = User.FindFirst("node_id")?.Value;
                var nodeName = User.FindFirst("node_name")?.Value;

                if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(nodeName))
                {
                    return Unauthorized("Invalid token claims");
                }

                // Get current roles
                var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();

                // Generate new token
                var token = _jwtService.GenerateToken(nodeId, nodeName, roles);
                var apiKey = _jwtService.GenerateApiKey(nodeId);

                return Ok(new AuthResponse
                {
                    Token = token,
                    ApiKey = apiKey,
                    NodeId = nodeId,
                    NodeName = nodeName,
                    ExpiresAt = DateTime.UtcNow.AddHours(24),
                    Roles = roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, "Error refreshing token");
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public ActionResult Logout()
        {
            // In a real implementation, you might want to blacklist the token
            var nodeId = User.FindFirst("node_id")?.Value;
            _logger.LogInformation("Node logged out: {NodeId}", nodeId);
            
            return Ok(new { message = "Logged out successfully" });
        }

        private string GetClientIpAddress()
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            
            // Check for forwarded IP in case of proxy/load balancer
            if (HttpContext.Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                ipAddress = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
            }
            else if (HttpContext.Request.Headers.ContainsKey("X-Real-IP"))
            {
                ipAddress = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
            }

            return ipAddress ?? "Unknown";
        }
    }

    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public List<string> Roles { get; set; } = new();
    }
} 