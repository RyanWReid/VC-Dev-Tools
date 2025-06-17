function Get-VCNode {
    <#
    .SYNOPSIS
        Retrieves VCDevTool node information
    .DESCRIPTION
        Gets node details from the VCDevTool API. Can retrieve specific nodes by ID or filter nodes by various criteria.
    .PARAMETER Id
        Specific node ID to retrieve
    .PARAMETER Name
        Filter nodes by name
    .PARAMETER Status
        Filter nodes by status (Active, Inactive, Offline)
    .PARAMETER IsOnline
        Filter nodes by online status
    .PARAMETER Page
        Page number for pagination (default: 1)
    .PARAMETER PageSize
        Number of results per page (default: 50)
    .EXAMPLE
        Get-VCNode
        Get all nodes
    .EXAMPLE
        Get-VCNode -Id 1
        Get specific node by ID
    .EXAMPLE
        Get-VCNode -Status Active -IsOnline $true
        Get all active online nodes
    #>
    [CmdletBinding(DefaultParameterSetName = 'All')]
    param(
        [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
        [int]$Id,
        
        [Parameter(ParameterSetName = 'Filter')]
        [string]$Name,
        
        [Parameter(ParameterSetName = 'Filter')]
        [ValidateSet('Active', 'Inactive', 'Offline', 'Maintenance')]
        [string]$Status,
        
        [Parameter(ParameterSetName = 'Filter')]
        [bool]$IsOnline,
        
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
            # Get specific node
            if (-not (Test-VCNodeId -NodeId $Id)) {
                throw "Invalid node ID: $Id"
            }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/$Id" -Method Get
            return ConvertTo-VCNodeObject -ApiResponse $Response
        }
        else {
            # Get nodes with filters
            $QueryParams = Get-VCPagingParameters -Page $Page -PageSize $PageSize -SortBy "Name" -SortOrder "asc"
            
            if ($Name) { $QueryParams.name = $Name }
            if ($Status) { $QueryParams.status = $Status }
            if ($PSBoundParameters.ContainsKey('IsOnline')) { $QueryParams.isOnline = $IsOnline.ToString().ToLower() }
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/nodes" -Method Get -QueryParameters $QueryParams
            
            $Nodes = @()
            foreach ($NodeData in $Response.Nodes) {
                $Nodes += ConvertTo-VCNodeObject -ApiResponse $NodeData
            }
            
            return $Nodes
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Register-VCNode {
    <#
    .SYNOPSIS
        Registers a new VCDevTool worker node
    .DESCRIPTION
        Registers a new node in the VCDevTool system with specified capabilities
    .PARAMETER Name
        Unique name for the node
    .PARAMETER MachineName
        Machine/computer name
    .PARAMETER Capabilities
        Array of task types this node can handle
    .PARAMETER MaxConcurrentTasks
        Maximum number of concurrent tasks
    .PARAMETER Tags
        Optional tags for node categorization
    .EXAMPLE
        Register-VCNode -Name "Worker01" -MachineName "DESKTOP-001" -Capabilities @("VolumeCompression", "PackageTask")
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        
        [Parameter()]
        [string]$MachineName = $env:COMPUTERNAME,
        
        [Parameter(Mandatory = $true)]
        [string[]]$Capabilities,
        
        [Parameter()]
        [int]$MaxConcurrentTasks = 2,
        
        [Parameter()]
        [string[]]$Tags
    )
    
    try {
        # Validate node name
        if (-not (Test-VCNodeName -NodeName $Name)) {
            throw "Invalid node name: $Name"
        }
        
        # Validate capabilities
        foreach ($Capability in $Capabilities) {
            if (-not (Test-VCTaskType -TaskType $Capability)) {
                throw "Invalid capability: $Capability"
            }
        }
        
        # Build registration request
        $NodeRequest = @{
            Name = $Name
            MachineName = $MachineName
            Capabilities = $Capabilities
            MaxConcurrentTasks = $MaxConcurrentTasks
            Platform = [System.Environment]::OSVersion.Platform.ToString()
            Architecture = if ([System.Environment]::Is64BitProcess) { "x64" } else { "x86" }
            Version = "1.0.0"
        }
        
        if ($Tags) {
            $NodeRequest.Tags = $Tags
        }
        
        if ($PSCmdlet.ShouldProcess("Node '$Name' on machine '$MachineName'", "Register node")) {
            $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/register" -Method Post -Body $NodeRequest
            
            Write-Host "Node registered successfully with ID: $($Response.Id)" -ForegroundColor Green
            return ConvertTo-VCNodeObject -ApiResponse $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Unregister-VCNode {
    <#
    .SYNOPSIS
        Unregisters a VCDevTool worker node
    .DESCRIPTION
        Removes a node from the VCDevTool system
    .PARAMETER Id
        Node ID to unregister
    .PARAMETER Force
        Force unregistration without confirmation
    .PARAMETER Reason
        Optional reason for unregistration
    .EXAMPLE
        Unregister-VCNode -Id 1 -Force -Reason "Decommissioning hardware"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
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
            if (-not (Test-VCNodeId -NodeId $Id)) {
                throw "Invalid node ID: $Id"
            }
            
            # Get node to verify it exists
            $Node = Get-VCNode -Id $Id
            if (-not $Node) {
                throw "Node not found: $Id"
            }
            
            if ($Node.ActiveTasks -gt 0 -and -not $Force) {
                throw "Node has $($Node.ActiveTasks) active tasks. Use -Force to unregister anyway."
            }
            
            $UnregisterRequest = @{}
            if ($Reason) {
                $UnregisterRequest.Reason = $Reason
            }
            
            if ($Force -or $PSCmdlet.ShouldProcess("Node $Id ($($Node.Name))", "Unregister node")) {
                Invoke-VCApiRequest -Endpoint "/api/nodes/$Id/unregister" -Method Post -Body $UnregisterRequest
                
                Write-Host "Node $Id has been unregistered" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Error (Format-VCApiError -ErrorRecord $_)
        }
    }
}

function Test-VCNode {
    <#
    .SYNOPSIS
        Tests connectivity and health of a VCDevTool node
    .DESCRIPTION
        Performs health checks on a specific node
    .PARAMETER Id
        Node ID to test
    .PARAMETER IncludePerformanceTest
        Include performance benchmarking
    .EXAMPLE
        Test-VCNode -Id 1 -IncludePerformanceTest
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$IncludePerformanceTest
    )
    
    try {
        if (-not (Test-VCNodeId -NodeId $Id)) {
            throw "Invalid node ID: $Id"
        }
        
        $QueryParams = @{}
        if ($IncludePerformanceTest) {
            $QueryParams.includePerformanceTest = 'true'
        }
        
        Write-Host "Testing node $Id..." -ForegroundColor Yellow
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/$Id/test" -Method Post -QueryParameters $QueryParams
        
        $TestResult = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.NodeTestResult'
            NodeId = $Id
            IsOnline = $Response.IsOnline
            ResponseTime = $Response.ResponseTimeMs
            Status = $Response.Status
            Version = $Response.Version
            LastSeen = if ($Response.LastSeen) { [DateTime]$Response.LastSeen } else { $null }
            PerformanceScore = $Response.PerformanceScore
            Errors = $Response.Errors
            Warnings = $Response.Warnings
        }
        
        # Display results
        if ($TestResult.IsOnline) {
            Write-Host "Node $Id is online and responsive" -ForegroundColor Green
            Write-Host "Response time: $($TestResult.ResponseTime) ms" -ForegroundColor Cyan
        } else {
            Write-Host "Node $Id is offline or not responding" -ForegroundColor Red
        }
        
        if ($TestResult.Errors) {
            Write-Warning "Errors detected: $($TestResult.Errors -join '; ')"
        }
        
        if ($TestResult.Warnings) {
            Write-Warning "Warnings: $($TestResult.Warnings -join '; ')"
        }
        
        return $TestResult
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCNodeHealth {
    <#
    .SYNOPSIS
        Gets detailed health metrics for a VCDevTool node
    .DESCRIPTION
        Retrieves comprehensive health and performance data for a node
    .PARAMETER Id
        Node ID to get health data for
    .PARAMETER IncludeHistory
        Include historical health data
    .EXAMPLE
        Get-VCNodeHealth -Id 1 -IncludeHistory
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$IncludeHistory
    )
    
    try {
        if (-not (Test-VCNodeId -NodeId $Id)) {
            throw "Invalid node ID: $Id"
        }
        
        $QueryParams = @{}
        if ($IncludeHistory) {
            $QueryParams.includeHistory = 'true'
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/$Id/health" -Method Get -QueryParameters $QueryParams
        
        return $Response
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Set-VCNodeConfiguration {
    <#
    .SYNOPSIS
        Updates configuration for a VCDevTool node
    .DESCRIPTION
        Modifies node settings and capabilities
    .PARAMETER Id
        Node ID to configure
    .PARAMETER MaxConcurrentTasks
        Maximum concurrent tasks
    .PARAMETER Capabilities
        Updated capabilities list
    .PARAMETER Tags
        Updated tags
    .PARAMETER Status
        Node status
    .EXAMPLE
        Set-VCNodeConfiguration -Id 1 -MaxConcurrentTasks 4 -Status Active
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [int]$MaxConcurrentTasks,
        
        [Parameter()]
        [string[]]$Capabilities,
        
        [Parameter()]
        [string[]]$Tags,
        
        [Parameter()]
        [ValidateSet('Active', 'Inactive', 'Maintenance')]
        [string]$Status
    )
    
    try {
        if (-not (Test-VCNodeId -NodeId $Id)) {
            throw "Invalid node ID: $Id"
        }
        
        $UpdateRequest = @{}
        
        if ($MaxConcurrentTasks) {
            $UpdateRequest.MaxConcurrentTasks = $MaxConcurrentTasks
        }
        
        if ($Capabilities) {
            foreach ($Capability in $Capabilities) {
                if (-not (Test-VCTaskType -TaskType $Capability)) {
                    throw "Invalid capability: $Capability"
                }
            }
            $UpdateRequest.Capabilities = $Capabilities
        }
        
        if ($Tags) {
            $UpdateRequest.Tags = $Tags
        }
        
        if ($Status) {
            $UpdateRequest.Status = $Status
        }
        
        if ($UpdateRequest.Count -eq 0) {
            throw "No configuration changes specified"
        }
        
        if ($PSCmdlet.ShouldProcess("Node $Id", "Update configuration")) {
            $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/$Id/configuration" -Method Put -Body $UpdateRequest
            
            Write-Host "Node $Id configuration updated successfully" -ForegroundColor Green
            return ConvertTo-VCNodeObject -ApiResponse $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Restart-VCNode {
    <#
    .SYNOPSIS
        Restarts a VCDevTool worker node service
    .DESCRIPTION
        Sends a restart command to a node's service
    .PARAMETER Id
        Node ID to restart
    .PARAMETER Force
        Force restart without confirmation
    .PARAMETER WaitForCompletion
        Wait for restart to complete
    .EXAMPLE
        Restart-VCNode -Id 1 -Force -WaitForCompletion
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,
        
        [Parameter()]
        [switch]$Force,
        
        [Parameter()]
        [switch]$WaitForCompletion
    )
    
    try {
        if (-not (Test-VCNodeId -NodeId $Id)) {
            throw "Invalid node ID: $Id"
        }
        
        # Get node info
        $Node = Get-VCNode -Id $Id
        if (-not $Node) {
            throw "Node not found: $Id"
        }
        
        if ($Node.ActiveTasks -gt 0 -and -not $Force) {
            throw "Node has $($Node.ActiveTasks) active tasks. Use -Force to restart anyway."
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess("Node $Id ($($Node.Name))", "Restart node service")) {
            $RestartRequest = @{
                WaitForCompletion = $WaitForCompletion.IsPresent
            }
            
            Write-Host "Sending restart command to node $Id..." -ForegroundColor Yellow
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/nodes/$Id/restart" -Method Post -Body $RestartRequest
            
            if ($WaitForCompletion) {
                Write-Host "Waiting for node to come back online..." -ForegroundColor Yellow
                
                $Timeout = 300 # 5 minutes
                $StartTime = Get-Date
                
                do {
                    Start-Sleep -Seconds 5
                    $TestResult = Test-VCNode -Id $Id
                    
                    if ($TestResult.IsOnline) {
                        Write-Host "Node $Id is back online" -ForegroundColor Green
                        return $TestResult
                    }
                    
                    $ElapsedSeconds = ((Get-Date) - $StartTime).TotalSeconds
                    Write-Host "Waiting... ($([int]$ElapsedSeconds)s elapsed)" -ForegroundColor Cyan
                    
                } while ($ElapsedSeconds -lt $Timeout)
                
                Write-Warning "Node did not come back online within $Timeout seconds"
            } else {
                Write-Host "Restart command sent to node $Id" -ForegroundColor Green
            }
            
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
} 