# Test Lock Acquisition and Release After Authentication Fix
Write-Host "Testing file lock operations after authentication fix..." -ForegroundColor Green

# Start the API first
Write-Host "Starting API server..." -ForegroundColor Cyan
$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project VCDevTool.API" -WorkingDirectory "C:\Development\VC-Dev-Tool" -WindowStyle Hidden -PassThru

# Wait for API to start
Write-Host "Waiting for API to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 8

try {
    # Test 1: Try to acquire a lock
    Write-Host "`nTest 1: Acquiring file lock..." -ForegroundColor Cyan
    $lockRequest = @{
        FilePath = "C:\TestFolder\TestFile.txt"
        NodeId = "TEST-NODE-123"
    } | ConvertTo-Json

    $acquireResponse = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/acquire" -Method POST -Body $lockRequest -ContentType "application/json"
    Write-Host "SUCCESS: Lock acquired!" -ForegroundColor Green

    # Test 2: Try to acquire the same lock (should fail)
    Write-Host "`nTest 2: Attempting to acquire same lock with different node..." -ForegroundColor Cyan
    $lockRequest2 = @{
        FilePath = "C:\TestFolder\TestFile.txt"
        NodeId = "TEST-NODE-456"
    } | ConvertTo-Json

    try {
        $acquireResponse2 = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/acquire" -Method POST -Body $lockRequest2 -ContentType "application/json"
        Write-Host "UNEXPECTED: Second lock acquisition succeeded (should have failed)" -ForegroundColor Red
    }
    catch {
        Write-Host "SUCCESS: Second lock acquisition failed as expected - $($_.Exception.Message)" -ForegroundColor Green
    }

    # Test 3: List active locks
    Write-Host "`nTest 3: Getting active locks..." -ForegroundColor Cyan
    $activeLocks = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks" -Method GET
    Write-Host "Active locks count: $($activeLocks.Count)" -ForegroundColor Green
    foreach ($lock in $activeLocks) {
        Write-Host "  - File: $($lock.FilePath), Node: $($lock.LockingNodeId)" -ForegroundColor Gray
    }

    # Test 4: Release the lock
    Write-Host "`nTest 4: Releasing file lock..." -ForegroundColor Cyan
    $releaseResponse = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/release" -Method POST -Body $lockRequest -ContentType "application/json"
    Write-Host "SUCCESS: Lock released!" -ForegroundColor Green

    # Test 5: Verify lock is gone
    Write-Host "`nTest 5: Verifying lock is released..." -ForegroundColor Cyan
    $activeLocks2 = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks" -Method GET
    Write-Host "Active locks count after release: $($activeLocks2.Count)" -ForegroundColor Green

    # Test 6: Reset all locks
    Write-Host "`nTest 6: Testing lock reset..." -ForegroundColor Cyan
    $resetResponse = Invoke-RestMethod -Uri "http://localhost:5289/api/filelocks/reset" -Method POST
    Write-Host "SUCCESS: All locks reset - $($resetResponse.message)" -ForegroundColor Green

    Write-Host "`nAll lock tests completed successfully!" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}
finally {
    # Clean up
    Write-Host "`nStopping API server..." -ForegroundColor Yellow
    if ($apiProcess -and !$apiProcess.HasExited) {
        $apiProcess.Kill()
        $apiProcess.WaitForExit(5000)
    }
    Write-Host "Test completed." -ForegroundColor Cyan
}