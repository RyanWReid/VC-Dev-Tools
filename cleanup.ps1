# Kill all VCDevTool processes
Get-Process -Name "VCDevTool*" -ErrorAction SilentlyContinue | ForEach-Object { 
    Write-Host "Killing process: $($_.Name) (PID: $($_.Id))"
    Stop-Process -Id $_.Id -Force
}

# Kill any dotnet processes that are hosting the API or client
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { 
    $_.CommandLine -match "VCDevTool.API|VCDevTool.Client" 
} | ForEach-Object {
    Write-Host "Killing dotnet process: $($_.Id)"
    Stop-Process -Id $_.Id -Force
}

# More aggressive approach - kill all dotnet processes (caution: will kill all dotnet processes)
# Uncomment if needed
<# 
Write-Host "Killing all dotnet processes..."
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
#>

# Kill by port - check both ports
$portProcesses = netstat -ano | Select-String ":5288|:5289"
if ($portProcesses) {
    $portProcesses | ForEach-Object {
        if ($_ -match "LISTENING\s+(\d+)") {
            $processId = $matches[1]
            Write-Host "Found process using port 5288/5289: PID $processId"
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            
            # Get more details about this process
            $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($process) {
                Write-Host "Process details: $($process.Name) ($($process.Id))"
            }
        }
    }
}

# Double-check IIS Express or other web servers
Get-Process -Name "iisexpress", "w3wp" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Killing web server process: $($_.Name) (PID: $($_.Id))"
    Stop-Process -Id $_.Id -Force
}

# Wait a moment for processes to be fully killed
Write-Host "Waiting for processes to terminate..."
Start-Sleep -Seconds 3

# Clean build artifacts
Write-Host "Cleaning build artifacts..."
try {
    Remove-Item -Recurse -Force VCDevTool.API\bin -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force VCDevTool.API\obj -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force VCDevTool.Client\bin -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force VCDevTool.Client\obj -ErrorAction SilentlyContinue
    
    # Full clean with dotnet CLI
    dotnet clean
    
    # Delete VS temporary files
    Get-ChildItem -Path . -Include *.suo, *.user, *.userosscache, *.sln.docstates, .vs -Recurse -Force | 
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    Write-Host "Error during cleanup: $_"
}

# Final check of ports
Write-Host "Checking if ports are now free..."
$portCheck = netstat -ano | Select-String ":5288|:5289"
if ($portCheck) {
    Write-Host "WARNING: Ports 5288/5289 are still in use by some process!"
    $portCheck
} else {
    Write-Host "Ports 5288 and 5289 are now free."
}

Write-Host "All related processes have been terminated and build artifacts cleaned." 