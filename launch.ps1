# VCDevTool Build & Launch Script
# This script stops running processes, builds the client project, and launches the app with elevated privileges if successful

# 1. Stop any running instances of the application
Write-Host "Stopping any running VCDevTool processes..." -ForegroundColor Yellow
Get-Process | Where-Object { $_.ProcessName -like "*VCDevTool.client*" } | ForEach-Object { 
    Write-Host "Stopping $($_.ProcessName)..." 
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue 
}

# 2. Build only the client project
Write-Host "Building VCDevTool.Client..." -ForegroundColor Yellow
dotnet build VCDevTool.Client/VCDevTool.Client.csproj

# 3. Check if build was successful
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful! Launching application with elevated privileges..." -ForegroundColor Green
    
    # Define possible paths for the executable
    $possiblePaths = @(
        "$(Get-Location)\VCDevTool.Client\bin\Debug\net9.0-windows\VCDevTool.Client.exe",
        "$(Get-Location)\VCDevTool.Client\bin\Debug\net8.0-windows\VCDevTool.Client.exe",
        "$(Get-Location)\VCDevTool.Client\bin\Debug\net7.0-windows\VCDevTool.Client.exe",
        "$(Get-Location)\VCDevTool.Client\bin\Debug\net6.0-windows\VCDevTool.Client.exe"
    )
    
    # Find the first path that exists
    $clientExePath = $possiblePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    
    if ($clientExePath) {
        Write-Host "Found executable at: $clientExePath" -ForegroundColor Green
        # Launch the client application without elevated privileges
        Start-Process -FilePath $clientExePath
    } else {
        Write-Host "Could not find the client executable. Please check the build output path." -ForegroundColor Red
        # Try to find any executable in the bin directory
        $foundExes = Get-ChildItem -Path "$(Get-Location)\VCDevTool.Client\bin" -Recurse -Filter "*.exe" | Select-Object -ExpandProperty FullName
        if ($foundExes) {
            Write-Host "Found these executables in the bin directory:" -ForegroundColor Yellow
            $foundExes | ForEach-Object { Write-Host "  $_" }
        }
    }
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
