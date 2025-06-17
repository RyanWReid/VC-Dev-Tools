# Test Exact Client Behavior
Write-Host "Testing exact client behavior..." -ForegroundColor Green

# Simulate the exact data the client generates
$machineId = $env:COMPUTERNAME
$nodeData = @{
    Id = $machineId
    Name = $machineId
    IpAddress = "127.0.0.154" # Valid IP
    HardwareFingerprint = "HW-$machineId-$(Get-Random -Maximum 999999)"
    WindowsIdentity = @{
        UserName = "$env:USERDOMAIN\$env:USERNAME"
        Domain = $env:USERDOMAIN  
        IsAuthenticated = $true
    }
} | ConvertTo-Json -Depth 3

Write-Host "Exact client node data:" -ForegroundColor Cyan
Write-Host $nodeData -ForegroundColor Gray

Write-Host "`nTesting registration..." -ForegroundColor Yellow
try {
    $result = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json" -Verbose
    Write-Host "SUCCESS: Registration worked!" -ForegroundColor Green
    Write-Host "Token received: $($result.Token[0..30] -join '')..." -ForegroundColor Cyan
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Error details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
    if ($_.Exception.Response) {
        Write-Host "Status code: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

Write-Host "`nTesting if node already exists (409 conflict)..." -ForegroundColor Yellow
try {
    $result2 = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
    Write-Host "Unexpected: Second registration also succeeded" -ForegroundColor Yellow
}
catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "Expected: Got 409 Conflict for duplicate node" -ForegroundColor Green
        
        # Now test login
        Write-Host "`nTesting login for existing node..." -ForegroundColor Yellow
        
        $nodeObj = $nodeData | ConvertFrom-Json
        $loginData = @{
            NodeId = $nodeObj.Id
            HardwareFingerprint = $nodeObj.HardwareFingerprint
        } | ConvertTo-Json
        
        try {
            $loginResult = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/login" -Method POST -Body $loginData -ContentType "application/json"
            Write-Host "SUCCESS: Login worked!" -ForegroundColor Green
            Write-Host "Login token: $($loginResult.Token[0..30] -join '')..." -ForegroundColor Cyan
        }
        catch {
            Write-Host "LOGIN FAILED: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.ErrorDetails) {
                Write-Host "Login error details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "Unexpected error: $($_.Exception.Message)" -ForegroundColor Red
    }
}