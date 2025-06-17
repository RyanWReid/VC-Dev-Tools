function Get-VCAuditLog {
    <#
    .SYNOPSIS
        Retrieves VCDevTool audit log entries
    .DESCRIPTION
        Gets audit trail information for security and compliance monitoring
    .PARAMETER StartDate
        Filter audit logs from this date
    .PARAMETER EndDate
        Filter audit logs to this date
    .PARAMETER UserId
        Filter by specific user ID
    .PARAMETER Action
        Filter by specific action type
    .PARAMETER ResourceType
        Filter by resource type (Task, Node, System, etc.)
    .PARAMETER ResourceId
        Filter by specific resource ID
    .PARAMETER Count
        Maximum number of entries to return
    .PARAMETER IncludeDetails
        Include detailed audit information
    .EXAMPLE
        Get-VCAuditLog -StartDate (Get-Date).AddDays(-7) -Action "TaskCreated"
    .EXAMPLE
        Get-VCAuditLog -UserId "domain\user" -ResourceType Task -Count 100
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate,
        
        [Parameter()]
        [string]$UserId,
        
        [Parameter()]
        [ValidateSet('Login', 'Logout', 'TaskCreated', 'TaskStarted', 'TaskCompleted', 'TaskFailed', 'TaskCancelled', 
                     'NodeRegistered', 'NodeUnregistered', 'ConfigurationChanged', 'UserAdded', 'UserRemoved', 
                     'PermissionChanged', 'SystemBackup', 'SystemRestart')]
        [string]$Action,
        
        [Parameter()]
        [ValidateSet('Task', 'Node', 'User', 'System', 'Configuration', 'Database')]
        [string]$ResourceType,
        
        [Parameter()]
        [string]$ResourceId,
        
        [Parameter()]
        [ValidateRange(1, 10000)]
        [int]$Count = 100,
        
        [Parameter()]
        [switch]$IncludeDetails
    )
    
    try {
        # Check permissions for audit log access
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for audit log access"
        }
        
        $QueryParams = @{
            count = $Count
            includeDetails = $IncludeDetails.IsPresent
        }
        
        if ($StartDate) { $QueryParams.startDate = $StartDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        if ($EndDate) { $QueryParams.endDate = $EndDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        if ($UserId) { $QueryParams.userId = $UserId }
        if ($Action) { $QueryParams.action = $Action }
        if ($ResourceType) { $QueryParams.resourceType = $ResourceType }
        if ($ResourceId) { $QueryParams.resourceId = $ResourceId }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/security/audit" -Method Get -QueryParameters $QueryParams
        
        $AuditEntries = @()
        foreach ($AuditData in $Response.AuditEntries) {
            $AuditEntry = [PSCustomObject]@{
                PSTypeName = 'VCDevTool.AuditEntry'
                Id = $AuditData.Id
                Timestamp = [DateTime]$AuditData.Timestamp
                UserId = $AuditData.UserId
                UserName = $AuditData.UserName
                Action = $AuditData.Action
                ResourceType = $AuditData.ResourceType
                ResourceId = $AuditData.ResourceId
                ResourceName = $AuditData.ResourceName
                IPAddress = $AuditData.IPAddress
                UserAgent = $AuditData.UserAgent
                Success = $AuditData.Success
                ErrorMessage = $AuditData.ErrorMessage
                SessionId = $AuditData.SessionId
                CorrelationId = $AuditData.CorrelationId
                AdditionalData = $AuditData.AdditionalData
            }
            
            if ($IncludeDetails -and $AuditData.Details) {
                $AuditEntry | Add-Member -NotePropertyName "Details" -NotePropertyValue $AuditData.Details
            }
            
            $AuditEntries += $AuditEntry
        }
        
        # Add summary information
        if ($Response.Summary) {
            $AuditEntries | Add-Member -NotePropertyName "Summary" -NotePropertyValue $Response.Summary
        }
        
        return $AuditEntries
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Export-VCAuditReport {
    <#
    .SYNOPSIS
        Exports audit log data to various formats for compliance reporting
    .DESCRIPTION
        Creates comprehensive audit reports in CSV, JSON, or XML format for compliance and security analysis
    .PARAMETER OutputPath
        Path where audit report will be saved
    .PARAMETER Format
        Export format (CSV, JSON, XML, PDF)
    .PARAMETER StartDate
        Filter audit logs from this date
    .PARAMETER EndDate
        Filter audit logs to this date
    .PARAMETER ReportType
        Type of audit report to generate
    .PARAMETER IncludeCharts
        Include charts and graphs (PDF format only)
    .PARAMETER IncludeStatistics
        Include summary statistics
    .EXAMPLE
        Export-VCAuditReport -OutputPath "C:\Reports\Audit_$(Get-Date -Format 'yyyyMMdd').csv" -Format CSV -StartDate (Get-Date).AddDays(-30)
    .EXAMPLE
        Export-VCAuditReport -OutputPath "C:\Reports\SecurityReport.pdf" -Format PDF -ReportType Security -IncludeCharts -IncludeStatistics
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        
        [Parameter(Mandatory = $true)]
        [ValidateSet('CSV', 'JSON', 'XML', 'PDF')]
        [string]$Format,
        
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate,
        
        [Parameter()]
        [ValidateSet('Full', 'Security', 'Compliance', 'UserActivity', 'SystemChanges')]
        [string]$ReportType = 'Full',
        
        [Parameter()]
        [switch]$IncludeCharts,
        
        [Parameter()]
        [switch]$IncludeStatistics = $true
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for audit report generation"
        }
        
        # Validate output path
        $OutputDir = Split-Path $OutputPath -Parent
        if (-not (Test-Path $OutputDir)) {
            New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
        }
        
        $ReportRequest = @{
            Format = $Format
            ReportType = $ReportType
            IncludeCharts = $IncludeCharts.IsPresent
            IncludeStatistics = $IncludeStatistics.IsPresent
        }
        
        if ($StartDate) { $ReportRequest.StartDate = $StartDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        if ($EndDate) { $ReportRequest.EndDate = $EndDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        
        if ($PSCmdlet.ShouldProcess("VCDevTool Audit Data", "Export $ReportType audit report to $OutputPath")) {
            Write-Host "Generating $ReportType audit report..." -ForegroundColor Yellow
            Write-Host "This may take several minutes for large date ranges..." -ForegroundColor Cyan
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/security/audit/export" -Method Post -Body $ReportRequest -TimeoutSeconds 1800
            
            # Download the report file
            if ($Response.DownloadUrl) {
                Write-Host "Downloading audit report..." -ForegroundColor Yellow
                
                try {
                    $WebClient = New-Object System.Net.WebClient
                    if ($script:VCConnection.Headers.Authorization) {
                        $WebClient.Headers.Add("Authorization", $script:VCConnection.Headers.Authorization)
                    }
                    $WebClient.DownloadFile($Response.DownloadUrl, $OutputPath)
                    $WebClient.Dispose()
                }
                catch {
                    throw "Failed to download audit report: $($_.Exception.Message)"
                }
            }
            else {
                # Direct response data
                switch ($Format) {
                    'CSV' { $Response.Data | Out-File -FilePath $OutputPath -Encoding UTF8 }
                    'JSON' { $Response.Data | ConvertTo-Json -Depth 10 | Out-File -FilePath $OutputPath -Encoding UTF8 }
                    'XML' { $Response.Data | Out-File -FilePath $OutputPath -Encoding UTF8 }
                    default { $Response.Data | Out-File -FilePath $OutputPath }
                }
            }
            
            $FileInfo = Get-Item $OutputPath
            
            Write-Host "Audit report exported successfully" -ForegroundColor Green
            Write-Host "File: $($FileInfo.FullName)" -ForegroundColor Cyan
            Write-Host "Size: $([math]::Round($FileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
            
            # Display report summary
            if ($Response.ReportSummary) {
                Write-Host "`nReport Summary:" -ForegroundColor Yellow
                Write-Host "  Total Entries: $($Response.ReportSummary.TotalEntries)" -ForegroundColor Cyan
                Write-Host "  Date Range: $($Response.ReportSummary.DateRange)" -ForegroundColor Cyan
                Write-Host "  Generated: $($Response.ReportSummary.GeneratedAt)" -ForegroundColor Cyan
                
                if ($Response.ReportSummary.Statistics) {
                    Write-Host "  Statistics:" -ForegroundColor Cyan
                    $Response.ReportSummary.Statistics | ForEach-Object {
                        Write-Host "    - $($_.Key): $($_.Value)" -ForegroundColor Gray
                    }
                }
            }
            
            return [PSCustomObject]@{
                PSTypeName = 'VCDevTool.AuditReport'
                FilePath = $FileInfo.FullName
                Format = $Format
                ReportType = $ReportType
                SizeBytes = $FileInfo.Length
                CreatedAt = Get-Date
                DateRange = @{
                    StartDate = $StartDate
                    EndDate = $EndDate
                }
                Summary = $Response.ReportSummary
            }
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Test-VCSecurity {
    <#
    .SYNOPSIS
        Performs security assessment and vulnerability checks
    .DESCRIPTION
        Runs comprehensive security tests including authentication, authorization, and configuration validation
    .PARAMETER TestType
        Type of security test to perform
    .PARAMETER IncludeRecommendations
        Include security recommendations
    .PARAMETER GenerateReport
        Generate detailed security report
    .PARAMETER ReportPath
        Path to save security report
    .EXAMPLE
        Test-VCSecurity -TestType All -IncludeRecommendations -GenerateReport -ReportPath "C:\Reports\SecurityTest.json"
    .EXAMPLE
        Test-VCSecurity -TestType Authentication
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('Authentication', 'Authorization', 'Configuration', 'NetworkSecurity', 'DataProtection', 'All')]
        [string]$TestType = 'All',
        
        [Parameter()]
        [switch]$IncludeRecommendations,
        
        [Parameter()]
        [switch]$GenerateReport,
        
        [Parameter()]
        [string]$ReportPath
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for security testing"
        }
        
        $SecurityTestRequest = @{
            TestType = $TestType
            IncludeRecommendations = $IncludeRecommendations.IsPresent
            GenerateReport = $GenerateReport.IsPresent
        }
        
        Write-Host "Running VCDevTool security assessment..." -ForegroundColor Yellow
        Write-Host "Test Type: $TestType" -ForegroundColor Cyan
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/security/test" -Method Post -Body $SecurityTestRequest -TimeoutSeconds 300
        
        $SecurityTest = [PSCustomObject]@{
            PSTypeName = 'VCDevTool.SecurityTest'
            TestType = $TestType
            ExecutedAt = Get-Date
            OverallStatus = $Response.OverallStatus
            Score = $Response.SecurityScore
            TestResults = @()
            Vulnerabilities = @()
            Recommendations = @()
            ComplianceStatus = $Response.ComplianceStatus
        }
        
        # Process test results
        foreach ($TestResult in $Response.TestResults) {
            $Result = [PSCustomObject]@{
                Category = $TestResult.Category
                TestName = $TestResult.TestName
                Status = $TestResult.Status
                Score = $TestResult.Score
                Message = $TestResult.Message
                Details = $TestResult.Details
                Severity = $TestResult.Severity
                Remediation = $TestResult.Remediation
            }
            $SecurityTest.TestResults += $Result
        }
        
        # Process vulnerabilities
        if ($Response.Vulnerabilities) {
            foreach ($Vulnerability in $Response.Vulnerabilities) {
                $Vuln = [PSCustomObject]@{
                    Id = $Vulnerability.Id
                    Title = $Vulnerability.Title
                    Severity = $Vulnerability.Severity
                    Description = $Vulnerability.Description
                    Impact = $Vulnerability.Impact
                    Remediation = $Vulnerability.Remediation
                    CvssScore = $Vulnerability.CvssScore
                    Category = $Vulnerability.Category
                    AffectedComponents = $Vulnerability.AffectedComponents
                }
                $SecurityTest.Vulnerabilities += $Vuln
            }
        }
        
        # Process recommendations
        if ($IncludeRecommendations -and $Response.Recommendations) {
            foreach ($Recommendation in $Response.Recommendations) {
                $Rec = [PSCustomObject]@{
                    Category = $Recommendation.Category
                    Priority = $Recommendation.Priority
                    Title = $Recommendation.Title
                    Description = $Recommendation.Description
                    Implementation = $Recommendation.Implementation
                    Benefit = $Recommendation.Benefit
                    Effort = $Recommendation.Effort
                }
                $SecurityTest.Recommendations += $Rec
            }
        }
        
        # Display summary
        $StatusColor = switch ($SecurityTest.OverallStatus) {
            'Secure' { 'Green' }
            'Warning' { 'Yellow' }
            'Vulnerable' { 'Red' }
            default { 'White' }
        }
        
        Write-Host "`nSecurity Assessment Results:" -ForegroundColor White
        Write-Host "Overall Status: $($SecurityTest.OverallStatus) (Score: $($SecurityTest.Score)/100)" -ForegroundColor $StatusColor
        
        if ($SecurityTest.Vulnerabilities.Count -gt 0) {
            Write-Host "Vulnerabilities Found: $($SecurityTest.Vulnerabilities.Count)" -ForegroundColor Red
            
            $CriticalVulns = $SecurityTest.Vulnerabilities | Where-Object { $_.Severity -eq 'Critical' }
            $HighVulns = $SecurityTest.Vulnerabilities | Where-Object { $_.Severity -eq 'High' }
            
            if ($CriticalVulns.Count -gt 0) {
                Write-Host "  Critical: $($CriticalVulns.Count)" -ForegroundColor Red
            }
            if ($HighVulns.Count -gt 0) {
                Write-Host "  High: $($HighVulns.Count)" -ForegroundColor Yellow
            }
        }
        
        if ($SecurityTest.Recommendations.Count -gt 0) {
            Write-Host "Recommendations: $($SecurityTest.Recommendations.Count)" -ForegroundColor Cyan
        }
        
        # Save report if requested
        if ($GenerateReport) {
            if (-not $ReportPath) {
                $ReportPath = "VCDevTool_SecurityReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
            }
            
            try {
                $SecurityTest | ConvertTo-Json -Depth 10 | Out-File -FilePath $ReportPath -Encoding UTF8
                Write-Host "Security report saved to: $ReportPath" -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to save security report: $($_.Exception.Message)"
            }
        }
        
        return $SecurityTest
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCSecurityConfiguration {
    <#
    .SYNOPSIS
        Gets current security configuration settings
    .DESCRIPTION
        Retrieves security-related configuration including authentication, authorization, and encryption settings
    .PARAMETER Category
        Specific security category to retrieve
    .EXAMPLE
        Get-VCSecurityConfiguration
    .EXAMPLE
        Get-VCSecurityConfiguration -Category Authentication
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('Authentication', 'Authorization', 'Encryption', 'Auditing', 'NetworkSecurity', 'All')]
        [string]$Category = 'All'
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for security configuration access"
        }
        
        $QueryParams = @{}
        if ($Category -ne 'All') {
            $QueryParams.category = $Category
        }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/security/configuration" -Method Get -QueryParameters $QueryParams
        
        return $Response
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Set-VCSecurityConfiguration {
    <#
    .SYNOPSIS
        Updates security configuration settings
    .DESCRIPTION
        Modifies security-related configuration. Requires administrative privileges.
    .PARAMETER Category
        Security category to update
    .PARAMETER Settings
        Hashtable of security settings to update
    .PARAMETER Force
        Force update without confirmation
    .PARAMETER ValidateOnly
        Only validate settings without applying changes
    .EXAMPLE
        Set-VCSecurityConfiguration -Category Authentication -Settings @{RequireStrongPasswords=$true; SessionTimeoutMinutes=60} -Force
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Authentication', 'Authorization', 'Encryption', 'Auditing', 'NetworkSecurity')]
        [string]$Category,
        
        [Parameter(Mandatory = $true)]
        [hashtable]$Settings,
        
        [Parameter()]
        [switch]$Force,
        
        [Parameter()]
        [switch]$ValidateOnly
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for security configuration changes"
        }
        
        $ConfigRequest = @{
            Category = $Category
            Settings = $Settings
            ValidateOnly = $ValidateOnly.IsPresent
        }
        
        if ($ValidateOnly) {
            Write-Host "Validating security configuration..." -ForegroundColor Yellow
            
            $Response = Invoke-VCApiRequest -Endpoint "/api/security/configuration/validate" -Method Post -Body $ConfigRequest
            
            if ($Response.IsValid) {
                Write-Host "Security configuration is valid" -ForegroundColor Green
            } else {
                Write-Host "Security configuration validation failed:" -ForegroundColor Red
                $Response.ValidationErrors | ForEach-Object {
                    Write-Host "  - $($_.Property): $($_.Message)" -ForegroundColor Yellow
                }
            }
            
            return $Response
        }
        
        if ($Force -or $PSCmdlet.ShouldProcess("$Category security configuration", "Update settings")) {
            $Response = Invoke-VCApiRequest -Endpoint "/api/security/configuration" -Method Put -Body $ConfigRequest
            
            Write-Host "Security configuration updated successfully" -ForegroundColor Green
            
            if ($Response.RequiresRestart) {
                Write-Warning "Security configuration changes require system restart to take effect"
            }
            
            if ($Response.SecurityImpact) {
                Write-Host "Security Impact: $($Response.SecurityImpact)" -ForegroundColor Cyan
            }
            
            return $Response
        }
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
}

function Get-VCSecurityEvents {
    <#
    .SYNOPSIS
        Gets security-related events and alerts
    .DESCRIPTION
        Retrieves security events, failed login attempts, and suspicious activities
    .PARAMETER EventType
        Type of security event to retrieve
    .PARAMETER Severity
        Event severity level
    .PARAMETER StartDate
        Filter events from this date
    .PARAMETER EndDate
        Filter events to this date
    .PARAMETER Count
        Maximum number of events to return
    .EXAMPLE
        Get-VCSecurityEvents -EventType FailedLogin -Severity High -Count 50
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [ValidateSet('FailedLogin', 'UnauthorizedAccess', 'SuspiciousActivity', 'ConfigurationChange', 
                     'PrivilegeEscalation', 'DataAccess', 'SystemIntrusion', 'All')]
        [string]$EventType = 'All',
        
        [Parameter()]
        [ValidateSet('Low', 'Medium', 'High', 'Critical')]
        [string]$Severity,
        
        [Parameter()]
        [DateTime]$StartDate,
        
        [Parameter()]
        [DateTime]$EndDate,
        
        [Parameter()]
        [ValidateRange(1, 1000)]
        [int]$Count = 100
    )
    
    try {
        # Check permissions
        if (-not (Test-VCUserPermissions -Operation "administration")) {
            throw "Insufficient permissions for security event access"
        }
        
        $QueryParams = @{
            eventType = $EventType
            count = $Count
        }
        
        if ($Severity) { $QueryParams.severity = $Severity }
        if ($StartDate) { $QueryParams.startDate = $StartDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        if ($EndDate) { $QueryParams.endDate = $EndDate.ToString('yyyy-MM-ddTHH:mm:ssZ') }
        
        $Response = Invoke-VCApiRequest -Endpoint "/api/security/events" -Method Get -QueryParameters $QueryParams
        
        $SecurityEvents = @()
        foreach ($EventData in $Response.Events) {
            $SecurityEvent = [PSCustomObject]@{
                PSTypeName = 'VCDevTool.SecurityEvent'
                Id = $EventData.Id
                EventType = $EventData.EventType
                Severity = $EventData.Severity
                Timestamp = [DateTime]$EventData.Timestamp
                Source = $EventData.Source
                Description = $EventData.Description
                IPAddress = $EventData.IPAddress
                UserId = $EventData.UserId
                UserAgent = $EventData.UserAgent
                Details = $EventData.Details
                ActionTaken = $EventData.ActionTaken
                Resolved = $EventData.Resolved
            }
            $SecurityEvents += $SecurityEvent
        }
        
        return $SecurityEvents
    }
    catch {
        Write-Error (Format-VCApiError -ErrorRecord $_)
    }
} 