# Debug Client Authentication Issues
Write-Host "=== Debug Client Authentication ===" -ForegroundColor Cyan

# Test what the client would actually send
Write-Host "`n1. Testing what a real client registration looks like..." -ForegroundColor Green

# Simulate the exact request the client would make
$clientNodeData = @{
    Id = "$env:COMPUTERNAME-$(Get-Date -Format 'yyyyMMdd')"
    Name = $env:COMPUTERNAME
    IpAddress = "127.0.0.150" # Valid IP that should pass validation
    HardwareFingerprint = "DEBUG-HW-$env:COMPUTERNAME-$(Get-Random -Maximum 99999)"
    WindowsIdentity = @{
        UserName = "$env:USERDOMAIN\$env:USERNAME"
        Domain = $env:USERDOMAIN
        IsAuthenticated = $true
    }
} | ConvertTo-Json -Depth 3

Write-Host "Sending node data:" -ForegroundColor Cyan
Write-Host $clientNodeData -ForegroundColor Gray

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $clientNodeData -ContentType "application/json" -Verbose
    Write-Host "`n✓ SUCCESS: Client-style registration worked!" -ForegroundColor Green
    Write-Host "Token: $($response.Token[0..30] -join '')..." -ForegroundColor Cyan
    
    # Test if we can use this token
    $authHeaders = @{ 
        "Authorization" = "Bearer $($response.Token)"
        "Content-Type" = "application/json"
    }
    
    $nodes = Invoke-RestMethod -Uri "http://localhost:5289/api/nodes" -Headers $authHeaders -Verbose
    Write-Host "✓ Authenticated nodes call worked! Found $($nodes.Count) nodes" -ForegroundColor Green
    
}
catch {
    Write-Host "`n✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode
        Write-Host "Status Code: $statusCode" -ForegroundColor Yellow
        
        try {
            $responseStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($responseStream)
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body: $responseBody" -ForegroundColor Yellow
        }
        catch {
            Write-Host "Could not read response body" -ForegroundColor Yellow
        }
    }
    
    Write-Host "`nFull exception details:" -ForegroundColor Yellow
    Write-Host $_.Exception.ToString() -ForegroundColor Gray
}

Write-Host "`n2. Testing API health..." -ForegroundColor Green
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5289/health" -TimeoutSec 5
    Write-Host "✓ API health check passed" -ForegroundColor Green
}
catch {
    Write-Host "✗ API health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Debug Complete ===" -ForegroundColor Cyan