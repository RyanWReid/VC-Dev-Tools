# Test with API logging visible
Write-Host "Starting API with visible logging..." -ForegroundColor Green

# Start API in background and capture logs
$apiJob = Start-Job -ScriptBlock {
    Set-Location "C:\Development\VC-Dev-Tool"
    dotnet run --project VCDevTool.API
}

Start-Sleep -Seconds 8

try {
    Write-Host "`nTesting lock acquisition..." -ForegroundColor Cyan
    
    # Simple lock request
    $lockRequest = @{
        FilePath = "test.txt"
        NodeId = "node1"
    } | ConvertTo-Json

    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5289/api/filelocks/acquire" -Method POST -Body $lockRequest -ContentType "application/json"
        Write-Host "SUCCESS: Lock acquired! Status: $($response.StatusCode)" -ForegroundColor Green
    }
    catch {
        Write-Host "FAILED: Lock acquisition failed" -ForegroundColor Red
        Write-Host "Status Code: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
        
        # Get the actual response
        try {
            $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
            $errorResponse = $reader.ReadToEnd()
            Write-Host "Error response: $errorResponse" -ForegroundColor Yellow
        }
        catch {
            Write-Host "Could not read response body" -ForegroundColor Gray
        }
    }
    
    Write-Host "`nAPI Job Output:" -ForegroundColor Cyan
    $jobOutput = Receive-Job -Job $apiJob
    Write-Host $jobOutput -ForegroundColor Gray
}
finally {
    Write-Host "`nStopping API job..." -ForegroundColor Yellow
    Stop-Job -Job $apiJob
    Remove-Job -Job $apiJob
}