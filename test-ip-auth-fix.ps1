# Test IP Address Authentication Fix
# This script tests the fixes for 127.x.x.x IP authentication issues

Write-Host "🔧 Testing IP Address Authentication Fix..." -ForegroundColor Cyan

$apiBaseUrl = "http://localhost:5289"

Write-Host "`n🧪 Test 1: Multiple registrations with same machine (simulating the issue)..." -ForegroundColor Green

# Simulate the problematic scenario where same machine gets different IPs
$machineId = "test-machine-$(Get-Random -Maximum 999)"
$hardwareFingerprint = "HW-FINGERPRINT-$machineId"

Write-Host "   Machine ID: $machineId" -ForegroundColor Cyan
Write-Host "   Hardware Fingerprint: $hardwareFingerprint" -ForegroundColor Cyan

# Registration 1: First run (should succeed)
Write-Host "`n   Registration 1 (First run)..." -ForegroundColor Yellow
$testNode1 = @{
    "Id" = $machineId
    "Name" = "Test Machine - First Run"
    "IpAddress" = "NODE-$machineId-123456"  # Using new format
    "HardwareFingerprint" = $hardwareFingerprint
} | ConvertTo-Json

try {
    $registerResponse1 = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $testNode1 -ContentType "application/json"
    Write-Host "     ✓ First registration successful" -ForegroundColor Green
    Write-Host "     Token: $($registerResponse1.Token[0..20] -join '')..." -ForegroundColor Cyan
    $token1 = $registerResponse1.Token
}
catch {
    Write-Host "     ✗ First registration failed: $($_.Exception.Message)" -ForegroundColor Red
    $token1 = $null
}

# Registration 2: Second run with different IP (should work now)
Write-Host "`n   Registration 2 (Second run - different IP)..." -ForegroundColor Yellow
$testNode2 = @{
    "Id" = $machineId
    "Name" = "Test Machine - Second Run" 
    "IpAddress" = "NODE-$machineId-789012"  # Different IP, same machine
    "HardwareFingerprint" = $hardwareFingerprint  # Same hardware fingerprint
} | ConvertTo-Json

try {
    $registerResponse2 = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $testNode2 -ContentType "application/json"
    Write-Host "     ✓ Second registration successful" -ForegroundColor Green
    Write-Host "     Token: $($registerResponse2.Token[0..20] -join '')..." -ForegroundColor Cyan
    $token2 = $registerResponse2.Token
}
catch {
    Write-Host "     ✗ Second registration failed: $($_.Exception.Message)" -ForegroundColor Red
    $token2 = $null
}

# Test both tokens work
if ($token1) {
    Write-Host "`n   Testing first token..." -ForegroundColor Yellow
    try {
        $authHeaders1 = @{ "Authorization" = "Bearer $token1" }
        $tasksResponse1 = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Headers $authHeaders1
        Write-Host "     ✓ First token works - accessed tasks endpoint" -ForegroundColor Green
    }
    catch {
        Write-Host "     ✗ First token failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

if ($token2) {
    Write-Host "`n   Testing second token..." -ForegroundColor Yellow
    try {
        $authHeaders2 = @{ "Authorization" = "Bearer $token2" }
        $tasksResponse2 = Invoke-RestMethod -Uri "$apiBaseUrl/api/tasks" -Headers $authHeaders2
        Write-Host "     ✓ Second token works - accessed tasks endpoint" -ForegroundColor Green
    }
    catch {
        Write-Host "     ✗ Second token failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n🧪 Test 2: Register multiple different machines with similar IPs..." -ForegroundColor Green

# Test that different machines can have similar-looking IPs
for ($i = 1; $i -le 3; $i++) {
    $machineId = "machine-$i"
    $nodeData = @{
        "Id" = $machineId
        "Name" = "Test Machine $i"
        "IpAddress" = "NODE-MACHINE$i-$(Get-Random -Maximum 999999)"
        "HardwareFingerprint" = "HW-$machineId-$(New-Guid)"
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "$apiBaseUrl/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
        Write-Host "   ✓ Machine $i registered successfully" -ForegroundColor Green
    }
    catch {
        Write-Host "   ✗ Machine $i registration failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n🧪 Test 3: Check if database has nodes with different IPs..." -ForegroundColor Green

# Test with a known token to see nodes
if ($token2) {
    try {
        $authHeaders = @{ "Authorization" = "Bearer $token2" }
        $nodesResponse = Invoke-RestMethod -Uri "$apiBaseUrl/api/nodes" -Headers $authHeaders
        
        Write-Host "   ✓ Found $($nodesResponse.Count) nodes in database:" -ForegroundColor Green
        $nodesResponse | ForEach-Object {
            Write-Host "     - $($_.Name): $($_.IpAddress)" -ForegroundColor Cyan
        }
        
        # Check for any 127.x.x.x addresses
        $legacyIPs = $nodesResponse | Where-Object { $_.IpAddress -match "^127\." }
        if ($legacyIPs) {
            Write-Host "   ⚠️  Found $($legacyIPs.Count) nodes with legacy 127.x.x.x IPs:" -ForegroundColor Yellow
            $legacyIPs | ForEach-Object {
                Write-Host "     - $($_.Name): $($_.IpAddress)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "   ✓ No problematic 127.x.x.x IP addresses found!" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "   ℹ️  Cannot check nodes: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n✅ IP Address Authentication Fix Test Completed!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Summary of fixes applied:" -ForegroundColor Cyan
Write-Host "   1. ✅ Removed IP address unique constraint from database" -ForegroundColor Green
Write-Host "   2. ✅ Improved IP address detection with network interface scanning" -ForegroundColor Green
Write-Host "   3. ✅ Enhanced fallback to use machine name instead of random IPs" -ForegroundColor Green
Write-Host "   4. ✅ Authentication logic already prioritizes hardware fingerprint" -ForegroundColor Green
Write-Host ""
Write-Host "🎯 If registrations above succeeded, the authentication issue is fixed!" -ForegroundColor Yellow
Write-Host "📱 Try running your client again - it should work consistently now." -ForegroundColor Yellow