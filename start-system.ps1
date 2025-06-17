# VCDevTool System Startup Script
# This script starts the API server and client application in the correct order

Write-Host "=== VCDevTool System Startup ===" -ForegroundColor Cyan
Write-Host "Starting VCDevTool API and Client applications..." -ForegroundColor Yellow

# Function to check if a port is available
function Test-Port {
    param([int]$Port)
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
        $listener.Start()
        $listener.Stop()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) { $listener.Stop() }
    }
}

# Function to wait for API to be ready
function Wait-ForApi {
    param([string]$ApiUrl = "http://localhost:5289")
    
    Write-Host "Waiting for API to be ready..." -ForegroundColor Yellow
    $maxAttempts = 30
    $attempt = 0
    
    do {
        $attempt++
        try {
            # Try to connect to the API
            $response = Invoke-WebRequest -Uri "$ApiUrl/health" -Method GET -TimeoutSec 2 -ErrorAction Stop
            Write-Host "✓ API is ready!" -ForegroundColor Green
            return $true
        }
        catch {
            if ($attempt -eq 1) {
                Write-Host "  Waiting for API server to start..." -ForegroundColor Gray
            }
            Start-Sleep -Seconds 2
        }
    } while ($attempt -lt $maxAttempts)
    
    Write-Host "✗ API failed to start within expected time" -ForegroundColor Red
    return $false
}

# Step 1: Check if port 5289 is available
Write-Host "`n1. Checking system requirements..." -ForegroundColor Yellow

# Check if port is already in use
$portInUse = -not (Test-Port -Port 5289)
if ($portInUse) {
    Write-Host "  ✓ Port 5289 is already in use (API may already be running)" -ForegroundColor Green
} else {
    Write-Host "  ✓ Port 5289 is available" -ForegroundColor Green
}

# Step 2: Build projects
Write-Host "`n2. Building projects..." -ForegroundColor Yellow
try {
    Write-Host "  Building API..." -ForegroundColor Gray
    $apiResult = dotnet build VCDevTool.API --verbosity quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ API build successful" -ForegroundColor Green
    } else {
        throw "API build failed"
    }
    
    Write-Host "  Building Client..." -ForegroundColor Gray
    $clientResult = dotnet build VCDevTool.Client --verbosity quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Client build successful" -ForegroundColor Green
    } else {
        throw "Client build failed"
    }
}
catch {
    Write-Host "  ✗ Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 3: Start API server (if not already running)
Write-Host "`n3. Starting API server..." -ForegroundColor Yellow

if (-not $portInUse) {
    try {
        # Start API in a new window
        $apiProcess = Start-Process -FilePath "powershell.exe" -ArgumentList @(
            "-NoExit",
            "-Command",
            "cd '$PWD'; Write-Host 'Starting VCDevTool API Server...' -ForegroundColor Green; dotnet run --project VCDevTool.API"
        ) -PassThru -WindowStyle Normal
        
        Write-Host "  ✓ API server starting (PID: $($apiProcess.Id))..." -ForegroundColor Green
        
        # Wait for API to be ready
        $apiReady = Wait-ForApi
        if (-not $apiReady) {
            Write-Host "  ✗ API server failed to start properly" -ForegroundColor Red
            exit 1
        }
    }
    catch {
        Write-Host "  ✗ Failed to start API server: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  ✓ API server appears to be already running" -ForegroundColor Green
    
    # Still wait to make sure it's responding
    $apiReady = Wait-ForApi
    if (-not $apiReady) {
        Write-Host "  ! API server is not responding properly. You may need to restart it." -ForegroundColor Yellow
    }
}

# Step 4: Test API connection
Write-Host "`n4. Testing API connection..." -ForegroundColor Yellow
try {
    # Test node registration
    $headers = @{ "Content-Type" = "application/json" }
    $testNodeData = @{
        "Id" = "startup-test-node-$(Get-Random -Maximum 9999)"
        "Name" = "Startup Test Node"
        "IpAddress" = "127.0.0.1"
        "HardwareFingerprint" = "STARTUP-TEST-$(New-Guid)"
    } | ConvertTo-Json
    
    $response = Invoke-WebRequest -Uri "http://localhost:5289/api/auth/register" -Method POST -Body $testNodeData -Headers $headers -UseBasicParsing
    
    if ($response.StatusCode -eq 201) {
        Write-Host "  ✓ API registration test successful" -ForegroundColor Green
    } else {
        Write-Host "  ! API responded but registration test failed (Status: $($response.StatusCode))" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "  ✗ API connection test failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    The client may still work, but authentication might fail." -ForegroundColor Yellow
}

# Step 5: Start client application
Write-Host "`n5. Starting client application..." -ForegroundColor Yellow

try {
    # Check if we should use the built executable or dotnet run
    $clientExePaths = @(
        ".\VCDevTool.Client\bin\Debug\net9.0-windows\VCDevTool.Client.exe",
        ".\VCDevTool.Client\bin\Debug\net8.0-windows\VCDevTool.Client.exe",
        ".\VCDevTool.Client\bin\Debug\net7.0-windows\VCDevTool.Client.exe"
    )
    
    $clientExe = $clientExePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    
    if ($clientExe) {
        Write-Host "  ✓ Starting client from executable: $clientExe" -ForegroundColor Green
        Start-Process -FilePath $clientExe -WorkingDirectory $PWD
    } else {
        Write-Host "  ✓ Starting client with dotnet run..." -ForegroundColor Green
        Start-Process -FilePath "powershell.exe" -ArgumentList @(
            "-NoExit",
            "-Command",
            "cd '$PWD'; Write-Host 'Starting VCDevTool Client...' -ForegroundColor Green; dotnet run --project VCDevTool.Client"
        ) -WindowStyle Normal
    }
}
catch {
    Write-Host "  ✗ Failed to start client: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    You can try starting it manually with: dotnet run --project VCDevTool.Client" -ForegroundColor Yellow
}

# Final status
Write-Host "`n=== Startup Complete ===" -ForegroundColor Cyan
Write-Host "✓ API Server: http://localhost:5289" -ForegroundColor Green
Write-Host "✓ Swagger UI: http://localhost:5289/swagger" -ForegroundColor Green
Write-Host "✓ Client Application: Should be starting now" -ForegroundColor Green

Write-Host "`nTroubleshooting:" -ForegroundColor Yellow
Write-Host "- If client connection fails, check the Debug Output panel in the client" -ForegroundColor Gray
Write-Host "- Run 'netstat -an | findstr :5289' to verify API is listening" -ForegroundColor Gray
Write-Host "- Run '.\test-system.ps1' for comprehensive testing" -ForegroundColor Gray
Write-Host "- See AUTHENTICATION_TROUBLESHOOTING.md for detailed help" -ForegroundColor Gray

Write-Host "`nPress any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") 