# Hướng dẫn PostgreSQL cho dự án renting_room

> Dành cho người **chưa từng dùng PostgreSQL** (đã quen SQL Server hoặc SQL nói chung).
> Mọi lệnh trong tài liệu đã được chạy thử trên chính database của dự án.
> Lệnh viết cho **PowerShell** (terminal mặc định của VS Code trên Windows) trừ khi ghi khác.

## Mục lục

1. [PostgreSQL trong dự án này](#1-postgresql-trong-dự-án-này)
2. [Chạy dự án lần đầu](#2-chạy-dự-án-lần-đầu)
3. [Bật / tắt / xóa database](#3-bật--tắt--xóa-database)
4. [Xem dữ liệu bằng giao diện](#4-xem-dữ-liệu-bằng-giao-diện)
5. [Dùng dòng lệnh psql](#5-dùng-dòng-lệnh-psql)
6. [Khác biệt so với SQL Server — phải nhớ](#6-khác-biệt-so-với-sql-server--phải-nhớ)
7. [Các tính năng PostgreSQL dự án đang dùng](#7-các-tính-năng-postgresql-dự-án-đang-dùng)
8. [Migration với EF Core](#8-migration-với-ef-core)
9. [Xem vì sao truy vấn chậm](#9-xem-vì-sao-truy-vấn-chậm)
10. [Backup và restore](#10-backup-và-restore)
11. [Lỗi thường gặp](#11-lỗi-thường-gặp)
12. [Khi lên production](#12-khi-lên-production)

---

## 1. PostgreSQL trong dự án này

```
┌──────────────┐   EF Core + Npgsql   ┌──────────────────────────────┐
│  API (.NET)  │ ───────────────────► │ PostgreSQL 16 (Docker)       │
│  localhost   │   localhost:5432     │ container: renting_room_db   │
└──────────────┘                      │ database:  renting_room      │
                                      │ dữ liệu lưu trong volume     │
                                      └──────────────────────────────┘
```

| Thành phần | Giá trị dev |
|------------|-------------|
| Chạy bằng | Docker Compose — file [`docker-compose.yml`](../../docker-compose.yml) |
| Host / Port | `localhost` / `5432` |
| Database | `renting_room` |
| User / Mật khẩu | `postgres` / `postgres` (**chỉ dùng ở máy dev**) |
| Múi giờ server | `Asia/Ho_Chi_Minh` |
| Thư viện .NET | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Quy ước tên | bảng & cột viết thường, nối bằng `_` (snake_case): `users`, `phone_normalized` |

**Bạn không tạo bảng bằng tay.** Bảng, index, ràng buộc đều sinh từ code C# qua **EF Core migration** (mục 8).

## 2. Chạy dự án lần đầu

```powershell
# 1. Bật Docker Desktop, đợi "Engine running"

# 2. Bật PostgreSQL (tại thư mục gốc dự án)
docker compose up -d

# 3. Khai báo tài khoản quản trị đầu tiên (lưu ngoài repo, chỉ trên máy bạn)
cd renting_room
dotnet user-secrets set "Bootstrap:Admin:Phone" "0900000001"
dotnet user-secrets set "Bootstrap:Admin:Password" "<mật khẩu ≥ 12 ký tự>"
# Khóa băm số CCCD (bắt buộc, ≥ 32 ký tự, KHÔNG được đổi sau khi đã có dữ liệu)
dotnet user-secrets set "PersonalData:HashKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự>"

# 4. Chạy API — lần đầu sẽ tự tạo bảng (migration) và tạo tài khoản admin
dotnet run --launch-profile http
```

Mở `http://localhost:5213/swagger` → gọi `POST /api/v1/auth/login` → bấm **Authorize**, dán `accessToken`.

Lần chạy đầu ở Development còn tự tạo **dữ liệu mẫu** (tổ chức `DEMO`, chủ trọ `0900000009` / `ChuTroDemo2026`) với đủ tình huống
nghiệp vụ để test tay — xem [demo-data.md](demo-data.md).

> Ở môi trường Development, khóa ký JWT được sinh ngẫu nhiên mỗi lần chạy ⇒ **restart API thì phải đăng nhập lại**.
> Muốn giữ phiên qua các lần restart: `dotnet user-secrets set "Jwt:SigningKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự>"`.

## 3. Bật / tắt / xóa database

| Việc | Lệnh | Dữ liệu |
|------|------|---------|
| Bật | `docker compose up -d` | giữ nguyên |
| Xem trạng thái | `docker compose ps` (phải thấy `healthy`) | — |
| Xem log PostgreSQL | `docker compose logs -f postgres` | — |
| Tắt | `docker compose down` | **giữ nguyên** (nằm trong volume) |
| **Xóa sạch làm lại từ đầu** | `docker compose down -v` rồi `docker compose up -d` | **mất hết** — lần chạy API kế tiếp tự tạo lại bảng + admin |

Tắt máy / tắt Docker không làm mất dữ liệu. Chỉ có `-v` mới xóa.

## 4. Xem dữ liệu bằng giao diện

Tương đương SQL Server Management Studio. Chọn một trong các công cụ:

| Công cụ | Ghi chú |
|---------|---------|
| **DBeaver Community** (khuyên dùng) | Miễn phí, mở được cả PostgreSQL/SQL Server/MySQL, có sơ đồ quan hệ (ER diagram) |
| **pgAdmin 4** | Công cụ chính thức của PostgreSQL |
| **Extension "PostgreSQL" của Microsoft cho VS Code** | Chạy query ngay trong VS Code |

Kết nối mới → chọn **PostgreSQL** → nhập:

```
Host: localhost    Port: 5432    Database: renting_room
Username: postgres Password: postgres
```

Trong cây thư mục: `renting_room` → `Schemas` → **`public`** → `Tables`.
(`public` tương đương `dbo` của SQL Server.)

> ⚠️ Không sửa dữ liệu tài chính / tài khoản trực tiếp bằng tay trên DB — đi qua API để giữ đúng quy tắc nghiệp vụ.
> Sửa tay chỉ dùng khi debug trên máy dev.

## 5. Dùng dòng lệnh psql

`psql` là công cụ dòng lệnh của PostgreSQL (như `sqlcmd` của SQL Server). Nó có sẵn **bên trong container**, không cần cài:

```powershell
docker exec -it renting_room_db psql -U postgres -d renting_room
```

Lệnh bắt đầu bằng `\` là lệnh của psql (không phải SQL):

| Lệnh | Ý nghĩa |
|------|---------|
| `\l` | Liệt kê database |
| `\c ten_db` | Chuyển sang database khác |
| `\dt` | Liệt kê bảng |
| `\d users` | Xem cấu trúc bảng `users` (cột, index, ràng buộc) |
| `\di` | Liệt kê index |
| `\x` | Bật/tắt hiển thị dọc (dễ đọc khi bảng nhiều cột) |
| `\timing` | Bật hiển thị thời gian chạy query |
| `\q` | Thoát |

Câu SQL phải **kết thúc bằng dấu `;`** thì mới chạy.

Ví dụ trên dữ liệu thật của dự án:

```sql
-- Tài khoản đang có
SELECT role, full_name, phone_normalized, must_change_password, status
FROM users
ORDER BY created_at;

-- Refresh token còn hiệu lực của một user
SELECT id, family_id, created_at, expires_at
FROM refresh_tokens
WHERE user_id = '9a35411d-a59c-4b06-9c52-0fb27758c8f6'
  AND revoked_at IS NULL;

-- Lịch sử migration đã chạy
SELECT * FROM __ef_migrations_history;
```

Chạy một câu mà không vào màn hình psql:

```powershell
docker exec renting_room_db psql -U postgres -d renting_room -c "SELECT count(*) FROM users;"
```

## 6. Khác biệt so với SQL Server — phải nhớ

### 6.1 Cú pháp

| Việc | SQL Server | PostgreSQL |
|------|-----------|-----------|
| Lấy N dòng | `SELECT TOP 10 ...` | `SELECT ... LIMIT 10` |
| Phân trang | `OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY` | `LIMIT 10 OFFSET 20` |
| Giờ hiện tại | `SYSDATETIMEOFFSET()` | `now()` |
| Nối chuỗi | `a + b` | `a \|\| b` |
| Tìm không phân biệt hoa thường | `LIKE` (collation mặc định đã CI) | `ILIKE` |
| Lấy dòng vừa insert/update | `OUTPUT inserted.*` | `RETURNING *` |
| Insert, trùng thì bỏ qua | `MERGE` / `IF NOT EXISTS` | `INSERT ... ON CONFLICT DO NOTHING` |
| Ép kiểu | `CAST(x AS int)` | `CAST(x AS int)` hoặc `x::int` |
| Kiểu chuỗi Unicode | `NVARCHAR` | `varchar` / `text` (UTF-8 sẵn, tiếng Việt OK) |
| GUID | `uniqueidentifier` | `uuid` |
| Đúng/sai | `bit` (0/1) | `boolean` (`true`/`false`, psql hiển thị `t`/`f`) |
| Ngày giờ có múi giờ | `datetimeoffset` | `timestamptz` |
| Schema mặc định | `dbo` | `public` |

### 6.2 ⚠️ Chuỗi PHÂN BIỆT hoa thường

```sql
SELECT 'Minh@Gmail.com' = 'minh@gmail.com';   -- PostgreSQL: false  (SQL Server: true)
```

Vì vậy dự án **chuẩn hóa trước khi lưu**: email về chữ thường, SĐT về dạng `0xxxxxxxxx`, mã tổ chức về chữ hoa
(`ContactNormalizer`, `Organization.NormalizeCode`). Nếu quên chuẩn hóa, unique index sẽ **không** chặn được
`Minh@Gmail.com` và `minh@gmail.com` là hai tài khoản khác nhau.

### 6.3 ⚠️ Tên bảng/cột: viết thường, đừng đặt trong ngoặc kép

PostgreSQL tự đổi tên **không có ngoặc kép** về chữ thường; tên **có ngoặc kép** thì giữ nguyên hoa thường:

```sql
SELECT * FROM Users;     -- chạy được (hiểu thành users)
SELECT * FROM "Users";   -- LỖI: relation "Users" does not exist
```

Dự án dùng `UseSnakeCaseNamingConvention()` nên mọi tên đều viết thường: viết SQL tay thoải mái, không cần ngoặc kép.

### 6.4 Thời gian

- Cột `timestamptz` **lưu dạng UTC**, khi hiển thị đổi theo múi giờ của phiên kết nối
  (container đặt `Asia/Ho_Chi_Minh` nên psql hiển thị `+07`).
- Code C# luôn dùng `DateTimeOffset` và lấy giờ qua `TimeProvider` — không dùng `DateTime.Now`.

### 6.5 Không có `IDENTITY_INSERT`, `GO`, `sp_xxx`

- Câu lệnh phân tách bằng `;` (không có `GO`).
- Xem cấu trúc bảng: `\d ten_bang` thay cho `sp_help`.

## 7. Các tính năng PostgreSQL dự án đang dùng

Đây là lý do chọn PostgreSQL: **DB tự chặn dữ liệu sai**, kể cả khi hai request chạy cùng lúc.

| Tính năng | Ví dụ trong dự án | Tác dụng |
|-----------|------------------|----------|
| **Partial unique index** (unique có điều kiện) | `ux_users_phone ... WHERE phone_normalized IS NOT NULL`; `ux_users_organization_owner ... WHERE role = 'OrgOwner'` | SĐT không trùng; mỗi tổ chức đúng 1 chủ trọ |
| **CHECK constraint** | `ck_users_role_organization`: SystemAdmin ⇔ không thuộc tổ chức | Dữ liệu sai bị DB từ chối dù code có bug |
| **Cột hệ thống `xmin`** | `Version` của entity ánh xạ `xmin` | Chống ghi đè khi 2 người sửa cùng lúc (optimistic concurrency) |
| **`UPDATE ... WHERE` nguyên tử** (EF `ExecuteUpdateAsync`) | Xoay vòng refresh token: `WHERE id = ... AND revoked_at IS NULL` | 2 request cùng refresh 1 token → chỉ 1 thành công |
| **Exclusion constraint** (sắp làm — M05) | Hợp đồng cùng phòng không chồng thời gian | Chống cho thuê 1 phòng 2 lần |

Xem ràng buộc thật của một bảng: `\d users` trong psql.

### Mã lỗi PostgreSQL → mã lỗi API

Khi DB chặn ghi, `GlobalExceptionHandler` đổi mã lỗi PostgreSQL thành ProblemDetails:

| SQLSTATE | Tên | API trả về |
|----------|-----|-----------|
| `23505` | unique_violation | 409 — `PHONE_TAKEN`, `EMAIL_TAKEN`, `ORG_CODE_TAKEN`… (tra theo tên index trong `DbConstraints`) hoặc `DUPLICATE_VALUE` |
| `23503` | foreign_key_violation | 409 `REFERENCE_CONFLICT` |
| `23P01` | exclusion_violation | 409 `OVERLAP_CONFLICT` |
| `23514` | check_violation | 422 `CONSTRAINT_VIOLATION` (đồng thời ghi log lỗi — thường là bug) |
| `40001` | serialization_failure | 409 `CONCURRENCY_CONFLICT` |

## 8. Migration với EF Core

Migration = file C# mô tả thay đổi cấu trúc DB, nằm ở `renting_room.Infrastructure/Persistence/Migrations`.

Quy trình khi đổi entity / cấu hình:

```powershell
# 1. Sửa entity (Domain) hoặc cấu hình (Infrastructure/Persistence/Configurations)

# 2. Tạo migration — đặt tên mô tả thay đổi
dotnet ef migrations add AddProperties -p renting_room.Infrastructure -s renting_room.Infrastructure -o Persistence/Migrations

# 3. XEM câu SQL sẽ chạy trước khi áp dụng (rất nên làm)
dotnet ef migrations script -p renting_room.Infrastructure -s renting_room.Infrastructure

# 4. Áp dụng vào DB dev (hoặc chỉ cần chạy API — Development tự migrate khi khởi động)
dotnet ef database update -p renting_room.Infrastructure -s renting_room.Infrastructure
```

Lỡ tạo sai migration **chưa áp dụng**: `dotnet ef migrations remove -p renting_room.Infrastructure -s renting_room.Infrastructure`.

Quy tắc:

- ❌ **Không sửa** migration đã chạy trên DB chung/production. Muốn đổi → tạo migration mới.
- ❌ Không xóa cột đang có dữ liệu trong một bước; làm 2 bước (ngừng dùng → xóa ở bản sau).
- ✅ Production: sinh script idempotent rồi chạy trong pipeline deploy (cấu hình `Database:MigrateOnStartup=false`):
  ```powershell
  dotnet ef migrations script --idempotent -o migrate.sql -p renting_room.Infrastructure -s renting_room.Infrastructure
  ```

Lệnh `dotnet ef` tự dùng DB dev (`localhost:5432`). Trỏ DB khác:
`$env:ConnectionStrings__DefaultConnection = "Host=...;Database=...;Username=...;Password=..."`.

## 9. Xem vì sao truy vấn chậm

Thêm `EXPLAIN ANALYZE` trước câu SELECT (tương đương "Include Actual Execution Plan" của SSMS):

```sql
EXPLAIN ANALYZE SELECT * FROM users WHERE phone_normalized = '0912345678';
```

```
Index Scan using ux_users_phone on users  (actual time=0.011..0.012 rows=1 loops=1)
Execution Time: 0.039 ms
```

Đọc nhanh:

| Thấy | Ý nghĩa |
|------|---------|
| `Index Scan` / `Index Only Scan` | Dùng index — tốt |
| `Seq Scan` trên bảng lớn | Quét toàn bảng — có thể thiếu index |
| `rows=` ước lượng lệch xa thực tế | Thống kê cũ — chạy `ANALYZE ten_bang;` |

DBeaver/pgAdmin có nút vẽ execution plan thành sơ đồ.

Xem câu SQL mà EF Core sinh ra: đặt log `"Microsoft.EntityFrameworkCore.Database.Command": "Information"`
trong `appsettings.Development.json`.

## 10. Backup và restore

```powershell
# Backup (định dạng nén -Fc) rồi chép ra máy
docker exec renting_room_db pg_dump -U postgres -d renting_room -Fc -f /tmp/renting_room.dump
docker cp renting_room_db:/tmp/renting_room.dump .\renting_room.dump

# Restore vào một database mới
docker cp .\renting_room.dump renting_room_db:/tmp/renting_room.dump
docker exec renting_room_db psql -U postgres -c "CREATE DATABASE renting_room_restore;"
docker exec renting_room_db pg_restore -U postgres -d renting_room_restore /tmp/renting_room.dump
```

> Nếu chạy trong **Git Bash** thay vì PowerShell: gõ `export MSYS_NO_PATHCONV=1` trước,
> nếu không Git Bash sẽ tự đổi `/tmp/...` thành đường dẫn Windows và lệnh báo lỗi.

## 11. Lỗi thường gặp

| Thông báo | Nguyên nhân | Cách xử lý |
|-----------|-------------|-----------|
| `Failed to connect to 127.0.0.1:5432` / `Connection refused` | Docker hoặc container chưa chạy | Bật Docker Desktop → `docker compose up -d` → `docker compose ps` |
| `error during connect ... dockerDesktopLinuxEngine` | Docker Desktop đang tắt | Mở Docker Desktop |
| `Bind for 0.0.0.0:5432 failed: port is already allocated` | Máy đã có PostgreSQL khác chiếm cổng 5432 | Tạo file `.env` với `POSTGRES_PORT=5433` và sửa `Port=5433` trong connection string |
| `password authentication failed for user "postgres"` | Đổi mật khẩu trong `.env` sau khi volume đã tạo (mật khẩu chỉ áp dụng lần đầu) | `docker compose down -v` rồi `up -d` (mất dữ liệu dev), hoặc dùng lại mật khẩu cũ |
| `relation "Users" does not exist` | Viết tên trong ngoặc kép hoặc sai hoa thường | Dùng tên thường: `users` (mục 6.3) |
| `duplicate key value violates unique constraint "ux_users_phone"` | Dữ liệu trùng (SQLSTATE 23505) | Bình thường — API đã đổi thành 409 `PHONE_TAKEN` |
| Đăng nhập Swagger xong, restart API thì bị 401 | Khóa JWT dev sinh ngẫu nhiên mỗi lần chạy | Đăng nhập lại, hoặc đặt `Jwt:SigningKey` bằng user-secrets (mục 2) |
| `No SystemAdmin exists ... skipping admin seeding` (log cảnh báo) | Chưa đặt `Bootstrap:Admin:Password` | Làm bước 3 mục 2 rồi chạy lại |

## 12. Khi lên production

Không chạy DB bằng Docker trên máy cá nhân. Checklist:

- [ ] Dùng **dịch vụ PostgreSQL quản lý sẵn** (managed), đặt máy chủ tại Việt Nam (Luật Bảo vệ dữ liệu cá nhân 2025).
- [ ] Bật **backup tự động + Point-in-Time Recovery**; thử restore định kỳ (backup chưa thử restore = chưa có backup).
- [ ] Kết nối bắt buộc mã hóa: thêm `SSL Mode=Require` vào connection string.
- [ ] Tạo **user riêng cho ứng dụng**, chỉ quyền trên database `renting_room` — không dùng superuser `postgres`.
- [ ] Đặt bí mật qua biến môi trường / secret manager, **không** ghi vào `appsettings.json`:
  ```
  ConnectionStrings__DefaultConnection=Host=...;Port=5432;Database=renting_room;Username=app_user;Password=...;SSL Mode=Require
  Jwt__SigningKey=<chuỗi ngẫu nhiên ≥ 32 ký tự>
  ```
- [ ] Migration chạy trong pipeline deploy bằng script idempotent (mục 8), `Database:MigrateOnStartup=false`.
- [ ] Theo dõi: dung lượng, số kết nối, query chậm (`pg_stat_statements`), autovacuum.
- [ ] Nhiều instance API → cân nhắc **PgBouncer** khi số kết nối lớn (mỗi kết nối PostgreSQL là một tiến trình).

## Tài liệu tham khảo

- Tài liệu chính thức PostgreSQL: https://www.postgresql.org/docs/16/
- Npgsql EF Core provider: https://www.npgsql.org/efcore/
- Bảng mã lỗi SQLSTATE: https://www.postgresql.org/docs/16/errcodes-appendix.html
