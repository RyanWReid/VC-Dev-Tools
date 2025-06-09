# Test Script for Concurrent Folder Processing
# This script creates test folders with VDB files and verifies concurrent processing

Write-Host "=== Testing Concurrent Folder Processing ===" -ForegroundColor Green

# Create test directory structure
$testRoot = "C:\temp\vdb-test"
$folders = @("Folder1", "Folder2", "Folder3", "Folder4", "Folder5")

Write-Host "Creating test directory structure at: $testRoot" -ForegroundColor Yellow

# Clean up existing test directory
if (Test-Path $testRoot) {
    Remove-Item $testRoot -Recurse -Force
}

# Create test folders with dummy VDB files
foreach ($folder in $folders) {
    $folderPath = Join-Path $testRoot $folder
    New-Item -ItemType Directory -Path $folderPath -Force | Out-Null
    
    # Create 2-3 dummy VDB files in each folder
    $fileCount = Get-Random -Minimum 2 -Maximum 4
    for ($i = 1; $i -le $fileCount; $i++) {
        $vdbFile = Join-Path $folderPath "test_file_$i.vdb"
        "dummy vdb content for testing" | Out-File -FilePath $vdbFile -Encoding UTF8
    }
    
    Write-Host "Created folder: $folder with $fileCount VDB files" -ForegroundColor Cyan
}

Write-Host "`nTest directory structure created successfully!" -ForegroundColor Green
Write-Host "Test root: $testRoot" -ForegroundColor White
Write-Host "`nTo test concurrent processing:" -ForegroundColor Yellow
Write-Host "1. Start multiple client nodes" -ForegroundColor White
Write-Host "2. Create a Volume Compression task with directory: $testRoot" -ForegroundColor White
Write-Host "3. Assign the task to multiple nodes" -ForegroundColor White
Write-Host "4. Watch the debug output to see nodes working on different folders simultaneously" -ForegroundColor White

Write-Host "`nExpected behavior:" -ForegroundColor Yellow
Write-Host "- Each node should acquire locks on different folders" -ForegroundColor White
Write-Host "- Multiple nodes should work simultaneously" -ForegroundColor White
Write-Host "- Debug output should show '[NodeName] Processing folder: ...' messages" -ForegroundColor White
Write-Host "- Folders should be processed in parallel, not sequentially" -ForegroundColor White

Write-Host "`nPress any key to continue..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") 