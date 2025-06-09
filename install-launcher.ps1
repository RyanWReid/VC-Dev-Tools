param (
    [Parameter(Mandatory=$false)]
    [string]$ServerUrl = "http://localhost:5289",
    
    [Parameter(Mandatory=$false)]
    [string]$InstallDir = "$env:ProgramFiles\VCDevTool"
)

Write-Host "VCDevTool Launcher Installer" -ForegroundColor Cyan
Write-Host "==========================" -ForegroundColor Cyan
Write-Host "Server URL: $ServerUrl"
Write-Host "Install Directory: $InstallDir"

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "This script requires administrator privileges. Please run as administrator." -ForegroundColor Red
    exit 1
}

# Create installation directory if it doesn't exist
if (-not (Test-Path $InstallDir)) {
    New-Item -Path $InstallDir -ItemType Directory | Out-Null
    Write-Host "Created installation directory: $InstallDir" -ForegroundColor Green
} else {
    Write-Host "Installation directory already exists." -ForegroundColor Yellow
}

# Download the launcher
$launcherUrl = "$ServerUrl/api/updates/launcher"
$launcherPath = Join-Path $InstallDir "VCDevTool.Launcher.exe"

try {
    Write-Host "Downloading launcher from $launcherUrl..."
    Invoke-WebRequest -Uri $launcherUrl -OutFile $launcherPath
    Write-Host "Launcher downloaded successfully." -ForegroundColor Green
} catch {
    Write-Host "Failed to download launcher: $_" -ForegroundColor Red
    exit 1
}

# Configure the launcher
$configPath = Join-Path $InstallDir "launcher.config"
@"
{
  "UpdateServerUrl": "$ServerUrl/api/updates"
}
"@ | Out-File -FilePath $configPath -Encoding utf8

Write-Host "Launcher configured with server URL: $ServerUrl" -ForegroundColor Green

# Create desktop shortcut
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "VCDevTool.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $launcherPath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "VCDevTool Application"
$Shortcut.Save()

Write-Host "Desktop shortcut created: $shortcutPath" -ForegroundColor Green

# Create start menu shortcut
$startMenuPath = [Environment]::GetFolderPath("Programs")
$startMenuFolder = Join-Path $startMenuPath "VCDevTool"
if (-not (Test-Path $startMenuFolder)) {
    New-Item -Path $startMenuFolder -ItemType Directory | Out-Null
}
$startMenuShortcutPath = Join-Path $startMenuFolder "VCDevTool.lnk"

$Shortcut = $WshShell.CreateShortcut($startMenuShortcutPath)
$Shortcut.TargetPath = $launcherPath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "VCDevTool Application"
$Shortcut.Save()

Write-Host "Start menu shortcut created: $startMenuShortcutPath" -ForegroundColor Green

# Launch the application
$launchNow = Read-Host "Do you want to launch VCDevTool now? (Y/N)"
if ($launchNow -eq "Y" -or $launchNow -eq "y") {
    Write-Host "Launching VCDevTool..."
    Start-Process -FilePath $launcherPath -WorkingDirectory $InstallDir
}

Write-Host "VCDevTool has been successfully installed!" -ForegroundColor Green
Write-Host "You can run it from the desktop shortcut or start menu." -ForegroundColor Green 