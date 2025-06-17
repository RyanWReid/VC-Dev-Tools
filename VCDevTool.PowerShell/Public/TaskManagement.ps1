function Get-VCTask {
    <#
    .SYNOPSIS
        Retrieves VCDevTool task information
    .DESCRIPTION
        Gets task details from the VCDevTool API. Can retrieve specific tasks by ID or filter tasks by various criteria.
    .PARAMETER Id
        Specific task ID to retrieve
    .PARAMETER Status
        Filter tasks by status (Pending, Running, Completed, Failed, Cancelled)
    .PARAMETER Type
        Filter tasks by type (VolumeCompression, RealityCapture, PackageTask, etc.)
    .PARAMETER AssignedNode
        Filter tasks assigned to a specific node
    .PARAMETER StartDate
        Filter tasks created after this date
    .PARAMETER EndDate
        Filter tasks created before this date
    .PARAMETER Page
        Page number for pagination (default: 1)
    .PARAMETER PageSize
        Number of results per page (default: 50)
    .EXAMPLE
        Get-VCTask
        Get all tasks with default pagination
    .EXAMPLE
        Get-VCTask -Id 123
        Get specific task by ID
    .EXAMPLE
        Get-VCTask -Status Running -Type VolumeCompression
        Get all running volume compression tasks
    #>
    [CmdletBinding(DefaultParameterSetName = 'All')]
    param(
        [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
        [int]$Id,
        
        [Parameter(ParameterSetName = 'Filter')]
        [ValidateSet('Pending', 'Running', 'Completed', 'Failed', 'Cancelled', 'Paused')]
        [string]$Status,
        
        [Parameter(ParameterSetName = 'Filter')]
        [ValidateSet('VolumeCompression', 'RealityCapture', 'PackageTask', 'TestMessage', 'RenderThumbnails')]
        [string]$Type,
        
        [Parameter(ParameterSetName = 'Filter')]
        [string]$AssignedNode,
        
        [Parameter(ParameterSetName = 'Filter')]
        [DateTime]$StartDate,
        
        [Parameter(ParameterSetName = 'Filter')]
        [DateTime]$EndDate,
        
        [Parameter(ParameterSetName = 'All')]
        [Parameter(ParameterSetName = 'Filter')]
        [int]$Page = 1,
        
        [Parameter(ParameterSetName = 'All')]
        [Parameter(ParameterSetName = 'Filter')]
        [ValidateRange(1, 100)]
        [int]$PageSize = 50
    )
    
    try {
        if ($PSCmdlet.ParameterSetName -eq 'ById') {
            # Get specific task
            if (-not (Test-VCTaskId -TaskId $Id)) {
                throw "Invalid task ID: $Id"
            }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/tasks/$Id" -Method Get
            return ConvertTo-VCTaskObject -ApiResponse $Response
        }
        else {
            # Get tasks with filters
            $QueryParams = Get-VCPagingParameters -Page $Page -PageSize $PageSize -SortBy "CreatedAt" -SortOrder "desc"
            
            if ($Status) { $QueryParams.status = $Status }
            if ($Type) { $QueryParams.type = $Type }
            if ($AssignedNode) { $QueryParams.assignedNode = $AssignedNode }
            if ($StartDate) { $QueryParams.startDate = $StartDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
            if ($EndDate) { $QueryParams.endDate = $EndDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/tasks" -Method Get -QueryParameters $QueryParams
            
            $Tasks = @()
            foreach ($TaskData in $Response.Tasks) {
                $Tasks += ConvertTo-VCTaskObject -ApiResponse $TaskData
            }
            
            # Add pagination info as note properties
            if ($Response.TotalCount) {
                $Tasks | Add-Member -NotePropertyName "TotalCount" -NotePropertyValue $Response.TotalCount
                $Tasks | Add-Member -NotePropertyName "CurrentPage" -NotePropertyValue $Page
                $Tasks | Add-Member -NotePropertyName "PageSize" -NotePropertyValue $PageSize
                $Tasks | Add-Member -NotePropertyName "TotalPages" -NotePropertyValue ([Math]::Ceiling($Response.TotalCount / $PageSize))
            }
            
            return $Tasks
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Start-VCTask {
    <#
    .SYNOPSIS
        Creates and starts a new VCDevTool task
    .DESCRIPTION
        Creates a new task in the VCDevTool system with specified parameters
    .PARAMETER Type
        Type of task to create
    .PARAMETER Parameters
        Task-specific parameters as a hashtable
    .PARAMETER Priority
        Task priority (1-10 or Low/Normal/High/Critical)
    .PARAMETER AssignToNode
        Specific node to assign the task to (optional)
    .PARAMETER EstimatedDuration
        Estimated duration for the task in minutes
    .EXAMPLE
        Start-VCTask -Type TestMessage -Parameters @{Message="Hello World"} -Priority Normal
    .EXAMPLE
        Start-VCTask -Type VolumeCompression -Parameters @{
            SourcePath="C:\Data\Input"
            TargetPath="C:\Data\Output"
            CompressionLevel="Normal"
        } -Priority High
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('VolumeCompression', 'RealityCapture', 'PackageTask', 'TestMessage', 'RenderThumbnails')]
        [string]$Type,
        
        [Parameter(Mandatory = $true)]
        [hashtable]$Parameters,
        
        [Parameter()]
        [object]$Priority = 'Normal',
        
        [Parameter()]
        [string]$AssignToNode,
        
        [Parameter()]
        [int]$EstimatedDuration
    )
    
    try {
        # Validate parameters
        if (-not (Test-VCTaskType -TaskType $Type)) {
            throw "Unsupported task type: $Type"
        }
        
        if (-not (Test-VCTaskParameters -TaskType $Type -Parameters $Parameters)) {
            throw "Invalid parameters for task type $Type"
        }
        
        if (-not (Test-VCPriority -Priority $Priority)) {
            throw "Invalid priority value: $Priority"
        }
        
        $PriorityInt = ConvertTo-VCPriorityInt -Priority $Priority
        
        # Build task creation request
        $TaskRequest = @{
            Type = $Type
            Parameters = $Parameters
            Priority = $PriorityInt
        }
        
        if ($AssignToNode) {
            $TaskRequest.AssignedNode = $AssignToNode
        }
        
        if ($EstimatedDuration) {
            $TaskRequest.EstimatedDuration = $EstimatedDuration
        }
        
        # Confirm action if needed
        $TaskDescription = "$Type task with priority $Priority"
        if ($AssignToNode) {
            $TaskDescription += " assigned to $AssignToNode"
        }
        
        if ($PSCmdlet.ShouldProcess($TaskDescription, "Create and start task")) {
            $Response = Invoke-VCApiRequest -Endpoint "/api/tasks" -Method Post -Body $TaskRequest
            
            Write-Host "Task created successfully with ID: $($Response.Id)" -ForegroundColor Green
            return ConvertTo-VCTaskObject -ApiResponse $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Stop-VCTask {
    <#
    .SYNOPSIS
        Stops a running VCDevTool task
    .DESCRIPTION
        Cancels a running task or removes a pending task from the queue
    .PARAMETER Id
        Task ID to stop
    .PARAMETER Force
        Force stop without confirmation
    .PARAMETER Reason
        Optional reason for stopping the task
    .EXAMPLE
        Stop-VCTask -Id 123
    .EXAMPLE
        Stop-VCTask -Id 123 -Force -Reason "System maintenance required"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$Force,
        
        [Parameter()]
        [string]$Reason
    )
    
    process {
        try {
            if (-not (Test-VCTaskId -TaskId $Id)) {
                throw "Invalid task ID: $Id"
            }
            
            # Get current task status
            $Task = Get-VCTask -Id $Id
            if (-not $Task) {
                throw "Task not found: $Id"
            }
            
            if ($Task.Status -in @('Completed', 'Failed', 'Cancelled')) {
                Write-Warning "Task $Id is already in $($Task.Status) status"
                return
            }
            
            $StopRequest = @{}
            if ($Reason) {
                $StopRequest.Reason = $Reason
            }
            
            if ($Force -or $PSCmdlet.ShouldProcess("Task $Id ($($Task.Type))", "Stop task")) {
                $Response = Invoke-VCApiRequest -Endpoint "/api/tasks/$Id/cancel" -Method Post -Body $StopRequest
                
                Write-Host "Task $Id has been stopped" -ForegroundColor Yellow
                return ConvertTo-VCTaskObject -ApiResponse $Response
            }
        }
        catch {
            Write-Error (Format-VCApiError -ErrorRecord $_)
        }
    }
}

function Restart-VCTask {
    <#
    .SYNOPSIS
        Restarts a failed or completed VCDevTool task
    .DESCRIPTION
        Creates a new task with the same parameters as a previous task
    .PARAMETER Id
        Original task ID to restart
    .PARAMETER Force
        Force restart without confirmation
    .EXAMPLE
        Restart-VCTask -Id 123
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$Force
    )
    
    try {
        if (-not (Test-VCTaskId -TaskId $Id)) {
            throw "Invalid task ID: $Id"
        }
        
        # Get original task
        $OriginalTask = Get-VCTask -Id $Id
        if (-not $OriginalTask) {
            throw "Task not found: $Id"
        }
        
        if ($OriginalTask.Status -eq 'Running') {
            throw "Cannot restart a running task. Stop it first."
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess("Task $Id ($($OriginalTask.Type))", "Restart task")) {
            $RestartRequest = @{
                OriginalTaskId = $Id
            }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/tasks/restart" -Method Post -Body $RestartRequest
            
            Write-Host "Task restarted with new ID: $($Response.Id)" -ForegroundColor Green
            return ConvertTo-VCTaskObject -ApiResponse $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Remove-VCTask {
    <#
    .SYNOPSIS
        Removes a VCDevTool task from the system
    .DESCRIPTION
        Permanently deletes a task record. Only completed, failed, or cancelled tasks can be removed.
    .PARAMETER Id
        Task ID to remove
    .PARAMETER Force
        Force removal without confirmation
    .EXAMPLE
        Remove-VCTask -Id 123 -Force
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$Force
    )
    
    process {
        try {
            if (-not (Test-VCTaskId -TaskId $Id)) {
                throw "Invalid task ID: $Id"
            }
            
            # Get task to verify it can be removed
            $Task = Get-VCTask -Id $Id
            if (-not $Task) {
                throw "Task not found: $Id"
            }
            
            if ($Task.Status -in @('Running', 'Pending')) {
                throw "Cannot remove active tasks. Stop the task first."
            }
            
            if ($Force -or $PSCmdlet.ShouldProcess("Task $Id ($($Task.Type))", "Remove task permanently")) {
                Invoke-VCApiRequest -Endpoint "/api/tasks/$Id" -Method Delete
                
                Write-Host "Task $Id has been removed" -ForegroundColor Green
            }
        }
        catch {
            Write-Error (Format-VCApiError -ErrorRecord $_)
        }
    }
}

function Set-VCTaskPriority {
    <#
    .SYNOPSIS
        Updates the priority of a pending VCDevTool task
    .DESCRIPTION
        Changes the priority of a task that is still in pending status
    .PARAMETER Id
        Task ID to update
    .PARAMETER Priority
        New priority value
    .EXAMPLE
        Set-VCTaskPriority -Id 123 -Priority High
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter(Mandatory = $true)]
        [object]$Priority
    )
    
    try {
        if (-not (Test-VCTaskId -TaskId $Id)) {
            throw "Invalid task ID: $Id"
        }
        
        if (-not (Test-VCPriority -Priority $Priority)) {
            throw "Invalid priority value: $Priority"
        }
        
        $PriorityInt = ConvertTo-VCPriorityInt -Priority $Priority
        
        if ($PSCmdlet.ShouldProcess("Task $Id", "Update priority to $Priority")) {
            $UpdateRequest = @{
                Priority = $PriorityInt
            }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/tasks/$Id/priority" -Method Put -Body $UpdateRequest
            
            Write-Host "Task $Id priority updated to $Priority" -ForegroundColor Green
            return ConvertTo-VCTaskObject -ApiResponse $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCTaskHistory {
    <#
    .SYNOPSIS
        Gets the execution history of VCDevTool tasks
    .DESCRIPTION
        Retrieves detailed history and logs for task execution
    .PARAMETER Id
        Specific task ID to get history for
    .PARAMETER IncludeLogs
        Include detailed execution logs
    .EXAMPLE
        Get-VCTaskHistory -Id 123 -IncludeLogs
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$IncludeLogs
    )
    
    try {
        if (-not (Test-VCTaskId -TaskId $Id)) {
            throw "Invalid task ID: $Id"
        }
        
        $QueryParams = @{}
        if ($IncludeLogs) {
            $QueryParams.includeLogs = 'true'
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/tasks/$Id/history" -Method Get -QueryParameters $QueryParams
        
        return $Response
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Export-VCTaskReport {
    <#
    .SYNOPSIS
        Exports task data to various formats
    .DESCRIPTION
        Exports task information and statistics to CSV, JSON, or Excel format
    .PARAMETER Format
        Export format (CSV, JSON, Excel)
    .PARAMETER OutputPath
        Output file path
    .PARAMETER StartDate
        Filter tasks from this date
    .PARAMETER EndDate
        Filter tasks to this date
    .PARAMETER Status
        Filter by task status
    .EXAMPLE
        Export-VCTaskReport -Format CSV -OutputPath "C:\Reports\tasks.csv" -StartDate (Get-Date).AddDays(-30)
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('CSV', 'JSON', 'Excel')]
        [string]$Format,
        
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate,
        
        [Parameter()]
        [string]$Status
    )
    
    try {
        $Tasks = Get-VCTask -StartDate $StartDate -EndDate $EndDate -Status $Status -PageSize 1000
        
        switch ($Format) {
            'CSV' {
                $Tasks | Export-Csv -Path $OutputPath -NoTypeInformation
            }
            'JSON' {
                $Tasks | ConvertTo-Json -Depth 5 | Out-File -FilePath $OutputPath
            }
            'Excel' {
                # Would require ImportExcel module
                if (Get-Module -ListAvailable -Name ImportExcel) {
                    $Tasks | Export-Excel -Path $OutputPath -AutoSize -TableStyle Medium2
                } else {
                    throw "ImportExcel module required for Excel export"
                }
            }
        }
        
        Write-Host "Task report exported to: $OutputPath" -ForegroundColor Green
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
} 