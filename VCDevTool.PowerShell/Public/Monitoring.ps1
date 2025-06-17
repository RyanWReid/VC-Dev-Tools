function Get-VCPerformanceMetrics {
    <#
    .SYNOPSIS
        Gets VCDevTool system performance metrics
    .DESCRIPTION
        Retrieves performance data including CPU, memory, disk usage, and task processing metrics
    .PARAMETER Hours
        Number of hours of historical data to retrieve (default: 1)
    .PARAMETER MetricType
        Specific type of metrics to retrieve
    .PARAMETER NodeId
        Get metrics for a specific node
    .PARAMETER Granularity
        Data granularity (Minute, Hour, Day)
    .EXAMPLE
        Get-VCPerformanceMetrics -Hours 24 -Granularity Hour
    .EXAMPLE
        Get-VCPerformanceMetrics -NodeId 1 -MetricType CPU -Hours 6
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateRange(1, 168)] # Max 1 week
        [int]$Hours = 1,
        
        [Parameter()]
        [ValidateSet('CPU', 'Memory', 'Disk', 'Network', 'Tasks', 'All')]
        [string]$MetricType = 'All',
        
        [Parameter()]
        [int]$NodeId,
        
        [Parameter()]
        [ValidateSet('Minute', 'Hour', 'Day')]
        [string]$Granularity = 'Minute'
    )
    
    try {
        $QueryParams = @{
            hours = $Hours
            metricType = $MetricType
            granularity = $Granularity
        }
        
        if ($NodeId) {
            if (-not (Test-VCNodeId -NodeId $NodeId)) {
                throw "Invalid node ID: $NodeId"
            }
            $QueryParams.nodeId = $NodeId
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/monitoring/performance" -Method Get -QueryParameters $QueryParams
        
        $Metrics = @()
        foreach ($MetricData in $Response.Metrics) {
            $Metric = [PSCustomObject]@{
                PSTypeName = 'VCDevTool.PerformanceMetric'
                Timestamp = [DateTime]$MetricData.Timestamp
                NodeId = $MetricData.NodeId
                NodeName = $MetricData.NodeName
                MetricType = $MetricData.MetricType
                Value = $MetricData.Value
                Unit = $MetricData.Unit
                Threshold = $MetricData.Threshold
                Status = $MetricData.Status
                Details = $MetricData.Details
            }
            $Metrics += $Metric
        }
        
        # Add summary statistics
        if ($Response.Summary) {
            $Metrics | Add-Member -NotePropertyName "Summary" -NotePropertyValue $Response.Summary
        }
        
        return $Metrics
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCTaskStatistics {
    <#
    .SYNOPSIS
        Gets task processing statistics and analytics
    .DESCRIPTION
        Retrieves comprehensive statistics about task execution, performance, and trends
    .PARAMETER TimeRange
        Time range for statistics (Hour, Day, Week, Month)
    .PARAMETER GroupBy
        Group statistics by specified field
    .PARAMETER TaskType
        Filter by specific task type
    .PARAMETER IncludeTrends
        Include trend analysis
    .EXAMPLE
        Get-VCTaskStatistics -TimeRange Week -GroupBy Type -IncludeTrends
    .EXAMPLE
        Get-VCTaskStatistics -TimeRange Day -TaskType VolumeCompression
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('Hour', 'Day', 'Week', 'Month', 'Quarter', 'Year')]
        [string]$TimeRange = 'Day',
        
        [Parameter()]
        [ValidateSet('Type', 'Status', 'Priority', 'Node', 'User', 'Hour', 'Day')]
        [string]$GroupBy,
        
        [Parameter()]
        [string]$TaskType,
        
        [Parameter()]
        [switch]$IncludeTrends
    )
    
    try {
        $QueryParams = @{
            timeRange = $TimeRange
            includeTrends = $IncludeTrends.IsPresent
        }
        
        if ($GroupBy) {
            $QueryParams.groupBy = $GroupBy
        }
        
        if ($TaskType) {
            if (-not (Test-VCTaskType -TaskType $TaskType)) {
                throw "Invalid task type: $TaskType"
            }
            $QueryParams.taskType = $TaskType
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/monitoring/tasks/statistics" -Method Get -QueryParameters $QueryParams
        
        $Statistics = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.TaskStatistics'
            TimeRange = $TimeRange
            GeneratedAt = Get-Date
            TotalTasks = $Response.TotalTasks
            CompletedTasks = $Response.CompletedTasks
            FailedTasks = $Response.FailedTasks
            CancelledTasks = $Response.CancelledTasks
            AverageExecutionTime = $Response.AverageExecutionTimeSeconds
            MedianExecutionTime = $Response.MedianExecutionTimeSeconds
            SuccessRate = $Response.SuccessRate
            FailureRate = $Response.FailureRate
            ThroughputPerHour = $Response.ThroughputPerHour
            GroupedStatistics = $Response.GroupedStatistics
        }
        
        if ($IncludeTrends -and $Response.Trends) {
            $Statistics | Add-Member -NotePropertyName "Trends" -NotePropertyValue $Response.Trends
        }
        
        return $Statistics
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCNodeStatistics {
    <#
    .SYNOPSIS
        Gets node performance and utilization statistics
    .DESCRIPTION
        Retrieves statistics about node performance, task distribution, and resource utilization
    .PARAMETER NodeId
        Specific node ID to get statistics for
    .PARAMETER TimeRange
        Time range for statistics
    .PARAMETER IncludeCapacityAnalysis
        Include capacity planning analysis
    .PARAMETER IncludeHealthHistory
        Include health status history
    .EXAMPLE
        Get-VCNodeStatistics -TimeRange Week -IncludeCapacityAnalysis
    .EXAMPLE
        Get-VCNodeStatistics -NodeId 1 -TimeRange Day -IncludeHealthHistory
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$NodeId,
        
        [Parameter()]
        [ValidateSet('Hour', 'Day', 'Week', 'Month')]
        [string]$TimeRange = 'Day',
        
        [Parameter()]
        [switch]$IncludeCapacityAnalysis,
        
        [Parameter()]
        [switch]$IncludeHealthHistory
    )
    
    try {
        $QueryParams = @{
            timeRange = $TimeRange
            includeCapacityAnalysis = $IncludeCapacityAnalysis.IsPresent
            includeHealthHistory = $IncludeHealthHistory.IsPresent
        }
        
        if ($NodeId) {
            if (-not (Test-VCNodeId -NodeId $NodeId)) {
                throw "Invalid node ID: $NodeId"
            }
            $QueryParams.nodeId = $NodeId
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/monitoring/nodes/statistics" -Method Get -QueryParameters $QueryParams
        
        if ($NodeId) {
            # Single node statistics
            $NodeStats = [PSCustomObject]@{
                PSTypeName = 'VCDevTool.NodeStatistics'
                NodeId = $Response.NodeId
                NodeName = $Response.NodeName
                TimeRange = $TimeRange
                GeneratedAt = Get-Date
                TasksProcessed = $Response.TasksProcessed
                TasksCompleted = $Response.TasksCompleted
                TasksFailed = $Response.TasksFailed
                AverageTaskDuration = $Response.AverageTaskDurationSeconds
                Utilization = $Response.UtilizationPercentage
                UptimePercentage = $Response.UptimePercentage
                AverageResponseTime = $Response.AverageResponseTimeMs
                ResourceUtilization = $Response.ResourceUtilization
            }
            
            if ($IncludeCapacityAnalysis -and $Response.CapacityAnalysis) {
                $NodeStats | Add-Member -NotePropertyName "CapacityAnalysis" -NotePropertyValue $Response.CapacityAnalysis
            }
            
            if ($IncludeHealthHistory -and $Response.HealthHistory) {
                $NodeStats | Add-Member -NotePropertyName "HealthHistory" -NotePropertyValue $Response.HealthHistory
            }
            
            return $NodeStats
        }
        else {
            # All nodes statistics
            $AllNodesStats = @()
            foreach ($NodeData in $Response.Nodes) {
                $NodeStats = [PSCustomObject]@{
                    PSTypeName = 'VCDevTool.NodeStatistics'
                    NodeId = $NodeData.NodeId
                    NodeName = $NodeData.NodeName
                    TimeRange = $TimeRange
                    TasksProcessed = $NodeData.TasksProcessed
                    TasksCompleted = $NodeData.TasksCompleted
                    TasksFailed = $NodeData.TasksFailed
                    AverageTaskDuration = $NodeData.AverageTaskDurationSeconds
                    Utilization = $NodeData.UtilizationPercentage
                    UptimePercentage = $NodeData.UptimePercentage
                    AverageResponseTime = $NodeData.AverageResponseTimeMs
                    ResourceUtilization = $NodeData.ResourceUtilization
                }
                $AllNodesStats += $NodeStats
            }
            
            # Add summary statistics
            if ($Response.Summary) {
                $AllNodesStats | Add-Member -NotePropertyName "Summary" -NotePropertyValue $Response.Summary
            }
            
            return $AllNodesStats
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Export-VCDiagnostics {
    <#
    .SYNOPSIS
        Exports comprehensive diagnostic information
    .DESCRIPTION
        Creates a diagnostic package containing logs, configuration, performance data, and system information
    .PARAMETER OutputPath
        Path where diagnostic package will be saved
    .PARAMETER IncludeLogs
        Include system logs in the diagnostic package
    .PARAMETER IncludePerformanceData
        Include performance metrics
    .PARAMETER IncludeConfiguration
        Include system configuration
    .PARAMETER DaysHistory
        Number of days of historical data to include
    .PARAMETER Compress
        Create compressed archive
    .EXAMPLE
        Export-VCDiagnostics -OutputPath "C:\Diagnostics\VCDiag_$(Get-Date -Format 'yyyyMMdd').zip" -IncludeLogs -IncludePerformanceData -DaysHistory 7 -Compress
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        
        [Parameter()]
        [switch]$IncludeLogs = $true,
        
        [Parameter()]
        [switch]$IncludePerformanceData = $true,
        
        [Parameter()]
        [switch]$IncludeConfiguration = $true,
        
        [Parameter()]
        [ValidateRange(1, 30)]
        [int]$DaysHistory = 3,
        
        [Parameter()]
        [switch]$Compress = $true
    )
    
    try {
        # Validate output path
        $OutputDir = Split-Path $OutputPath -Parent
        if (-not (Test-Path $OutputDir)) {
            New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
        }
        
        $DiagnosticsRequest = @{
            IncludeLogs = $IncludeLogs.IsPresent
            IncludePerformanceData = $IncludePerformanceData.IsPresent
            IncludeConfiguration = $IncludeConfiguration.IsPresent
            DaysHistory = $DaysHistory
            Compress = $Compress.IsPresent
        }
        
        if ($PSCmdlet.ShouldProcess("VCDevTool System", "Export diagnostics to $OutputPath")) {
            Write-Host "Generating diagnostic package..." -ForegroundColor Yellow
            Write-Host "This may take several minutes depending on data size..." -ForegroundColor Cyan
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/monitoring/diagnostics/export" -Method Post -Body $DiagnosticsRequest -TimeoutSeconds 1800
            
            # Download the diagnostic package
            if ($Response.DownloadUrl) {
                Write-Host "Downloading diagnostic package..." -ForegroundColor Yellow
                
                try {
                    $WebClient = New-Object System.Net.WebClient
                    if ($script:VCConnection.Headers.Authorization) {
                        $WebClient.Headers.Add("Authorization", $script:VCConnection.Headers.Authorization)
                    }
                    $WebClient.DownloadFile($Response.DownloadUrl, $OutputPath)
                    $WebClient.Dispose()
                }
                catch {
                    throw "Failed to download diagnostic package: $($_.Exception.Message)"
                }
            }
            else {
                # Direct response data
                $Response.Data | Out-File -FilePath $OutputPath -Encoding UTF8
            }
            
            $FileInfo = Get-Item $OutputPath
            
            Write-Host "Diagnostic package created successfully" -ForegroundColor Green
            Write-Host "File: $($FileInfo.FullName)" -ForegroundColor Cyan
            Write-Host "Size: $([math]::Round($FileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
            
            # Display package contents summary
            if ($Response.PackageContents) {
                Write-Host "`nPackage Contents:" -ForegroundColor Yellow
                $Response.PackageContents | ForEach-Object {
                    Write-Host "  - $($_.Name): $($_.Description) ($($_.Size))" -ForegroundColor Cyan
                }
            }
            
            return [PSCustomObject]@{
                PSTypeName = 'VCDevTool.DiagnosticPackage'
                FilePath = $FileInfo.FullName
                SizeBytes = $FileInfo.Length
                CreatedAt = Get-Date
                IncludedComponents = @{
                    Logs = $IncludeLogs.IsPresent
                    PerformanceData = $IncludePerformanceData.IsPresent
                    Configuration = $IncludeConfiguration.IsPresent
                }
                HistoryDays = $DaysHistory
                Compressed = $Compress.IsPresent
                Contents = $Response.PackageContents
            }
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCSystemHealth {
    <#
    .SYNOPSIS
        Gets overall system health status and alerts
    .DESCRIPTION
        Performs comprehensive health checks and returns system status with any alerts or warnings
    .PARAMETER IncludeRecommendations
        Include performance and configuration recommendations
    .PARAMETER CheckConnectivity
        Include connectivity tests to all nodes
    .EXAMPLE
        Get-VCSystemHealth -IncludeRecommendations -CheckConnectivity
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$IncludeRecommendations,
        
        [Parameter()]
        [switch]$CheckConnectivity
    )
    
    try {
        $QueryParams = @{
            includeRecommendations = $IncludeRecommendations.IsPresent
            checkConnectivity = $CheckConnectivity.IsPresent
        }
        
        Write-Host "Performing system health check..." -ForegroundColor Yellow
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/monitoring/health" -Method Get -QueryParameters $QueryParams -TimeoutSeconds 120
        
        $HealthStatus = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.SystemHealth'
            OverallStatus = $Response.OverallStatus
            Score = $Response.HealthScore
            CheckedAt = Get-Date
            Components = @()
            Alerts = @()
            Warnings = @()
            Recommendations = @()
        }
        
        # Process component health
        foreach ($Component in $Response.Components) {
            $ComponentHealth = [PSCustomObject]@{
                Name = $Component.Name
                Status = $Component.Status
                Score = $Component.Score
                Message = $Component.Message
                LastCheck = [DateTime]$Component.LastCheck
                Details = $Component.Details
            }
            $HealthStatus.Components += $ComponentHealth
        }
        
        # Process alerts
        foreach ($Alert in $Response.Alerts) {
            $AlertObject = [PSCustomObject]@{
                Severity = $Alert.Severity
                Component = $Alert.Component
                Message = $Alert.Message
                Timestamp = [DateTime]$Alert.Timestamp
                ActionRequired = $Alert.ActionRequired
            }
            $HealthStatus.Alerts += $AlertObject
        }
        
        # Process warnings
        foreach ($Warning in $Response.Warnings) {
            $WarningObject = [PSCustomObject]@{
                Component = $Warning.Component
                Message = $Warning.Message
                Timestamp = [DateTime]$Warning.Timestamp
                Suggestion = $Warning.Suggestion
            }
            $HealthStatus.Warnings += $WarningObject
        }
        
        # Process recommendations
        if ($IncludeRecommendations -and $Response.Recommendations) {
            foreach ($Recommendation in $Response.Recommendations) {
                $RecommendationObject = [PSCustomObject]@{
                    Category = $Recommendation.Category
                    Priority = $Recommendation.Priority
                    Title = $Recommendation.Title
                    Description = $Recommendation.Description
                    Action = $Recommendation.Action
                    Impact = $Recommendation.Impact
                }
                $HealthStatus.Recommendations += $RecommendationObject
            }
        }
        
        # Display summary
        $StatusColor = switch ($HealthStatus.OverallStatus) {
            'Healthy' { 'Green' }
            'Warning' { 'Yellow' }
            'Critical' { 'Red' }
            default { 'White' }
        }
        
        Write-Host "System Health: $($HealthStatus.OverallStatus) (Score: $($HealthStatus.Score)/100)" -ForegroundColor $StatusColor
        
        if ($HealthStatus.Alerts.Count -gt 0) {
            Write-Host "Alerts: $($HealthStatus.Alerts.Count)" -ForegroundColor Red
        }
        
        if ($HealthStatus.Warnings.Count -gt 0) {
            Write-Host "Warnings: $($HealthStatus.Warnings.Count)" -ForegroundColor Yellow
        }
        
        if ($HealthStatus.Recommendations.Count -gt 0) {
            Write-Host "Recommendations: $($HealthStatus.Recommendations.Count)" -ForegroundColor Cyan
        }
        
        return $HealthStatus
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Watch-VCSystemMetrics {
    <#
    .SYNOPSIS
        Continuously monitors and displays real-time system metrics
    .DESCRIPTION
        Provides a real-time dashboard view of system performance metrics
    .PARAMETER RefreshInterval
        Refresh interval in seconds (default: 10)
    .PARAMETER MetricTypes
        Types of metrics to display
    .PARAMETER Duration
        How long to monitor (in minutes, 0 = infinite)
    .EXAMPLE
        Watch-VCSystemMetrics -RefreshInterval 5 -Duration 30
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateRange(5, 300)]
        [int]$RefreshInterval = 10,
        
        [Parameter()]
        [string[]]$MetricTypes = @('CPU', 'Memory', 'Tasks'),
        
        [Parameter()]
        [int]$Duration = 0
    )
    
    try {
        $StartTime = Get-Date
        $EndTime = if ($Duration -gt 0) { $StartTime.AddMinutes($Duration) } else { [DateTime]::MaxValue }
        
        Write-Host "Starting real-time system monitoring..." -ForegroundColor Green
        Write-Host "Press Ctrl+C to stop monitoring" -ForegroundColor Yellow
        Write-Host "Refresh interval: $RefreshInterval seconds" -ForegroundColor Cyan
        if ($Duration -gt 0) {
            Write-Host "Monitoring duration: $Duration minutes" -ForegroundColor Cyan
        }
        Write-Host "`n" + ("=" * 80) + "`n" -ForegroundColor White
        
        do {
            try {
                # Clear previous display (keeping header)
                $CursorTop = $Host.UI.RawUI.CursorPosition.Y
                
                # Get current metrics
                $SystemStatus = Get-VCSystemStatus -Detailed
                $CurrentTime = Get-Date
                
                # Display header
                Write-Host "VCDevTool System Monitor - $($CurrentTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor White
                Write-Host ("=" * 80) -ForegroundColor White
                
                # Display system overview
                Write-Host "System Status: " -NoNewline
                $StatusColor = if ($SystemStatus.Status -eq 'Healthy') { 'Green' } else { 'Yellow' }
                Write-Host $SystemStatus.Status -ForegroundColor $StatusColor
                
                Write-Host "Nodes: $($SystemStatus.OnlineNodes)/$($SystemStatus.TotalNodes) online" -ForegroundColor Cyan
                Write-Host "Tasks: $($SystemStatus.RunningTasks) running, $($SystemStatus.PendingTasks) pending" -ForegroundColor Cyan
                
                # Display metrics
                if ('CPU' -in $MetricTypes -and $SystemStatus.SystemLoad) {
                    $LoadColor = if ($SystemStatus.SystemLoad -gt 80) { 'Red' } elseif ($SystemStatus.SystemLoad -gt 60) { 'Yellow' } else { 'Green' }
                    Write-Host "CPU Load: $($SystemStatus.SystemLoad)%" -ForegroundColor $LoadColor
                }
                
                if ('Memory' -in $MetricTypes -and $SystemStatus.MemoryUsage) {
                    $MemColor = if ($SystemStatus.MemoryUsage -gt 85) { 'Red' } elseif ($SystemStatus.MemoryUsage -gt 70) { 'Yellow' } else { 'Green' }
                    Write-Host "Memory Usage: $($SystemStatus.MemoryUsage)%" -ForegroundColor $MemColor
                }
                
                if ('Disk' -in $MetricTypes -and $SystemStatus.DiskUsage) {
                    $DiskColor = if ($SystemStatus.DiskUsage -gt 90) { 'Red' } elseif ($SystemStatus.DiskUsage -gt 80) { 'Yellow' } else { 'Green' }
                    Write-Host "Disk Usage: $($SystemStatus.DiskUsage)%" -ForegroundColor $DiskColor
                }
                
                Write-Host "`nNext refresh in $RefreshInterval seconds..." -ForegroundColor Gray
                
                # Wait for next refresh
                Start-Sleep -Seconds $RefreshInterval
                
                # Clear for next iteration
                Clear-Host
                
            }
            catch {
                Write-Warning "Error updating metrics: $($_.Exception.Message)"
                Start-Sleep -Seconds $RefreshInterval
            }
            
        } while ((Get-Date) -lt $EndTime)
        
        Write-Host "`nMonitoring stopped." -ForegroundColor Yellow
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
} 