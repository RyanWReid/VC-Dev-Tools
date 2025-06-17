# Network Debug for Client Issues
Write-Host "=== Network Debug Test ===" -ForegroundColor Cyan

Write-Host "`n1. Testing basic connectivity..." -ForegroundColor Green
try {
    $ping = Test-NetConnection -ComputerName "localhost" -Port 5289 -InformationLevel Quiet
    if ($ping) {
        Write-Host "✓ Port 5289 is reachable" -ForegroundColor Green
    } else {
        Write-Host "✗ Port 5289 is not reachable" -ForegroundColor Red
    }
}
catch {
    Write-Host "✗ Network test failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n2. Testing with different URLs..." -ForegroundColor Green

$urls = @(
    "http://localhost:5289/api/auth/register",
    "http://127.0.0.1:5289/api/auth/register", 
    "http://[::1]:5289/api/auth/register"
)

$testData = @{
    Id = "network-test"
    Name = "Network Test"
    IpAddress = "127.0.0.152"
    HardwareFingerprint = "NETWORK-TEST"
} | ConvertTo-Json

foreach ($url in $urls) {
    Write-Host "Testing: $url" -ForegroundColor Cyan
    try {
        $result = Invoke-RestMethod -Uri $url -Method POST -Body $testData -ContentType "application/json" -TimeoutSec 10
        Write-Host "  ✓ SUCCESS" -ForegroundColor Green
    }
    catch {
        Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            Write-Host "    Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
        }
    }
}

Write-Host "`n3. Testing what the actual client machine name would be..." -ForegroundColor Green
$realClientData = @{
    Id = $env:COMPUTERNAME
    Name = $env:COMPUTERNAME
    IpAddress = "127.0.0.153"
    HardwareFingerprint = "HW-$env:COMPUTERNAME"
} | ConvertTo-Json

Write-Host "Real client data would be:" -ForegroundColor Cyan
Write-Host $realClientData -ForegroundColor Gray

try {
    $result = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $realClientData -ContentType "application/json"
    Write-Host "✓ Real client data registration SUCCESS" -ForegroundColor Green
}
catch {
    Write-Host "✗ Real client data registration FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Network Debug Complete ===" -ForegroundColor Cyan