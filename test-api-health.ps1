# Test API Health
Write-Host "Testing API server health..." -ForegroundColor Green

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5289/health" -TimeoutSec 10
    Write-Host "SUCCESS: API responded with status $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Content: $($response.Content)" -ForegroundColor Cyan
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    
    # Try different URLs
    $urls = @(
        "http://127.0.0.1:5289/health",
        "http://localhost:5289/",
        "http://127.0.0.1:5289/"
    )
    
    foreach ($url in $urls) {
        Write-Host "Trying: $url" -ForegroundColor Yellow
        try {
            $resp = Invoke-WebRequest -Uri $url -TimeoutSec 5
            Write-Host "  SUCCESS: Status $($resp.StatusCode)" -ForegroundColor Green
            break
        }
        catch {
            Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}