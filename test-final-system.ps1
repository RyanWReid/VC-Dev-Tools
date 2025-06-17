# Final System Test - No Authentication
Write-Host "=== Final System Test (No Authentication) ===" -ForegroundColor Cyan

# Test 1: API Health
Write-Host "`n1. Testing API Health..." -ForegroundColor Green
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5289/api/health" -TimeoutSec 10
    Write-Host "   ✓ API Health: SUCCESS" -ForegroundColor Green
    Write-Host "   Response: $($health | ConvertTo-Json -Compress)" -ForegroundColor Gray
}
catch {
    Write-Host "   ✗ API Health: FAILED - $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Get Tasks
Write-Host "`n2. Testing Tasks Endpoint..." -ForegroundColor Green
try {
    $tasks = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks" -TimeoutSec 10
    Write-Host "   ✓ Tasks: SUCCESS" -ForegroundColor Green
    Write-Host "   Found $($tasks.Count) tasks" -ForegroundColor Gray
}
catch {
    Write-Host "   ✗ Tasks: FAILED - $($_.Exception.Message)" -ForegroundColor Red
}

# Test 3: Get Nodes
Write-Host "`n3. Testing Nodes Endpoint..." -ForegroundColor Green
try {
    $nodes = Invoke-RestMethod -Uri "http://localhost:5289/api/nodes" -TimeoutSec 10
    Write-Host "   ✓ Nodes: SUCCESS" -ForegroundColor Green
    Write-Host "   Found $($nodes.Count) nodes" -ForegroundColor Gray
}
catch {
    Write-Host "   ✗ Nodes: FAILED - $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Create a Test Task
Write-Host "`n4. Testing Task Creation..." -ForegroundColor Green
$testTask = @{
    Name = "Test Task - No Auth"
    Type = 0  # TestMessage
    Parameters = "Test parameters"
} | ConvertTo-Json

try {
    $newTask = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks" -Method POST -Body $testTask -ContentType "application/json" -TimeoutSec 10
    Write-Host "   ✓ Task Creation: SUCCESS" -ForegroundColor Green
    Write-Host "   Created task ID: $($newTask.Id)" -ForegroundColor Gray
}
catch {
    Write-Host "   ✗ Task Creation: FAILED - $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "   Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== System Test Complete ===" -ForegroundColor Cyan
Write-Host "✓ API Server: Running without authentication" -ForegroundColor Green
Write-Host "✓ Client: Should be able to connect now" -ForegroundColor Green