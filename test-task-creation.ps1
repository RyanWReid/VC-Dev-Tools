# Test Task Creation Without Authentication
Write-Host "Testing task creation without authentication..." -ForegroundColor Green

# Test creating a simple task
$taskData = @{
    Name = "Test Process Task"
    Type = 0  # TestMessage type
    Parameters = @{
        Message = "Hello from test task"
        Delay = 5
    } | ConvertTo-Json
} | ConvertTo-Json

Write-Host "Creating test task..." -ForegroundColor Cyan
Write-Host "Task data: $taskData" -ForegroundColor Gray

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks" -Method POST -Body $taskData -ContentType "application/json"
    Write-Host "SUCCESS: Task created!" -ForegroundColor Green
    Write-Host "Task ID: $($response.Id)" -ForegroundColor Cyan
    Write-Host "Task Name: $($response.Name)" -ForegroundColor Cyan
    Write-Host "Task Status: $($response.Status)" -ForegroundColor Cyan
    
    # Get all tasks to verify
    $tasks = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks"
    Write-Host "Total tasks in system: $($tasks.Count)" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}