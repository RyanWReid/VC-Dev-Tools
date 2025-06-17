# clear-locks.ps1

<##
.SYNOPSIS
  Clears stale file locks from the VCDevTool LocalDB database.
.DESCRIPTION
  Uses PowerShell ADO.NET to connect to the LocalDB instance and delete entries in the FileLocks table matching an optional pattern, bypassing external SQL tools.
.EXAMPLE
  .\clear-locks.ps1 "Flashbang_01%"
##>

param(
    [string]$Pattern = "%"
)

$connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VCDevToolDb;Trusted_Connection=True;"
Write-Host "Connecting to LocalDB with connection string: $connectionString"
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $conn.Open()

    Write-Host "Clearing file locks matching pattern '$Pattern'..."
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "DELETE FROM FileLocks WHERE FilePath LIKE @Pattern;"
    $paramObj = New-Object System.Data.SqlClient.SqlParameter("@Pattern", [System.Data.SqlDbType]::VarChar, 255)
    $paramObj.Value = $Pattern
    $cmd.Parameters.Add($paramObj) | Out-Null

    $rowsAffected = $cmd.ExecuteNonQuery()
    Write-Host "$rowsAffected lock(s) deleted."

    $conn.Close()
} catch {
    Write-Host "Error clearing locks: $_"
} 