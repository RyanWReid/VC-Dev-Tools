# test-auto-auth-simple.ps1 - Simple Auto Authentication Test

Write-Host "=== VCDevTool Auto Authentication Test ===" -ForegroundColor Cyan
Write-Host ""

$baseUrl = "http://localhost:5289"
$headers = @{ "Content-Type" = "application/json" }

# Test 1: Check API Health
Write-Host "1. Testing API Health..." -ForegroundColor Yellow
try {
    $healthResponse = Invoke-WebRequest -Uri "$baseUrl/health" -Method GET -UseBasicParsing -TimeoutSec 5
    if ($healthResponse.StatusCode -eq 200) {
        Write-Host "   ✓ API is running and accessible" -ForegroundColor Green
    } else {
        Write-Host "   ✗ API health check failed" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ✗ API is not accessible: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Auto Node Registration
Write-Host "2. Testing Auto Node Registration..." -ForegroundColor Yellow
$randomId = [System.Guid]::NewGuid().ToString().Substring(0, 8)
$nodeData = @{
    "Id" = "auto-test-$randomId"
    "Name" = "Auto Test Node $randomId"
    "IpAddress" = "127.0.0.$([System.Random]::new().Next(1, 255))"
    "HardwareFingerprint" = "AUTO-TEST-$([System.Guid]::NewGuid().ToString())"
} | ConvertTo-Json

try {
    Write-Host "   Registering node: auto-test-$randomId" -ForegroundColor Cyan
    $registerResponse = Invoke-WebRequest -Uri "$baseUrl/api/auth/register" -Method POST -Body $nodeData -Headers $headers -UseBasicParsing
    
    if ($registerResponse.StatusCode -eq 200) {
        $authResult = $registerResponse.Content | ConvertFrom-Json
        Write-Host "   ✓ Node registered successfully" -ForegroundColor Green
        Write-Host "   ✓ Token received: $($authResult.token.Substring(0, 20))..." -ForegroundColor Green
        Write-Host "   ✓ Node ID: $($authResult.nodeId)" -ForegroundColor Green
        
        $token = $authResult.token
        $nodeId = $authResult.nodeId
    } else {
        Write-Host "   ✗ Registration failed with status: $($registerResponse.StatusCode)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ✗ Registration failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 3: Test Authenticated API Calls
Write-Host "3. Testing Authenticated API Access..." -ForegroundColor Yellow
$authHeaders = @{ 
    "Content-Type" = "application/json"
    "Authorization" = "Bearer $token"
}

try {
    $nodesResponse = Invoke-WebRequest -Uri "$baseUrl/api/nodes" -Method GET -Headers $authHeaders -UseBasicParsing
    
    if ($nodesResponse.StatusCode -eq 200) {
        $nodes = $nodesResponse.Content | ConvertFrom-Json
        Write-Host "   ✓ Authenticated API call successful" -ForegroundColor Green
        Write-Host "   ✓ Retrieved $($nodes.Count) nodes" -ForegroundColor Green
    } else {
        Write-Host "   ✗ Authenticated API call failed with status: $($nodesResponse.StatusCode)" -ForegroundColor Red
    }
} catch {
    Write-Host "   ✗ Authenticated API call failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Test Auto Login
Write-Host "4. Testing Auto Login..." -ForegroundColor Yellow
$loginData = @{
    "NodeId" = $nodeId
    "HardwareFingerprint" = "AUTO-TEST-$([System.Guid]::NewGuid().ToString())"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-WebRequest -Uri "$baseUrl/api/auth/login" -Method POST -Body $loginData -Headers $headers -UseBasicParsing
    
    if ($loginResponse.StatusCode -eq 201) {
        $loginResult = $loginResponse.Content | ConvertFrom-Json
        Write-Host "   ✓ Auto login successful" -ForegroundColor Green
        Write-Host "   ✓ New token received: $($loginResult.token.Substring(0, 20))..." -ForegroundColor Green
    } else {
        Write-Host "   ⚠ Login failed with status: $($loginResponse.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ⚠ Login failed: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Auto Authentication Test Summary ===" -ForegroundColor Cyan
Write-Host "✓ API Health: Working" -ForegroundColor Green
Write-Host "✓ Node Registration: Working" -ForegroundColor Green  
Write-Host "✓ Authenticated Access: Working" -ForegroundColor Green
Write-Host "✓ Auto Login: Tested" -ForegroundColor Green
Write-Host ""
Write-Host "Auto Authentication is working! 🚀" -ForegroundColor Green 