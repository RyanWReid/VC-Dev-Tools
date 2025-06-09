# Windows Authentication Implementation Guide for VCDevTool

## Overview

This document provides a comprehensive guide for implementing Windows Authentication in the VCDevTool system according to industry standards and enterprise best practices. The implementation supports both Windows Authentication (Kerberos/NTLM) and JWT fallback for API clients.

## Architecture Components

### 1. Active Directory Service (`VCDevTool.API/Services/ActiveDirectoryService.cs`)
- **Purpose**: Handles AD integration, user/group lookups, and computer account management
- **Features**: 
  - User information retrieval with caching
  - Group membership validation
  - Computer account verification
  - Role mapping from AD groups
  - Claims identity creation

### 2. Authorization Handlers (`VCDevTool.API/Authorization/ADGroupAuthorizationHandler.cs`)
- **ADGroupAuthorizationHandler**: Validates AD group membership
- **ADRoleAuthorizationHandler**: Maps AD groups to application roles
- **ComputerAccountAuthorizationHandler**: Validates computer accounts

### 3. Windows Authentication Middleware (`VCDevTool.API/Middleware/WindowsAuthenticationMiddleware.cs`)
- **Purpose**: Enriches Windows identity with AD claims
- **Features**:
  - Automatic AD lookup for authenticated users
  - Claims enrichment with roles and groups
  - Fallback to basic Windows identity if AD unavailable

### 4. Enhanced Data Models
- **ComputerNode**: Extended with AD properties (DN, OU, groups, etc.)
- **Database Schema**: Optimized indexes for AD queries

## Configuration

### appsettings.json Configuration

```json
{
  "ActiveDirectory": {
    "Domain": "company.local",
    "AdminGroups": [
      "VCDevTool_Administrators",
      "Domain Admins",
      "IT_Administrators"
    ],
    "UserGroups": [
      "VCDevTool_Users",
      "Domain Users"
    ],
    "NodeGroups": [
      "VCDevTool_ComputerNodes",
      "VCDevTool_ProcessingNodes"
    ],
    "LdapPath": "LDAP://DC=company,DC=local",
    "ServiceAccountUsername": "",
    "ServiceAccountPassword": "",
    "EnableGroupCaching": true,
    "GroupCacheExpirationMinutes": 30,
    "UseKerberos": true,
    "ComputerAccountOUs": [
      "OU=VCDevTool,OU=Computers,DC=company,DC=local",
      "OU=ProcessingNodes,OU=Computers,DC=company,DC=local"
    ]
  },
  "WindowsAuthentication": {
    "Enabled": true,
    "EnableKerberosAuthentication": true,
    "UseActiveDirectory": true,
    "FallbackToJwt": true,
    "RequireHttpsForKerberos": true,
    "PersistKerberosCredentials": false,
    "PersistNtlmCredentials": false
  }
}
```

### IIS Configuration (web.config)

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\VCDevTool.API.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
      
      <!-- Windows Authentication Configuration -->
      <security>
        <authentication>
          <windowsAuthentication enabled="true">
            <providers>
              <add value="Negotiate" />
              <add value="NTLM" />
            </providers>
          </windowsAuthentication>
          <anonymousAuthentication enabled="false" />
        </authentication>
        <authorization>
          <remove users="*" roles="" verbs="" />
          <add accessType="Allow" users="*" />
        </authorization>
      </security>
    </system.webServer>
  </location>
</configuration>
```

## Implementation Steps

### Step 1: Package Dependencies

Add the following NuGet packages to `VCDevTool.API.csproj`:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="9.0.3" />
<PackageReference Include="System.DirectoryServices" Version="9.0.0" />
<PackageReference Include="System.DirectoryServices.AccountManagement" Version="9.0.0" />
<PackageReference Include="System.DirectoryServices.Protocols" Version="9.0.0" />
```

### Step 2: Program.cs Configuration

```csharp
// Configure Active Directory options
builder.Services.Configure<ActiveDirectoryOptions>(
    builder.Configuration.GetSection(ActiveDirectoryOptions.Section));

// Configure Windows Authentication options
builder.Services.Configure<WindowsAuthenticationOptions>(
    builder.Configuration.GetSection(WindowsAuthenticationOptions.Section));

// Get Windows Authentication settings
var windowsAuthConfig = builder.Configuration.GetSection("WindowsAuthentication");
var isWindowsAuthEnabled = windowsAuthConfig.GetValue<bool>("Enabled");

// Configure Authentication
if (isWindowsAuthEnabled)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate(options =>
        {
            options.EnableKerberosAuthentication = true;
            options.PersistKerberosCredentials = false;
            options.PersistNtlmCredentials = false;
        })
        .AddJwtBearer(options =>
        {
            // JWT configuration for API clients
            // ... JWT configuration
        });
}

// Configure Authorization with AD group support
builder.Services.AddAuthorization(options =>
{
    // Active Directory group-based policies
    options.AddPolicy("ADAdminPolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_Administrators", "Domain Admins" })));
    
    options.AddPolicy("ADUserPolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_Users", "Domain Users" })));
    
    options.AddPolicy("ADNodePolicy", policy =>
        policy.Requirements.Add(new ADGroupRequirement(new[] { "VCDevTool_ComputerNodes" })));
});

// Register services
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IAuthorizationHandler, ADGroupAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ADRoleAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ComputerAccountAuthorizationHandler>();

// In the middleware pipeline
app.UseAuthentication();

// Add Windows Authentication enrichment middleware after authentication
if (isWindowsAuthEnabled)
{
    app.UseWindowsAuthenticationEnrichment();
}

app.UseAuthorization();
```

### Step 3: Controller Authorization

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ADUserPolicy")] // Require AD user group membership
public class TasksController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "ADAdminPolicy")] // Admin-only endpoint
    public async Task<IActionResult> GetAllTasks()
    {
        // Implementation
    }

    [HttpPost]
    [Authorize(Policy = "ADUserPolicy")] // User or Admin access
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
    {
        // Implementation
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ADNodePolicy")] // Computer accounts only
public class NodesController : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> RegisterNode([FromBody] RegisterNodeRequest request)
    {
        // Validate computer account
        var computerName = User.Identity?.Name;
        // Implementation
    }
}
```

## Security Features

### 1. Group-Based Authorization
- **Admin Groups**: Full system access
- **User Groups**: Standard user operations
- **Node Groups**: Computer account operations

### 2. Claims Enrichment
- Automatic AD lookup for authenticated users
- Role mapping from AD groups
- Group membership claims
- Department and contact information

### 3. Caching Strategy
- Group membership caching (30 minutes default)
- User information caching
- Thread-safe cache operations
- Configurable expiration

### 4. Fallback Mechanisms
- JWT authentication for API clients
- Basic Windows identity if AD unavailable
- Graceful degradation

## Deployment Considerations

### 1. IIS Configuration
- Enable Windows Authentication
- Configure Kerberos/NTLM providers
- Set up application pool identity
- Configure delegation if needed

### 2. Active Directory Setup
- Create security groups for VCDevTool
- Configure service principal names (SPNs)
- Set up computer accounts in appropriate OUs
- Configure group policies

### 3. Network Configuration
- Ensure Kerberos ports are open (88, 464)
- Configure DNS properly
- Set up time synchronization
- Configure firewalls for AD communication

### 4. Service Account Configuration
```powershell
# Create service account
New-ADUser -Name "VCDevToolService" -AccountPassword (ConvertTo-SecureString "Password123!" -AsPlainText -Force) -Enabled $true

# Set SPN for Kerberos
setspn -A HTTP/vcdevtool.company.local VCDevToolService
setspn -A HTTP/vcdevtool VCDevToolService

# Grant permissions
Add-ADGroupMember -Identity "VCDevTool_ServiceAccounts" -Members "VCDevToolService"
```

## Testing and Validation

### 1. Authentication Testing
```csharp
[Test]
public async Task WindowsAuthentication_ShouldEnrichClaimsWithADGroups()
{
    // Arrange
    var user = new WindowsIdentity("DOMAIN\\testuser");
    
    // Act
    var result = await _adService.GetUserInfoAsync("testuser");
    
    // Assert
    Assert.IsNotNull(result);
    Assert.Contains("VCDevTool_Users", result.Groups);
}
```

### 2. Authorization Testing
```csharp
[Test]
public async Task AdminEndpoint_ShouldRequireAdminGroup()
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/api/admin/users");
    
    // Assert
    Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

### 3. Integration Testing
- Test with real AD environment
- Validate group membership resolution
- Test computer account authentication
- Verify claims enrichment

## Monitoring and Logging

### 1. Structured Logging
```csharp
_logger.LogInformation("User {Username} authenticated via Windows with {GroupCount} groups", 
    username, userInfo.Groups.Count);

_logger.LogWarning("User {Username} not authorized. Required groups: {RequiredGroups}", 
    username, string.Join(", ", requirement.AllowedGroups));
```

### 2. Performance Monitoring
- AD query response times
- Cache hit rates
- Authentication success/failure rates
- Group resolution performance

### 3. Security Auditing
- Failed authentication attempts
- Unauthorized access attempts
- Group membership changes
- Computer account activities

## Troubleshooting

### Common Issues

1. **Kerberos Authentication Failures**
   - Check SPN configuration
   - Verify time synchronization
   - Check DNS resolution
   - Validate delegation settings

2. **AD Group Resolution Issues**
   - Verify service account permissions
   - Check LDAP connectivity
   - Validate group membership
   - Review cache configuration

3. **Computer Account Problems**
   - Ensure computer is domain-joined
   - Verify OU placement
   - Check group membership
   - Validate trust relationships

### Diagnostic Commands

```powershell
# Test Kerberos
klist tickets

# Test AD connectivity
Test-ComputerSecureChannel -Verbose

# Check SPN configuration
setspn -L VCDevToolService

# Test LDAP
ldp.exe
```

## Best Practices

### 1. Security
- Use HTTPS in production
- Implement proper SPN configuration
- Regular security group audits
- Monitor authentication logs

### 2. Performance
- Enable group caching
- Optimize AD queries
- Use connection pooling
- Monitor cache hit rates

### 3. Reliability
- Implement fallback mechanisms
- Handle AD connectivity issues
- Graceful degradation
- Proper error handling

### 4. Maintenance
- Regular group membership reviews
- Service account password rotation
- Monitor AD health
- Update security policies

## Conclusion

This Windows Authentication implementation provides enterprise-grade security for the VCDevTool system while maintaining compatibility with existing JWT-based API clients. The solution follows industry best practices for Active Directory integration and provides robust authorization mechanisms based on group membership and role mapping.

The implementation is designed to be scalable, maintainable, and secure, with proper fallback mechanisms and comprehensive logging for troubleshooting and monitoring. 