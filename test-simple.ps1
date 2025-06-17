# Simple Authentication Test
Write-Host "Testing API authentication..." -ForegroundColor Green

$body = @{
    Id = "test-auth-1"
    Name = "Test Node"
    IpAddress = "127.0.0.150"
    HardwareFingerprint = "TEST-HW-12345"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $body -ContentType "application/json"
    Write-Host "SUCCESS: Registration worked!" -ForegroundColor Green
    Write-Host "Token: $($response.Token)" -ForegroundColor Cyan
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $responseBody = $_.Exception.Response.Content.ReadAsStringAsync().Result
        Write-Host "Response: $responseBody" -ForegroundColor Yellow
    }
}