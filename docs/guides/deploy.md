# Deploy bằng Docker (compose production)

> Plan: [12-deployment-ops.md](../plans/12-deployment-ops.md) DO-03. Kho tệp: [file-storage.md](file-storage.md). Bí mật: [secrets-and-cicd.md](secrets-and-cicd.md).
> **Phạm vi hiện tại:** postgres + migrate + MinIO + api. **Chưa có:** web (Caddy + UI), backup tự động, cloudflared / HTTPS, script `deploy` có rollback (DO-04…DO-08).

## 1. Bên trong stack

| Service | Vai trò | Cổng ra host |
|---|---|---|
| `postgres` | PostgreSQL 16, volume `pgdata`; lần đầu khởi tạo tạo 2 role: `renting_room_owner` (chỉ migrate) và `renting_room_app` (API, quyền tối thiểu) | **không** |
| `migrate` | Chạy `deploy/migrate.sql` bằng role owner rồi thoát; mỗi migration một transaction, chạy lại an toàn | không |
| `minio` | Kho ảnh / tài liệu, volume `minio_data` | **không** |
| `minio-setup` | Tạo bucket private và tài khoản **riêng của app** chỉ có quyền trên bucket đó; thoát khi xong | không |
| `api` | .NET, chạy user `app` (không root), filesystem chỉ đọc, `cap_drop: ALL`, healthcheck | `127.0.0.1:8080` |

Mạng `backend` là `internal` (postgres, minio không ra internet). `api` nằm thêm trên mạng `frontend` để gọi ra ngoài (Zalo, SMTP).

## 2. Lần đầu

```bash
cp .env.prod.example .env                 # KHÔNG commit
bash scripts/new-secrets.sh               # in khóa ngẫu nhiên → dán vào .env (ps1: powershell scripts/new-secrets.ps1)
```

Điền trong `.env`: `Jwt__SigningKey`, `PersonalData__HashKey`, `POSTGRES_PASSWORD`, `RR_OWNER_PASSWORD`, `RR_APP_PASSWORD`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`,
`MINIO_APP_ACCESS_KEY`, `MINIO_APP_SECRET_KEY`, `AllowedHosts` (tên miền thật **và** `localhost` cho healthcheck), `Bootstrap__Admin__*`.

```bash
sh scripts/make-migrate-sql.sh            # (ps1: powershell scripts/make-migrate-sql.ps1) — sinh deploy/migrate.sql từ các migration
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
docker compose -f docker-compose.prod.yml ps -a         # api healthy; migrate và minio-setup "exited 0"
curl -H "Host: localhost" http://127.0.0.1:8080/health/ready    # Healthy
```

Đăng nhập admin bootstrap bằng `Bootstrap__Admin__*`, **đổi mật khẩu ở lần đầu**, rồi **xóa** `Bootstrap__Admin__Password` khỏi `.env`.

## 3. Mỗi lần cập nhật phiên bản

1. `git pull`, rồi `sh scripts/make-migrate-sql.sh` (migration mới vào `deploy/migrate.sql`).
2. `docker compose -f docker-compose.prod.yml --env-file .env up -d --build` — `migrate` chạy lại (idempotent), `api` được tạo lại.
3. Kiểm `/health/ready`. Migration lỗi ⇒ `migrate` thoát khác 0, `api` **không** lên, bản cũ vẫn chạy (nếu đang chạy).

## 4. Đã kiểm chứng (16/11/2026, máy dev, bản thử rồi dọn)

- Cả stack lên từ đầu: `migrate` và `minio-setup` thoát 0, `api` healthy.
- Chỉ `api` có cổng ra host, bind `127.0.0.1`; postgres và minio không có.
- API chạy `uid=1654(app)`, ghi vào `/app` bị từ chối (read-only), ghi được `/keys` và `/data/files`.
- Role `renting_room_app`: không superuser, `CREATE TABLE` bị từ chối; 43 bảng thuộc `renting_room_owner`, app đọc / ghi được.
- Tài khoản MinIO của app: ghi / đọc / xóa trong bucket `renting-room` được; tạo bucket khác và gọi API quản trị bị `Access Denied`.
- Luồng thật: admin bootstrap → tạo chủ trọ → tạo người thuê → upload CCCD → tải về đúng từng byte; liên kết ký cho tệp nhạy cảm bị từ chối.
- `docker compose down` rồi `up -d`: dữ liệu và tệp còn nguyên, `migrate` chạy lại không lỗi.

## 5. Còn thiếu trước khi ra internet

- **HTTPS và web:** Caddy / Cloudflare Tunnel, UI build (DO-04…DO-07). Hiện API chỉ nghe `127.0.0.1`.
- **Backup:** `pg_dump` mã hóa + volume `minio_data` + `dpkeys` (DO-06). **Mất `dpkeys` hoặc `PersonalData__HashKey` là không đọc được CCCD đã mã hóa.**
- **Image MinIO:** máy chủ mới phải có sẵn image — xem [file-storage.md](file-storage.md) mục 6. `docker save` / `docker load`, hoặc registry riêng và đặt `MINIO_IMAGE`, `MINIO_MC_IMAGE`.
- **Ghim phiên bản image** (digest) cho `postgres`, `minio`, `mc`; quét lỗ hổng trước khi deploy.
- `KnownProxies` / `KnownNetworks` cho rate limit theo IP thật khi có reverse proxy.
