param (
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ".\VCDevTool.API\Updates"
)

Write-Host "Building VCDevTool Launcher..." -ForegroundColor Cyan

# Ensure the output directory exists
if (-not (Test-Path $OutputPath)) {
    New-Item -Path $OutputPath -ItemType Directory | Out-Null
    Write-Host "Created directory: $OutputPath"
}

# Build the launcher
Write-Host "Building launcher in $Configuration configuration..."
dotnet publish .\VCDevTool.Launcher\VCDevTool.Launcher.csproj -c $Configuration -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Copy the launcher to the API's update directory
$launcherSource = ".\VCDevTool.Launcher\bin\$Configuration\net9.0-windows\win-x64\publish\VCDevTool.Launcher.exe"
$launcherDestination = Join-Path $OutputPath "VCDevTool.Launcher.exe"

if (-not (Test-Path $launcherSource)) {
    Write-Host "Launcher executable not found at: $launcherSource" -ForegroundColor Red
    exit 1
}

Copy-Item -Path $launcherSource -Destination $launcherDestination -Force
Write-Host "Launcher copied to: $launcherDestination" -ForegroundColor Green

Write-Host "Launcher build and deployment completed successfully." -ForegroundColor Green
Write-Host "The API server can now serve the launcher to clients."