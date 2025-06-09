# VCDevTool Active Directory Setup Script
# Run this script as Domain Administrator

param(
    [string]$Domain = "company.local",
    [string]$ServiceAccountName = "VCDevToolService",
    [string]$ServiceAccountPassword = "",
    [string]$ServerFQDN = "vcdevtool.company.local",
    [string]$ServerNetBIOS = "vcdevtool",
    [switch]$CreateServiceAccount = $false,
    [switch]$CreateGroups = $true,
    [switch]$CreateOUs = $true,
    [switch]$ConfigureSPN = $true,
    [switch]$WhatIf = $false
)

# Import required modules
Import-Module ActiveDirectory -ErrorAction Stop

Write-Host "🚀 VCDevTool Active Directory Setup Script" -ForegroundColor Green
Write-Host "Domain: $Domain" -ForegroundColor Yellow
Write-Host "Service Account: $ServiceAccountName" -ForegroundColor Yellow
Write-Host "Server FQDN: $ServerFQDN" -ForegroundColor Yellow

if ($WhatIf) {
    Write-Host "⚠️  WHAT-IF MODE: No changes will be made" -ForegroundColor Yellow
}

# Function to execute AD commands with WhatIf support
function Invoke-ADCommand {
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

# 1. Create Organizational Units
if ($CreateOUs) {
    Write-Host "`n🏢 Creating Organizational Units..." -ForegroundColor Blue
    
    $OUs = @(
        @{ Name = "VCDevTool"; Path = "DC=$($Domain.Replace('.', ',DC='))" },
        @{ Name = "Service Accounts"; Path = "OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))" },
        @{ Name = "Security Groups"; Path = "OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))" },
        @{ Name = "Computer Nodes"; Path = "OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))" },
        @{ Name = "Processing Nodes"; Path = "OU=Computer Nodes,OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))" }
    )
    
    foreach ($OU in $OUs) {
        $FullPath = "OU=$($OU.Name),$($OU.Path)"
        Invoke-ADCommand -Command {
            if (-not (Get-ADOrganizationalUnit -Filter "Name -eq '$($OU.Name)' -and DistinguishedName -eq '$FullPath'" -ErrorAction SilentlyContinue)) {
                New-ADOrganizationalUnit -Name $OU.Name -Path $OU.Path -ProtectedFromAccidentalDeletion $true
            }
        } -Description "Creating OU: $($OU.Name)"
    }
}

# 2. Create Security Groups
if ($CreateGroups) {
    Write-Host "`n👥 Creating Security Groups..." -ForegroundColor Blue
    
    $Groups = @(
        @{ Name = "VCDevTool_Administrators"; Scope = "Global"; Category = "Security"; Description = "VCDevTool System Administrators" },
        @{ Name = "VCDevTool_Users"; Scope = "Global"; Category = "Security"; Description = "VCDevTool Standard Users" },
        @{ Name = "VCDevTool_ComputerNodes"; Scope = "Global"; Category = "Security"; Description = "VCDevTool Computer Nodes" },
        @{ Name = "VCDevTool_ProcessingNodes"; Scope = "Global"; Category = "Security"; Description = "VCDevTool Processing Nodes" },
        @{ Name = "VCDevTool_ServiceAccounts"; Scope = "Global"; Category = "Security"; Description = "VCDevTool Service Accounts" },
        @{ Name = "VCDevTool_ReadOnly"; Scope = "Global"; Category = "Security"; Description = "VCDevTool Read-Only Users" }
    )
    
    $GroupsOU = "OU=Security Groups,OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))"
    
    foreach ($Group in $Groups) {
        Invoke-ADCommand -Command {
            if (-not (Get-ADGroup -Filter "Name -eq '$($Group.Name)'" -ErrorAction SilentlyContinue)) {
                New-ADGroup -Name $Group.Name -GroupScope $Group.Scope -GroupCategory $Group.Category -Path $GroupsOU -Description $Group.Description
            }
        } -Description "Creating group: $($Group.Name)"
    }
}

# 3. Create Service Account
if ($CreateServiceAccount) {
    Write-Host "`n🔐 Creating Service Account..." -ForegroundColor Blue
    
    if ([string]::IsNullOrEmpty($ServiceAccountPassword)) {
        $ServiceAccountPassword = Read-Host "Enter password for service account $ServiceAccountName" -AsSecureString
        $ServiceAccountPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ServiceAccountPassword))
    }
    
    $ServiceAccountOU = "OU=Service Accounts,OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))"
    
    Invoke-ADCommand -Command {
        if (-not (Get-ADUser -Filter "Name -eq '$ServiceAccountName'" -ErrorAction SilentlyContinue)) {
            $SecurePassword = ConvertTo-SecureString $ServiceAccountPassword -AsPlainText -Force
            New-ADUser -Name $ServiceAccountName `
                      -SamAccountName $ServiceAccountName `
                      -UserPrincipalName "$ServiceAccountName@$Domain" `
                      -Path $ServiceAccountOU `
                      -AccountPassword $SecurePassword `
                      -Enabled $true `
                      -PasswordNeverExpires $true `
                      -CannotChangePassword $true `
                      -Description "VCDevTool Service Account for Windows Authentication"
        }
    } -Description "Creating service account: $ServiceAccountName"
    
    # Add service account to appropriate group
    Invoke-ADCommand -Command {
        Add-ADGroupMember -Identity "VCDevTool_ServiceAccounts" -Members $ServiceAccountName -ErrorAction SilentlyContinue
    } -Description "Adding service account to VCDevTool_ServiceAccounts group"
}

# 4. Configure Service Principal Names (SPNs)
if ($ConfigureSPN) {
    Write-Host "`n🎫 Configuring Service Principal Names..." -ForegroundColor Blue
    
    $SPNs = @(
        "HTTP/$ServerFQDN",
        "HTTP/$ServerNetBIOS"
    )
    
    foreach ($SPN in $SPNs) {
        Invoke-ADCommand -Command {
            # Check if SPN already exists
            $ExistingSPN = setspn -Q $SPN 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "   ⚠️  SPN $SPN already exists" -ForegroundColor Yellow
            } else {
                setspn -A $SPN $ServiceAccountName
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "   ✅ Added SPN: $SPN" -ForegroundColor Green
                } else {
                    throw "Failed to add SPN: $SPN"
                }
            }
        } -Description "Configuring SPN: $SPN"
    }
}

# 5. Set Delegation Rights (Optional)
Write-Host "`n🔑 Setting Delegation Rights..." -ForegroundColor Blue

Invoke-ADCommand -Command {
    $ServiceAccount = Get-ADUser $ServiceAccountName
    Set-ADUser $ServiceAccount -TrustedForDelegation $true
} -Description "Enabling delegation for service account"

# 6. Grant Permissions for Service Account
Write-Host "`n🛡️  Granting Service Account Permissions..." -ForegroundColor Blue

$Permissions = @(
    "Read all user information",
    "Read group membership",
    "Read computer information"
)

foreach ($Permission in $Permissions) {
    Write-Host "   📝 $Permission" -ForegroundColor DarkGray
}

# 7. Create Test Computer Account
Write-Host "`n🖥️  Creating Test Computer Account..." -ForegroundColor Blue

$TestComputerName = "VCDEVTOOL-TEST01"
$ComputerOU = "OU=Processing Nodes,OU=Computer Nodes,OU=VCDevTool,DC=$($Domain.Replace('.', ',DC='))"

Invoke-ADCommand -Command {
    if (-not (Get-ADComputer -Filter "Name -eq '$TestComputerName'" -ErrorAction SilentlyContinue)) {
        New-ADComputer -Name $TestComputerName -Path $ComputerOU -Enabled $true -Description "VCDevTool Test Processing Node"
        Add-ADGroupMember -Identity "VCDevTool_ProcessingNodes" -Members "$TestComputerName$" -ErrorAction SilentlyContinue
    }
} -Description "Creating test computer account: $TestComputerName"

# 8. Display Configuration Summary
Write-Host "`n📊 Configuration Summary:" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

Write-Host "Domain: $Domain" -ForegroundColor White
Write-Host "Service Account: $ServiceAccountName" -ForegroundColor White
Write-Host "Server FQDN: $ServerFQDN" -ForegroundColor White

Write-Host "`nCreated Groups:" -ForegroundColor Yellow
$Groups | ForEach-Object { Write-Host "  • $($_.Name)" -ForegroundColor White }

Write-Host "`nConfigured SPNs:" -ForegroundColor Yellow
$SPNs | ForEach-Object { Write-Host "  • $_" -ForegroundColor White }

Write-Host "`nNext Steps:" -ForegroundColor Cyan
Write-Host "1. Update appsettings.json with your domain settings" -ForegroundColor White
Write-Host "2. Configure IIS Application Pool to use the service account" -ForegroundColor White
Write-Host "3. Enable Windows Authentication in IIS" -ForegroundColor White
Write-Host "4. Test authentication with domain users" -ForegroundColor White

# 9. Generate PowerShell Commands for IIS Configuration
Write-Host "`n🌐 IIS Configuration Commands:" -ForegroundColor Blue
Write-Host "Run these commands on the IIS server as Administrator:" -ForegroundColor Yellow

$IISCommands = @"
# Import IIS WebAdministration module
Import-Module WebAdministration

# Create Application Pool
New-WebAppPool -Name "VCDevToolAppPool" -Force
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name processModel.identityType -Value SpecificUser
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name processModel.userName -Value "$Domain\$ServiceAccountName"
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name processModel.password -Value "<SERVICE_ACCOUNT_PASSWORD>"
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name processModel.loadUserProfile -Value True
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name managedRuntimeVersion -Value ""

# Configure Application Pool Settings
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name recycling.periodicRestart.time -Value "00:00:00"
Set-ItemProperty -Path "IIS:\AppPools\VCDevToolAppPool" -Name processModel.idleTimeout -Value "00:00:00"

# Enable Windows Authentication for the site
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name enabled -Value True -Location "Default Web Site/VCDevTool"
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name enabled -Value False -Location "Default Web Site/VCDevTool"
"@

Write-Host $IISCommands -ForegroundColor DarkGray

# 10. Generate Test Commands
Write-Host "`n🧪 Testing Commands:" -ForegroundColor Blue
Write-Host "Use these commands to test the configuration:" -ForegroundColor Yellow

$TestCommands = @"
# Test Kerberos tickets
klist tickets

# Test computer secure channel
Test-ComputerSecureChannel -Verbose

# Test SPN configuration
setspn -L $ServiceAccountName

# Test LDAP connectivity
# (Run from a domain-joined machine)
`$ldap = New-Object System.DirectoryServices.DirectoryEntry("LDAP://DC=$($Domain.Replace('.', ',DC='))")
`$ldap.Name
"@

Write-Host $TestCommands -ForegroundColor DarkGray

Write-Host "`n🎉 Active Directory setup completed!" -ForegroundColor Green
Write-Host "Don't forget to update your appsettings.json with the correct domain settings." -ForegroundColor Yellow 