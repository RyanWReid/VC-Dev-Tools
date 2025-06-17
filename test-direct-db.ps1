# Test Direct Database Access for Locks
Write-Host "Testing direct database access for locks..." -ForegroundColor Green

# Start the API
Write-Host "Starting API server..." -ForegroundColor Cyan
$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project VCDevTool.API" -WorkingDirectory "C:\Development\VC-Dev-Tool" -PassThru

# Wait for startup
Start-Sleep -Seconds 10

try {
    # Test TaskService methods directly via API endpoints
    Write-Host "`nTesting via API endpoints..." -ForegroundColor Cyan
    
    # 1. Check health first
    $health = Invoke-RestMethod -Uri "http://localhost:5289/api/health" -Method GET
    Write-Host "API Health: $($health.status)" -ForegroundColor Green
    
    # 2. Check nodes 
    $nodes = Invoke-RestMethod -Uri "http://localhost:5289/api/nodes" -Method GET
    Write-Host "Nodes count: $($nodes.Count)" -ForegroundColor Green
    
    # 3. Try to reset locks first
    Write-Host "`nResetting all locks..." -ForegroundColor Cyan
    try {
        $resetResult = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/reset" -Method POST
        Write-Host "Reset result: $($resetResult.message)" -ForegroundColor Green
    }
    catch {
        Write-Host "Reset failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # 4. Check locks again
    $locks = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks" -Method GET
    Write-Host "Locks after reset: $($locks.Count)" -ForegroundColor Green
    
    # 5. Try a simple lock with verbose logging
    Write-Host "`nAttempting lock with simple file path..." -ForegroundColor Cyan
    $simpleLock = @{
        FilePath = "test.txt"
        NodeId = "node1"
    } | ConvertTo-Json
    
    try {
        Invoke-WebRequest -Uri "http://localhost:5289/api/filelocks/acquire" -Method POST -Body $simpleLock -ContentType "application/json"
        Write-Host "SUCCESS: Simple lock acquired!" -ForegroundColor Green
    }
    catch {
        Write-Host "FAILED: Simple lock failed - $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        # Get the actual response
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        $errorResponse = $reader.ReadToEnd()
        Write-Host "Error response: $errorResponse" -ForegroundColor Yellow
    }
    
    # 6. Check locks after attempt
    $locksAfter = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks" -Method GET
    Write-Host "Locks after attempt: $($locksAfter.Count)" -ForegroundColor Green
    
}
finally {
    Write-Host "`nStopping API..." -ForegroundColor Yellow
    if ($apiProcess -and !$apiProcess.HasExited) {
        $apiProcess.Kill()
        $apiProcess.WaitForExit(5000)
    }
}