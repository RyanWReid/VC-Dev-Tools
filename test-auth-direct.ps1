# test-auth-direct.ps1 - Direct Authentication Test

Write-Host "=== Direct Authentication Test ===" -ForegroundColor Cyan

$baseUrl = "http://localhost:5289"
$headers = @{ "Content-Type" = "application/json" }

# Test the auth registration endpoint directly
Write-Host "Testing direct auth registration..." -ForegroundColor Yellow

$nodeData = @{
    "Id" = "direct-test-$(Get-Random)"
    "Name" = "Direct Test Node"
    "IpAddress" = "127.0.0.$(Get-Random -Minimum 1 -Maximum 255)"
    "HardwareFingerprint" = "DIRECT-TEST-$(Get-Random)"
}

$json = $nodeData | ConvertTo-Json
Write-Host "Node data: $json" -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/auth/register" -Method POST -Body $json -Headers $headers -UseBasicParsing
    
    Write-Host "Response Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response Content: $($response.Content)" -ForegroundColor Green
    
    if ($response.StatusCode -eq 200) {
        $result = $response.Content | ConvertFrom-Json
        Write-Host "✓ Success! Token: $($result.token.Substring(0, 20))..." -ForegroundColor Green
        Write-Host "✓ Node ID: $($result.nodeId)" -ForegroundColor Green
    }
} catch {
    Write-Host "✗ Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $errorContent = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($errorContent)
        $errorBody = $reader.ReadToEnd()
        Write-Host "Error body: $errorBody" -ForegroundColor Red
    }
} 