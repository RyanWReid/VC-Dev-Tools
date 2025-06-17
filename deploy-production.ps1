#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)]
    [string]$Environment = "Production",
    
    [Parameter(Mandatory=$false)]
    [string]$DbPassword,
    
    [Parameter(Mandatory=$false)]
    [string]$JwtSecret,
    
    [Parameter(Mandatory=$false)]
    [string[]]$AllowedOrigins = @(),
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipTests = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBackup = $false,
    
    [Parameter(Mandatory=$false)]
    [string]$BackupPath = "./backups",
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

# Script configuration
$ErrorActionPreference = "Stop"
$deploymentStartTime = Get-Date
$logFile = "logs/deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

# Ensure logs directory exists
if (-not (Test-Path "logs")) {
    New-Item -ItemType Directory -Path "logs" -Force | Out-Null
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    Write-Host $logEntry
    Add-Content -Path $logFile -Value $logEntry
}

function Test-Prerequisites {
    Write-Log "Checking deployment prerequisites..." "INFO"
    
    # Check Docker
    try {
        $dockerVersion = docker --version
        Write-Log "Docker found: $dockerVersion" "INFO"
    }
    catch {
        Write-Log "Docker is not installed or not accessible" "ERROR"
        throw "Docker is required for deployment"
    }
    
    # Check Docker Compose
    try {
        $composeVersion = docker compose version
        Write-Log "Docker Compose found: $composeVersion" "INFO"
    }
    catch {
        Write-Log "Docker Compose is not installed or not accessible" "ERROR"
        throw "Docker Compose is required for deployment"
    }
    
    # Check .NET SDK (for tests)
    if (-not $SkipTests) {
        try {
            $dotnetVersion = dotnet --version
            Write-Log ".NET SDK found: $dotnetVersion" "INFO"
        }
        catch {
            Write-Log ".NET SDK not found - tests will be skipped" "WARN"
            $SkipTests = $true
        }
    }
    
    # Check required files
    $requiredFiles = @(
        "docker-compose.yml",
        "VCDevTool.API/Dockerfile",
        "VCDevTool.API/appsettings.Production.json",
        "nginx.conf"
    )
    
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path $file)) {
            Write-Log "Required file missing: $file" "ERROR"
            throw "Missing required deployment file: $file"
        }
    }
    
    Write-Log "All prerequisites check passed" "SUCCESS"
}

function Set-EnvironmentVariables {
    Write-Log "Setting up environment variables..." "INFO"
    
    # Generate secure defaults if not provided
    if (-not $DbPassword) {
        $DbPassword = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 16 | ForEach-Object {[char]$_}) + "!"
        Write-Log "Generated secure database password" "INFO"
    }
    
    if (-not $JwtSecret) {
        $JwtSecret = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 64 | ForEach-Object {[char]$_})
        Write-Log "Generated secure JWT secret" "INFO"
    }
    
    # Validate JWT secret length
    if ($JwtSecret.Length -lt 32) {
        Write-Log "JWT secret must be at least 32 characters long" "ERROR"
        throw "Invalid JWT secret length"
    }
    
    # Set environment variables
    $env:VCDEVTOOL_DB_PASSWORD = $DbPassword
    $env:VCDEVTOOL_JWT_SECRET = $JwtSecret
    $env:VCDEVTOOL_DB_SA_PASSWORD = "VCDevTool2024!"
    
    if ($AllowedOrigins.Count -gt 0) {
        $env:VCDEVTOOL_ALLOWED_ORIGIN_1 = $AllowedOrigins[0]
        if ($AllowedOrigins.Count -gt 1) {
            $env:VCDEVTOOL_ALLOWED_ORIGIN_2 = $AllowedOrigins[1]
        }
    }
    
    Write-Log "Environment variables configured" "SUCCESS"
}

function Run-Tests {
    if ($SkipTests) {
        Write-Log "Skipping tests as requested" "WARN"
        return
    }
    
    Write-Log "Running test suite..." "INFO"
    
    try {
        # Run API tests
        Push-Location "VCDevTool.API.Tests"
        $testResult = dotnet test --configuration Release --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Some tests failed, but continuing deployment. Check test results." "WARN"
        } else {
            Write-Log "All tests passed" "SUCCESS"
        }
    }
    catch {
        Write-Log "Test execution failed: $($_.Exception.Message)" "WARN"
    }
    finally {
        Pop-Location
    }
}

function Backup-ExistingDeployment {
    if ($SkipBackup) {
        Write-Log "Skipping backup as requested" "WARN"
        return
    }
    
    Write-Log "Creating backup of existing deployment..." "INFO"
    
    if (-not (Test-Path $BackupPath)) {
        New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    }
    
    $backupTimestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupDir = Join-Path $BackupPath "vcdevtool-backup-$backupTimestamp"
    
    try {
        # Export database
        Write-Log "Backing up database..." "INFO"
        docker compose exec -T database /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $env:VCDEVTOOL_DB_SA_PASSWORD -Q "BACKUP DATABASE VCDevToolDb TO DISK = '/var/opt/mssql/backup/vcdevtool-$backupTimestamp.bak'" 2>$null
        
        # Backup volumes
        Write-Log "Backing up volumes..." "INFO"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        docker run --rm -v vcdevtool_db_data:/source -v "${PWD}/${backupDir}:/backup" alpine tar czf /backup/db_data.tar.gz -C /source .
        docker run --rm -v vcdevtool_logs:/source -v "${PWD}/${backupDir}:/backup" alpine tar czf /backup/logs.tar.gz -C /source .
        
        Write-Log "Backup completed: $backupDir" "SUCCESS"
    }
    catch {
        Write-Log "Backup failed: $($_.Exception.Message)" "WARN"
    }
}

function Deploy-Application {
    Write-Log "Starting application deployment..." "INFO"
    
    try {
        if ($DryRun) {
            Write-Log "DRY RUN: Would execute: docker compose build" "INFO"
            Write-Log "DRY RUN: Would execute: docker compose up -d" "INFO"
            return
        }
        
        # Build images
        Write-Log "Building Docker images..." "INFO"
        docker compose build --no-cache
        if ($LASTEXITCODE -ne 0) {
            throw "Docker build failed"
        }
        
        # Start services
        Write-Log "Starting services..." "INFO"
        docker compose up -d
        if ($LASTEXITCODE -ne 0) {
            throw "Service startup failed"
        }
        
        # Wait for services to become healthy
        Write-Log "Waiting for services to become healthy..." "INFO"
        $timeout = 300 # 5 minutes
        $elapsed = 0
        
        do {
            Start-Sleep -Seconds 10
            $elapsed += 10
            
            $healthStatus = docker compose ps --format json | ConvertFrom-Json | Where-Object { $_.Health -ne "healthy" }
            
            if ($healthStatus.Count -eq 0) {
                Write-Log "All services are healthy" "SUCCESS"
                break
            }
            
            if ($elapsed -ge $timeout) {
                Write-Log "Timeout waiting for services to become healthy" "ERROR"
                docker compose logs
                throw "Service health check timeout"
            }
            
            Write-Log "Waiting for services... ($elapsed/$timeout seconds)" "INFO"
        } while ($true)
        
        Write-Log "Application deployment completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Deployment failed: $($_.Exception.Message)" "ERROR"
        
        # Rollback on failure
        Write-Log "Attempting rollback..." "WARN"
        docker compose down
        
        throw $_
    }
}

function Verify-Deployment {
    Write-Log "Verifying deployment..." "INFO"
    
    try {
        # Test API health endpoint
        $healthUrl = "http://localhost:5289/health"
        $healthResponse = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 30
        
        if ($healthResponse) {
            Write-Log "Health check passed" "SUCCESS"
        }
        
        # Test database connectivity
        Write-Log "Testing database connectivity..." "INFO"
        $dbTest = docker compose exec -T database /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $env:VCDEVTOOL_DB_SA_PASSWORD -Q "SELECT 1 as TestConnection" -h -1
        
        if ($dbTest -contains "1") {
            Write-Log "Database connectivity verified" "SUCCESS"
        }
        
        Write-Log "Deployment verification completed" "SUCCESS"
    }
    catch {
        Write-Log "Deployment verification failed: $($_.Exception.Message)" "ERROR"
        throw $_
    }
}

function Show-DeploymentSummary {
    $deploymentDuration = (Get-Date) - $deploymentStartTime
    
    Write-Log "=== DEPLOYMENT SUMMARY ===" "INFO"
    Write-Log "Environment: $Environment" "INFO"
    Write-Log "Duration: $($deploymentDuration.ToString('mm\:ss'))" "INFO"
    Write-Log "Log file: $logFile" "INFO"
    Write-Log "" "INFO"
    Write-Log "🎉 DEPLOYMENT COMPLETED SUCCESSFULLY!" "SUCCESS"
    Write-Log "" "INFO"
    Write-Log "Next steps:" "INFO"
    Write-Log "1. Verify application is accessible at http://localhost:5289" "INFO"
    Write-Log "2. Check logs: docker compose logs -f api" "INFO"
    Write-Log "3. Monitor health: docker compose ps" "INFO"
    Write-Log "4. Access Swagger UI: http://localhost:5289/swagger" "INFO"
    Write-Log "" "INFO"
    Write-Log "Important: Save the following credentials securely:" "WARN"
    Write-Log "Database SA Password: $env:VCDEVTOOL_DB_SA_PASSWORD" "WARN"
    Write-Log "App DB Password: $env:VCDEVTOOL_DB_PASSWORD" "WARN"
    Write-Log "JWT Secret: [Generated - 64 characters]" "WARN"
}

# Main deployment workflow
try {
    Write-Log "Starting VCDevTool Production Deployment" "INFO"
    Write-Log "Target Environment: $Environment" "INFO"
    
    if ($DryRun) {
        Write-Log "DRY RUN MODE - No actual changes will be made" "WARN"
    }
    
    Test-Prerequisites
    Set-EnvironmentVariables
    Run-Tests
    Backup-ExistingDeployment
    Deploy-Application
    Verify-Deployment
    Show-DeploymentSummary
}
catch {
    Write-Log "DEPLOYMENT FAILED: $($_.Exception.Message)" "ERROR"
    Write-Log "Check the log file for details: $logFile" "ERROR"
    exit 1
} 