# VCDevTool IIS Configuration Script
# Run this script as Administrator on the IIS server

param(
    [string]$SiteName = "Default Web Site",
    [string]$ApplicationName = "VCDevTool",
    [string]$ApplicationPath = "/VCDevTool",
    [string]$PhysicalPath = "C:\inetpub\wwwroot\VCDevTool",
    [string]$AppPoolName = "VCDevToolAppPool",
    [string]$ServiceAccount = "company\VCDevToolService",
    [string]$ServiceAccountPassword = "",
    [switch]$CreateSite = $false,
    [switch]$WhatIf = $false
)

# Import required modules
Import-Module WebAdministration -ErrorAction Stop

Write-Host "🌐 VCDevTool IIS Configuration Script" -ForegroundColor Green
Write-Host "Site: $SiteName" -ForegroundColor Yellow
Write-Host "Application: $ApplicationName" -ForegroundColor Yellow
Write-Host "App Pool: $AppPoolName" -ForegroundColor Yellow
Write-Host "Service Account: $ServiceAccount" -ForegroundColor Yellow

if ($WhatIf) {
    Write-Host "⚠️  WHAT-IF MODE: No changes will be made" -ForegroundColor Yellow
}

# Function to execute IIS commands with WhatIf support
function Invoke-IISCommand {
    param(
        [scriptblock]$Command,
        [string]$Description
    )
    
    Write-Host "📝 $Description" -ForegroundColor Cyan
    
    if ($WhatIf) {
        Write-Host "   [WHAT-IF] Would execute: $($Command.ToString())" -ForegroundColor DarkGray
    } else {
        try {
            & $Command
            Write-Host "   ✅ Success" -ForegroundColor Green
        } catch {
            Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
            throw
        }
    }
}

# 1. Create Application Pool
Write-Host "`n🏊 Creating Application Pool..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Remove existing app pool if it exists
    if (Get-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue) {
        Remove-WebAppPool -Name $AppPoolName -Confirm:$false
    }
    
    # Create new app pool
    New-WebAppPool -Name $AppPoolName -Force
} -Description "Creating application pool: $AppPoolName"

# 2. Configure Application Pool Settings
Write-Host "`n⚙️ Configuring Application Pool Settings..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Set .NET Core (no managed runtime)
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
    
    # Set process model to use service account
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value SpecificUser
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.userName -Value $ServiceAccount
    
    # Load user profile for Windows Authentication
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.loadUserProfile -Value True
    
    # Set 32-bit applications
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name enable32BitAppOnWin64 -Value False
} -Description "Configuring basic app pool settings"

# Set service account password if provided
if (-not [string]::IsNullOrEmpty($ServiceAccountPassword)) {
    Invoke-IISCommand -Command {
        Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.password -Value $ServiceAccountPassword
    } -Description "Setting service account password"
} else {
    Write-Host "   ⚠️  Service account password not provided - you'll need to set this manually" -ForegroundColor Yellow
}

# 3. Configure Application Pool Advanced Settings
Write-Host "`n🔧 Configuring Advanced Application Pool Settings..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Disable periodic restart
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name recycling.periodicRestart.time -Value "00:00:00"
    
    # Disable idle timeout
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value "00:00:00"
    
    # Set maximum worker processes
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name processModel.maxProcesses -Value 1
    
    # Configure rapid fail protection
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name failure.rapidFailProtection -Value True
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name failure.rapidFailProtectionInterval -Value "00:05:00"
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name failure.rapidFailProtectionMaxCrashes -Value 5
    
    # Set startup mode
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name startMode -Value AlwaysRunning
} -Description "Configuring advanced app pool settings"

# 4. Create or Configure Website/Application
if ($CreateSite) {
    Write-Host "`n🌐 Creating Website..." -ForegroundColor Blue
    
    Invoke-IISCommand -Command {
        # Create physical directory
        if (-not (Test-Path $PhysicalPath)) {
            New-Item -Path $PhysicalPath -ItemType Directory -Force
        }
        
        # Remove existing site if it exists
        if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
            Remove-Website -Name $SiteName -Confirm:$false
        }
        
        # Create new website
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port 80
    } -Description "Creating website: $SiteName"
} else {
    Write-Host "`n📱 Creating Application..." -ForegroundColor Blue
    
    Invoke-IISCommand -Command {
        # Create physical directory
        if (-not (Test-Path $PhysicalPath)) {
            New-Item -Path $PhysicalPath -ItemType Directory -Force
        }
        
        # Remove existing application if it exists
        if (Get-WebApplication -Site $SiteName -Name $ApplicationName -ErrorAction SilentlyContinue) {
            Remove-WebApplication -Site $SiteName -Name $ApplicationName -Confirm:$false
        }
        
        # Create new application
        New-WebApplication -Site $SiteName -Name $ApplicationName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName
    } -Description "Creating application: $ApplicationName"
}

# 5. Configure Authentication
Write-Host "`n🔐 Configuring Authentication..." -ForegroundColor Blue

$LocationPath = if ($CreateSite) { $SiteName } else { "$SiteName$ApplicationPath" }

Invoke-IISCommand -Command {
    # Enable Windows Authentication
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name enabled -Value True -Location $LocationPath
    
    # Configure Windows Authentication providers
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication/providers" -Name ".[value='Negotiate']" -Value "Negotiate" -Location $LocationPath -ErrorAction SilentlyContinue
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication/providers" -Name ".[value='NTLM']" -Value "NTLM" -Location $LocationPath -ErrorAction SilentlyContinue
    
    # Enable extended protection
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name extendedProtection.tokenChecking -Value Allow -Location $LocationPath
} -Description "Enabling Windows Authentication"

Invoke-IISCommand -Command {
    # Disable Anonymous Authentication
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name enabled -Value False -Location $LocationPath
} -Description "Disabling Anonymous Authentication"

# 6. Configure HTTPS (if certificate available)
Write-Host "`n🔒 Configuring HTTPS..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Check if HTTPS binding exists
    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue
    
    if (-not $httpsBinding) {
        # Add HTTPS binding (you'll need to configure the certificate separately)
        New-WebBinding -Name $SiteName -Protocol https -Port 443
        Write-Host "   ⚠️  HTTPS binding created - you'll need to configure SSL certificate" -ForegroundColor Yellow
    } else {
        Write-Host "   ℹ️  HTTPS binding already exists" -ForegroundColor Gray
    }
} -Description "Configuring HTTPS binding"

# 7. Set File Permissions
Write-Host "`n📁 Setting File Permissions..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Grant app pool identity read/execute permissions
    $acl = Get-Acl $PhysicalPath
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($accessRule)
    
    # Grant specific permissions to service account
    $serviceAccessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($ServiceAccount, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($serviceAccessRule)
    
    Set-Acl -Path $PhysicalPath -AclObject $acl
} -Description "Setting file system permissions"

# 8. Configure Request Filtering
Write-Host "`n🛡️  Configuring Request Filtering..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Set maximum request size (100MB)
    Set-WebConfigurationProperty -Filter "/system.webServer/security/requestFiltering/requestLimits" -Name maxAllowedContentLength -Value 104857600 -Location $LocationPath
    
    # Configure file extension filtering
    $dangerousExtensions = @(".exe", ".bat", ".cmd", ".com", ".pif", ".scr", ".vbs")
    foreach ($ext in $dangerousExtensions) {
        Add-WebConfigurationProperty -Filter "/system.webServer/security/requestFiltering/fileExtensions" -Name "." -Value @{fileExtension=$ext; allowed=$false} -Location $LocationPath -ErrorAction SilentlyContinue
    }
} -Description "Configuring request filtering"

# 9. Test Configuration
Write-Host "`n🧪 Testing Configuration..." -ForegroundColor Blue

Invoke-IISCommand -Command {
    # Test app pool
    $appPool = Get-WebAppPool -Name $AppPoolName
    if ($appPool.State -ne "Started") {
        Start-WebAppPool -Name $AppPoolName
    }
    
    # Test site/application
    if ($CreateSite) {
        $site = Get-Website -Name $SiteName
        if ($site.State -ne "Started") {
            Start-Website -Name $SiteName
        }
    }
} -Description "Starting services"

# 10. Display Configuration Summary
Write-Host "`n📊 Configuration Summary:" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

Write-Host "Application Pool: $AppPoolName" -ForegroundColor White
Write-Host "Site/Application: $SiteName$ApplicationPath" -ForegroundColor White
Write-Host "Physical Path: $PhysicalPath" -ForegroundColor White
Write-Host "Service Account: $ServiceAccount" -ForegroundColor White

Write-Host "`nAuthentication Settings:" -ForegroundColor Yellow
Write-Host "  • Windows Authentication: Enabled" -ForegroundColor White
Write-Host "  • Anonymous Authentication: Disabled" -ForegroundColor White
Write-Host "  • Providers: Negotiate, NTLM" -ForegroundColor White

Write-Host "`nNext Steps:" -ForegroundColor Cyan
Write-Host "1. Deploy your VCDevTool application to: $PhysicalPath" -ForegroundColor White
Write-Host "2. Configure SSL certificate for HTTPS" -ForegroundColor White
Write-Host "3. Test Windows Authentication with domain users" -ForegroundColor White
Write-Host "4. Monitor application logs for any issues" -ForegroundColor White

# 11. Generate Test Commands
Write-Host "`n🧪 Testing Commands:" -ForegroundColor Blue
Write-Host "Use these commands to test the configuration:" -ForegroundColor Yellow

$TestCommands = @"
# Test application pool status
Get-WebAppPool -Name $AppPoolName | Select Name, State, ProcessModel

# Test website/application status
Get-Website -Name $SiteName | Select Name, State, ApplicationPool
Get-WebApplication -Site $SiteName | Select Path, ApplicationPool

# Test authentication configuration
Get-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name enabled -Location "$LocationPath"
Get-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name enabled -Location "$LocationPath"

# Test from browser (replace with your server name)
# https://your-server-name$ApplicationPath
"@

Write-Host $TestCommands -ForegroundColor DarkGray

Write-Host "`n🎉 IIS configuration completed!" -ForegroundColor Green

if (-not $WhatIf) {
    Write-Host "Your VCDevTool application is ready for deployment." -ForegroundColor Yellow
    Write-Host "Remember to:" -ForegroundColor Yellow
    Write-Host "  • Deploy the application files" -ForegroundColor White
    Write-Host "  • Configure the SSL certificate" -ForegroundColor White
    Write-Host "  • Update connection strings" -ForegroundColor White
    Write-Host "  • Test with domain users" -ForegroundColor White
} 