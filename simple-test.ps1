try {
    $health = Invoke-RestMethod -Uri "http://localhost:5289/api/health"
    Write-Host "API Health: SUCCESS"
    
    $tasks = Invoke-RestMethod -Uri "http://localhost:5289/api/tasks"
    Write-Host "Tasks endpoint: SUCCESS - $($tasks.Count) tasks"
    
    Write-Host "SYSTEM IS WORKING WITHOUT AUTHENTICATION!"
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)"
}