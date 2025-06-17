# Test Login Fix
Write-Host "Testing login authentication fix..." -ForegroundColor Green

# Step 1: Register a node
$nodeData = @{
    Id = "login-test-node"
    Name = "Login Test Node"
    IpAddress = "127.0.0.155"
    HardwareFingerprint = "LOGIN-TEST-HW-12345"
} | ConvertTo-Json

Write-Host "1. Registering node first time..." -ForegroundColor Cyan
try {
    $regResult = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
    Write-Host "   SUCCESS: First registration worked" -ForegroundColor Green
    Write-Host "   Token: $($regResult.Token[0..20] -join '')..." -ForegroundColor Gray
    Write-Host "   API Key: $($regResult.ApiKey)" -ForegroundColor Gray
}
catch {
    Write-Host "   FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

# Step 2: Try to register again (should get 409)
Write-Host "`n2. Trying to register same node again (expect 409)..." -ForegroundColor Cyan
try {
    $regResult2 = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $nodeData -ContentType "application/json"
    Write-Host "   UNEXPECTED: Second registration also succeeded" -ForegroundColor Yellow
}
catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "   EXPECTED: Got 409 Conflict for duplicate node" -ForegroundColor Green
    } else {
        Write-Host "   UNEXPECTED ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Step 3: Test login with empty API key (the fix)
Write-Host "`n3. Testing login with empty API key..." -ForegroundColor Cyan
$loginData = @{
    NodeId = "login-test-node"
    ApiKey = ""  # Empty API key as per fix
    HardwareFingerprint = "LOGIN-TEST-HW-12345"
} | ConvertTo-Json

try {
    $loginResult = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/login" -Method POST -Body $loginData -ContentType "application/json"
    Write-Host "   SUCCESS: Login with empty API key worked!" -ForegroundColor Green
    Write-Host "   Token: $($loginResult.Token[0..20] -join '')..." -ForegroundColor Gray
    Write-Host "   API Key: $($loginResult.ApiKey)" -ForegroundColor Gray
}
catch {
    Write-Host "   FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "   Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Login Fix Test Complete ===" -ForegroundColor Green