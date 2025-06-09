# VCDevTool PowerShell Module

**Version**: 1.0.0  
**Compatibility**: PowerShell 5.1+ (Windows PowerShell and PowerShell Core)  
**Author**: VCDevTool Development Team

## Overview

The VCDevTool PowerShell module provides comprehensive command-line management capabilities for the VCDevTool enterprise task management system. This module enables IT administrators and developers to manage tasks, nodes, and system configuration through PowerShell scripts and interactive sessions.

## Features

- **Task Management**: Create, monitor, and control distributed tasks
- **Node Administration**: Manage worker nodes and their configurations
- **System Monitoring**: Real-time system status and performance metrics
- **Enterprise Integration**: Windows Authentication and Active Directory support
- **Automation-Ready**: Full scripting support with pipeline integration
- **Rich Formatting**: Color-coded output and structured data presentation

## Installation

### Prerequisites

- PowerShell 5.1 or later
- Network access to VCDevTool API server
- Appropriate permissions for task management operations

### Manual Installation

1. Copy the module folder to your PowerShell modules directory:
   ```powershell
   $ModulePath = "$env:USERPROFILE\Documents\PowerShell\Modules\VCDevTool"
   New-Item -Path $ModulePath -ItemType Directory -Force
   Copy-Item -Path ".\VCDevTool.PowerShell\*" -Destination $ModulePath -Recurse
   ```

2. Import the module:
   ```powershell
   Import-Module VCDevTool
   ```

3. Verify installation:
   ```powershell
   Get-Module VCDevTool
   Get-Command -Module VCDevTool
   ```

### PowerShell Gallery Installation (Future)

```powershell
Install-Module -Name VCDevTool -Scope CurrentUser
Import-Module VCDevTool
```

## Quick Start

### 1. Connect to VCDevTool API

```powershell
# Connect using Windows Authentication (recommended)
Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001"

# Connect using Basic Authentication
$Credential = Get-Credential
Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001" -Credential $Credential

# Test connection
Test-VCConnection -Detailed
```

### 2. Basic Task Management

```powershell
# View all tasks
Get-VCTask

# View running tasks only
Get-VCTask -Status Running

# Get specific task details
Get-VCTask -Id 123 | Format-List

# Create a simple test task
Start-VCTask -Type TestMessage -Parameters @{Message="Hello from PowerShell"} -Priority Normal

# Stop a running task
Stop-VCTask -Id 123 -Reason "Maintenance required"
```

### 3. Node Management

```powershell
# View all nodes
Get-VCNode

# Check node health
Get-VCNode | Where-Object {$_.IsOnline -eq $false}

# Get detailed node information
Get-VCNode -Id 1 | Format-List
```

## Command Reference

### Connection Management

| Cmdlet | Description | Example |
|--------|-------------|---------|
| `Connect-VCDevTool` | Connect to VCDevTool API | `Connect-VCDevTool -ApiEndpoint "https://server:7001"` |
| `Disconnect-VCDevTool` | Disconnect from API | `Disconnect-VCDevTool` |
| `Test-VCConnection` | Test connection status | `Test-VCConnection -Detailed` |

### Task Management

| Cmdlet | Description | Example |
|--------|-------------|---------|
| `Get-VCTask` | Retrieve task information | `Get-VCTask -Status Running -Type VolumeCompression` |
| `Start-VCTask` | Create and start a new task | `Start-VCTask -Type TestMessage -Parameters @{Message="Test"}` |
| `Stop-VCTask` | Cancel a running task | `Stop-VCTask -Id 123 -Force` |
| `Restart-VCTask` | Restart a failed task | `Restart-VCTask -Id 123` |
| `Remove-VCTask` | Delete a task record | `Remove-VCTask -Id 123 -Force` |
| `Set-VCTaskPriority` | Update task priority | `Set-VCTaskPriority -Id 123 -Priority High` |
| `Get-VCTaskHistory` | Get task execution history | `Get-VCTaskHistory -Id 123 -IncludeLogs` |
| `Export-VCTaskReport` | Export task data | `Export-VCTaskReport -Format CSV -OutputPath "tasks.csv"` |

### Node Management

| Cmdlet | Description | Example |
|--------|-------------|---------|
| `Get-VCNode` | Retrieve node information | `Get-VCNode -Status Active` |
| `Register-VCNode` | Register a new node | `Register-VCNode -Name "Worker01" -Capabilities @("VolumeCompression")` |
| `Unregister-VCNode` | Remove a node | `Unregister-VCNode -Id 1 -Force` |
| `Test-VCNode` | Test node connectivity | `Test-VCNode -Id 1` |
| `Get-VCNodeHealth` | Get node health metrics | `Get-VCNodeHealth -Id 1` |
| `Restart-VCNode` | Restart node service | `Restart-VCNode -Id 1` |

### System Administration

| Cmdlet | Description | Example |
|--------|-------------|---------|
| `Get-VCSystemStatus` | Get system overview | `Get-VCSystemStatus` |
| `Get-VCConfiguration` | View module configuration | `Get-VCConfiguration` |
| `Set-VCConfiguration` | Update configuration | `Set-VCConfiguration -DefaultTimeout 60` |
| `Reset-VCConfiguration` | Reset to defaults | `Reset-VCConfiguration -Force` |
| `Get-VCVersion` | Get version information | `Get-VCVersion` |

### Monitoring and Analytics

| Cmdlet | Description | Example |
|--------|-------------|---------|
| `Get-VCPerformanceMetrics` | Get performance data | `Get-VCPerformanceMetrics -Hours 24` |
| `Get-VCTaskStatistics` | Get task statistics | `Get-VCTaskStatistics -GroupBy Type` |
| `Get-VCNodeStatistics` | Get node statistics | `Get-VCNodeStatistics` |
| `Export-VCDiagnostics` | Export diagnostic data | `Export-VCDiagnostics -OutputPath "diagnostics.zip"` |

## Advanced Usage Examples

### Bulk Task Operations

```powershell
# Create multiple compression tasks
$SourceFolders = @("C:\Data\Folder1", "C:\Data\Folder2", "C:\Data\Folder3")

$Tasks = foreach ($Folder in $SourceFolders) {
    Start-VCTask -Type VolumeCompression -Parameters @{
        SourcePath = $Folder
        TargetPath = "$Folder.compressed"
        CompressionLevel = "Normal"
    } -Priority Normal
}

# Monitor progress
do {
    $RunningTasks = $Tasks | ForEach-Object { Get-VCTask -Id $_.Id } | Where-Object { $_.Status -eq "Running" }
    Write-Host "Running tasks: $($RunningTasks.Count)"
    Start-Sleep -Seconds 10
} while ($RunningTasks.Count -gt 0)
```

### Automated System Monitoring

```powershell
# Daily system health check script
function Invoke-VCHealthCheck {
    param([string]$ReportPath = "C:\Reports\VCHealth_$(Get-Date -Format 'yyyyMMdd').txt")
    
    $Report = @()
    $Report += "VCDevTool Health Report - $(Get-Date)"
    $Report += "=" * 50
    
    # System status
    $SystemStatus = Get-VCSystemStatus
    $Report += "System Status: $($SystemStatus.Status)"
    $Report += "Active Tasks: $($SystemStatus.ActiveTasks)"
    $Report += "Online Nodes: $($SystemStatus.OnlineNodes)"
    
    # Failed tasks in last 24 hours
    $FailedTasks = Get-VCTask -Status Failed -StartDate (Get-Date).AddDays(-1)
    $Report += "Failed Tasks (24h): $($FailedTasks.Count)"
    
    # Offline nodes
    $OfflineNodes = Get-VCNode | Where-Object { -not $_.IsOnline }
    $Report += "Offline Nodes: $($OfflineNodes.Count)"
    if ($OfflineNodes) {
        $Report += "Offline Node Details:"
        $OfflineNodes | ForEach-Object { $Report += "  - $($_.Name) (Last seen: $($_.LastSeen))" }
    }
    
    $Report | Out-File -FilePath $ReportPath
    Write-Host "Health report saved to: $ReportPath"
}
```

### Task Pipeline Processing

```powershell
# Pipeline example: Get failed tasks and restart them
Get-VCTask -Status Failed | 
    Where-Object { $_.CreatedAt -gt (Get-Date).AddHours(-2) } |
    ForEach-Object { 
        Write-Host "Restarting failed task $($_.Id) - $($_.Type)"
        Restart-VCTask -Id $_.Id -Force 
    }
```

### Configuration Management

```powershell
# Environment-specific configuration
$Environment = "Production"

switch ($Environment) {
    "Development" {
        Set-VCConfiguration -DefaultApiEndpoint "https://dev-vcserver:7001" -EnableLogging $true -LogLevel "Verbose"
    }
    "Production" {
        Set-VCConfiguration -DefaultApiEndpoint "https://vcserver.company.local:7001" -EnableLogging $true -LogLevel "Warning"
    }
}

# Save current configuration
$Config = Get-VCConfiguration
$Config | Export-Clixml -Path "VCConfig_Backup_$(Get-Date -Format 'yyyyMMdd').xml"
```

## Error Handling

The module provides comprehensive error handling with detailed error messages:

```powershell
try {
    $Task = Start-VCTask -Type VolumeCompression -Parameters @{
        SourcePath = "C:\NonExistentPath"
        TargetPath = "C:\Output"
        CompressionLevel = "Normal"
    }
}
catch {
    Write-Error "Task creation failed: $($_.Exception.Message)"
    # Handle error appropriately
}
```

## Authentication and Security

### Windows Authentication (Recommended)

```powershell
# Uses current Windows credentials
Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001" -UseWindowsAuthentication
```

### Basic Authentication

```powershell
# Prompt for credentials
$Cred = Get-Credential -Message "Enter VCDevTool credentials"
Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001" -Credential $Cred

# Using saved credentials
$SecurePassword = ConvertTo-SecureString "password" -AsPlainText -Force
$Cred = New-Object System.Management.Automation.PSCredential("username", $SecurePassword)
Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001" -Credential $Cred
```

## Configuration Options

The module stores configuration in `$env:USERPROFILE\.vcdevtool\config.json`:

```json
{
  "DefaultApiEndpoint": "https://localhost:7001",
  "DefaultTimeout": 30,
  "RetryAttempts": 3,
  "EnableLogging": true,
  "LogLevel": "Information"
}
```

### Logging

Logs are written to `$env:USERPROFILE\.vcdevtool\vcdevtool.log` when enabled:

```powershell
# Enable detailed logging
Set-VCConfiguration -EnableLogging $true -LogLevel "Verbose"

# View recent logs
Get-Content "$env:USERPROFILE\.vcdevtool\vcdevtool.log" -Tail 20
```

## Performance Tips

1. **Use pagination for large datasets**:
   ```powershell
   Get-VCTask -PageSize 100 -Page 1
   ```

2. **Filter at the server level**:
   ```powershell
   # Efficient - filters on server
   Get-VCTask -Status Running -Type VolumeCompression
   
   # Inefficient - downloads all tasks then filters
   Get-VCTask | Where-Object {$_.Status -eq "Running" -and $_.Type -eq "VolumeCompression"}
   ```

3. **Use specific task IDs when possible**:
   ```powershell
   Get-VCTask -Id 123  # Faster than filtering all tasks
   ```

## Troubleshooting

### Common Issues

1. **Connection Failures**:
   ```powershell
   # Test endpoint accessibility
   Test-NetConnection -ComputerName "vcserver.company.local" -Port 7001
   
   # Verify API endpoint format
   Test-VCConnection -Detailed
   ```

2. **Authentication Issues**:
   ```powershell
   # Check Windows authentication
   whoami
   
   # Test with explicit credentials
   Connect-VCDevTool -ApiEndpoint "https://server:7001" -Credential (Get-Credential)
   ```

3. **Certificate Issues (HTTPS)**:
   ```powershell
   # Ignore certificate errors (development only)
   [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
   ```

### Debug Mode

```powershell
# Enable verbose output
$VerbosePreference = "Continue"
Connect-VCDevTool -ApiEndpoint "https://server:7001" -Verbose

# View detailed error information
$ErrorActionPreference = "Stop"
try {
    Get-VCTask -Id 999999
}
catch {
    $_ | Format-List * -Force
}
```

## Integration Examples

### PowerShell DSC

```powershell
Configuration VCDevToolSetup {
    param([string]$ApiEndpoint)
    
    Script VCDevToolModule {
        SetScript = {
            Import-Module VCDevTool -Force
            Connect-VCDevTool -ApiEndpoint $using:ApiEndpoint
        }
        TestScript = {
            try {
                Import-Module VCDevTool -ErrorAction Stop
                return Test-VCConnection
            }
            catch { return $false }
        }
        GetScript = { @{Result = (Get-Module VCDevTool).Version} }
    }
}
```

### Scheduled Tasks

```powershell
# Register a scheduled task for health monitoring
$Action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-Command `"Import-Module VCDevTool; Connect-VCDevTool -ApiEndpoint 'https://server:7001'; Invoke-VCHealthCheck`""
$Trigger = New-ScheduledTaskTrigger -Daily -At "08:00"
Register-ScheduledTask -TaskName "VCDevTool Health Check" -Action $Action -Trigger $Trigger
```

## Contributing

To contribute to the VCDevTool PowerShell module:

1. Follow PowerShell scripting best practices
2. Include comprehensive help documentation
3. Add appropriate error handling
4. Test with both PowerShell 5.1 and PowerShell Core
5. Ensure compatibility with enterprise environments

## Support

For support and additional documentation:

- **Internal Wiki**: [Company Wiki/VCDevTool](internal-link)
- **IT Support**: helpdesk@company.local
- **Development Team**: vcdevtool-dev@company.local

## Version History

### 1.0.0 (2024-12-28)
- Initial release
- Core task management functionality
- Node administration capabilities
- Windows Authentication support
- Comprehensive help documentation
- Enterprise integration features

---

**Note**: This module is designed for enterprise use within corporate networks. Ensure proper security policies are followed when deploying and using this module in production environments. 