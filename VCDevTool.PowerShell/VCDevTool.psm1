#Requires -Version 5.1

<#
.SYNOPSIS
    VCDevTool PowerShell Module - Enterprise Task Management System
.DESCRIPTION
    This module provides comprehensive PowerShell cmdlets for managing VCDevTool
    tasks, nodes, and system administration in enterprise environments.
.AUTHOR
    VCDevTool Development Team
.VERSION
    1.0.0
#>

# Module variables
$script:ModuleRoot = $PSScriptRoot
$script:VCConnection = $null
$script:VCApiEndpoint = $null
$script:VCAuthToken = $null

# Import private functions
$PrivateFunctions = @(Get-ChildItem -Path "$PSScriptRoot\Private\*.ps1" -ErrorAction SilentlyContinue)

foreach ($Import in $PrivateFunctions) {
    try {
        . $Import.FullName
        Write-Verbose "Imported private function: $($Import.BaseName)"
    }
    catch {
        Write-Error "Failed to import private function $($Import.FullName): $($_.Exception.Message)"
    }
}

# Import public functions
$PublicFunctions = @(Get-ChildItem -Path "$PSScriptRoot\Public\*.ps1" -ErrorAction SilentlyContinue)

foreach ($Import in $PublicFunctions) {
    try {
        . $Import.FullName
        Write-Verbose "Imported public function: $($Import.BaseName)"
    }
    catch {
        Write-Error "Failed to import public function $($Import.FullName): $($_.Exception.Message)"
    }
}

# Module initialization
$InitializationScript = {
    # Set default configuration
    $script:VCModuleConfig = @{
        DefaultApiEndpoint = "https://localhost:7001"
        DefaultTimeout = 30
        RetryAttempts = 3
        EnableLogging = $true
        LogLevel = "Information"
        ConfigPath = "$env:USERPROFILE\.vcdevtool\config.json"
    }

    # Create configuration directory if it doesn't exist
    $ConfigDir = Split-Path $script:VCModuleConfig.ConfigPath -Parent
    if (-not (Test-Path $ConfigDir)) {
        New-Item -Path $ConfigDir -ItemType Directory -Force | Out-Null
    }

    # Load saved configuration if exists
    if (Test-Path $script:VCModuleConfig.ConfigPath) {
        try {
            $SavedConfig = Get-Content $script:VCModuleConfig.ConfigPath | ConvertFrom-Json
            foreach ($Key in $SavedConfig.PSObject.Properties.Name) {
                $script:VCModuleConfig[$Key] = $SavedConfig.$Key
            }
            Write-Verbose "Loaded configuration from $($script:VCModuleConfig.ConfigPath)"
        }
        catch {
            Write-Warning "Failed to load configuration: $($_.Exception.Message)"
        }
    }

    # Initialize logging
    if ($script:VCModuleConfig.EnableLogging) {
        $LogPath = "$ConfigDir\vcdevtool.log"
        $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        "[$TimeStamp] VCDevTool PowerShell Module loaded" | Out-File -FilePath $LogPath -Append
    }

    Write-Verbose "VCDevTool PowerShell Module initialized successfully"
}

# Execute initialization
& $InitializationScript

# Export module members (aliases are defined in the manifest)
Export-ModuleMember -Function $PublicFunctions.BaseName

# Module cleanup on removal
$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove = {
    Write-Verbose "Cleaning up VCDevTool PowerShell Module"
    
    # Disconnect any active connections
    if ($script:VCConnection) {
        try {
            Disconnect-VCDevTool -Force
        }
        catch {
            Write-Warning "Failed to disconnect cleanly: $($_.Exception.Message)"
        }
    }
    
    # Clear module variables
    $script:VCConnection = $null
    $script:VCApiEndpoint = $null
    $script:VCAuthToken = $null
    
    Write-Verbose "VCDevTool PowerShell Module cleanup completed"
} 