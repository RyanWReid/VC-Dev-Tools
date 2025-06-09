# VCDevTool Windows Authentication Test Script
# Run this script to test the complete Windows Authentication setup

param(
    [string]$ApiBaseUrl = "https://localhost:5289",
    [string]$Domain = "company.local",
    [string]$ServiceAccount = "VCDevToolService",
    [string]$TestUser = "",
    [string]$TestPassword = "",
    [switch]$TestAD = $true,
    [switch]$TestAPI = $true,
    [switch]$TestIIS = $true,
    [switch]$Verbose = $false
)

Write-Host "🧪 VCDevTool Windows Authentication Test Suite" -ForegroundColor Green
Write-Host "Domain: $Domain" -ForegroundColor Yellow
Write-Host "API URL: $ApiBaseUrl" -ForegroundColor Yellow
Write-Host "Service Account: $ServiceAccount" -ForegroundColor Yellow

$TestResults = @()

# Function to add test result
function Add-TestResult {
    param(
        [string]$Category,
        [string]$Test,
        [bool]$Passed,
        [string]$Details = "",
        [string]$Error = ""
    )
    
    $TestResults += [PSCustomObject]@{
        Category = $Category
        Test = $Test
        Passed = $Passed
        Details = $Details
        Error = $Error
        Timestamp = Get-Date
    }
    
    $Status = if ($Passed) { "✅ PASS" } else { "❌ FAIL" }
    $Color = if ($Passed) { "Green" } else { "Red" }
    
    Write-Host "[$Status] $Category - $Test" -ForegroundColor $Color
    if ($Details -and $Verbose) {
        Write-Host "   Details: $Details" -ForegroundColor Gray
    }
    if ($Error -and -not $Passed) {
        Write-Host "   Error: $Error" -ForegroundColor Red
    }
}

# 1. Test Active Directory Configuration
if ($TestAD) {
    Write-Host "`n🔍 Testing Active Directory Configuration..." -ForegroundColor Blue
    
    # Test AD Module
    try {
        Import-Module ActiveDirectory -ErrorAction Stop
        Add-TestResult -Category "AD" -Test "ActiveDirectory Module" -Passed $true -Details "Module loaded successfully"
    } catch {
        Add-TestResult -Category "AD" -Test "ActiveDirectory Module" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Domain Connectivity
    try {
        $domain = Get-ADDomain -Identity $Domain -ErrorAction Stop
        Add-TestResult -Category "AD" -Test "Domain Connectivity" -Passed $true -Details "Connected to domain: $($domain.NetBIOSName)"
    } catch {
        Add-TestResult -Category "AD" -Test "Domain Connectivity" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Service Account
    try {
        $serviceUser = Get-ADUser -Identity $ServiceAccount -ErrorAction Stop
        Add-TestResult -Category "AD" -Test "Service Account Exists" -Passed $true -Details "Account found: $($serviceUser.DistinguishedName)"
        
        # Check if service account is enabled
        if ($serviceUser.Enabled) {
            Add-TestResult -Category "AD" -Test "Service Account Enabled" -Passed $true
        } else {
            Add-TestResult -Category "AD" -Test "Service Account Enabled" -Passed $false -Error "Service account is disabled"
        }
    } catch {
        Add-TestResult -Category "AD" -Test "Service Account Exists" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Security Groups
    $RequiredGroups = @(
        "VCDevTool_Administrators",
        "VCDevTool_Users", 
        "VCDevTool_ComputerNodes",
        "VCDevTool_ProcessingNodes",
        "VCDevTool_ServiceAccounts"
    )
    
    foreach ($GroupName in $RequiredGroups) {
        try {
            $group = Get-ADGroup -Identity $GroupName -ErrorAction Stop
            Add-TestResult -Category "AD" -Test "Group: $GroupName" -Passed $true -Details "Group found with $((Get-ADGroupMember -Identity $GroupName).Count) members"
        } catch {
            Add-TestResult -Category "AD" -Test "Group: $GroupName" -Passed $false -Error $_.Exception.Message
        }
    }
    
    # Test SPN Configuration
    try {
        $spnOutput = setspn -L $ServiceAccount 2>&1
        if ($LASTEXITCODE -eq 0) {
            $spnCount = ($spnOutput | Where-Object { $_ -like "HTTP/*" }).Count
            Add-TestResult -Category "AD" -Test "SPN Configuration" -Passed ($spnCount -gt 0) -Details "Found $spnCount HTTP SPNs"
        } else {
            Add-TestResult -Category "AD" -Test "SPN Configuration" -Passed $false -Error "setspn command failed"
        }
    } catch {
        Add-TestResult -Category "AD" -Test "SPN Configuration" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Computer Account (current machine)
    try {
        $computerName = $env:COMPUTERNAME
        $computer = Get-ADComputer -Identity $computerName -ErrorAction Stop
        Add-TestResult -Category "AD" -Test "Computer Account" -Passed $true -Details "Computer found: $($computer.DistinguishedName)"
        
        # Check domain trust
        $trustResult = Test-ComputerSecureChannel -ErrorAction SilentlyContinue
        Add-TestResult -Category "AD" -Test "Domain Trust" -Passed $trustResult -Details "Secure channel status: $trustResult"
    } catch {
        Add-TestResult -Category "AD" -Test "Computer Account" -Passed $false -Error $_.Exception.Message
    }
}

# 2. Test IIS Configuration
if ($TestIIS) {
    Write-Host "`n🌐 Testing IIS Configuration..." -ForegroundColor Blue
    
    # Test IIS Module
    try {
        Import-Module WebAdministration -ErrorAction Stop
        Add-TestResult -Category "IIS" -Test "WebAdministration Module" -Passed $true
    } catch {
        Add-TestResult -Category "IIS" -Test "WebAdministration Module" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Application Pool
    try {
        $appPool = Get-WebAppPool -Name "VCDevToolAppPool" -ErrorAction Stop
        $isRunning = $appPool.State -eq "Started"
        Add-TestResult -Category "IIS" -Test "Application Pool" -Passed $isRunning -Details "State: $($appPool.State)"
        
        # Check app pool identity
        if ($appPool.processModel.identityType -eq "SpecificUser") {
            Add-TestResult -Category "IIS" -Test "App Pool Identity" -Passed $true -Details "Using specific user: $($appPool.processModel.userName)"
        } else {
            Add-TestResult -Category "IIS" -Test "App Pool Identity" -Passed $false -Error "Not using specific user identity"
        }
    } catch {
        Add-TestResult -Category "IIS" -Test "Application Pool" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Windows Authentication
    try {
        $winAuth = Get-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name enabled -Location "Default Web Site/VCDevTool" -ErrorAction Stop
        Add-TestResult -Category "IIS" -Test "Windows Authentication" -Passed $winAuth.Value -Details "Enabled: $($winAuth.Value)"
        
        $anonAuth = Get-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name enabled -Location "Default Web Site/VCDevTool" -ErrorAction Stop
        Add-TestResult -Category "IIS" -Test "Anonymous Authentication" -Passed (-not $anonAuth.Value) -Details "Disabled: $(-not $anonAuth.Value)"
    } catch {
        Add-TestResult -Category "IIS" -Test "Authentication Configuration" -Passed $false -Error $_.Exception.Message
    }
}

# 3. Test API Endpoints
if ($TestAPI) {
    Write-Host "`n🔌 Testing API Endpoints..." -ForegroundColor Blue
    
    # Test API Health
    try {
        $healthResponse = Invoke-WebRequest -Uri "$ApiBaseUrl/health" -UseDefaultCredentials -ErrorAction Stop
        $isHealthy = $healthResponse.StatusCode -eq 200
        Add-TestResult -Category "API" -Test "Health Endpoint" -Passed $isHealthy -Details "Status: $($healthResponse.StatusCode)"
    } catch {
        Add-TestResult -Category "API" -Test "Health Endpoint" -Passed $false -Error $_.Exception.Message
    }
    
    # Test Authentication Required Endpoint
    try {
        $taskResponse = Invoke-WebRequest -Uri "$ApiBaseUrl/api/tasks" -UseDefaultCredentials -ErrorAction Stop
        $isAuthenticated = $taskResponse.StatusCode -eq 200
        Add-TestResult -Category "API" -Test "Authenticated Endpoint" -Passed $isAuthenticated -Details "Status: $($taskResponse.StatusCode)"
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 401) {
            Add-TestResult -Category "API" -Test "Authenticated Endpoint" -Passed $false -Error "Authentication required (401) - check user permissions"
        } else {
            Add-TestResult -Category "API" -Test "Authenticated Endpoint" -Passed $false -Error $_.Exception.Message
        }
    }
    
    # Test Windows Identity
    try {
        $identityResponse = Invoke-WebRequest -Uri "$ApiBaseUrl/api/debug/identity" -UseDefaultCredentials -ErrorAction Stop
        if ($identityResponse.StatusCode -eq 200) {
            $identity = $identityResponse.Content | ConvertFrom-Json
            $hasWindowsIdentity = $identity.AuthenticationType -eq "Negotiate" -or $identity.AuthenticationType -eq "NTLM"
            Add-TestResult -Category "API" -Test "Windows Identity" -Passed $hasWindowsIdentity -Details "Auth Type: $($identity.AuthenticationType), User: $($identity.Name)"
        }
    } catch {
        Add-TestResult -Category "API" -Test "Windows Identity" -Passed $false -Error $_.Exception.Message
    }
}

# 4. Test Kerberos/NTLM
Write-Host "`n🎫 Testing Kerberos/NTLM..." -ForegroundColor Blue

# Test Kerberos Tickets
try {
    $tickets = klist tickets 2>&1
    if ($LASTEXITCODE -eq 0) {
        $ticketCount = ($tickets | Where-Object { $_ -like "*krbtgt*" }).Count
        Add-TestResult -Category "Kerberos" -Test "Kerberos Tickets" -Passed ($ticketCount -gt 0) -Details "Found $ticketCount tickets"
    } else {
        Add-TestResult -Category "Kerberos" -Test "Kerberos Tickets" -Passed $false -Error "klist command failed"
    }
} catch {
    Add-TestResult -Category "Kerberos" -Test "Kerberos Tickets" -Passed $false -Error $_.Exception.Message
}

# Test Time Sync (important for Kerberos)
try {
    $w32tm = w32tm /query /status 2>&1
    if ($LASTEXITCODE -eq 0) {
        $syncStatus = $w32tm | Where-Object { $_ -like "*Last Successful Sync Time*" }
        Add-TestResult -Category "Kerberos" -Test "Time Synchronization" -Passed ($syncStatus.Count -gt 0) -Details "Time sync appears configured"
    } else {
        Add-TestResult -Category "Kerberos" -Test "Time Synchronization" -Passed $false -Error "w32tm command failed"
    }
} catch {
    Add-TestResult -Category "Kerberos" -Test "Time Synchronization" -Passed $false -Error $_.Exception.Message
}

# 5. Test Network Configuration
Write-Host "`n🌐 Testing Network Configuration..." -ForegroundColor Blue

# Test DNS Resolution
try {
    $dnsResult = Resolve-DnsName -Name $Domain -ErrorAction Stop
    Add-TestResult -Category "Network" -Test "DNS Resolution" -Passed $true -Details "Resolved $Domain"
} catch {
    Add-TestResult -Category "Network" -Test "DNS Resolution" -Passed $false -Error $_.Exception.Message
}

# Test Port Connectivity (Kerberos)
try {
    $kerberosPort = Test-NetConnection -ComputerName $Domain -Port 88 -ErrorAction Stop
    Add-TestResult -Category "Network" -Test "Kerberos Port (88)" -Passed $kerberosPort.TcpTestSucceeded -Details "Connection: $($kerberosPort.TcpTestSucceeded)"
} catch {
    Add-TestResult -Category "Network" -Test "Kerberos Port (88)" -Passed $false -Error $_.Exception.Message
}

# Test LDAP Port
try {
    $ldapPort = Test-NetConnection -ComputerName $Domain -Port 389 -ErrorAction Stop
    Add-TestResult -Category "Network" -Test "LDAP Port (389)" -Passed $ldapPort.TcpTestSucceeded -Details "Connection: $($ldapPort.TcpTestSucceeded)"
} catch {
    Add-TestResult -Category "Network" -Test "LDAP Port (389)" -Passed $false -Error $_.Exception.Message
}

# 6. Generate Test Report
Write-Host "`n📊 Test Results Summary:" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

$Categories = $TestResults | Group-Object Category

foreach ($Category in $Categories) {
    $TotalTests = $Category.Group.Count
    $PassedTests = ($Category.Group | Where-Object { $_.Passed }).Count
    $FailedTests = $TotalTests - $PassedTests
    $PassRate = [math]::Round(($PassedTests / $TotalTests) * 100, 1)
    
    $Color = if ($PassRate -eq 100) { "Green" } elseif ($PassRate -ge 75) { "Yellow" } else { "Red" }
    
    Write-Host "`n$($Category.Name) Tests: $PassedTests/$TotalTests ($PassRate%)" -ForegroundColor $Color
    
    foreach ($Test in $Category.Group | Where-Object { -not $_.Passed }) {
        Write-Host "  ❌ $($Test.Test): $($Test.Error)" -ForegroundColor Red
    }
}

# Overall Summary
$TotalTests = $TestResults.Count
$TotalPassed = ($TestResults | Where-Object { $_.Passed }).Count
$OverallPassRate = [math]::Round(($TotalPassed / $TotalTests) * 100, 1)

Write-Host "`nOverall Results: $TotalPassed/$TotalTests tests passed ($OverallPassRate%)" -ForegroundColor $(if ($OverallPassRate -eq 100) { "Green" } elseif ($OverallPassRate -ge 75) { "Yellow" } else { "Red" })

# 7. Recommendations
Write-Host "`n💡 Recommendations:" -ForegroundColor Cyan

if ($OverallPassRate -lt 100) {
    Write-Host "Issues found that need attention:" -ForegroundColor Yellow
    
    $FailedTests = $TestResults | Where-Object { -not $_.Passed }
    foreach ($FailedTest in $FailedTests) {
        Write-Host "  • Fix $($FailedTest.Category) - $($FailedTest.Test)" -ForegroundColor Red
    }
    
    Write-Host "`nCommon Solutions:" -ForegroundColor White
    Write-Host "  • Ensure computer is domain-joined and time is synchronized" -ForegroundColor Gray
    Write-Host "  • Verify service account password and permissions" -ForegroundColor Gray
    Write-Host "  • Check firewall settings for Kerberos/LDAP ports" -ForegroundColor Gray
    Write-Host "  • Ensure SPNs are correctly configured" -ForegroundColor Gray
    Write-Host "  • Verify IIS application pool configuration" -ForegroundColor Gray
} else {
    Write-Host "🎉 All tests passed! Windows Authentication is properly configured." -ForegroundColor Green
}

# 8. Export Results
$ReportPath = "WindowsAuth_TestResults_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
$TestResults | ConvertTo-Json -Depth 3 | Out-File -FilePath $ReportPath -Encoding UTF8

Write-Host "`n📄 Detailed results exported to: $ReportPath" -ForegroundColor Cyan

# 9. Next Steps
Write-Host "`n📋 Next Steps:" -ForegroundColor Blue
Write-Host "1. Review any failed tests and implement fixes" -ForegroundColor White
Write-Host "2. Test with actual domain users in VCDevTool groups" -ForegroundColor White
Write-Host "3. Monitor authentication logs for issues" -ForegroundColor White
Write-Host "4. Test from client machines in different network segments" -ForegroundColor White
Write-Host "5. Validate SSL certificate configuration for production" -ForegroundColor White

Write-Host "`n🎯 Testing completed!" -ForegroundColor Green 