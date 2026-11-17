#!/usr/bin/env bash
# Sinh khóa ngẫu nhiên cho production. In ra màn hình, KHÔNG ghi file: dán vào .env / GitHub Secrets / trình quản lý mật khẩu.
set -euo pipefail
gen() { head -c "$1" /dev/urandom | base64 | tr -d '\n=+/' | head -c "$2"; }
echo "Jwt__SigningKey=$(gen 64 64)"
echo "PersonalData__HashKey=$(gen 64 48)"
echo "POSTGRES_PASSWORD=$(gen 48 32)"
echo "RR_OWNER_PASSWORD=$(gen 48 32)"
echo "RR_APP_PASSWORD=$(gen 48 32)"
echo "MINIO_ROOT_PASSWORD=$(gen 48 32)"
echo "MINIO_APP_SECRET_KEY=$(gen 48 40)"
echo "# MINIO_ROOT_USER / MINIO_APP_ACCESS_KEY: tự đặt tên (VD rrroot, rrapp)."
echo "# Cảnh báo: đổi PersonalData__HashKey sau khi có dữ liệu thì tìm/chống trùng CCCD cũ hỏng."
