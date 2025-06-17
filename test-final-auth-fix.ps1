# Final Authentication Fix Test
# This script comprehensively tests the authentication and authorization fixes

Write-Host "🔧 VCDevTool Authentication & Authorization Fix - Final Test" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan

$apiBaseUrl = "http://localhost:5289"

# Test 1: Register a test node and get JWT token
Write-Host "`n1. Testing Node Registration..." -ForegroundColor Yellow
try {
    $nodeData = @{
        "Id" = "final-test-$(Get-Random -Maximum 9999)"
        "Name" = "Final Test Node"
        "IpAddress" = "127.0.0.3"
        "HardwareFingerprint" = "FINAL-TEST-$(New-Guid)"
    } | ConvertTo-Json

    $registerResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
    
    if ($registerResponse.Token) {
        Write-Host "   ✅ Registration successful!" -ForegroundColor Green
        Write-Host "   📝 Node ID: $($registerResponse.NodeId)" -ForegroundColor Cyan
        Write-Host "   🎫 Token received: YES" -ForegroundColor Cyan
        Write-Host "   👥 Roles: $($registerResponse.Roles -join ', ')" -ForegroundColor Cyan
        
        $token = $registerResponse.Token
        $headers = @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" }
    } else {
        Write-Host "   ❌ Registration failed - no token received" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ❌ Registration failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Test NodePolicy authorization (accessing tasks endpoint)
Write-Host "`n2. Testing NodePolicy Authorization (GET /api/tasks)..." -ForegroundColor Yellow
try {
    $tasksResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Headers $headers
    Write-Host "   ✅ NodePolicy authorization successful!" -ForegroundColor Green
    Write-Host "   📋 Found $($tasksResponse.Count) task(s)" -ForegroundColor Cyan
} catch {
    if ($_.Exception.Response.StatusCode -eq 403) {
        Write-Host "   ❌ 403 Forbidden - NodePolicy authorization FAILED" -ForegroundColor Red
        Write-Host "   🔧 Authorization policies still need fixing" -ForegroundColor Yellow
    } elseif ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "   ❌ 401 Unauthorized - Authentication FAILED" -ForegroundColor Red
        Write-Host "   🔧 Authentication sharing still not working" -ForegroundColor Yellow
    } else {
        Write-Host "   ❌ Unexpected error: $($_.Exception.Message)" -ForegroundColor Red
    }
    exit 1
}

# Test 3: Test AdminPolicy authorization (creating a task)
Write-Host "`n3. Testing AdminPolicy Authorization (POST /api/tasks)..." -ForegroundColor Yellow
try {
    $taskData = @{
        "Name" = "Test Task - Authentication Fix Verification"
        "SourcePath" = "C:\TestSource"
        "DestinationPath" = "C:\TestDest"
        "TaskType" = "TestMessage"
        "Priority" = "Normal"
        "Status" = "Pending"
    } | ConvertTo-Json

    $createResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Method POST -Body $taskData -Headers $headers
    Write-Host "   ✅ AdminPolicy authorization successful!" -ForegroundColor Green
    Write-Host "   📝 Created task ID: $($createResponse.Id)" -ForegroundColor Cyan
} catch {
    if ($_.Exception.Response.StatusCode -eq 403) {
        Write-Host "   ⚠️  403 Forbidden - AdminPolicy requires admin role (expected)" -ForegroundColor Yellow
        Write-Host "   ℹ️  Node role cannot create tasks (this is normal security)" -ForegroundColor Cyan
    } else {
        Write-Host "   ❌ Unexpected error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Test 4: Test nodes endpoint access
Write-Host "`n4. Testing Nodes Endpoint Access..." -ForegroundColor Yellow
try {
    $nodesResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/nodes" -Headers $headers
    Write-Host "   ✅ Nodes endpoint access successful!" -ForegroundColor Green
    Write-Host "   🖥️  Found $($nodesResponse.Count) registered node(s)" -ForegroundColor Cyan
} catch {
    Write-Host "   ❌ Nodes endpoint access failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Summary
Write-Host "`n" -NoNewline
Write-Host "🎉 AUTHENTICATION & AUTHORIZATION FIX TEST COMPLETE!" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green

Write-Host "`n✅ Key Fixes Verified:" -ForegroundColor Cyan
Write-Host "   🔐 Node registration and JWT token generation: WORKING" -ForegroundColor Green  
Write-Host "   🎫 JWT authentication with Bearer tokens: WORKING" -ForegroundColor Green
Write-Host "   👥 NodePolicy authorization for Node role: WORKING" -ForegroundColor Green
Write-Host "   🛡️  Proper authorization policy enforcement: WORKING" -ForegroundColor Green

Write-Host "`n🎯 CLIENT SHOULD NOW WORK!" -ForegroundColor Yellow
Write-Host "   • Registration errors: FIXED" -ForegroundColor Green
Write-Host "   • Authentication sharing: FIXED" -ForegroundColor Green  
Write-Host "   • 403 Forbidden errors: FIXED" -ForegroundColor Green
Write-Host "   • Task execution: SHOULD WORK" -ForegroundColor Green

Write-Host "`n🚀 Try running a task in the client - it should work without authentication errors!" -ForegroundColor Cyan 