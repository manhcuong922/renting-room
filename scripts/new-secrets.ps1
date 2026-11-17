# Generate random production secrets. Prints to screen, writes NO file: paste into .env / GitHub Secrets / password manager.
# Works on Windows PowerShell 5.1 and pwsh 7.
function New-Secret([int]$Length) {
  $bytes = New-Object byte[] 96
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($bytes)
  $rng.Dispose()
  (([Convert]::ToBase64String($bytes)) -replace '[^A-Za-z0-9]', '').Substring(0, $Length)
}
"Jwt__SigningKey=$(New-Secret 64)"
"PersonalData__HashKey=$(New-Secret 48)"
"POSTGRES_PASSWORD=$(New-Secret 32)"
"RR_OWNER_PASSWORD=$(New-Secret 32)"
"RR_APP_PASSWORD=$(New-Secret 32)"
"MINIO_ROOT_PASSWORD=$(New-Secret 32)"
"MINIO_APP_SECRET_KEY=$(New-Secret 40)"
"# MINIO_ROOT_USER / MINIO_APP_ACCESS_KEY: choose your own names (e.g. rrroot, rrapp)."
"# WARNING: changing PersonalData__HashKey after data exists breaks lookup/duplicate check of existing national IDs."
