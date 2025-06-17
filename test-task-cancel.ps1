# Test task cancellation functionality
Write-Host "Testing task cancellation API..." -ForegroundColor Green

# Start API in background
$apiJob = Start-Job -ScriptBlock {
    Set-Location "C:\Development\VC-Dev-Tool"
    dotnet run --project VCDevTool.API
}

Start-Sleep -Seconds 8

try {
    Write-Host "`n1. Creating a test task..." -ForegroundColor Cyan
    
    # Create a test task
    $taskRequest = @{
        Name = "Test Cancellation Task"
        Type = 1  # TestMessage
        TargetPath = "C:\temp\test"
        Status = 0  # Pending
    } | ConvertTo-Json
    
    $createResponse = Invoke-WebRequest -Uri "http://localhost:5289/api/tasks" -Method POST -Body $taskRequest -ContentType "application/json"
    $task = $createResponse.Content | ConvertFrom-Json
    Write-Host "Created task ID: $($task.Id)" -ForegroundColor Green
    
    Write-Host "`n2. Setting task to Running status..." -ForegroundColor Cyan
    
    # Update task to Running status
    $statusRequest = @{
        Status = 1  # Running
        ResultMessage = "Task started"
    } | ConvertTo-Json
    
    $updateResponse = Invoke-WebRequest -Uri "http://localhost:5289/api/tasks/$($task.Id)/status" -Method PUT -Body $statusRequest -ContentType "application/json"
    Write-Host "Task set to Running" -ForegroundColor Green
    
    Write-Host "`n3. Testing task cancellation..." -ForegroundColor Cyan
    
    # Cancel the task
    $cancelRequest = @{
        Status = 4  # Cancelled
        ResultMessage = "Task was manually aborted by user"
    } | ConvertTo-Json
    
    try {
        $cancelResponse = Invoke-WebRequest -Uri "http://localhost:5289/api/tasks/$($task.Id)/status" -Method PUT -Body $cancelRequest -ContentType "application/json"
        Write-Host "SUCCESS: Task cancelled successfully! Status: $($cancelResponse.StatusCode)" -ForegroundColor Green
        
        # Verify the task status
        $verifyResponse = Invoke-WebRequest -Uri "http://localhost:5289/api/tasks/$($task.Id)" -Method GET
        $updatedTask = $verifyResponse.Content | ConvertFrom-Json
        Write-Host "Final task status: $($updatedTask.Status) (4 = Cancelled)" -ForegroundColor Yellow
    }
    catch {
        Write-Host "FAILED: Task cancellation failed" -ForegroundColor Red
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