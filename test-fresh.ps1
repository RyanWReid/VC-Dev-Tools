# Fresh Authentication Test with Unique ID
Write-Host "Testing API authentication with fresh node..." -ForegroundColor Green

$uniqueId = "test-node-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 999)"

$body = @{
    Id = $uniqueId
    Name = "Fresh Test Node"
    IpAddress = "127.0.0.$(Get-Random -Minimum 100 -Maximum 200)"
    HardwareFingerprint = "FRESH-HW-$(Get-Random -Maximum 99999)"
} | ConvertTo-Json

Write-Host "Registering node: $uniqueId" -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $body -ContentType "application/json"
    Write-Host "SUCCESS: Registration worked!" -ForegroundColor Green
    Write-Host "Token received: $($response.Token[0..30] -join '')..." -ForegroundColor Cyan
    
    # Test authenticated API call
    $authHeaders = @{ "Authorization" = "Bearer $($response.Token)" }
    $tasks = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks" -Headers $authHeaders
    Write-Host "SUCCESS: Authenticated API call worked!" -ForegroundColor Green
    Write-Host "Found $($tasks.Count) tasks in system" -ForegroundColor Cyan
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}