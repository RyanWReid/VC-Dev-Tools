# Simple Debug for Client Auth
Write-Host "Testing client-style authentication..." -ForegroundColor Green

$nodeData = @{
    Id = "$env:COMPUTERNAME-DEBUG"
    Name = $env:COMPUTERNAME  
    IpAddress = "127.0.0.151"
    HardwareFingerprint = "DEBUG-$env:COMPUTERNAME"
} | ConvertTo-Json

Write-Host "Node data: $nodeData" -ForegroundColor Gray

try {
    $result = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
    Write-Host "SUCCESS: $($result.Token)" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
}