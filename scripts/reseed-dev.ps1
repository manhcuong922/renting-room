# Seed lại dữ liệu demo cho DB dev (container renting_room_db): xóa DB `renting_room` rồi tạo lại rỗng.
# Vì sao cần: ngày demo tính theo HÔM NAY LÚC SEED. DB seed lâu ngày bị "cũ" (vd. seed 20/10, nay 17/11 ⇒ thiếu phiếu tháng 10 ⇒
# "Tạo phiếu tháng 11" bị bỏ qua hàng loạt với PREVIOUS_PERIOD_NOT_BILLED). Seed lại trước mỗi đợt test tay.
# Cách dùng: dừng API dev (nếu đang chạy) → powershell scripts/reseed-dev.ps1 [-Yes] → chạy lại API (tự migrate + seed).
# Chỉ xóa DB của app; không đụng volume MinIO. Dữ liệu tự nhập tay trong DB dev sẽ MẤT.
param([switch]$Yes, [string]$Container = 'renting_room_db', [string]$Database = 'renting_room')
$ErrorActionPreference = 'Stop'
if (-not $Yes) {
    $answer = Read-Host "Xóa toàn bộ DB '$Database' trong container '$Container' và seed lại? (gõ yes)"
    if ($answer -ne 'yes') { Write-Host 'Đã hủy.'; exit 1 }
}
docker exec $Container psql -U postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $Database WITH (FORCE);" -c "CREATE DATABASE $Database;"
Write-Host "Đã tạo lại DB '$Database'. Chạy API (Development) để migrate + seed."
