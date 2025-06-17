using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace VCDevTool.API.Services
{
    public class ActiveDirectoryOptions
    {
        public const string Section = "ActiveDirectory";
        
        public string Domain { get; set; } = string.Empty;
        public string[] AdminGroups { get; set; } = Array.Empty<string>();
        public string[] UserGroups { get; set; } = Array.Empty<string>();
        public string[] NodeGroups { get; set; } = Array.Empty<string>();
        public string LdapPath { get; set; } = string.Empty;
        public string ServiceAccountUsername { get; set; } = string.Empty;
        public string ServiceAccountPassword { get; set; } = string.Empty;
        public bool EnableGroupCaching { get; set; } = true;
        public int GroupCacheExpirationMinutes { get; set; } = 30;
        public bool UseKerberos { get; set; } = true;
        public string[] ComputerAccountOUs { get; set; } = Array.Empty<string>();
    }

    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public List<string> Groups { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public string Sid { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    public class ComputerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string DnsHostName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime LastLogon { get; set; }
        public List<string> Groups { get; set; } = new();
    }

    public interface IActiveDirectoryService
    {
        Task<UserInfo?> GetUserInfoAsync(string username);
        Task<UserInfo?> GetUserInfoBySidAsync(string sid);
        Task<bool> IsUserInGroupAsync(string username, string groupName);
        Task<List<string>> GetUserGroupsAsync(string username);
        Task<List<string>> GetUserRolesAsync(string username);
        Task<ComputerInfo?> GetComputerInfoAsync(string computerName);
        Task<bool> IsComputerInGroupAsync(string computerName, string groupName);
        Task<bool> ValidateUserCredentialsAsync(string username, string password);
        Task<bool> IsUserEnabledAsync(string username);
        Task<ClaimsIdentity> CreateClaimsIdentityAsync(string username);
        void ClearCache();
    }

    public class ActiveDirectoryService : IActiveDirectoryService
    {
        private readonly ActiveDirectoryOptions _options;
        private readonly ILogger<ActiveDirectoryService> _logger;
        private readonly MemoryCache _cache;
        private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

        public ActiveDirectoryService(
            IOptions<ActiveDirectoryOptions> options,
            ILogger<ActiveDirectoryService> logger)
        {
            _options = options.Value;
            _logger = logger;
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 10000 // Limit cache size
            });
        }

        public async Task<UserInfo?> GetUserInfoAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            // Remove domain prefix if present
            var cleanUsername = username.Contains('\\') ? username.Split('\\')[1] : username;
            
            var cacheKey = $"user_{cleanUsername}";
            
            if (_options.EnableGroupCaching && _cache.TryGetValue(cacheKey, out UserInfo? cachedUser))
            {
                _logger.LogDebug("Retrieved user info from cache for {Username}", cleanUsername);
                return cachedUser;
            }

            try
            {
                using var context = new PrincipalContext(ContextType.Domain, _options.Domain);
                using var userPrincipal = UserPrincipal.FindByIdentity(context, cleanUsername);
                
                if (userPrincipal == null)
                {
                    _logger.LogWarning("User {Username} not found in Active Directory", cleanUsername);
                    return null;
                }

                var userInfo = new UserInfo
                {
                    Username = userPrincipal.SamAccountName ?? cleanUsername,
                    DisplayName = userPrincipal.DisplayName ?? string.Empty,
                    Email = userPrincipal.EmailAddress ?? string.Empty,
                    Sid = userPrincipal.Sid?.ToString() ?? string.Empty,
                    IsEnabled = userPrincipal.Enabled ?? false
                };

                // Get additional properties using DirectoryEntry
                if (userPrincipal.GetUnderlyingObject() is DirectoryEntry directoryEntry)
                {
                    userInfo.Department = directoryEntry.Properties["department"]?.Value?.ToString() ?? string.Empty;
                }

                // Get group memberships
                userInfo.Groups = await GetUserGroupsAsync(cleanUsername);
                userInfo.Roles = await GetUserRolesAsync(cleanUsername);

                // Cache the result
                if (_options.EnableGroupCaching)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.GroupCacheExpirationMinutes),
                        Size = 1
                    };
                    _cache.Set(cacheKey, userInfo, cacheOptions);
                }

                _logger.LogInformation("Retrieved user info for {Username} with {GroupCount} groups", 
                    cleanUsername, userInfo.Groups.Count);
                
                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user info for {Username}", cleanUsername);
                return null;
            }
        }

        public async Task<UserInfo?> GetUserInfoBySidAsync(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid))
                return null;

            try
            {
                using var context = new PrincipalContext(ContextType.Domain, _options.Domain);
                using var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
                
                if (userPrincipal?.SamAccountName == null)
                    return null;

                return await GetUserInfoAsync(userPrincipal.SamAccountName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user info by SID {Sid}", sid);
                return null;
            }
        }

        public async Task<bool> IsUserInGroupAsync(string username, string groupName)
        {
            var groups = await GetUserGroupsAsync(username);
            return groups.Contains(groupName, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<List<string>> GetUserGroupsAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return new List<string>();

            var cleanUsername = username.Contains('\\') ? username.Split('\\')[1] : username;
            var cacheKey = $"groups_{cleanUsername}";

            if (_options.EnableGroupCaching && _cache.TryGetValue(cacheKey, out List<string>? cachedGroups))
            {
                return cachedGroups ?? new List<string>();
            }

            try
            {
                await _cacheSemaphore.WaitAsync();
                
                // Double-check cache after acquiring lock
                if (_options.EnableGroupCaching && _cache.TryGetValue(cacheKey, out cachedGroups))
                {
                    return cachedGroups ?? new List<string>();
                }

                using var context = new PrincipalContext(ContextType.Domain, _options.Domain);
                using var userPrincipal = UserPrincipal.FindByIdentity(context, cleanUsername);
                
                if (userPrincipal == null)
                    return new List<string>();

                var groups = new List<string>();
                var authorizationGroups = userPrincipal.GetAuthorizationGroups();
                
                foreach (var group in authorizationGroups)
                {
                    if (group is GroupPrincipal groupPrincipal && !string.IsNullOrWhiteSpace(groupPrincipal.Name))
                    {
                        groups.Add(groupPrincipal.Name);
                    }
                }

                // Cache the result
                if (_options.EnableGroupCaching)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.GroupCacheExpirationMinutes),
                        Size = 1
                    };
                    _cache.Set(cacheKey, groups, cacheOptions);
                }

                _logger.LogDebug("Retrieved {GroupCount} groups for user {Username}", groups.Count, cleanUsername);
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving groups for user {Username}", cleanUsername);
                return new List<string>();
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        public async Task<List<string>> GetUserRolesAsync(string username)
        {
            var groups = await GetUserGroupsAsync(username);
            var roles = new List<string>();

            // Map AD groups to application roles
            foreach (var group in groups)
            {
                if (_options.AdminGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    roles.Add("Admin");
                }
                else if (_options.UserGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    roles.Add("User");
                }
                else if (_options.NodeGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    roles.Add("Node");
                }
            }

            // Remove duplicates
            return roles.Distinct().ToList();
        }

        public async Task<ComputerInfo?> GetComputerInfoAsync(string computerName)
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return null;

            // Clean computer name (remove domain and trailing $)
            var cleanName = computerName.Contains('\\') ? computerName.Split('\\')[1] : computerName;
            cleanName = cleanName.TrimEnd('$');

            try
            {
                using var context = new PrincipalContext(ContextType.Domain, _options.Domain);
                using var computerPrincipal = ComputerPrincipal.FindByIdentity(context, cleanName);
                
                if (computerPrincipal == null)
                    return null;

                var computerInfo = new ComputerInfo
                {
                    Name = computerPrincipal.Name ?? cleanName,
                    DistinguishedName = computerPrincipal.DistinguishedName ?? string.Empty,
                    IsEnabled = computerPrincipal.Enabled ?? false,
                    LastLogon = computerPrincipal.LastLogon ?? DateTime.MinValue
                };

                // Get additional properties
                if (computerPrincipal.GetUnderlyingObject() is DirectoryEntry directoryEntry)
                {
                    computerInfo.OperatingSystem = directoryEntry.Properties["operatingSystem"]?.Value?.ToString() ?? string.Empty;
                    computerInfo.DnsHostName = directoryEntry.Properties["dNSHostName"]?.Value?.ToString() ?? string.Empty;
                }

                // Get group memberships for computer
                var groups = new List<string>();
                
                // ComputerPrincipal doesn't have GetAuthorizationGroups, use Groups property instead
                foreach (var group in computerPrincipal.GetGroups())
                {
                    if (group is GroupPrincipal groupPrincipal && !string.IsNullOrWhiteSpace(groupPrincipal.Name))
                    {
                        groups.Add(groupPrincipal.Name);
                    }
                }
                
                computerInfo.Groups = groups;

                return computerInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving computer info for {ComputerName}", cleanName);
                return null;
            }
        }

        public async Task<bool> IsComputerInGroupAsync(string computerName, string groupName)
        {
            var computerInfo = await GetComputerInfoAsync(computerName);
            return computerInfo?.Groups.Contains(groupName, StringComparer.OrdinalIgnoreCase) ?? false;
        }

        public async Task<bool> ValidateUserCredentialsAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            try
            {
                using var context = new PrincipalContext(ContextType.Domain, _options.Domain);
                return await Task.FromResult(context.ValidateCredentials(username, password));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating credentials for user {Username}", username);
                return false;
            }
        }

        public async Task<bool> IsUserEnabledAsync(string username)
        {
            var userInfo = await GetUserInfoAsync(username);
            return userInfo?.IsEnabled ?? false;
        }

        public async Task<ClaimsIdentity> CreateClaimsIdentityAsync(string username)
        {
            var userInfo = await GetUserInfoAsync(username);
            if (userInfo == null)
                throw new InvalidOperationException($"User {username} not found in Active Directory");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userInfo.Username),
                new Claim(ClaimTypes.NameIdentifier, userInfo.Sid),
                new Claim("DisplayName", userInfo.DisplayName),
                new Claim(ClaimTypes.Email, userInfo.Email),
                new Claim("Department", userInfo.Department)
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

            return new ClaimsIdentity(claims, "Windows");
        }

        public void ClearCache()
        {
            _cache.Clear();
            _logger.LogInformation("Active Directory cache cleared");
        }

        public void Dispose()
        {
            _cache?.Dispose();
            _cacheSemaphore?.Dispose();
        }
    }
} 