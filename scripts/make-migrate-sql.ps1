# Sinh deploy/migrate.sql (idempotent) từ các migration EF. Chạy trước mỗi lần deploy; migrate.sql KHÔNG commit.
# Cần: dotnet SDK + dotnet-ef (dotnet tool install --global dotnet-ef).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet ef migrations script --idempotent --project src/renting_room.Infrastructure --startup-project src/renting_room.Api --output deploy/migrate.sql
if ($LASTEXITCODE -ne 0) { throw 'dotnet ef failed' }
"Wrote deploy/migrate.sql ($((Get-Content deploy/migrate.sql).Count) lines)"
