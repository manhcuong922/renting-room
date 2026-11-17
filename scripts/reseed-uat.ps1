# Seed lại dữ liệu demo cho stack UAT (docker-compose.uat.yml): xóa volume DB + file của stack UAT rồi dựng lại.
# Chỉ đụng project `renting_room_uat` — không động tới stack production (`renting_room_prod`) hay DB dev.
# Cách dùng: powershell scripts/reseed-uat.ps1 [-Yes]
param([switch]$Yes, [string]$EnvFile = '.env.uat')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $EnvFile)) { throw "Thiếu $EnvFile — copy từ .env.uat.example rồi điền." }
if (-not $Yes) {
    $answer = Read-Host 'Xóa toàn bộ dữ liệu stack UAT (project renting_room_uat) và seed lại? (gõ yes)'
    if ($answer -ne 'yes') { Write-Host 'Đã hủy.'; exit 1 }
}
docker compose -p renting_room_uat -f docker-compose.uat.yml --env-file $EnvFile down -v
docker compose -p renting_room_uat -f docker-compose.uat.yml --env-file $EnvFile up -d --build
Write-Host 'Đang khởi động + seed (khoảng 1–2 phút). Theo dõi: docker compose -p renting_room_uat -f docker-compose.uat.yml logs -f api'
