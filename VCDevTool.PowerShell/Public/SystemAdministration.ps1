function Get-VCSystemStatus {
    <#
    .SYNOPSIS
        Gets overall VCDevTool system status and metrics
    .DESCRIPTION
        Retrieves comprehensive system health information including task queues, node status, and performance metrics
    .PARAMETER Detailed
        Include detailed performance metrics
    .EXAMPLE
        Get-VCSystemStatus -Detailed
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$Detailed
    )
    
    try {
        $QueryParams = @{}
        if ($Detailed) {
            $QueryParams.detailed = 'true'
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/system/status" -Method Get -QueryParameters $QueryParams
        
        $SystemStatus = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.SystemStatus'
            Status = $Response.Status
            Version = $Response.Version
            Uptime = $Response.Uptime
            TotalNodes = $Response.TotalNodes
            OnlineNodes = $Response.OnlineNodes
            OfflineNodes = $Response.OfflineNodes
            TotalTasks = $Response.TotalTasks
            PendingTasks = $Response.PendingTasks
            RunningTasks = $Response.RunningTasks
            CompletedTasks = $Response.CompletedTasks
            FailedTasks = $Response.FailedTasks
            SystemLoad = $Response.SystemLoad
            MemoryUsage = $Response.MemoryUsage
            DiskUsage = $Response.DiskUsage
            DatabaseConnections = $Response.DatabaseConnections
            ApiEndpoint = $script:VCApiEndpoint
            LastRefresh = Get-Date
        }
        
        if ($Detailed -and $Response.DetailedMetrics) {
            $SystemStatus | Add-Member -NotePropertyName "DetailedMetrics" -NotePropertyValue $Response.DetailedMetrics
        }
        
        return $SystemStatus
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Backup-VCDatabase {
    <#
    .SYNOPSIS
        Creates a backup of the VCDevTool database
    .DESCRIPTION
        Initiates a database backup operation with optional compression and encryption
    .PARAMETER BackupPath
        Path where backup file will be created
    .PARAMETER Compress
        Enable backup compression
    .PARAMETER IncludeLogs
        Include transaction logs in backup
    .PARAMETER Description
        Optional backup description
    .PARAMETER WaitForCompletion
        Wait for backup to complete
    .EXAMPLE
        Backup-VCDatabase -BackupPath "C:\Backups\VCDevTool_$(Get-Date -Format 'yyyyMMdd_HHmmss').bak" -Compress -WaitForCompletion
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupPath,
        
        [Parameter()]
        [switch]$Compress,
        
        [Parameter()]
        [switch]$IncludeLogs,
        
        [Parameter()]
        [string]$Description,
        
        [Parameter()]
        [switch]$WaitForCompletion
    )
    
    try {
        # Validate backup path
        $BackupDir = Split-Path $BackupPath -Parent
        if (-not (Test-Path $BackupDir)) {
            throw "Backup directory does not exist: $BackupDir"
        }
        
        # Check permissions for system administration
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for database backup operations"
        }
        
        $BackupRequest = @{
            BackupPath = $BackupPath
            Compress = $Compress.IsPresent
            IncludeLogs = $IncludeLogs.IsPresent
            WaitForCompletion = $WaitForCompletion.IsPresent
        }
        
        if ($Description) {
            $BackupRequest.Description = $Description
        }
        
        if ($PSCmdlet.ShouldProcess("VCDevTool Database", "Create backup at $BackupPath")) {
            Write-Host "Starting database backup..." -ForegroundColor Yellow
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/database/backup" -Method Post -Body $BackupRequest -TimeoutSeconds 1800 # 30 minutes
            
            if ($WaitForCompletion) {
                Write-Host "Backup completed successfully" -ForegroundColor Green
                Write-Host "Backup file: $($Response.BackupPath)" -ForegroundColor Cyan
                Write-Host "File size: $($Response.FileSizeBytes / 1MB) MB" -ForegroundColor Cyan
                Write-Host "Duration: $($Response.DurationSeconds) seconds" -ForegroundColor Cyan
            } else {
                Write-Host "Backup operation initiated with ID: $($Response.BackupId)" -ForegroundColor Green
                Write-Host "Use Get-VCBackupStatus -Id $($Response.BackupId) to check progress" -ForegroundColor Cyan
            }
            
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCLogs {
    <#
    .SYNOPSIS
        Retrieves VCDevTool system logs
    .DESCRIPTION
        Gets system logs with filtering options for troubleshooting and monitoring
    .PARAMETER Level
        Log level filter (Verbose, Information, Warning, Error)
    .PARAMETER Source
        Log source filter (API, TaskService, NodeService, etc.)
    .PARAMETER StartDate
        Filter logs from this date
    .PARAMETER EndDate
        Filter logs to this date
    .PARAMETER Count
        Maximum number of log entries to return
    .PARAMETER Search
        Search text within log messages
    .EXAMPLE
        Get-VCLogs -Level Error -Count 50
    .EXAMPLE
        Get-VCLogs -Source TaskService -StartDate (Get-Date).AddHours(-1) -Search "failed"
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal')]
        [string]$Level,
        
        [Parameter()]
        [string]$Source,
        
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate,
        
        [Parameter()]
        [ValidateRange(1, 10000)]
        [int]$Count = 100,
        
        [Parameter()]
        [string]$Search
    )
    
    try {
        $QueryParams = @{
            count = $Count
        }
        
        if ($Level) { $QueryParams.level = $Level }
        if ($Source) { $QueryParams.source = $Source }
        if ($Search) { $QueryParams.search = $Search }
        if ($StartDate) { $QueryParams.startDate = $StartDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        if ($EndDate) { $QueryParams.endDate = $EndDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/system/logs" -Method Get -QueryParameters $QueryParams
        
        $LogEntries = @()
        foreach ($LogData in $Response.Logs) {
            $LogEntry = [PSCustomObject]@{
                PSTypeName = 'VCDevTool.LogEntry'
                Timestamp = [DateTime]$LogData.Timestamp
                Level = $LogData.Level
                Source = $LogData.Source
                Message = $LogData.Message
                Exception = $LogData.Exception
                Properties = $LogData.Properties
                CorrelationId = $LogData.CorrelationId
                UserId = $LogData.UserId
                MachineName = $LogData.MachineName
            }
            $LogEntries += $LogEntry
        }
        
        return $LogEntries
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Clear-VCLogs {
    <#
    .SYNOPSIS
        Clears VCDevTool system logs
    .DESCRIPTION
        Removes log entries based on specified criteria. Use with caution.
    .PARAMETER OlderThan
        Remove logs older than specified timespan
    .PARAMETER Level
        Only remove logs of specified level
    .PARAMETER Source
        Only remove logs from specified source
    .PARAMETER Force
        Force clear without confirmation
    .PARAMETER DryRun
        Show what would be deleted without actually deleting
    .EXAMPLE
        Clear-VCLogs -OlderThan (New-TimeSpan -Days 30) -Force
    .EXAMPLE
        Clear-VCLogs -Level Debug -DryRun
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter()]
        [TimeSpan]$OlderThan,
        
        [Parameter()]
        [ValidateSet('Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal')]
        [string]$Level,
        
        [Parameter()]
        [string]$Source,
        
        [Parameter()]
        [switch]$Force,
        
        [Parameter()]
        [switch]$DryRun
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for log management operations"
        }
        
        $ClearRequest = @{
            DryRun = $DryRun.IsPresent
        }
        
        if ($OlderThan) {
            $ClearRequest.OlderThanDays = $OlderThan.TotalDays
        }
        
        if ($Level) {
            $ClearRequest.Level = $Level
        }
        
        if ($Source) {
            $ClearRequest.Source = $Source
        }
        
        # Build description for confirmation
        $Description = "log entries"
        if ($OlderThan) { $Description += " older than $($OlderThan.TotalDays) days" }
        if ($Level) { $Description += " with level $Level" }
        if ($Source) { $Description += " from source $Source" }
        
        if ($DryRun) {
            Write-Host "Dry run mode - no logs will be deleted" -ForegroundColor Yellow
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/logs/clear" -Method Post -Body $ClearRequest
            
            Write-Host "Would delete $($Response.EstimatedDeleteCount) log entries" -ForegroundColor Cyan
            return $Response
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess($Description, "Clear logs")) {
            Write-Host "Clearing logs..." -ForegroundColor Yellow
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/logs/clear" -Method Post -Body $ClearRequest
            
            Write-Host "Cleared $($Response.DeletedCount) log entries" -ForegroundColor Green
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCBackupStatus {
    <#
    .SYNOPSIS
        Gets the status of database backup operations
    .DESCRIPTION
        Retrieves status information for backup operations
    .PARAMETER Id
        Specific backup operation ID
    .PARAMETER All
        Get status of all recent backup operations
    .EXAMPLE
        Get-VCBackupStatus -Id "backup-123"
    .EXAMPLE
        Get-VCBackupStatus -All
    #>
    [CmdletBinding(DefaultParameterSetName = 'Recent')]
    param(
        [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
        [string]$Id,
        
        [Parameter(ParameterSetName = 'All')]
        [switch]$All
    )
    
    try {
        if ($PSCmdlet.ParameterSetName -eq 'ById') {
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/database/backup/$Id/status" -Method Get
        } else {
            $QueryParams = @{}
            if ($All) {
                $QueryParams.all = 'true'
            }
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/database/backup/status" -Method Get -QueryParameters $QueryParams
        }
        
        return $Response
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Invoke-VCSystemMaintenance {
    <#
    .SYNOPSIS
        Performs system maintenance operations
    .DESCRIPTION
        Executes various maintenance tasks like cleanup, optimization, and health checks
    .PARAMETER Operation
        Type of maintenance operation to perform
    .PARAMETER Force
        Force operation without confirmation
    .PARAMETER Schedule
        Schedule operation for later execution
    .EXAMPLE
        Invoke-VCSystemMaintenance -Operation DatabaseOptimization -Force
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('DatabaseOptimization', 'TempFileCleanup', 'LogCleanup', 'PerformanceAnalysis', 'HealthCheck')]
        [string]$Operation,
        
        [Parameter()]
        [switch]$Force,
        
        [Parameter()]
        [DateTime]$Schedule
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for system maintenance operations"
        }
        
        $MaintenanceRequest = @{
            Operation = $Operation
        }
        
        if ($Schedule) {
            $MaintenanceRequest.ScheduledAt = $Schedule.ToString('yyyy-MM-ddTHH:mm:ssZ')
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess("VCDevTool System", "Perform $Operation maintenance")) {
            Write-Host "Starting $Operation maintenance operation..." -ForegroundColor Yellow
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/maintenance" -Method Post -Body $MaintenanceRequest -TimeoutSeconds 1800
            
            if ($Schedule) {
                Write-Host "Maintenance operation scheduled for $Schedule" -ForegroundColor Green
            } else {
                Write-Host "Maintenance operation completed successfully" -ForegroundColor Green
                if ($Response.Results) {
                    $Response.Results | ForEach-Object {
                        Write-Host "  - $($_.Message)" -ForegroundColor Cyan
                    }
                }
            }
            
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCSystemConfiguration {
    <#
    .SYNOPSIS
        Gets VCDevTool system configuration settings
    .DESCRIPTION
        Retrieves current system configuration including database, logging, and performance settings
    .PARAMETER Section
        Specific configuration section to retrieve
    .EXAMPLE
        Get-VCSystemConfiguration
    .EXAMPLE
        Get-VCSystemConfiguration -Section Database
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('Database', 'Logging', 'Performance', 'Security', 'Tasks', 'Nodes')]
        [string]$Section
    )
    
    try {
        $QueryParams = @{}
        if ($Section) {
            $QueryParams.section = $Section
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/system/configuration" -Method Get -QueryParameters $QueryParams
        
        return $Response
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Set-VCSystemConfiguration {
    <#
    .SYNOPSIS
        Updates VCDevTool system configuration settings
    .DESCRIPTION
        Modifies system configuration settings. Requires administrative privileges.
    .PARAMETER Section
        Configuration section to update
    .PARAMETER Settings
        Hashtable of settings to update
    .PARAMETER Force
        Force update without confirmation
    .EXAMPLE
        Set-VCSystemConfiguration -Section Performance -Settings @{MaxConcurrentTasks=10; TaskTimeoutMinutes=60} -Force
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Database', 'Logging', 'Performance', 'Security', 'Tasks', 'Nodes')]
        [string]$Section,
        
        [Parameter(Mandatory = $true)]
        [hashtable]$Settings,
        
        [Parameter()]
        [switch]$Force
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for system configuration changes"
        }
        
        $ConfigRequest = @{
            Section = $Section
            Settings = $Settings
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess("$Section configuration", "Update system settings")) {
            $Response = Invoke-VCApiRequest -Endpoint "/api/system/configuration" -Method Put -Body $ConfigRequest
            
            Write-Host "System configuration updated successfully" -ForegroundColor Green
            
            if ($Response.RequiresRestart) {
                Write-Warning "Configuration changes require system restart to take effect"
            }
            
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
} 