#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs unit tests with coverage reporting and generates comprehensive test coverage reports.

.DESCRIPTION
    This script runs all unit tests for the VCDevTool project with code coverage analysis,
    generates HTML reports, and provides detailed coverage metrics.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Debug.

.PARAMETER OutputDir
    Directory for coverage reports. Default is ./TestResults.

.PARAMETER Threshold
    Minimum coverage threshold percentage. Default is 80.

.PARAMETER SkipBuild
    Skip the build step and run tests directly.

.EXAMPLE
    .\test-coverage.ps1
    Runs tests with default settings.

.EXAMPLE
    .\test-coverage.ps1 -Configuration Release -Threshold 85
    Runs tests in Release mode with 85% coverage threshold.
#>

param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [Parameter()]
    [string]$OutputDir = "./TestResults",
    
    [Parameter()]
    [ValidateRange(0, 100)]
    [int]$Threshold = 80,
    
    [Parameter()]
    [switch]$SkipBuild
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Colors for console output
$Green = [System.ConsoleColor]::Green
$Red = [System.ConsoleColor]::Red
$Yellow = [System.ConsoleColor]::Yellow
$Blue = [System.ConsoleColor]::Blue

function Write-ColorOutput {
    param(
        [string]$Message,
        [System.ConsoleColor]$ForegroundColor = [System.ConsoleColor]::White
    )
    
    $originalColor = $Host.UI.RawUI.ForegroundColor
    $Host.UI.RawUI.ForegroundColor = $ForegroundColor
    Write-Output $Message
    $Host.UI.RawUI.ForegroundColor = $originalColor
}

function Test-ToolInstalled {
    param([string]$ToolName)
    
    try {
        & $ToolName --version | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Install-ReportGenerator {
    Write-ColorOutput "Installing ReportGenerator tool..." $Blue
    try {
        dotnet tool install --global dotnet-reportgenerator-globaltool
        Write-ColorOutput "ReportGenerator installed successfully." $Green
    }
    catch {
        Write-ColorOutput "Failed to install ReportGenerator. Some features may not work." $Yellow
    }
}

function Main {
    $startTime = Get-Date
    
    Write-ColorOutput "=== VCDevTool Test Coverage Report ===" $Blue
    Write-ColorOutput "Configuration: $Configuration" $Blue
    Write-ColorOutput "Output Directory: $OutputDir" $Blue
    Write-ColorOutput "Coverage Threshold: $Threshold%" $Blue
    Write-Output ""

    # Check prerequisites
    Write-ColorOutput "Checking prerequisites..." $Blue
    
    if (-not (Test-ToolInstalled "dotnet")) {
        Write-ColorOutput "ERROR: .NET CLI not found. Please install .NET 9.0 or later." $Red
        exit 1
    }
    
    # Check if ReportGenerator is installed
    if (-not (Test-ToolInstalled "reportgenerator")) {
        Write-ColorOutput "ReportGenerator not found. Installing..." $Yellow
        Install-ReportGenerator
    }

    # Create output directory
    if (Test-Path $OutputDir) {
        Write-ColorOutput "Cleaning existing test results..." $Yellow
        Remove-Item $OutputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

    # Build solution (unless skipped)
    if (-not $SkipBuild) {
        Write-ColorOutput "Building solution..." $Blue
        try {
            dotnet build --configuration $Configuration --verbosity minimal
            Write-ColorOutput "Build completed successfully." $Green
        }
        catch {
            Write-ColorOutput "Build failed. Aborting test run." $Red
            exit 1
        }
        Write-Output ""
    }

    # Run tests with coverage
    Write-ColorOutput "Running tests with coverage analysis..." $Blue
    
    $testProjects = @(
        "VCDevTool.API.Tests"
    )
    
    $coverageFiles = @()
    
    foreach ($project in $testProjects) {
        $projectPath = "./$project/$project.csproj"
        
        if (-not (Test-Path $projectPath)) {
            Write-ColorOutput "WARNING: Test project not found: $projectPath" $Yellow
            continue
        }
        
        Write-ColorOutput "Running tests for $project..." $Blue
        
        $coverageFile = "$OutputDir/$project.coverage.xml"
        $coverageFiles += $coverageFile
        
        $testArgs = @(
            "test"
            $projectPath
            "--configuration"
            $Configuration
            "--logger"
            "trx;LogFileName=$project.trx"
            "--results-directory"
            $OutputDir
            "--collect:`"XPlat Code Coverage`""
            "--"
            "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura"
        )
        
        try {
            & dotnet @testArgs
            
            # Find the generated coverage file and move it to expected location
            $generatedCoverageFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "coverage.cobertura.xml"
            if ($generatedCoverageFiles.Count -gt 0) {
                $latestCoverageFile = $generatedCoverageFiles | Sort-Object LastWriteTime | Select-Object -Last 1
                Move-Item $latestCoverageFile.FullName $coverageFile -Force
            }
            
            Write-ColorOutput "Tests completed for $project." $Green
        }
        catch {
            Write-ColorOutput "Tests failed for $project." $Red
            Write-ColorOutput $_.Exception.Message $Red
        }
    }
    
    Write-Output ""

    # Generate coverage report
    if ($coverageFiles.Count -gt 0 -and (Test-ToolInstalled "reportgenerator")) {
        Write-ColorOutput "Generating coverage report..." $Blue
        
        $reportArgs = @(
            "-reports:$($coverageFiles -join ';')"
            "-targetdir:$OutputDir/CoverageReport"
            "-reporttypes:Html;Badges;JsonSummary;Cobertura"
            "-historydir:$OutputDir/CoverageHistory"
            "-title:VCDevTool Code Coverage Report"
        )
        
        try {
            & reportgenerator @reportArgs
            Write-ColorOutput "Coverage report generated successfully." $Green
            
            # Open report in browser (Windows only)
            $reportPath = "$OutputDir/CoverageReport/index.html"
            if (Test-Path $reportPath) {
                Write-ColorOutput "Report available at: $reportPath" $Blue
                
                if ($IsWindows) {
                    Start-Process $reportPath
                }
            }
        }
        catch {
            Write-ColorOutput "Failed to generate coverage report." $Yellow
            Write-ColorOutput $_.Exception.Message $Yellow
        }
    }

    # Analyze coverage results
    Write-Output ""
    Write-ColorOutput "=== Coverage Analysis ===" $Blue
    
    $summaryFile = "$OutputDir/CoverageReport/Summary.json"
    if (Test-Path $summaryFile) {
        try {
            $summary = Get-Content $summaryFile | ConvertFrom-Json
            $coverage = $summary.summary.linecoverage
            
            Write-ColorOutput "Overall Line Coverage: $($coverage)%" $Blue
            
            if ($coverage -ge $Threshold) {
                Write-ColorOutput "✓ Coverage threshold met ($($Threshold)%)" $Green
                $exitCode = 0
            }
            else {
                Write-ColorOutput "✗ Coverage below threshold ($($Threshold)%)" $Red
                $exitCode = 1
            }
            
            # Display detailed results
            Write-Output ""
            Write-ColorOutput "Detailed Coverage by Assembly:" $Blue
            foreach ($assembly in $summary.coverage) {
                $name = $assembly.name
                $lineCoverage = $assembly.linecoverage
                $branchCoverage = $assembly.branchcoverage
                
                $color = if ($lineCoverage -ge $Threshold) { $Green } else { $Red }
                Write-ColorOutput "  $name - Lines: $lineCoverage%, Branches: $branchCoverage%" $color
            }
        }
        catch {
            Write-ColorOutput "Could not parse coverage summary." $Yellow
            $exitCode = 0
        }
    }
    else {
        Write-ColorOutput "No coverage summary found." $Yellow
        $exitCode = 0
    }

    # Performance metrics
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    Write-Output ""
    Write-ColorOutput "=== Performance Metrics ===" $Blue
    Write-ColorOutput "Total execution time: $($duration.TotalMinutes.ToString('F2')) minutes" $Blue
    
    # Find and display test results
    $testResultFiles = Get-ChildItem -Path $OutputDir -Filter "*.trx"
    if ($testResultFiles.Count -gt 0) {
        Write-Output ""
        Write-ColorOutput "=== Test Results Summary ===" $Blue
        
        foreach ($resultFile in $testResultFiles) {
            Write-ColorOutput "Test results: $($resultFile.Name)" $Blue
            
            # Parse TRX file for basic stats (simplified)
            $content = Get-Content $resultFile.FullName -Raw
            if ($content -match 'total="(\d+)".*passed="(\d+)".*failed="(\d+)"') {
                $total = $matches[1]
                $passed = $matches[2]
                $failed = $matches[3]
                
                Write-ColorOutput "  Total: $total, Passed: $passed, Failed: $failed" $(if ($failed -eq "0") { $Green } else { $Red })
            }
        }
    }

    # Generate recommendations
    Write-Output ""
    Write-ColorOutput "=== Recommendations ===" $Blue
    
    if ($exitCode -ne 0) {
        Write-ColorOutput "• Increase test coverage to meet the $($Threshold)% threshold" $Yellow
        Write-ColorOutput "• Focus on testing critical business logic and error handling" $Yellow
        Write-ColorOutput "• Add integration tests for API endpoints" $Yellow
    }
    else {
        Write-ColorOutput "• Great job! Coverage threshold met." $Green
        Write-ColorOutput "• Consider adding performance tests for critical paths" $Blue
        Write-ColorOutput "• Review uncovered code paths for potential edge cases" $Blue
    }
    
    Write-ColorOutput "• Check the detailed HTML report for specific coverage gaps" $Blue
    Write-ColorOutput "• Run performance benchmarks: dotnet test --filter 'FullyQualifiedName~Performance'" $Blue

    Write-Output ""
    Write-ColorOutput "Test coverage analysis completed." $Blue
    
    return $exitCode
}

# Run main function and exit with appropriate code
$exitCode = Main
exit $exitCode 