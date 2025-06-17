# Debug Lock Issues
Write-Host "Debugging lock acquisition issues..." -ForegroundColor Green

# Start the API first
Write-Host "Starting API server..." -ForegroundColor Cyan
$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project VCDevTool.API" -WorkingDirectory "C:\Development\VC-Dev-Tool" -PassThru

# Wait longer for API to start
Write-Host "Waiting for API to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

try {
    # First, check if API is responding
    Write-Host "`nChecking API health..." -ForegroundColor Cyan
    try {
        $healthCheck = Invoke-RestMethod -Uri "http://localhost:5289/api/nodes" -Method GET -TimeoutSec 5
        Write-Host "SUCCESS: API is responding" -ForegroundColor Green
    }
    catch {
        Write-Host "FAILED: API not responding - $($_.Exception.Message)" -ForegroundColor Red
        return
    }

    # Check current locks
    Write-Host "`nChecking existing locks..." -ForegroundColor Cyan
    try {
        $currentLocks = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks" -Method GET
        Write-Host "Current locks: $($currentLocks.Count)" -ForegroundColor Green
        foreach ($lock in $currentLocks) {
            Write-Host "  - File: $($lock.FilePath), Node: $($lock.LockingNodeId)" -ForegroundColor Gray
        }
        
        # If there are existing locks, reset them
        if ($currentLocks.Count -gt 0) {
            Write-Host "Resetting existing locks..." -ForegroundColor Yellow
            $resetResponse = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/reset" -Method POST
            Write-Host "Reset result: $($resetResponse.message)" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "Error checking locks: $($_.Exception.Message)" -ForegroundColor Red
    }

    # Now try to acquire a lock with detailed error info
    Write-Host "`nAttempting lock acquisition..." -ForegroundColor Cyan
    $lockRequest = @{
        FilePath = "C:\TestFolder\TestFile.txt"
        NodeId = "TEST-NODE-123"
    } | ConvertTo-Json

    Write-Host "Request body: $lockRequest" -ForegroundColor Gray

    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5289/api/filelocks/acquire" -Method POST -Body $lockRequest -ContentType "application/json"
        Write-Host "SUCCESS: Lock acquired! Status: $($response.StatusCode)" -ForegroundColor Green
    }
    catch {
        Write-Host "FAILED: Lock acquisition failed" -ForegroundColor Red
        Write-Host "Status Code: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
        Write-Host "Status Description: $($_.Exception.Response.StatusDescription)" -ForegroundColor Yellow
        
        if ($_.ErrorDetails) {
            Write-Host "Error Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
        }
        
        # Try to read the response stream for more details
        try {
            $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body: $responseBody" -ForegroundColor Yellow
        }
        catch {
            Write-Host "Could not read response body" -ForegroundColor Gray
        }
    }
}
finally {
    # Clean up
    Write-Host "`nStopping API server..." -ForegroundColor Yellow
    if ($apiProcess -and !$apiProcess.HasExited) {
        $apiProcess.Kill() 
        $apiProcess.WaitForExit(5000)
    }
    Write-Host "Debug completed." -ForegroundColor Cyan
}