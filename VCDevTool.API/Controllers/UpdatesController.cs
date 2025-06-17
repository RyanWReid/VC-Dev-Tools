using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Net.Mime;
using System.Reflection;

namespace VCDevTool.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UpdatesController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<UpdatesController> _logger;

        public UpdatesController(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<UpdatesController> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult CheckForUpdates([FromQuery] string currentVersion)
        {
            try
            {
                // Parse current version
                if (!Version.TryParse(currentVersion, out var clientVersion))
                {
                    return BadRequest("Invalid version format");
                }

                // Get latest version from configuration
                var latestVersionStr = _configuration["ApplicationUpdates:LatestVersion"];
                if (!Version.TryParse(latestVersionStr, out var latestVersion))
                {
                    _logger.LogError("Invalid latest version format in configuration: {Version}", latestVersionStr);
                    return StatusCode(500, "Server configuration error");
                }

                // Determine if update is needed
                var updateAvailable = latestVersion > clientVersion;
                
                // Get release notes
                var releaseNotes = _configuration["ApplicationUpdates:ReleaseNotes"] ?? string.Empty;

                // Build response
                var result = new
                {
                    LatestVersion = latestVersionStr,
                    UpdateAvailable = updateAvailable,
                    UpdateUrl = $"{Request.Scheme}://{Request.Host}/api/updates/download",
                    ReleaseNotes = releaseNotes
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for updates");
                return StatusCode(500, "An error occurred while checking for updates");
            }
        }

        [HttpGet("download")]
        public IActionResult DownloadUpdate()
        {
            try
            {
                // Get the update package path from configuration
                var updatePackagePath = _configuration["ApplicationUpdates:PackagePath"];
                
                if (string.IsNullOrEmpty(updatePackagePath))
                {
                    _logger.LogError("Update package path not configured");
                    return NotFound("Update package not found");
                }

                // Resolve path (can be relative to content root)
                if (!Path.IsPathRooted(updatePackagePath))
                {
                    updatePackagePath = Path.Combine(_environment.ContentRootPath, updatePackagePath);
                }

                // Check if file exists
                if (!System.IO.File.Exists(updatePackagePath))
                {
                    _logger.LogError("Update package file not found at {Path}", updatePackagePath);
                    return NotFound("Update package not found");
                }

                // Return the file
                var fileStream = new FileStream(updatePackagePath, FileMode.Open, FileAccess.Read);
                return File(fileStream, MediaTypeNames.Application.Octet, "update.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading update package");
                return StatusCode(500, "An error occurred while downloading the update");
            }
        }
        
        [HttpGet("launcher")]
        public IActionResult DownloadLauncher()
        {
            try
            {
                // Get the launcher path from configuration
                var launcherPath = _configuration["ApplicationUpdates:LauncherPath"];
                
                if (string.IsNullOrEmpty(launcherPath))
                {
                    _logger.LogError("Launcher path not configured");
                    return NotFound("Launcher not found");
                }

                // Resolve path (can be relative to content root)
                if (!Path.IsPathRooted(launcherPath))
                {
                    launcherPath = Path.Combine(_environment.ContentRootPath, launcherPath);
                }

                // Check if file exists
                if (!System.IO.File.Exists(launcherPath))
                {
                    _logger.LogError("Launcher file not found at {Path}", launcherPath);
                    return NotFound("Launcher not found");
                }

                // Return the file
                var fileStream = new FileStream(launcherPath, FileMode.Open, FileAccess.Read);
                return File(fileStream, MediaTypeNames.Application.Octet, "VCDevTool.Launcher.exe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading launcher");
                return StatusCode(500, "An error occurred while downloading the launcher");
            }
        }
    }
} 