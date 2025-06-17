using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using VCDevTool.API.Services;

namespace VCDevTool.API.Authorization
{
    /// <summary>
    /// Authorization requirement for Active Directory group membership
    /// </summary>
    public class ADGroupRequirement : IAuthorizationRequirement
    {
        public string[] AllowedGroups { get; }
        public bool RequireAll { get; }

        public ADGroupRequirement(string[] allowedGroups, bool requireAll = false)
        {
            AllowedGroups = allowedGroups ?? throw new ArgumentNullException(nameof(allowedGroups));
            RequireAll = requireAll;
        }

        public ADGroupRequirement(string allowedGroup) : this(new[] { allowedGroup })
        {
        }
    }

    /// <summary>
    /// Authorization handler for Active Directory group requirements
    /// </summary>
    public class ADGroupAuthorizationHandler : AuthorizationHandler<ADGroupRequirement>
    {
        private readonly IActiveDirectoryService _adService;
        private readonly ILogger<ADGroupAuthorizationHandler> _logger;
        private readonly ActiveDirectoryOptions _adOptions;

        public ADGroupAuthorizationHandler(
            IActiveDirectoryService adService,
            ILogger<ADGroupAuthorizationHandler> logger,
            IOptions<ActiveDirectoryOptions> adOptions)
        {
            _adService = adService;
            _logger = logger;
            _adOptions = adOptions.Value;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ADGroupRequirement requirement)
        {
            // Get the user's identity
            if (context.User?.Identity?.Name == null)
            {
                _logger.LogWarning("Authorization failed: No user identity found");
                context.Fail();
                return;
            }

            var username = context.User.Identity.Name;
            
            try
            {
                // Check if user groups are already cached in claims
                var groupClaims = context.User.FindAll("Groups").Select(c => c.Value).ToList();
                
                List<string> userGroups;
                if (groupClaims.Any())
                {
                    // Use cached groups from claims
                    userGroups = groupClaims;
                    _logger.LogDebug("Using cached groups from claims for user {Username}: {Groups}", 
                        username, string.Join(", ", userGroups));
                }
                else
                {
                    // Retrieve groups from Active Directory
                    userGroups = await _adService.GetUserGroupsAsync(username);
                    _logger.LogDebug("Retrieved {GroupCount} groups from AD for user {Username}", 
                        userGroups.Count, username);
                }

                // Check group membership
                bool isAuthorized = requirement.RequireAll
                    ? requirement.AllowedGroups.All(group => 
                        userGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                    : requirement.AllowedGroups.Any(group => 
                        userGroups.Contains(group, StringComparer.OrdinalIgnoreCase));

                if (isAuthorized)
                {
                    _logger.LogInformation("User {Username} authorized via AD group membership", username);
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogWarning("User {Username} not authorized. Required groups: {RequiredGroups}, User groups: {UserGroups}", 
                        username, string.Join(", ", requirement.AllowedGroups), string.Join(", ", userGroups));
                    context.Fail();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during AD group authorization for user {Username}", username);
                context.Fail();
            }
        }
    }

    /// <summary>
    /// Authorization requirement for specific roles with AD group mapping
    /// </summary>
    public class ADRoleRequirement : IAuthorizationRequirement
    {
        public string[] RequiredRoles { get; }
        public bool RequireAll { get; }

        public ADRoleRequirement(string[] requiredRoles, bool requireAll = false)
        {
            RequiredRoles = requiredRoles ?? throw new ArgumentNullException(nameof(requiredRoles));
            RequireAll = requireAll;
        }

        public ADRoleRequirement(string requiredRole) : this(new[] { requiredRole })
        {
        }
    }

    /// <summary>
    /// Authorization handler for role-based requirements with AD group mapping
    /// </summary>
    public class ADRoleAuthorizationHandler : AuthorizationHandler<ADRoleRequirement>
    {
        private readonly IActiveDirectoryService _adService;
        private readonly ILogger<ADRoleAuthorizationHandler> _logger;

        public ADRoleAuthorizationHandler(
            IActiveDirectoryService adService,
            ILogger<ADRoleAuthorizationHandler> logger)
        {
            _adService = adService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ADRoleRequirement requirement)
        {
            if (context.User?.Identity?.Name == null)
            {
                _logger.LogWarning("Authorization failed: No user identity found");
                context.Fail();
                return;
            }

            var username = context.User.Identity.Name;

            try
            {
                // Check if roles are already cached in claims
                var roleClaims = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
                
                List<string> userRoles;
                if (roleClaims.Any())
                {
                    userRoles = roleClaims;
                    _logger.LogDebug("Using cached roles from claims for user {Username}: {Roles}", 
                        username, string.Join(", ", userRoles));
                }
                else
                {
                    // Get roles from Active Directory
                    userRoles = await _adService.GetUserRolesAsync(username);
                    _logger.LogDebug("Retrieved {RoleCount} roles from AD for user {Username}", 
                        userRoles.Count, username);
                }

                // Check role membership
                bool isAuthorized = requirement.RequireAll
                    ? requirement.RequiredRoles.All(role => 
                        userRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    : requirement.RequiredRoles.Any(role => 
                        userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

                if (isAuthorized)
                {
                    _logger.LogInformation("User {Username} authorized via AD role membership", username);
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogWarning("User {Username} not authorized. Required roles: {RequiredRoles}, User roles: {UserRoles}", 
                        username, string.Join(", ", requirement.RequiredRoles), string.Join(", ", userRoles));
                    context.Fail();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during AD role authorization for user {Username}", username);
                context.Fail();
            }
        }
    }

    /// <summary>
    /// Authorization requirement for computer accounts
    /// </summary>
    public class ComputerAccountRequirement : IAuthorizationRequirement
    {
        public string[] AllowedComputerGroups { get; }

        public ComputerAccountRequirement(string[] allowedComputerGroups)
        {
            AllowedComputerGroups = allowedComputerGroups ?? throw new ArgumentNullException(nameof(allowedComputerGroups));
        }
    }

    /// <summary>
    /// Authorization handler for computer account requirements
    /// </summary>
    public class ComputerAccountAuthorizationHandler : AuthorizationHandler<ComputerAccountRequirement>
    {
        private readonly IActiveDirectoryService _adService;
        private readonly ILogger<ComputerAccountAuthorizationHandler> _logger;

        public ComputerAccountAuthorizationHandler(
            IActiveDirectoryService adService,
            ILogger<ComputerAccountAuthorizationHandler> logger)
        {
            _adService = adService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ComputerAccountRequirement requirement)
        {
            var computerName = context.User?.Identity?.Name;
            
            if (string.IsNullOrWhiteSpace(computerName))
            {
                _logger.LogWarning("Computer account authorization failed: No computer identity found");
                context.Fail();
                return;
            }

            try
            {
                var computerInfo = await _adService.GetComputerInfoAsync(computerName);
                
                if (computerInfo == null || !computerInfo.IsEnabled)
                {
                    _logger.LogWarning("Computer account {ComputerName} not found or disabled in AD", computerName);
                    context.Fail();
                    return;
                }

                // Check if computer is in allowed groups
                bool isAuthorized = requirement.AllowedComputerGroups.Any(group =>
                    computerInfo.Groups.Contains(group, StringComparer.OrdinalIgnoreCase));

                if (isAuthorized)
                {
                    _logger.LogInformation("Computer account {ComputerName} authorized", computerName);
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogWarning("Computer account {ComputerName} not in allowed groups. Required: {RequiredGroups}, Computer groups: {ComputerGroups}",
                        computerName, string.Join(", ", requirement.AllowedComputerGroups), string.Join(", ", computerInfo.Groups));
                    context.Fail();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during computer account authorization for {ComputerName}", computerName);
                context.Fail();
            }
        }
    }
} 