param (
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$ReleaseNotes = "New version available",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ".\VCDevTool.API\Updates"
)

# Ensure the output directory exists
if (-not (Test-Path $OutputPath)) {
    New-Item -Path $OutputPath -ItemType Directory | Out-Null
    Write-Host "Created directory: $OutputPath"
}

# Define source directory (the published client application)
$sourceDir = ".\VCDevTool.Client\bin\Release\net9.0-windows\publish"

# Check if source directory exists
if (-not (Test-Path $sourceDir)) {
    Write-Host "Source directory not found: $sourceDir" -ForegroundColor Red
    Write-Host "Please build the client application with 'dotnet publish' first." -ForegroundColor Yellow
    exit 1
}

# Define output zip file
$zipFile = Join-Path $OutputPath "latest.zip"

# Create the zip file
Write-Host "Creating update package..."
Compress-Archive -Path "$sourceDir\*" -DestinationPath $zipFile -Force
Write-Host "Created update package: $zipFile" -ForegroundColor Green

# Update the configuration file with the new version
$appsettingsPath = ".\VCDevTool.API\appsettings.json"
if (Test-Path $appsettingsPath) {
    $config = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    
    # Check if ApplicationUpdates property exists
    if (-not (Get-Member -InputObject $config -Name "ApplicationUpdates" -MemberType Properties)) {
        # Add ApplicationUpdates property if it doesn't exist
        $config | Add-Member -NotePropertyName "ApplicationUpdates" -NotePropertyValue @{
            "LatestVersion" = $Version
            "PackagePath" = "Updates/latest.zip"
            "ReleaseNotes" = $ReleaseNotes
        }
    } else {
        # Update existing properties
        $config.ApplicationUpdates.LatestVersion = $Version
        $config.ApplicationUpdates.ReleaseNotes = $ReleaseNotes
    }
    
    # Save the updated configuration
    $config | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath
    Write-Host "Updated appsettings.json with version $Version" -ForegroundColor Green
} else {
    Write-Host "Warning: appsettings.json not found at $appsettingsPath" -ForegroundColor Yellow
}

Write-Host "Update package creation complete." -ForegroundColor Green
Write-Host "Don't forget to restart the API server for the changes to take effect." 