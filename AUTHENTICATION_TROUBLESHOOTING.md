# VCDevTool Authentication Troubleshooting Guide

## Overview
This guide helps troubleshoot authentication failures and node registration issues in VCDevTool.

## Common Issues and Solutions

### 1. "Failed to auth with API and register node when pressing connect"

**Root Cause**: The client cannot authenticate with the API server or register as a node.

**Step-by-Step Troubleshooting**:

#### A. Check API Server Status
```powershell
# Check if API is running on correct port
netstat -an | findstr ":5289"

# Should show:
# TCP    0.0.0.0:5289           0.0.0.0:0              LISTENING
```

#### B. Start API Server (if not running)
```powershell
# From the root directory:
dotnet run --project VCDevTool.API
```

#### C. Test API Connection
```powershell
# Run the system test script
.\test-system.ps1

# Or test manually:
$headers = @{ "Content-Type" = "application/json" }
$nodeData = @{
    "Id" = "test-node-manual"
    "Name" = "Manual Test Node"
    "IpAddress" = "127.0.0.1"
    "HardwareFingerprint" = "TEST-MANUAL-123"
} | ConvertTo-Json

Invoke-WebRequest -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -Headers $headers
```

### 2. "API is not accessible" Error

**Possible Causes**:
- API server not started
- Port 5289 blocked by firewall
- LocalDB not running/configured

**Solutions**:

#### A. Verify API Configuration
Check `VCDevTool.API/appsettings.json`:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5289"
      }
    }
  }
}
```

#### B. Check Database Connection
```powershell
# Initialize or reset database
.\nuke-db.ps1
```

#### C. Check Windows Firewall
```powershell
# Allow the port through Windows Firewall (run as Administrator)
New-NetFirewallRule -DisplayName "VCDevTool API" -Direction Inbound -Port 5289 -Protocol TCP -Action Allow
```

### 3. Database Connection Issues

**Symptoms**:
- 500 Internal Server Error during registration
- Database connection timeout errors in logs

**Solutions**:

#### A. Check LocalDB
```powershell
# List LocalDB instances
sqllocaldb info

# Start LocalDB
sqllocaldb start mssqllocaldb

# Check connection string in appsettings.json
```

#### B. Reset Database
```powershell
# Run the database reset script
.\nuke-db.ps1
```

### 4. Authentication Token Issues

**Symptoms**:
- 401 Unauthorized errors
- Token validation failures

**Solutions**:

#### A. Check JWT Configuration
Verify `VCDevTool.API/appsettings.json` has valid JWT settings:
```json
{
  "Jwt": {
    "SecretKey": "VCDevTool-Secret-Key-Change-This-In-Production-Environment-123456789abcdef",
    "Issuer": "VCDevTool",
    "Audience": "VCDevTool"
  }
}
```

#### B. Check Client Configuration
Verify `VCDevTool.Client/appsettings.json`:
```json
{
  "ApiSettings": {
    "BaseUrl": "http://localhost:5289"
  }
}
```

### 5. Windows Authentication Issues

**Symptoms**:
- Kerberos authentication failures
- Domain authentication not working

**Solutions**:

#### A. Disable Windows Authentication for Testing
In `VCDevTool.API/appsettings.json`:
```json
{
  "WindowsAuthentication": {
    "Enabled": false
  }
}
```

#### B. Check Active Directory Configuration
Verify AD settings match your domain environment.

## Quick Start Commands

### Start Everything Properly:
```powershell
# 1. Start API server
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'C:\Development\VC-Dev-Tool'; dotnet run --project VCDevTool.API"

# 2. Wait a few seconds, then start client
Start-Sleep 5
dotnet run --project VCDevTool.Client
```

### Use the Provided Scripts:
```powershell
# Start API
.\start-api.ps1

# In another terminal, start client
.\start-client.ps1

# Or use the main launcher
.\launch.ps1
```

### Test the System:
```powershell
# Comprehensive system test
.\test-system.ps1

# Test specific components
.\test-windows-authentication.ps1
.\test-concurrent-processing.ps1
```

## Debug Information

### Check Logs
- API logs: `VCDevTool.API/logs/`
- Client logs: Check the Debug Output window in the client

### Common Log Entries to Look For
- "Node registered successfully"
- "Authentication failed"
- "Database connection failed"
- "JWT token validation failed"

### Client Debug Output
The client application shows connection status and authentication attempts in its Debug Output panel.

## Production Considerations

### Security
- Change JWT secret key in production
- Use HTTPS in production
- Configure proper Active Directory integration
- Use proper SQL Server instead of LocalDB

### Networking
- Configure firewall rules
- Set up load balancing if needed
- Configure reverse proxy (nginx example provided)

## Getting Help

1. Check the logs first
2. Run `.\test-system.ps1` to identify specific issues
3. Verify all services are running with `netstat -an | findstr ":5289"`
4. Check this troubleshooting guide for common solutions 