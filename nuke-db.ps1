# nuke-db.ps1

<##
.SYNOPSIS
  Drops and recreates the VCDevTool LocalDB database.
.DESCRIPTION
  This script uses the EF Core CLI to drop the LocalDB database and then apply migrations, effectively nuking all data.
  Requires dotnet CLI and EF Core tools.
.EXAMPLE
  .\nuke-db.ps1
##>

Write-Host "Dropping the VCDevTool database..."
dotnet ef database drop --project VCDevTool.API --force --no-build

Write-Host "Recreating the VCDevTool database..."
dotnet ef database update --project VCDevTool.API --no-build

Write-Host "Database nuke complete." 