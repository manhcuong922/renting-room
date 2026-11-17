#!/bin/sh
# Sinh deploy/migrate.sql (idempotent: chạy lại nhiều lần an toàn) từ các migration EF. Chạy trước mỗi lần deploy; migrate.sql KHÔNG commit.
# Cần: dotnet SDK + công cụ `dotnet-ef` (dotnet tool install --global dotnet-ef).
set -eu
cd "$(dirname "$0")/.."
dotnet ef migrations script --idempotent --project src/renting_room.Infrastructure --startup-project src/renting_room.Api --output deploy/migrate.sql
echo "Wrote deploy/migrate.sql ($(wc -l < deploy/migrate.sql) lines)"
