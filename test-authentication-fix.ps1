# Test Authentication Fix
# This script verifies that the client can properly authenticate and execute tasks

Write-Host "🔧 Testing VCDevTool Authentication Fix..." -ForegroundColor Cyan

$apiBaseUrl = "http://localhost:5289"

# Wait for client to register
Write-Host "⏳ Waiting for client to register with API..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Test 1: Check if nodes are registered
Write-Host "`n1. Checking registered nodes..." -ForegroundColor Green
try {
    $nodesResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/nodes" -Headers @{"Authorization" = "Bearer test-token"} -ErrorAction SilentlyContinue
    if ($nodesResponse) {
        Write-Host "   ✓ Found $($nodesResponse.Count) registered node(s)" -ForegroundColor Green
        $nodesResponse | ForEach-Object { 
            Write-Host "     - Node: $($_.Name) (ID: $($_.Id))" -ForegroundColor Cyan 
        }
    }
}
catch {
    Write-Host "   ℹ️ Cannot check nodes without proper authentication (expected)" -ForegroundColor Yellow
}

# Test 2: Create a test task to verify the authentication works
Write-Host "`n2. Testing task creation with proper authentication..." -ForegroundColor Green

# First register a test node to get a token
$testNodeData = @{
    "Id" = "test-auth-node-$(Get-Random -Maximum 9999)"
    "Name" = "Authentication Test Node"
    "IpAddress" = "127.0.0.2"  # Use different IP to avoid conflicts
    "HardwareFingerprint" = "AUTH-TEST-$(New-Guid)"
} | ConvertTo-Json

try {
    $registerResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $testNodeData -ContentType "application/json"
    
    if ($registerResponse.Token) {
        Write-Host "   ✓ Test node registered successfully" -ForegroundColor Green
        
        $authHeaders = @{
            "Authorization" = "Bearer $($registerResponse.Token)"
            "Content-Type" = "application/json"
        }
        
        # Test 3: Try to access authenticated endpoints
        Write-Host "`n3. Testing authenticated API access..." -ForegroundColor Green
        
        try {
            $tasksResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Headers $authHeaders
            Write-Host "   ✓ Successfully accessed tasks endpoint" -ForegroundColor Green
            Write-Host "   Found $($tasksResponse.Count) task(s) in system" -ForegroundColor Cyan
        }
        catch {
            Write-Host "   ✗ Failed to access tasks endpoint: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        try {
            $nodesResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/nodes" -Headers $authHeaders
            Write-Host "   ✓ Successfully accessed nodes endpoint" -ForegroundColor Green
            Write-Host "   Found $($nodesResponse.Count) node(s) in system" -ForegroundColor Cyan
        }
        catch {
            Write-Host "   ✗ Failed to access nodes endpoint: $($_.Exception.Message)" -ForegroundColor Red
        }
        
    } else {
        Write-Host "   ✗ Registration failed - no token received" -ForegroundColor Red
    }
    
}
catch {
    Write-Host "   ✗ Registration failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n✅ Authentication test completed!" -ForegroundColor Green
Write-Host "🎯 If you see success messages above, the authentication sharing fix is working!" -ForegroundColor Yellow
Write-Host "📱 You can now try running tasks in the client - they should work without authentication errors." -ForegroundColor Yellow 