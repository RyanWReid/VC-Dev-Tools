function Test-VCTaskId {
    <#
    .SYNOPSIS
        Validates a task ID parameter
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$TaskId
    )
    
    # Check if it's a valid integer
    if ($TaskId -is [int] -and $TaskId -gt 0) {
        return $true
    }
    
    # Check if it's a string that can be converted to int
    if ($TaskId -is [string] -and [int]::TryParse($TaskId, [ref]$null)) {
        $IntValue = [int]$TaskId
        return $IntValue -gt 0
    }
    
    return $false
}

function Test-VCNodeId {
    <#
    .SYNOPSIS
        Validates a node ID parameter
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$NodeId
    )
    
    # Similar to TaskId validation
    if ($NodeId -is [int] -and $NodeId -gt 0) {
        return $true
    }
    
    if ($NodeId -is [string] -and [int]::TryParse($NodeId, [ref]$null)) {
        $IntValue = [int]$NodeId
        return $IntValue -gt 0
    }
    
    return $false
}

function Test-VCPriority {
    <#
    .SYNOPSIS
        Validates task priority values
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Priority
    )
    
    # Priority should be between 1 (lowest) and 10 (highest)
    if ($Priority -is [int] -and $Priority -ge 1 -and $Priority -le 10) {
        return $true
    }
    
    if ($Priority -is [string]) {
        $ValidStrings = @('Low', 'Normal', 'High', 'Critical')
        return $Priority -in $ValidStrings
    }
    
    return $false
}

function ConvertTo-VCPriorityInt {
    <#
    .SYNOPSIS
        Converts priority string values to integers
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Priority
    )
    
    if ($Priority -is [int]) {
        return $Priority
    }
    
    switch ($Priority.ToString().ToLower()) {
        'low' { return 2 }
        'normal' { return 5 }
        'high' { return 8 }
        'critical' { return 10 }
        default { 
            if ([int]::TryParse($Priority, [ref]$null)) {
                return [int]$Priority
            }
            throw "Invalid priority value: $Priority"
        }
    }
}

function Test-VCFilePath {
    <#
    .SYNOPSIS
        Validates file paths for security and accessibility
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        
        [Parameter()]
        [switch]$MustExist,
        
        [Parameter()]
        [switch]$CheckWriteAccess
    )
    
    # Basic path validation
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }
    
    # Check for invalid characters
    $InvalidChars = [System.IO.Path]::GetInvalidPathChars()
    foreach ($Char in $InvalidChars) {
        if ($Path.Contains($Char)) {
            return $false
        }
    }
    
    # Check if path exists if required
    if ($MustExist -and -not (Test-Path $Path)) {
        return $false
    }
    
    # Check write access if required
    if ($CheckWriteAccess) {
        try {
            $TestFile = Join-Path $Path "test_write_$(Get-Random).tmp"
            New-Item -Path $TestFile -ItemType File -Force | Out-Null
            Remove-Item -Path $TestFile -Force
        }
        catch {
            return $false
        }
    }
    
    return $true
}

function Test-VCDateTimeRange {
    <#
    .SYNOPSIS
        Validates DateTime range parameters
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate
    )
    
    # If both are provided, start must be before end
    if ($StartDate -and $EndDate -and $StartDate -gt $EndDate) {
        return $false
    }
    
    # Dates shouldn't be too far in the future
    $MaxFutureDate = (Get-Date).AddYears(1)
    if ($StartDate -gt $MaxFutureDate -or $EndDate -gt $MaxFutureDate) {
        return $false
    }
    
    return $true
}

function Test-VCApiEndpointFormat {
    <#
    .SYNOPSIS
        Validates API endpoint URL format
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint
    )
    
    try {
        $Uri = [System.Uri]$Endpoint
        
        # Must be HTTP or HTTPS
        if ($Uri.Scheme -notin @('http', 'https')) {
            return $false
        }
        
        # Must have a host
        if ([string]::IsNullOrEmpty($Uri.Host)) {
            return $false
        }
        
        return $true
    }
    catch {
        return $false
    }
}

function Test-VCNodeName {
    <#
    .SYNOPSIS
        Validates node name format
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$NodeName
    )
    
    # Node name validation rules
    if ([string]::IsNullOrWhiteSpace($NodeName)) {
        return $false
    }
    
    # Length limits
    if ($NodeName.Length -lt 3 -or $NodeName.Length -gt 50) {
        return $false
    }
    
    # Valid characters: letters, numbers, hyphens, underscores
    if ($NodeName -notmatch '^[a-zA-Z0-9_-]+$') {
        return $false
    }
    
    # Cannot start or end with hyphen/underscore
    if ($NodeName.StartsWith('-') -or $NodeName.EndsWith('-') -or 
        $NodeName.StartsWith('_') -or $NodeName.EndsWith('_')) {
        return $false
    }
    
    return $true
}

function Test-VCTaskParameters {
    <#
    .SYNOPSIS
        Validates task parameters based on task type
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TaskType,
        
        [Parameter(Mandatory = $true)]
        [hashtable]$Parameters
    )
    
    switch ($TaskType) {
        'VolumeCompression' {
            # Required parameters for volume compression
            $RequiredParams = @('SourcePath', 'TargetPath', 'CompressionLevel')
            foreach ($Param in $RequiredParams) {
                if (-not $Parameters.ContainsKey($Param)) {
                    Write-Warning "Missing required parameter for VolumeCompression: $Param"
                    return $false
                }
            }
            
            # Validate paths
            if (-not (Test-VCFilePath -Path $Parameters.SourcePath -MustExist)) {
                Write-Warning "Invalid source path: $($Parameters.SourcePath)"
                return $false
            }
            
            # Validate compression level
            if ($Parameters.CompressionLevel -notin @('Fast', 'Normal', 'Maximum')) {
                Write-Warning "Invalid compression level: $($Parameters.CompressionLevel)"
                return $false
            }
        }
        
        'RealityCapture' {
            $RequiredParams = @('InputPath', 'OutputPath', 'Quality')
            foreach ($Param in $RequiredParams) {
                if (-not $Parameters.ContainsKey($Param)) {
                    Write-Warning "Missing required parameter for RealityCapture: $Param"
                    return $false
                }
            }
        }
        
        'PackageTask' {
            $RequiredParams = @('Files', 'PackageName')
            foreach ($Param in $RequiredParams) {
                if (-not $Parameters.ContainsKey($Param)) {
                    Write-Warning "Missing required parameter for PackageTask: $Param"
                    return $false
                }
            }
        }
        
        'TestMessage' {
            # Test message only needs a message parameter
            if (-not $Parameters.ContainsKey('Message')) {
                Write-Warning "Missing required parameter for TestMessage: Message"
                return $false
            }
        }
        
        default {
            Write-Warning "Unknown task type: $TaskType"
            return $false
        }
    }
    
    return $true
}

function Confirm-VCOperation {
    <#
    .SYNOPSIS
        Prompts user for confirmation of potentially destructive operations
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Operation,
        
        [Parameter()]
        [string]$Target,
        
        [Parameter()]
        [switch]$Force
    )
    
    if ($Force) {
        return $true
    }
    
    $Message = "Are you sure you want to $Operation"
    if ($Target) {
        $Message += " for $Target"
    }
    $Message += "?"
    
    $Choice = $Host.UI.PromptForChoice("Confirm Operation", $Message, @('&Yes', '&No'), 1)
    return $Choice -eq 0
}

function Test-VCUserPermissions {
    <#
    .SYNOPSIS
        Tests if current user has required permissions for operations
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Operation
    )
    
    # This would integrate with the actual permission system
    # For now, basic checks
    
    $CurrentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $Principal = New-Object System.Security.Principal.WindowsPrincipal($CurrentUser)
    
    switch ($Operation.ToLower()) {
        'administration' {
            # Check if user is in admin group
            return $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        
        'taskmanagement' {
            # Most users can manage tasks
            return $true
        }
        
        'nodemanagement' {
            # Node management requires elevated permissions
            return $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        
        default {
            return $true
        }
    }
} 