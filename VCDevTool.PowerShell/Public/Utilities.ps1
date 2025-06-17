function Connect-VCDevTool {
    <#
    .SYNOPSIS
        Establishes a connection to the VCDevTool API
    .DESCRIPTION
        Creates an authenticated session with the VCDevTool API server.
        Supports Windows Authentication and Basic Authentication.
    .PARAMETER ApiEndpoint
        VCDevTool API endpoint URL
    .PARAMETER Credential
        Username and password for Basic Authentication
    .PARAMETER UseWindowsAuthentication
        Use Windows/Kerberos authentication
    .PARAMETER TimeoutSeconds
        Request timeout in seconds (default: 30)
    .PARAMETER Force
        Force connection even if already connected
    .EXAMPLE
        Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001"
        Connect using Windows Authentication
    .EXAMPLE
        Connect-VCDevTool -ApiEndpoint "https://vcserver.company.local:7001" -Credential (Get-Credential)
        Connect using Basic Authentication
    #>
    [CmdletBinding(DefaultParameterSetName = 'Windows')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiEndpoint,
        
        [Parameter(ParameterSetName = 'Basic', Mandatory = $true)]
        [PSCredential]$Credential,
        
        [Parameter(ParameterSetName = 'Windows')]
        [switch]$UseWindowsAuthentication = $true,
        
        [Parameter()]
        [int]$TimeoutSeconds = 30,
        
        [Parameter()]
        [switch]$Force
    )
    
    try {
        # Validate endpoint format
        if (-not (Test-VCApiEndpointFormat -Endpoint $ApiEndpoint)) {
            throw "Invalid API endpoint format: $ApiEndpoint"
        }
        
        # Check if already connected
        if ((Test-VCConnectionState) -and -not $Force) {
            Write-Warning "Already connected to VCDevTool API. Use -Force to reconnect."
            return
        }
        
        # Close existing connection if forcing reconnection
        if ($Force -and $script:VCConnection) {
            Close-VCConnection -Force
        }
        
        Write-Host "Connecting to VCDevTool API at $ApiEndpoint..." -ForegroundColor Yellow
        
        # Initialize connection
        $ConnectionParams = @{
            ApiEndpoint = $ApiEndpoint
            TimeoutSeconds = $TimeoutSeconds
        }
        
        if ($PSCmdlet.ParameterSetName -eq 'Basic') {
            $ConnectionParams.Credential = $Credential
        } else {
            $ConnectionParams.UseWindowsAuthentication = $true
        }
        
        $Connected = Initialize-VCConnection @ConnectionParams
        
        if ($Connected) {
            Write-Host "Successfully connected to VCDevTool API" -ForegroundColor Green
            
            # Get and display server info
            try {
                $ServerInfo = Get-VCVersion
                Write-Host "Server Version: $($ServerInfo.Version)" -ForegroundColor Cyan
                Write-Host "Build Date: $($ServerInfo.BuildDate)" -ForegroundColor Cyan
            }
            catch {
                Write-Verbose "Could not retrieve server version information"
            }
            
            # Save connection configuration
            $ConfigToSave = @{
                DefaultApiEndpoint = $ApiEndpoint
                LastConnected = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssZ')
                UseWindowsAuth = $UseWindowsAuthentication.IsPresent
            }
            
            try {
                $ConfigToSave | ConvertTo-Json | Out-File -FilePath $script:VCModuleConfig.ConfigPath -Force
            }
            catch {
                Write-Verbose "Could not save configuration: $($_.Exception.Message)"
            }
        }
        else {
            throw "Failed to establish connection to VCDevTool API"
        }
    }
    catch {
        Write-Error "Connection failed: $($_.Exception.Message)"
    }
}

function Disconnect-VCDevTool {
    <#
    .SYNOPSIS
        Disconnects from the VCDevTool API
    .DESCRIPTION
        Closes the current API session and cleans up resources
    .PARAMETER Force
        Force disconnect without graceful session cleanup
    .EXAMPLE
        Disconnect-VCDevTool
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$Force
    )
    
    try {
        if (-not $script:VCConnection) {
            Write-Warning "Not connected to VCDevTool API"
            return
        }
        
        Write-Host "Disconnecting from VCDevTool API..." -ForegroundColor Yellow
        
        Close-VCConnection -Force:$Force
        
        Write-Host "Disconnected from VCDevTool API" -ForegroundColor Green
    }
    catch {
        Write-Error "Error during disconnect: $($_.Exception.Message)"
    }
}

function Test-VCConnection {
    <#
    .SYNOPSIS
        Tests the current VCDevTool API connection
    .DESCRIPTION
        Verifies that the API connection is active and responsive
    .PARAMETER Detailed
        Show detailed connection information
    .EXAMPLE
        Test-VCConnection -Detailed
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$Detailed
    )
    
    try {
        if (-not $script:VCConnection) {
            Write-Host "Not connected to VCDevTool API" -ForegroundColor Red
            return $false
        }
        
        $IsConnected = Test-VCConnectionState
        
        if ($IsConnected) {
            Write-Host "VCDevTool API connection is active" -ForegroundColor Green
            
            if ($Detailed) {
                Write-Host "API Endpoint: $($script:VCApiEndpoint)" -ForegroundColor Cyan
                Write-Host "Timeout: $($script:VCConnection.TimeoutSeconds) seconds" -ForegroundColor Cyan
                Write-Host "Authentication: $(if ($script:VCConnection.UseWindowsAuth) { 'Windows' } else { 'Basic' })" -ForegroundColor Cyan
                
                # Test response time
                $StartTime = Get-Date
                $Response = Invoke-VCApiRequest -Endpoint "/api/health" -Method Get
                $ResponseTime = (Get-Date) - $StartTime
                
                Write-Host "Response Time: $($ResponseTime.TotalMilliseconds.ToString('F0')) ms" -ForegroundColor Cyan
                Write-Host "Server Status: $($Response.Status)" -ForegroundColor Cyan
            }
            
            return $true
        }
        else {
            Write-Host "VCDevTool API connection is not active" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "Connection test failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Get-VCConfiguration {
    <#
    .SYNOPSIS
        Gets the current VCDevTool module configuration
    .DESCRIPTION
        Displays current module settings and configuration
    .EXAMPLE
        Get-VCConfiguration
    #>
    [CmdletBinding()]
    param()
    
    $Config = [PSCustomObject]@{
        PSTypeName = 'VCDevTool.Configuration'
        ModuleVersion = '1.0.0'
        DefaultApiEndpoint = $script:VCModuleConfig.DefaultApiEndpoint
        DefaultTimeout = $script:VCModuleConfig.DefaultTimeout
        RetryAttempts = $script:VCModuleConfig.RetryAttempts
        EnableLogging = $script:VCModuleConfig.EnableLogging
        LogLevel = $script:VCModuleConfig.LogLevel
        ConfigPath = $script:VCModuleConfig.ConfigPath
        CurrentConnection = if ($script:VCConnection) { 
            @{
                ApiEndpoint = $script:VCApiEndpoint
                IsConnected = Test-VCConnectionState
                UseWindowsAuth = $script:VCConnection.UseWindowsAuth
                TimeoutSeconds = $script:VCConnection.TimeoutSeconds
            }
        } else { $null }
    }
    
    return $Config
}

function Set-VCConfiguration {
    <#
    .SYNOPSIS
        Updates VCDevTool module configuration
    .DESCRIPTION
        Modifies module settings and saves them to the configuration file
    .PARAMETER DefaultApiEndpoint
        Default API endpoint URL
    .PARAMETER DefaultTimeout
        Default request timeout in seconds
    .PARAMETER RetryAttempts
        Number of retry attempts for failed requests
    .PARAMETER EnableLogging
        Enable module logging
    .PARAMETER LogLevel
        Logging level (Verbose, Information, Warning, Error)
    .EXAMPLE
        Set-VCConfiguration -DefaultApiEndpoint "https://newserver:7001" -EnableLogging $true
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter()]
        [string]$DefaultApiEndpoint,
        
        [Parameter()]
        [int]$DefaultTimeout,
        
        [Parameter()]
        [int]$RetryAttempts,
        
        [Parameter()]
        [bool]$EnableLogging,
        
        [Parameter()]
        [ValidateSet('Verbose', 'Information', 'Warning', 'Error')]
        [string]$LogLevel
    )
    
    try {
        $ConfigChanged = $false
        
        if ($DefaultApiEndpoint) {
            if (Test-VCApiEndpointFormat -Endpoint $DefaultApiEndpoint) {
                $script:VCModuleConfig.DefaultApiEndpoint = $DefaultApiEndpoint
                $ConfigChanged = $true
            } else {
                throw "Invalid API endpoint format: $DefaultApiEndpoint"
            }
        }
        
        if ($DefaultTimeout) {
            $script:VCModuleConfig.DefaultTimeout = $DefaultTimeout
            $ConfigChanged = $true
        }
        
        if ($RetryAttempts) {
            $script:VCModuleConfig.RetryAttempts = $RetryAttempts
            $ConfigChanged = $true
        }
        
        if ($PSBoundParameters.ContainsKey('EnableLogging')) {
            $script:VCModuleConfig.EnableLogging = $EnableLogging
            $ConfigChanged = $true
        }
        
        if ($LogLevel) {
            $script:VCModuleConfig.LogLevel = $LogLevel
            $ConfigChanged = $true
        }
        
        if ($ConfigChanged -and $PSCmdlet.ShouldProcess("VCDevTool configuration", "Update settings")) {
            # Save configuration
            $script:VCModuleConfig | ConvertTo-Json | Out-File -FilePath $script:VCModuleConfig.ConfigPath -Force
            Write-Host "Configuration updated successfully" -ForegroundColor Green
        }
    }
    catch {
        Write-Error "Failed to update configuration: $($_.Exception.Message)"
    }
}

function Reset-VCConfiguration {
    <#
    .SYNOPSIS
        Resets VCDevTool module configuration to defaults
    .DESCRIPTION
        Restores all module settings to their default values
    .PARAMETER Force
        Force reset without confirmation
    .EXAMPLE
        Reset-VCConfiguration -Force
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter()]
        [switch]$Force
    )
    
    if ($Force -or $PSCmdlet.ShouldProcess("VCDevTool configuration", "Reset to defaults")) {
        # Reset to default values
        $script:VCModuleConfig = @{
            DefaultApiEndpoint = "https://localhost:7001"
            DefaultTimeout = 30
            RetryAttempts = 3
            EnableLogging = $true
            LogLevel = "Information"
            ConfigPath = "$env:USERPROFILE\.vcdevtool\config.json"
        }
        
        # Save default configuration
        try {
            $script:VCModuleConfig | ConvertTo-Json | Out-File -FilePath $script:VCModuleConfig.ConfigPath -Force
            Write-Host "Configuration reset to defaults" -ForegroundColor Green
        }
        catch {
            Write-Error "Failed to save default configuration: $($_.Exception.Message)"
        }
    }
}

function Get-VCVersion {
    <#
    .SYNOPSIS
        Gets version information for VCDevTool components
    .DESCRIPTION
        Retrieves version information for the PowerShell module and connected API server
    .EXAMPLE
        Get-VCVersion
    #>
    [CmdletBinding()]
    param()
    
    try {
        $VersionInfo = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.VersionInfo'
            PowerShellModule = '1.0.0'
            ModulePath = $script:ModuleRoot
            ServerVersion = $null
            ServerBuildDate = $null
            ServerEnvironment = $null
            ApiEndpoint = $script:VCApiEndpoint
        }
        
        # Get server version if connected
        if (Test-VCConnectionState) {
            try {
                $ServerInfo = Invoke-VCApiRequest -Endpoint "/api/version" -Method Get
                $VersionInfo.ServerVersion = $ServerInfo.Version
                $VersionInfo.ServerBuildDate = $ServerInfo.BuildDate
                $VersionInfo.ServerEnvironment = $ServerInfo.Environment
            }
            catch {
                Write-Verbose "Could not retrieve server version: $($_.Exception.Message)"
            }
        }
        
        return $VersionInfo
    }
    catch {
        Write-Error "Failed to get version information: $($_.Exception.Message)"
    }
}

function Write-VCLog {
    <#
    .SYNOPSIS
        Writes messages to the VCDevTool module log
    .DESCRIPTION
        Internal function for logging module activities
    .PARAMETER Message
        Log message
    .PARAMETER Level
        Log level
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter()]
        [ValidateSet('Verbose', 'Information', 'Warning', 'Error')]
        [string]$Level = 'Information'
    )
    
    if (-not $script:VCModuleConfig.EnableLogging) {
        return
    }
    
    $LogLevels = @{
        'Verbose' = 0
        'Information' = 1
        'Warning' = 2
        'Error' = 3
    }
    
    $CurrentLevel = $LogLevels[$script:VCModuleConfig.LogLevel]
    $MessageLevel = $LogLevels[$Level]
    
    if ($MessageLevel -ge $CurrentLevel) {
        $LogPath = Split-Path $script:VCModuleConfig.ConfigPath -Parent | Join-Path -ChildPath "vcdevtool.log"
        $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        $LogEntry = "[$TimeStamp] [$Level] $Message"
        
        try {
            $LogEntry | Out-File -FilePath $LogPath -Append -ErrorAction SilentlyContinue
        }
        catch {
            # Silently fail if logging fails
        }
    }
}

function Clear-VCCache {
    <#
    .SYNOPSIS
        Clears cached data and temporary files
    .DESCRIPTION
        Removes cached API responses and temporary files created by the module
    .PARAMETER Force
        Force clear without confirmation
    .EXAMPLE
        Clear-VCCache -Force
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter()]
        [switch]$Force
    )
    
    if ($Force -or $PSCmdlet.ShouldProcess("VCDevTool cache", "Clear cached data")) {
        try {
            $CacheDir = Split-Path $script:VCModuleConfig.ConfigPath -Parent | Join-Path -ChildPath "cache"
            
            if (Test-Path $CacheDir) {
                Remove-Item -Path $CacheDir -Recurse -Force
                Write-Host "Cache cleared successfully" -ForegroundColor Green
            }
            else {
                Write-Host "No cache data found" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Error "Failed to clear cache: $($_.Exception.Message)"
        }
    }
}

# Create command aliases for convenience
New-Alias -Name "gvct" -Value "Get-VCTask" -Force
New-Alias -Name "svct" -Value "Start-VCTask" -Force
New-Alias -Name "spvct" -Value "Stop-VCTask" -Force 