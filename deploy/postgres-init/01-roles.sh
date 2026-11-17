#!/bin/sh
# Chạy MỘT lần khi volume Postgres còn trống (docker-entrypoint-initdb.d). Quyền tối thiểu theo plan M12 §9:
#   renting_room_owner — sở hữu schema, CHỈ dùng cho service migrate;
#   renting_room_app   — API kết nối bằng role này: SELECT/INSERT/UPDATE/DELETE, không CREATE, không superuser.
set -eu
: "${RR_OWNER_PASSWORD:?RR_OWNER_PASSWORD is required}"
: "${RR_APP_PASSWORD:?RR_APP_PASSWORD is required}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner_pw="$RR_OWNER_PASSWORD" -v app_pw="$RR_APP_PASSWORD" -v db="$POSTGRES_DB" <<'SQL'
CREATE ROLE renting_room_owner LOGIN PASSWORD :'owner_pw' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE renting_room_app   LOGIN PASSWORD :'app_pw'   NOSUPERUSER NOCREATEDB NOCREATEROLE;
ALTER DATABASE :"db" OWNER TO renting_room_owner;
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO renting_room_app;
GRANT USAGE ON SCHEMA public TO renting_room_app;
-- Bảng / sequence do owner tạo (migration) tự cấp quyền cho app.
ALTER DEFAULT PRIVILEGES FOR ROLE renting_room_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO renting_room_app;
ALTER DEFAULT PRIVILEGES FOR ROLE renting_room_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO renting_room_app;
SQL
