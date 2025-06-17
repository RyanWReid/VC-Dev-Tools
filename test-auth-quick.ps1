# Quick Authentication Test
# Tests the authentication fix for validation issues

Write-Host "🔧 Quick Authentication Test..." -ForegroundColor Cyan

$apiBaseUrl = "http://localhost:5289"

# Test 1: Simple registration with fallback IP
Write-Host "`n1. Testing registration with localhost IP..." -ForegroundColor Green

$testNode = @{
    "Id" = "test-quick-$(Get-Random -Maximum 999)"
    "Name" = "Quick Test Node"
    "IpAddress" = "127.0.0.150"  # Valid localhost IP
    "HardwareFingerprint" = "QUICK-TEST-$(New-Guid)"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $testNode -ContentType "application/json"
    
    if ($response.Token) {
        Write-Host "   ✓ Registration successful!" -ForegroundColor Green
        Write-Host "   Token received: $($response.Token[0..15] -join '')..." -ForegroundColor Cyan
        
        # Test authenticated request
        $authHeaders = @{ "Authorization" = "Bearer $($response.Token)" }
        $tasksResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Headers $authHeaders
        Write-Host "   ✓ Authenticated request successful!" -ForegroundColor Green
        Write-Host "   Found $($tasksResponse.Count) tasks" -ForegroundColor Cyan
    } else {
        Write-Host "   ✗ No token received" -ForegroundColor Red
    }
}
catch {
    Write-Host "   ✗ Registration failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $responseBody = $_.Exception.Response.Content.ReadAsStringAsync().Result
        Write-Host "   Response: $responseBody" -ForegroundColor Yellow
    }
}

# Test 2: Registration with node identifier (should work with new validation)
Write-Host "`n2. Testing registration with node identifier..." -ForegroundColor Green

$testNode2 = @{
    "Id" = "test-node-$(Get-Random -Maximum 999)"
    "Name" = "Node ID Test"
    "IpAddress" = "NODE-TESTMACHINE-ABC123"  # Node identifier format
    "HardwareFingerprint" = "NODE-ID-TEST-$(New-Guid)"
} | ConvertTo-Json

try {
    $response2 = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $testNode2 -ContentType "application/json"
    
    if ($response2.Token) {
        Write-Host "   ✓ Node identifier registration successful!" -ForegroundColor Green
        Write-Host "   Token received: $($response2.Token[0..15] -join '')..." -ForegroundColor Cyan
    } else {
        Write-Host "   ✗ No token received" -ForegroundColor Red
    }
}
catch {
    Write-Host "   ✗ Node identifier registration failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $responseBody = $_.Exception.Response.Content.ReadAsStringAsync().Result
        Write-Host "   Response: $responseBody" -ForegroundColor Yellow  
    }
}

Write-Host "`n✅ Quick authentication test completed!" -ForegroundColor Green
Write-Host "📋 If you see success messages above, authentication issues are fixed!" -ForegroundColor Yellow