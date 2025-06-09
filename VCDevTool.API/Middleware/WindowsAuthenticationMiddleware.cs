using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Principal;
using VCDevTool.API.Services;

namespace VCDevTool.API.Middleware
{
    public class WindowsAuthenticationOptions
    {
        public const string Section = "WindowsAuthentication";
        
        public bool Enabled { get; set; } = false;
        public bool EnableKerberosAuthentication { get; set; } = true;
        public bool UseActiveDirectory { get; set; } = true;
        public bool FallbackToJwt { get; set; } = true;
        public bool RequireHttpsForKerberos { get; set; } = true;
        public bool PersistKerberosCredentials { get; set; } = false;
        public bool PersistNtlmCredentials { get; set; } = false;
    }

    public class WindowsAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<WindowsAuthenticationMiddleware> _logger;
        private readonly WindowsAuthenticationOptions _options;
        private readonly IServiceProvider _serviceProvider;

        public WindowsAuthenticationMiddleware(
            RequestDelegate next,
            ILogger<WindowsAuthenticationMiddleware> logger,
            IOptions<WindowsAuthenticationOptions> options,
            IServiceProvider serviceProvider)
        {
            _next = next;
            _logger = logger;
            _options = options.Value;
            _serviceProvider = serviceProvider;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip if Windows Authentication is disabled
            if (!_options.Enabled)
            {
                await _next(context);
                return;
            }

            // Skip if already authenticated
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // Skip for health checks and swagger endpoints
            var path = context.Request.Path.Value?.ToLowerInvariant();
            if (path != null && (path.StartsWith("/health") || path.StartsWith("/swagger") || path.StartsWith("/.well-known")))
            {
                await _next(context);
                return;
            }

            try
            {
                // Check for Windows Authentication
                var windowsIdentity = context.User?.Identity as WindowsIdentity;
                if (windowsIdentity?.IsAuthenticated == true)
                {
                    await ProcessWindowsAuthenticationAsync(context, windowsIdentity);
                }
                else if (_options.FallbackToJwt)
                {
                    // Let JWT authentication handle it
                    _logger.LogDebug("Windows Authentication not available, falling back to JWT");
                }
                else
                {
                    // Windows Authentication required but not available
                    _logger.LogWarning("Windows Authentication required but not available for {Path}", path);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Windows Authentication required");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Windows Authentication middleware");
                
                if (!_options.FallbackToJwt)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("Authentication error");
                    return;
                }
            }

            await _next(context);
        }

        private async Task ProcessWindowsAuthenticationAsync(HttpContext context, WindowsIdentity windowsIdentity)
        {
            try
            {
                // Extract username from Windows identity
                var username = windowsIdentity.Name;
                if (string.IsNullOrWhiteSpace(username))
                {
                    _logger.LogWarning("Windows identity has no name");
                    return;
                }

                _logger.LogInformation("Processing Windows Authentication for user: {Username}", username);

                // Create scope for services
                using var scope = _serviceProvider.CreateScope();
                var adService = scope.ServiceProvider.GetService<IActiveDirectoryService>();

                if (adService == null || !_options.UseActiveDirectory)
                {
                    // Create basic claims without AD lookup
                    await CreateBasicWindowsIdentityAsync(context, windowsIdentity);
                    return;
                }

                // Get user information from Active Directory
                var userInfo = await adService.GetUserInfoAsync(username);
                if (userInfo == null)
                {
                    _logger.LogWarning("User {Username} not found in Active Directory", username);
                    
                    // Create basic identity if AD lookup fails
                    await CreateBasicWindowsIdentityAsync(context, windowsIdentity);
                    return;
                }

                // Create enriched claims identity
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userInfo.Username),
                    new Claim(ClaimTypes.NameIdentifier, userInfo.Sid),
                    new Claim(ClaimTypes.WindowsAccountName, username),
                    new Claim("DisplayName", userInfo.DisplayName),
                    new Claim(ClaimTypes.Email, userInfo.Email),
                    new Claim("Department", userInfo.Department),
                    new Claim(ClaimTypes.AuthenticationMethod, "Windows"),
                    new Claim("AuthenticationTime", DateTimeOffset.UtcNow.ToString())
                };

                // Add role claims
                foreach (var role in userInfo.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                // Add group claims
                foreach (var group in userInfo.Groups)
                {
                    claims.Add(new Claim("Groups", group));
                }

                // Add Windows-specific claims
                if (windowsIdentity.User != null)
                {
                    claims.Add(new Claim(ClaimTypes.Sid, windowsIdentity.User.ToString()));
                }

                if (windowsIdentity.AuthenticationType != null)
                {
                    claims.Add(new Claim("WindowsAuthenticationType", windowsIdentity.AuthenticationType));
                }

                // Create new identity with enriched claims
                var claimsIdentity = new ClaimsIdentity(claims, "Windows");
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                // Replace the context user
                context.User = claimsPrincipal;

                _logger.LogInformation("Successfully enriched Windows Authentication for user {Username} with {RoleCount} roles and {GroupCount} groups",
                    username, userInfo.Roles.Count, userInfo.Groups.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Windows Authentication for user {Username}", windowsIdentity.Name);
                
                // Fallback to basic Windows identity
                await CreateBasicWindowsIdentityAsync(context, windowsIdentity);
            }
        }

        private async Task CreateBasicWindowsIdentityAsync(HttpContext context, WindowsIdentity windowsIdentity)
        {
            var username = windowsIdentity.Name ?? "Unknown";
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.WindowsAccountName, username),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows"),
                new Claim("AuthenticationTime", DateTimeOffset.UtcNow.ToString())
            };

            if (windowsIdentity.User != null)
            {
                claims.Add(new Claim(ClaimTypes.Sid, windowsIdentity.User.ToString()));
                claims.Add(new Claim(ClaimTypes.NameIdentifier, windowsIdentity.User.ToString()));
            }

            if (windowsIdentity.AuthenticationType != null)
            {
                claims.Add(new Claim("WindowsAuthenticationType", windowsIdentity.AuthenticationType));
            }

            // Add basic role based on group membership
            if (windowsIdentity.Groups?.Any() == true)
            {
                claims.Add(new Claim(ClaimTypes.Role, "User"));
            }

            var claimsIdentity = new ClaimsIdentity(claims, "Windows");
            context.User = new ClaimsPrincipal(claimsIdentity);

            _logger.LogInformation("Created basic Windows identity for user {Username}", username);
            
            await Task.CompletedTask;
        }
    }

    public static class WindowsAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseWindowsAuthenticationEnrichment(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<WindowsAuthenticationMiddleware>();
        }
    }
} 