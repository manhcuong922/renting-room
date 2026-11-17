# Lưu ảnh và tài liệu (M09) — Local và MinIO

> Thiết kế: [09-file-storage.md](../plans/09-file-storage.md). API: [attachments.md](../api/attachments.md). Bí mật: [secrets-and-cicd.md](secrets-and-cicd.md).

## 1. Hai cách lưu, chọn bằng cấu hình

| | **Local** (mặc định) | **MinIO / S3** |
|---|---|---|
| File nằm ở | Thư mục trên máy chủ chạy API (`Storage:Local:RootPath`, mặc định `App_Data/files`) | Dịch vụ MinIO (container Docker) hoặc object storage nhà cung cấp |
| Cần cài thêm | Không | MinIO (đã có trong `docker-compose.yml`) |
| Khi nào dùng | Chạy thử, dev, không cài gì | Deploy thật (khuyến nghị) |
| Rủi ro | Mất file nếu thư mục / container bị xóa mà không có volume + backup | Phải backup volume MinIO |

**Không đăng ký MinIO ở đâu cả.** MinIO là phần mềm miễn phí, bạn tự chạy trong Docker và tự đặt tên đăng nhập / mật khẩu.

App **luôn chạy được khi không có MinIO**: không cấu hình gì thì lưu Local. Nếu đã chọn MinIO mà MinIO sập, chỉ upload / tải tệp báo 503 `STORAGE_UNAVAILABLE`, mọi chức năng khác vẫn chạy; `/health/ready` báo `storage: Degraded`.

## 2. Chạy trên máy dev

**Cách 1 — Local (không làm gì cả):** `dotnet run`. File lưu ở `src/renting_room.Api/App_Data/files` (đã `.gitignore`).

**Cách 2 — MinIO:**

```powershell
docker compose up -d                                  # Postgres + MinIO (cần image MinIO — xem mục 6)
powershell scripts/set-dev-secrets.ps1 -UseMinio      # ghi Storage:* vào user-secrets (ngoài repo)
dotnet run --project src/renting_room.Api
```

- Console MinIO: http://localhost:9001 — đăng nhập `minioadmin` / `minioadmin123` (mật khẩu dev, chỉ bind 127.0.0.1).
- Bucket `renting-room` **tự tạo** ở lần upload / health check đầu tiên (private). Không cần tạo tay.
- Quay về Local: xóa 4 khóa `Storage:*` khỏi user-secrets (`dotnet user-secrets remove "Storage:Provider" --project src/renting_room.Api` …).

Đổi qua lại giữa Local và MinIO **không làm mất file cũ**: mỗi tệp nhớ nó nằm ở đâu. Điều kiện: cấu hình MinIO vẫn còn đủ để đọc tệp đã lưu trên MinIO.

## 3. Deploy

Dùng [docker-compose.prod.yml](../../docker-compose.prod.yml) — hướng dẫn từng bước ở [deploy.md](deploy.md) (đã kiểm chứng chạy thật 16/11/2026). Compose này tự tạo bucket private và tài khoản MinIO riêng cho app (chỉ có quyền trên bucket), nên bạn không điền `Storage__S3__*` tay — chỉ cần các biến `MINIO_*`. Biến môi trường (xem [.env.prod.example](../../.env.prod.example)):

| Biến | Giá trị | Ghi chú |
|---|---|---|
| `Storage__Provider` | `S3` | `Local` nếu chưa dùng MinIO |
| `Storage__S3__ServiceUrl` | `http://minio:9000` | tên service MinIO trong compose |
| `Storage__S3__Bucket` | `renting-room` | |
| `Storage__S3__AccessKey` / `SecretKey` | **bạn tự sinh**, mạnh | khuyến nghị tạo **service account riêng** chỉ có quyền trên bucket này (MinIO console → Access Keys), không dùng root |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | **bạn tự sinh**, mạnh | tài khoản quản trị MinIO |
| `Storage__OrganizationQuotaBytes` | mặc định 5368709120 (5 GB) | hạn mức mỗi tổ chức |
| `Storage__PurgeAfterDays` | mặc định 30 (1–3650) | số ngày giữ file vật lý sau khi xóa mềm; job hằng ngày xóa file và trả hạn mức |

Quy tắc an toàn:

- `Storage__Provider=S3` mà thiếu `ServiceUrl` / `AccessKey` / `SecretKey` / `Bucket` ⇒ **app không khởi động**, thông báo nêu đúng khóa thiếu. Cố ý: không tự rơi về Local để tránh file nằm lẫn hai nơi.
- Bucket luôn **private**, không có URL công khai. Tệp nhạy cảm (ảnh CCCD…) chỉ tải qua API, mỗi lần tải có ghi audit.
- Volume MinIO (`minio_data`) và thư mục Local (`/data/files`) **phải được backup**; đĩa đầy ⇒ upload lỗi.
- Dùng object storage của nhà cung cấp (S3-compatible): chỉ đổi `ServiceUrl`, `AccessKey`, `SecretKey` (và `Region` nếu cần). Code không đổi.

## 4. Việc bạn phải tự làm

1. Cài / mở Docker Desktop, chạy `docker compose up -d`.
2. Chạy `powershell scripts/set-dev-secrets.ps1 -UseMinio` (nếu muốn dev bằng MinIO).
3. Khi deploy: tự sinh `MINIO_ROOT_PASSWORD`, `Storage__S3__SecretKey` (có thể dùng `scripts/new-secrets.*`), đặt vào `.env` (không commit).
4. Lên lịch backup volume MinIO.
5. Quyết định nơi đặt máy chủ nếu yêu cầu dữ liệu lưu trong nước (câu hỏi Q1 của plan M09).

## 5. Kiểm chứng

- `FileStorageTests` (16 test): chạy mặc định bằng Local, không cần MinIO — kiểm loại file, quyền, hạn mức, 503 khi MinIO không chạy, đổi Provider không mất file cũ, khởi động thất bại khi thiếu khóa.
- `MinioStorageTests` (2 test): chạy với **MinIO thật** trong container tạm — upload, tải, liên kết ký, bucket private, xóa, tự tạo bucket, health. Image lấy từ biến `MINIO_TEST_IMAGE` (mặc định `quay.io/minio/minio:latest`, image đã có sẵn trên máy dev).

## 6. Cảnh báo: nguồn image MinIO (kiểm 16/11/2026)

MinIO đã ngừng phát hành image công khai: `docker pull minio/minio` báo *repository does not exist*, `quay.io/minio/minio` báo *unauthorized*. Máy này vẫn chạy được vì image `quay.io/minio/minio:latest` (tạo 07/09/2025) đã nằm sẵn trong Docker. Hệ quả:

- **Máy mới / VPS mới không `docker compose up` được MinIO** nếu chưa có image. Cách xử lý: `docker save quay.io/minio/minio:latest | gzip > minio.tar.gz` trên máy đang có, chép sang máy mới rồi `docker load`; hoặc đẩy image đó lên registry riêng (GitHub Container Registry) và đặt `MINIO_IMAGE=ghcr.io/<bạn>/minio:<tag>`.
- Image cũ không nhận bản vá bảo mật mới. Với kho chứa ảnh CCCD, hãy cân nhắc kỹ trước khi đặt production lên image này.
- Code M09 nói chuyện bằng giao thức **S3 chuẩn**, nên đổi sang kho khác (RustFS, SeaweedFS, Garage — có image công khai — hoặc object storage thuê của nhà cung cấp) chỉ cần đổi `ServiceUrl` / `AccessKey` / `SecretKey`. Chưa kiểm thử với các kho đó.

## 7. Giới hạn hiện tại

- **Chưa mở upload cho hợp đồng** (scan HĐ, phụ lục, biên bản bàn giao) — tạm hoãn theo quyết định 16/11/2026.
- Xóa tệp là xóa mềm; job `AttachmentPurgeService` (mỗi ngày) xóa file vật lý sau `Storage:PurgeAfterDays` ngày và trả dung lượng hạn mức. Kho lỗi lúc dọn ⇒ bỏ qua, ngày sau thử lại. Ẩn danh người thuê thì xóa ngay.
- Chỉ bỏ EXIF cho ảnh JPEG; PNG / WEBP / HEIC giữ nguyên.
