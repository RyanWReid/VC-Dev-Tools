function Initialize-VCConnection {
    <#
    .SYNOPSIS
        Initializes a connection to the VCDevTool API
    .DESCRIPTION
        Sets up the HTTP client and authentication for VCDevTool API communication
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiEndpoint,
        
        [Parameter()]
        [PSCredential]$Credential,
        
        [Parameter()]
        [switch]$UseWindowsAuthentication,
        
        [Parameter()]
        [int]$TimeoutSeconds = 30
    )
    
    try {
        # Create session configuration
        $SessionConfig = @{
            ApiEndpoint = $ApiEndpoint
            TimeoutSeconds = $TimeoutSeconds
            UseWindowsAuth = $UseWindowsAuthentication.IsPresent
            Headers = @{
                'Content-Type' = 'application/json'
                'Accept' = 'application/json'
                'User-Agent' = "VCDevTool-PowerShell/1.0.0"
            }
        }
        
        # Configure authentication
        if ($UseWindowsAuthentication) {
            $SessionConfig.Headers['Authorization'] = "Negotiate"
        }
        elseif ($Credential) {
            $AuthBytes = [System.Text.Encoding]::UTF8.GetBytes("$($Credential.UserName):$($Credential.GetNetworkCredential().Password)")
            $AuthBase64 = [System.Convert]::ToBase64String($AuthBytes)
            $SessionConfig.Headers['Authorization'] = "Basic $AuthBase64"
        }
        
        # Test connection
        $TestUri = "$ApiEndpoint/api/health"
        $Response = Invoke-RestMethod -Uri $TestUri -Method Get -Headers $SessionConfig.Headers -TimeoutSec $TimeoutSeconds
        
        if ($Response.Status -eq "Healthy") {
            $script:VCConnection = $SessionConfig
            $script:VCApiEndpoint = $ApiEndpoint
            Write-Verbose "Successfully connected to VCDevTool API at $ApiEndpoint"
            return $true
        }
        else {
            throw "API health check failed: $($Response.Status)"
        }
    }
    catch {
        Write-Error "Failed to initialize VCDevTool connection: $($_.Exception.Message)"
        return $false
    }
}

function Test-VCConnectionState {
    <#
    .SYNOPSIS
        Tests if there is an active VCDevTool connection
    #>
    [CmdletBinding()]
    param()
    
    if (-not $script:VCConnection) {
        return $false
    }
    
    try {
        $TestUri = "$($script:VCApiEndpoint)/api/health"
        $Response = Invoke-RestMethod -Uri $TestUri -Method Get -Headers $script:VCConnection.Headers -TimeoutSec 10
        return ($Response.Status -eq "Healthy")
    }
    catch {
        Write-Verbose "Connection test failed: $($_.Exception.Message)"
        return $false
    }
}

function Close-VCConnection {
    <#
    .SYNOPSIS
        Closes the VCDevTool connection and cleans up resources
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [switch]$Force
    )
    
    try {
        if ($script:VCConnection -and -not $Force) {
            # Graceful disconnect - notify server if possible
            $DisconnectUri = "$($script:VCApiEndpoint)/api/session/disconnect"
            Invoke-RestMethod -Uri $DisconnectUri -Method Post -Headers $script:VCConnection.Headers -TimeoutSec 5
        }
    }
    catch {
        if (-not $Force) {
            Write-Warning "Failed to gracefully disconnect: $($_.Exception.Message)"
        }
    }
    finally {
        $script:VCConnection = $null
        $script:VCApiEndpoint = $null
        $script:VCAuthToken = $null
        Write-Verbose "VCDevTool connection closed"
    }
}

function Get-VCAuthHeader {
    <#
    .SYNOPSIS
        Gets the current authentication header for API requests
    #>
    [CmdletBinding()]
    param()
    
    if (-not $script:VCConnection) {
        throw "No active VCDevTool connection. Use Connect-VCDevTool first."
    }
    
    return $script:VCConnection.Headers.Clone()
}

function Invoke-VCApiRequest {
    <#
    .SYNOPSIS
        Makes an authenticated API request to VCDevTool
    .DESCRIPTION
        Handles retry logic, error handling, and authentication for API requests
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,
        
        [Parameter()]
        [Microsoft.PowerShell.Commands.WebRequestMethod]$Method = 'Get',
        
        [Parameter()]
        [hashtable]$Body,
        
        [Parameter()]
        [hashtable]$QueryParameters,
        
        [Parameter()]
        [int]$TimeoutSeconds,
        
        [Parameter()]
        [int]$RetryAttempts = 3
    )
    
    if (-not $script:VCConnection) {
        throw "No active VCDevTool connection. Use Connect-VCDevTool first."
    }
    
    # Build URI
    $Uri = "$($script:VCApiEndpoint)$Endpoint"
    if ($QueryParameters) {
        $QueryString = ($QueryParameters.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "&"
        $Uri += "?$QueryString"
    }
    
    # Prepare request parameters
    $RequestParams = @{
        Uri = $Uri
        Method = $Method
        Headers = Get-VCAuthHeader
        TimeoutSec = if ($TimeoutSeconds) { $TimeoutSeconds } else { $script:VCConnection.TimeoutSeconds }
    }
    
    if ($Body) {
        $RequestParams.Body = $Body | ConvertTo-Json -Depth 10
    }
    
    # Retry logic
    $Attempt = 0
    do {
        $Attempt++
        try {
            Write-Verbose "Making API request to $Uri (Attempt $Attempt)"
            $Response = Invoke-RestMethod @RequestParams
            return $Response
        }
        catch {
            $LastError = $_
            Write-Verbose "API request failed (Attempt $Attempt): $($_.Exception.Message)"
            
            # Check if we should retry
            if ($Attempt -lt $RetryAttempts) {
                $WaitTime = [Math]::Pow(2, $Attempt)  # Exponential backoff
                Write-Verbose "Retrying in $WaitTime seconds..."
                Start-Sleep -Seconds $WaitTime
            }
        }
    } while ($Attempt -lt $RetryAttempts)
    
    # All retries failed
    throw "API request failed after $RetryAttempts attempts. Last error: $($LastError.Exception.Message)"
} 