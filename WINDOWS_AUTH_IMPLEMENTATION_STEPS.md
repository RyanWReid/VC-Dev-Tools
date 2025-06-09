# VCDevTool Windows Authentication Implementation Steps

## 🚀 Quick Start Guide

This guide provides step-by-step instructions to implement Windows Authentication in your VCDevTool environment.

## Prerequisites

- [ ] Windows Server with IIS installed
- [ ] Active Directory domain
- [ ] Domain Administrator access
- [ ] VCDevTool application deployed

## Step 1: Update Configuration

### 1.1 Enable Windows Authentication in appsettings.json

Your `appsettings.json` has been updated with:
```json
"WindowsAuthentication": {
  "Enabled": true,
  // ... other settings
}
```

### 1.2 Update Domain Settings

Edit `VCDevTool.API/appsettings.json` and update the `ActiveDirectory` section:

```json
"ActiveDirectory": {
  "Domain": "your-domain.local",              // Replace with your domain
  "LdapPath": "LDAP://DC=your-domain,DC=local", // Update LDAP path
  "ServiceAccountUsername": "VCDevToolService",
  "ServiceAccountPassword": "",                // Leave empty for security
  // ... other settings remain the same
}
```

### 1.3 Update Production Configuration

Edit `VCDevTool.API/appsettings.Production.json` to include your production domain settings.

## Step 2: Run Active Directory Setup

### 2.1 Execute AD Setup Script

Run as **Domain Administrator**:

```powershell
# Review what will be created (dry run)
.\setup-ad-environment.ps1 -Domain "your-domain.local" -WhatIf

# Create the AD infrastructure
.\setup-ad-environment.ps1 -Domain "your-domain.local" -CreateGroups -CreateOUs

# Create service account (optional - you might have existing one)
.\setup-ad-environment.ps1 -Domain "your-domain.local" -CreateServiceAccount -ServiceAccountPassword "YourSecurePassword123!"

# Configure SPNs
.\setup-ad-environment.ps1 -Domain "your-domain.local" -ConfigureSPN -ServerFQDN "vcdevtool.your-domain.local"
```

### 2.2 Verify AD Setup

Check that the following were created:
- [ ] Organizational Units (VCDevTool, Service Accounts, Security Groups, Computer Nodes)
- [ ] Security Groups (VCDevTool_Administrators, VCDevTool_Users, etc.)
- [ ] Service Account (if created)
- [ ] SPNs configured

## Step 3: Configure IIS

### 3.1 Run IIS Configuration Script

Run as **Administrator** on IIS server:

```powershell
# Test configuration (dry run)
.\configure-iis.ps1 -WhatIf -ServiceAccount "your-domain\VCDevToolService"

# Apply configuration
.\configure-iis.ps1 -ServiceAccount "your-domain\VCDevToolService" -ServiceAccountPassword "YourSecurePassword123!"
```

### 3.2 Manual IIS Configuration (Alternative)

If you prefer manual configuration:

1. **Create Application Pool**:
   - Name: `VCDevToolAppPool`
   - .NET CLR Version: `No Managed Code`
   - Identity: `Specific User` → `your-domain\VCDevToolService`

2. **Configure Authentication**:
   - Navigate to your VCDevTool application in IIS Manager
   - Authentication → Enable `Windows Authentication`
   - Authentication → Disable `Anonymous Authentication`

3. **Deploy Application**:
   - Copy your published VCDevTool files to `C:\inetpub\wwwroot\VCDevTool`

## Step 4: Deploy Application

### 4.1 Publish Application

```powershell
# In your VCDevTool solution directory
dotnet publish VCDevTool.API -c Release -o "publish"
```

### 4.2 Copy Files to IIS

Copy the published files to your IIS application directory (e.g., `C:\inetpub\wwwroot\VCDevTool`).

### 4.3 Update Connection String

Update the connection string in `appsettings.Production.json` on the server:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-sql-server;Database=VCDevToolDb;Integrated Security=true;TrustServerCertificate=true;"
  }
}
```

## Step 5: Test Authentication

### 5.1 Run Automated Tests

```powershell
# Test the complete setup
.\test-windows-authentication.ps1 -Domain "your-domain.local" -ApiBaseUrl "https://your-server/VCDevTool"

# Test with verbose output
.\test-windows-authentication.ps1 -Domain "your-domain.local" -Verbose
```

### 5.2 Manual Testing

1. **Test Domain User Access**:
   - Add a domain user to `VCDevTool_Users` group
   - Open browser and navigate to `https://your-server/VCDevTool`
   - Should authenticate automatically with domain credentials

2. **Test API Endpoints**:
   ```powershell
   # Test authenticated endpoint
   Invoke-WebRequest -Uri "https://your-server/VCDevTool/api/tasks" -UseDefaultCredentials
   
   # Test identity endpoint
   Invoke-WebRequest -Uri "https://your-server/VCDevTool/api/debug/identity" -UseDefaultCredentials
   ```

## Step 6: Add Users to Groups

### 6.1 Add Domain Users

```powershell
# Add users to VCDevTool groups
Add-ADGroupMember -Identity "VCDevTool_Users" -Members "user1", "user2"
Add-ADGroupMember -Identity "VCDevTool_Administrators" -Members "admin1"
```

### 6.2 Add Computer Accounts

```powershell
# Add processing nodes to computer groups
Add-ADGroupMember -Identity "VCDevTool_ProcessingNodes" -Members "COMPUTER1$", "COMPUTER2$"
```

## Step 7: Configure Client Applications

### 7.1 Update Client Configuration

In your WPF client application, ensure it's configured to use Windows Authentication:

```json
{
  "ApiSettings": {
    "BaseUrl": "https://your-server/VCDevTool",
    "UseWindowsAuthentication": true
  }
}
```

### 7.2 Deploy Client to Domain Machines

Deploy the VCDevTool client to domain-joined machines and test connectivity.

## Troubleshooting

### Common Issues

1. **401 Unauthorized Errors**:
   - Check user is in appropriate AD group
   - Verify SPN configuration: `setspn -L VCDevToolService`
   - Check IIS authentication settings

2. **Kerberos Issues**:
   - Ensure time synchronization: `w32tm /query /status`
   - Check SPN duplicates: `setspn -X`
   - Verify delegation settings

3. **Application Pool Errors**:
   - Check service account password
   - Verify service account permissions
   - Review Windows Event Logs

### Diagnostic Commands

```powershell
# Check Kerberos tickets
klist tickets

# Test domain trust
Test-ComputerSecureChannel -Verbose

# Check SPN configuration
setspn -L VCDevToolService

# Test LDAP connectivity
Test-NetConnection -ComputerName your-domain.local -Port 389
```

## Security Best Practices

1. **Service Account Security**:
   - Use strong passwords
   - Set password to never expire
   - Grant minimum required permissions
   - Regular password rotation

2. **Network Security**:
   - Use HTTPS in production
   - Configure firewall rules
   - Monitor authentication logs

3. **Group Management**:
   - Regular group membership reviews
   - Principle of least privilege
   - Document group purposes

## Post-Implementation

### 1. Monitoring

- Set up monitoring for authentication failures
- Monitor application performance
- Track user access patterns

### 2. Maintenance

- Regular security group audits
- Service account password rotation
- Update SSL certificates

### 3. Scaling

- Plan for additional processing nodes
- Consider load balancing
- Plan for disaster recovery

## Success Criteria

✅ Domain users can authenticate without entering credentials  
✅ Group-based authorization works correctly  
✅ Computer accounts can register as processing nodes  
✅ API endpoints respond correctly to authenticated requests  
✅ No authentication-related errors in logs  

## Next Steps

1. **Production Deployment**:
   - Configure SSL certificates
   - Set up load balancing
   - Configure backup procedures

2. **Enhanced Features**:
   - Implement audit logging
   - Add performance monitoring
   - Configure auto-update mechanism

3. **Documentation**:
   - Create user guides
   - Document troubleshooting procedures
   - Train support staff

---

## Quick Reference Commands

```powershell
# Setup AD (run as Domain Admin)
.\setup-ad-environment.ps1 -Domain "your-domain.local"

# Configure IIS (run as Administrator)
.\configure-iis.ps1 -ServiceAccount "your-domain\VCDevToolService"

# Test Everything
.\test-windows-authentication.ps1 -Domain "your-domain.local"

# Add user to group
Add-ADGroupMember -Identity "VCDevTool_Users" -Members "username"

# Check authentication
Invoke-WebRequest -Uri "https://your-server/VCDevTool/api/tasks" -UseDefaultCredentials
```

This completes the Windows Authentication implementation for VCDevTool! 🎉 