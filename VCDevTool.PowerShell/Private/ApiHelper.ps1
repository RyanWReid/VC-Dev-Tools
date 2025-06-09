function ConvertTo-VCTaskObject {
    <#
    .SYNOPSIS
        Converts API response to PowerShell task object
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$ApiResponse
    )
    
    $TaskObject = [PSCustomObject]@{
        PSTypeName = 'VCDevTool.Task'
        Id = $ApiResponse.Id
        Type = $ApiResponse.Type
        Status = $ApiResponse.Status
        Priority = $ApiResponse.Priority
        Progress = $ApiResponse.Progress
        CreatedAt = [DateTime]$ApiResponse.CreatedAt
        StartedAt = if ($ApiResponse.StartedAt) { [DateTime]$ApiResponse.StartedAt } else { $null }
        CompletedAt = if ($ApiResponse.CompletedAt) { [DateTime]$ApiResponse.CompletedAt } else { $null }
        AssignedNode = $ApiResponse.AssignedNode
        Parameters = $ApiResponse.Parameters
        Result = $ApiResponse.Result
        ErrorMessage = $ApiResponse.ErrorMessage
        EstimatedDuration = $ApiResponse.EstimatedDuration
        ActualDuration = $ApiResponse.ActualDuration
    }
    
    return $TaskObject
}

function ConvertTo-VCNodeObject {
    <#
    .SYNOPSIS
        Converts API response to PowerShell node object
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$ApiResponse
    )
    
    $NodeObject = [PSCustomObject]@{
        PSTypeName = 'VCDevTool.Node'
        Id = $ApiResponse.Id
        Name = $ApiResponse.Name
        Status = $ApiResponse.Status
        IsOnline = $ApiResponse.IsOnline
        LastSeen = if ($ApiResponse.LastSeen) { [DateTime]$ApiResponse.LastSeen } else { $null }
        Version = $ApiResponse.Version
        Platform = $ApiResponse.Platform
        Architecture = $ApiResponse.Architecture
        ActiveTasks = $ApiResponse.ActiveTasks
        CompletedTasks = $ApiResponse.CompletedTasks
        FailedTasks = $ApiResponse.FailedTasks
        Capabilities = $ApiResponse.Capabilities
        Configuration = $ApiResponse.Configuration
        PerformanceMetrics = $ApiResponse.PerformanceMetrics
        MachineName = $ApiResponse.MachineName
        UserName = $ApiResponse.UserName
        ProcessId = $ApiResponse.ProcessId
    }
    
    return $NodeObject
}

function Format-VCApiError {
    <#
    .SYNOPSIS
        Formats API errors into user-friendly messages
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )
    
    $ErrorMessage = "VCDevTool API Error: "
    
    if ($ErrorRecord.Exception -is [Microsoft.PowerShell.Commands.HttpResponseException]) {
        $StatusCode = $ErrorRecord.Exception.Response.StatusCode
        $ErrorMessage += "HTTP $([int]$StatusCode) ($StatusCode)"
        
        try {
            $ResponseContent = $ErrorRecord.ErrorDetails.Message | ConvertFrom-Json
            if ($ResponseContent.Message) {
                $ErrorMessage += " - $($ResponseContent.Message)"
            }
            if ($ResponseContent.Details) {
                $ErrorMessage += " Details: $($ResponseContent.Details)"
            }
        }
        catch {
            $ErrorMessage += " - $($ErrorRecord.Exception.Message)"
        }
    }
    else {
        $ErrorMessage += $ErrorRecord.Exception.Message
    }
    
    return $ErrorMessage
}

function Test-VCTaskType {
    <#
    .SYNOPSIS
        Validates if a task type is supported
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TaskType
    )
    
    $SupportedTypes = @(
        'VolumeCompression',
        'RealityCapture',
        'PackageTask',
        'TestMessage',
        'RenderThumbnails',
        'DataProcessing',
        'FileTransfer',
        'SystemMaintenance'
    )
    
    return $TaskType -in $SupportedTypes
}

function Get-VCTaskStatusColor {
    <#
    .SYNOPSIS
        Gets console color for task status display
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status
    )
    
    switch ($Status.ToLower()) {
        'pending' { return 'Yellow' }
        'running' { return 'Cyan' }
        'completed' { return 'Green' }
        'failed' { return 'Red' }
        'cancelled' { return 'Magenta' }
        'paused' { return 'Blue' }
        default { return 'White' }
    }
}

function ConvertFrom-VCTimeSpan {
    <#
    .SYNOPSIS
        Converts TimeSpan strings from API to TimeSpan objects
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$TimeSpanString
    )
    
    if ([string]::IsNullOrEmpty($TimeSpanString)) {
        return $null
    }
    
    try {
        return [TimeSpan]::Parse($TimeSpanString)
    }
    catch {
        Write-Warning "Failed to parse TimeSpan: $TimeSpanString"
        return $null
    }
}

function Get-VCPagingParameters {
    <#
    .SYNOPSIS
        Builds paging parameters for API requests
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$Page = 1,
        
        [Parameter()]
        [int]$PageSize = 50,
        
        [Parameter()]
        [string]$SortBy,
        
        [Parameter()]
        [string]$SortOrder = 'asc'
    )
    
    $Parameters = @{
        page = $Page
        pageSize = $PageSize
    }
    
    if ($SortBy) {
        $Parameters.sortBy = $SortBy
        $Parameters.sortOrder = $SortOrder
    }
    
    return $Parameters
}

function Invoke-VCBulkOperation {
    <#
    .SYNOPSIS
        Performs bulk operations with progress tracking
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [array]$Items,
        
        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,
        
        [Parameter()]
        [string]$Activity = "Processing items",
        
        [Parameter()]
        [int]$BatchSize = 10
    )
    
    $Results = @()
    $TotalItems = $Items.Count
    $ProcessedItems = 0
    
    Write-Progress -Activity $Activity -Status "Starting..." -PercentComplete 0
    
    for ($i = 0; $i -lt $TotalItems; $i += $BatchSize) {
        $Batch = $Items[$i..($i + $BatchSize - 1)]
        
        foreach ($CurrentItem in $Batch) {
            try {
                $Result = & $Operation $CurrentItem
                $Results += $Result
            }
            catch {
                Write-Warning "Failed to process item $CurrentItem`: $($_.Exception.Message)"
                $Results += $null
            }
            
            $ProcessedItems++
            $PercentComplete = [math]::Round(($ProcessedItems / $TotalItems) * 100, 2)
            Write-Progress -Activity $Activity -Status "Processed $ProcessedItems of $TotalItems" -PercentComplete $PercentComplete
        }
    }
    
    Write-Progress -Activity $Activity -Completed
    return $Results
}

function Test-VCApiEndpoint {
    <#
    .SYNOPSIS
        Tests if an API endpoint is accessible
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,
        
        [Parameter()]
        [int]$TimeoutSeconds = 10
    )
    
    try {
        $TestUri = if ($Endpoint.EndsWith('/')) { "$($Endpoint)api/health" } else { "$Endpoint/api/health" }
        $Response = Invoke-RestMethod -Uri $TestUri -Method Get -TimeoutSec $TimeoutSeconds
        return $Response.Status -eq "Healthy"
    }
    catch {
        return $false
    }
} 