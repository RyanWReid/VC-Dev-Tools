# Test API without authentication
Write-Host "Testing API without authentication..." -ForegroundColor Green

$tests = @(
    @{ Url = "http://localhost:5289/health"; Name = "Health Check" },
    @{ Url = "http://localhost:5289/api/health"; Name = "API Health Check" },
    @{ Url = "http://localhost:5289/api/tasks"; Name = "Tasks Endpoint" },
    @{ Url = "http://localhost:5289/api/nodes"; Name = "Nodes Endpoint" }
)

foreach ($test in $tests) {
    Write-Host "`nTesting: $($test.Name)" -ForegroundColor Cyan
    Write-Host "URL: $($test.Url)" -ForegroundColor Gray
    
    try {
        $response = Invoke-RestMethod -Uri $test.Url -TimeoutSec 10
        Write-Host "  SUCCESS: Response received" -ForegroundColor Green
        if ($response -is [string] -and $response.Length -lt 200) {
            Write-Host "  Content: $response" -ForegroundColor White
        } elseif ($response -is [array]) {
            Write-Host "  Content: Array with $($response.Count) items" -ForegroundColor White
        } else {
            Write-Host "  Content: $($response.GetType().Name) object" -ForegroundColor White
        }
    }
    catch {
        Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            Write-Host "  Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
        }
    }
}

Write-Host "`n=== API Authentication Test Complete ===" -ForegroundColor Green