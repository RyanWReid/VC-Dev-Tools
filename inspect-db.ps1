# inspect-db.ps1

<##
.SYNOPSIS
  Inspects the VCDevToolDb by listing all tasks and file locks.
.DESCRIPTION
  Connects via PowerShell ADO.NET to the LocalDB instance and queries the Tasks and FileLocks tables, printing results to the console.
.EXAMPLE
  .\inspect-db.ps1
##>

param(
    [string]$LockPattern = "%",
    [switch]$ShowFileLocks
)

# Connection string for LocalDB
$connectionString = 'Server=(localdb)\MSSQLLocalDB;Database=VCDevToolDb;Trusted_Connection=True;'

# Ensure the LocalDB instance is running
Write-Host "Ensuring LocalDB instance 'MSSQLLocalDB' is running..."
sqllocaldb start "MSSQLLocalDB" | Out-Null

Write-Host "Connecting to LocalDB with connection string: $connectionString"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $conn.Open()

    # === Database File Paths ===
    Write-Host "`n=== Database File Paths ==="
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT name, physical_name FROM sys.database_files;"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Host "$($reader['name']) : $($reader['physical_name'])"
    }
    $reader.Close()

    # === Table Row Counts ===
    Write-Host "`n=== Table Row Counts ==="
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM Tasks;"
    $countTasks = $cmd.ExecuteScalar()
    Write-Host "Tasks count: $countTasks"
    $cmd.CommandText = "SELECT COUNT(*) FROM FileLocks;"
    $countLocks = $cmd.ExecuteScalar()
    Write-Host "FileLocks count: $countLocks"

    # Query the Tasks table
    Write-Host "
=== Tasks in Database ==="
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT Id, Name, Status, AssignedNodeId, CreatedAt, StartedAt, CompletedAt FROM Tasks ORDER BY Id;"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        $id = $reader["Id"]
        $name = $reader["Name"]
        $status = $reader["Status"]
        $node = $reader["AssignedNodeId"]
        $created = $reader["CreatedAt"]
        $started = $reader["StartedAt"]
        $completed = $reader["CompletedAt"]
        Write-Host "$id`t$name`t$status`tAssignedNode:$node`tCreated:$created`tStarted:$started`tCompleted:$completed"
    }
    $reader.Close()

    # Optionally query the FileLocks table
    if ($ShowFileLocks) {
        Write-Host "
=== File Locks matching '$LockPattern' ==="
        $cmd.CommandText = "SELECT Id, FilePath, LockingNodeId, AcquiredAt, LastUpdatedAt FROM FileLocks WHERE FilePath LIKE @Pattern;"
        $cmd.Parameters.Clear()
        $param = New-Object System.Data.SqlClient.SqlParameter("@Pattern", [System.Data.SqlDbType]::VarChar, 255)
        $param.Value = $LockPattern
        $cmd.Parameters.Add($param) | Out-Null
        $reader = $cmd.ExecuteReader()
        while ($reader.Read()) {
            $lid = $reader["Id"]
            $path = $reader["FilePath"]
            $locking = $reader["LockingNodeId"]
            $acq = $reader["AcquiredAt"]
            $last = $reader["LastUpdatedAt"]
            Write-Host "$lid`t$path`tNode:$locking`tAcquired:$acq`tLastUpdated:$last"
        }
        $reader.Close()
    }

    $conn.Close()
} catch {
    Write-Host "Error inspecting database: $_"
    Exit 1
} 