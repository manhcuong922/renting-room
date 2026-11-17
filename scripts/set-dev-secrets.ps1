# Store DEV secrets in user-secrets (outside the repo, cannot be committed by mistake). Run once per machine.
# Usage: powershell scripts/set-dev-secrets.ps1 [-ZaloToken "123:abc"] [-SmtpPassword "..."] [-UseMinio [-MinioUser u -MinioPassword p]]
param([string]$ZaloToken, [string]$SmtpPassword, [switch]$UseMinio, [string]$MinioUser = "minioadmin", [string]$MinioPassword = "minioadmin123")
$p = "src/renting_room.Api"
function New-Secret([int]$Length) {
  $bytes = New-Object byte[] 96
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($bytes)
  $rng.Dispose()
  (([Convert]::ToBase64String($bytes)) -replace '[^A-Za-z0-9]', '').Substring(0, $Length)
}
$existing = (dotnet user-secrets list --project $p) -join "`n"
if ($existing -notmatch '(?m)^Jwt:SigningKey') { dotnet user-secrets set "Jwt:SigningKey" (New-Secret 64) --project $p | Out-Null; "Created Jwt:SigningKey" }
if ($existing -notmatch '(?m)^PersonalData:HashKey') { dotnet user-secrets set "PersonalData:HashKey" (New-Secret 48) --project $p | Out-Null; "Created PersonalData:HashKey" }
if ($ZaloToken) { dotnet user-secrets set "ZaloBot:Token" $ZaloToken --project $p | Out-Null; "Set ZaloBot:Token" }
if ($SmtpPassword) { dotnet user-secrets set "Email:Smtp:Password" $SmtpPassword --project $p | Out-Null; "Set Email:Smtp:Password" }
"Done. List key names only: dotnet user-secrets list --project $p | ForEach-Object { `$_.Split('=')[0] }"
if ($UseMinio) {
  # M09: dùng MinIO của docker-compose (docker compose up -d). Bỏ lệnh này / xóa 4 khóa Storage:* để quay về lưu file trên ổ đĩa.
  dotnet user-secrets set "Storage:Provider" "S3" --project $p | Out-Null
  dotnet user-secrets set "Storage:S3:ServiceUrl" "http://localhost:9000" --project $p | Out-Null
  dotnet user-secrets set "Storage:S3:AccessKey" $MinioUser --project $p | Out-Null
  dotnet user-secrets set "Storage:S3:SecretKey" $MinioPassword --project $p | Out-Null
  "Set Storage:* to use MinIO at http://localhost:9000 (bucket renting-room is created on first upload)"
}
