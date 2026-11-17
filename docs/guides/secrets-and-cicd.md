# Bí mật (secrets) và CI/CD

> Liên quan: [12-deployment-ops.md](../plans/12-deployment-ops.md) (DO-01, DO-02), [zalo-bot-setup.md](zalo-bot-setup.md).

## 1. Nguyên tắc

- **Không giá trị bí mật nào nằm trong git.** `appsettings.json` chỉ có khóa rỗng; `appsettings.Development.json` chỉ chứa mật khẩu DB dev mặc định và tài khoản demo (công khai trong [demo-data.md](demo-data.md)).
- Bí mật đi qua **một trong ba đường**, không có đường thứ tư:

| Nơi chạy | Cách đặt | Ví dụ |
|---|---|---|
| Máy dev | user-secrets (nằm ngoài repo, `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) | `powershell scripts/set-dev-secrets.ps1 -ZaloToken "123:abc"` |
| Production (Docker) | file `.env` ngoài git, truyền vào container | mẫu: [.env.prod.example](../../.env.prod.example) |
| GitHub Actions | GitHub → Settings → Secrets and variables → Actions | chỉ cần khi có bước deploy (xem mục 4) |

- Quy ước biến môi trường: `__` thay cho `:` (`Jwt__SigningKey` = `Jwt:SigningKey`).
- Muốn dán vào file json cho tiện: tạo `appsettings.Local.json` cạnh `appsettings.json` (đã được `.gitignore`). Lưu ý: app **chưa nạp** file này; muốn dùng thì cần thêm một dòng `AddJsonFile` trong `Program.cs`. Cách khuyến nghị vẫn là user-secrets.

## 2. Danh sách bí mật

| Khóa | Bắt buộc? | Hậu quả khi lộ / khi đổi |
|---|---|---|
| `Jwt__SigningKey` (≥32 byte) | Có (ngoài Development) | Lộ ⇒ giả mạo được token bất kỳ ai, kể cả admin. Đổi ⇒ mọi access token đang dùng hết hiệu lực, người dùng đăng nhập lại |
| `PersonalData__HashKey` (≥32 ký tự) | Có | Lộ cùng DB ⇒ dò ngược được CCCD. **Đổi ⇒ tìm/chống trùng CCCD cũ hỏng**, mã liên kết Zalo chưa dùng mất hiệu lực |
| `ConnectionStrings__DefaultConnection` | Có | Chứa mật khẩu DB |
| `Bootstrap__Admin__Password` | Chỉ lần đầu | Bị bắt đổi ở lần đăng nhập đầu; xóa khỏi `.env` sau đó |
| `ZaloBot__Token` | Không | Lộ ⇒ ai cũng điều khiển được bot. Thu hồi/đổi ở Zalo Bot Creator |
| `Email__Smtp__Password` | Không | Dùng App Password, không dùng mật khẩu chính |

Sinh khóa: `bash scripts/new-secrets.sh` hoặc `powershell scripts/new-secrets.ps1` (in ra màn hình, không ghi file).

## 3. Chốt chặn chống lộ

1. `.gitignore` chặn `.env*` (trừ `*.example`), `appsettings.Local.json`, `secrets.json`, `*.pem|*.pfx|*.key|*.age`, `backups/`.
2. `.dockerignore` + `docker build` kiểm: image **không** chứa `.env`, `appsettings.Development.json`, `secrets.json`, chạy user `app` (không root).
3. **CI quét bí mật** bằng gitleaks trên toàn bộ lịch sử mỗi lần push/PR. Gitleaks đỏ ⇒ coi như khóa đã lộ.
4. **Nếu lỡ commit một khóa:** (a) **thu hồi/đổi khóa ngay** — xóa commit không đủ vì GitHub, fork, cache đã giữ; (b) mới đến việc dọn lịch sử.
5. Biến `VITE_*` ở UI **công khai** (nằm trong JS gửi cho trình duyệt): không bao giờ đặt khóa vào đó. CI kiểm `.env.example`.

## 4. GitHub Actions

| Workflow | Khi nào | Làm gì |
|---|---|---|
| `renting_room/.github/workflows/ci.yml` | push `main`, mọi PR | quét bí mật; build; kiểm gói NuGet có lỗ hổng; unit + architecture + integration test (Postgres thật qua Testcontainers); build Docker image và kiểm không root/không lọt file cấu hình dev |
| `renting_room/.github/workflows/cd.yml` | đẩy tag `v1.2.3` hoặc bấm tay | chạy lại CI, rồi build và đẩy image API lên `ghcr.io/<owner>/renting-room-api` bằng `GITHUB_TOKEN` (không cần bí mật nào thêm) |
| `renting_room_ui/.github/workflows/ci.yml` | push `main`, mọi PR | quét bí mật; lint; `npm audit` (mức high); build |
| `dependabot.yml` (cả 2 repo) | hằng tuần | PR cập nhật NuGet/npm/Actions/Docker |

Phát hành: `git tag v0.1.0 && git push origin v0.1.0`.

**Chưa có: bước tự deploy lên máy chủ.** Cần `scripts/deploy` (DO-05) và đích triển khai (laptop qua tunnel hay VPS) — chưa chốt. Khi có, thêm job `deploy` dùng GitHub Environment `production` (bật "Required reviewers" để phải duyệt tay) và secrets `SSH_HOST`, `SSH_USER`, `SSH_KEY`.

### Thiết lập một lần trên GitHub (làm tay, workflow không tự làm được)

- Settings → Code security: bật **Secret scanning** và **Push protection** (chặn push có khóa ngay từ máy).
- Settings → Branches: rule cho `main` — yêu cầu PR + các check `secret-scan`, `build-and-test`, `docker-build` (UI: `secret-scan`, `lint-build`) phải xanh.
- Settings → Actions → General: "Workflow permissions" = Read repository contents.
