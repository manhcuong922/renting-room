# M09 — File Storage (Ảnh & Tài liệu đính kèm)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L2 (lưu bản HĐ), L11 (dữ liệu cá nhân), LEG-06.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Cho phép upload/tải **ảnh và tài liệu** gắn với các đối tượng nghiệp vụ nhằm **lưu trữ thông tin**
(scan HĐ, ảnh CCCD, xác nhận tạm trú, ảnh công tơ, biên bản bàn giao, ảnh phòng, chứng từ chuyển khoản) — an toàn, có kiểm soát truy cập.

**Ngoài phạm vi**: chỉnh sửa/ký tài liệu, OCR (P3), quét virus nâng cao (P2: ClamAV).

**Phase**: P1.

**Trạng thái (16/11/2026)**: ✅ **đã code nền tảng + gắn vào người thuê, phòng, khu** (BE + UI; 16 test FileStorage + 2 test MinIO thật + e2e trình duyệt). **Chưa làm**: upload cho HĐ (⏸ hoãn), thumbnail / nén ảnh (P2). Trước đó, các module chờ M09: ảnh giấy tờ người thuê (M03),
file scan HĐ / giấy ủy quyền / ảnh tài sản (M02, M05 — tạm dùng cờ "đã có bản ký", CT-UC-23), ảnh công tơ (M06), ảnh phòng (M02), chứng từ chuyển khoản (M08).

**Cập nhật 16/11/2026 — chốt thiết kế triển khai**: dùng được cả **Local** (mặc định, không cần cài gì) và **MinIO/S3** (Docker) chọn bằng cấu hình;
app chạy bình thường khi không có MinIO (FS-BR-12…17). Làm nền tảng M09 trước (FS-01…FS-05), sau đó gắn upload vào các module đang chờ (§3.1, task FS-06…FS-10).

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Tệp đính kèm | `Attachment` | Metadata 1 file + vị trí lưu |
| Chủ sở hữu | `OwnerType` + `OwnerId` | `Property`, `Room`, `Renter`, `Contract`, `ResidenceRecord` (P2), `MeterReading`, `Invoice`, `Payment`, `ContractAsset` |
| Danh mục | `Category` | `ContractScan`, `ContractAddendum`, `HandoverMinutes`, `IdCardFront`, `IdCardBack`, `Portrait`, `ResidenceConfirmation`, `MeterPhoto`, `RoomPhoto`, `PaymentProof`, `ComplianceDoc`, `AuthorizationDoc` (giấy ủy quyền cho thuê — M02 PR-BR-13), `LessorIdCard` (giấy tờ bên cho thuê), `AssetPhoto` (tài sản bàn giao — M05 CT-UC-13), `Other` |
| Nhạy cảm | `IsSensitive` | Suy từ category (IdCard*, LessorIdCard, Portrait, ResidenceConfirmation, ContractScan, AuthorizationDoc) |

## 3. Nghiệp vụ — quy tắc

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| FS-BR-01 | Loại file cho phép: JPEG, PNG, WEBP, HEIC, PDF. Kiểm tra **magic bytes**, không tin `Content-Type`/đuôi file | Infrastructure |
| FS-BR-02 | Kích thước ≤ 10 MB/file; ≤ 20 file/lần; hạn mức tổng mỗi tổ chức (mặc định 5 GB, cấu hình) | Application |
| FS-BR-03 | Category hợp lệ theo OwnerType (VD `IdCardFront` chỉ cho `Renter`; `MeterPhoto` chỉ cho `MeterReading`) | Domain (bảng ánh xạ) |
| FS-BR-04 | Owner phải tồn tại **trong cùng tổ chức** tại thời điểm upload (kiểm qua `IAttachmentOwnerResolver` mỗi OwnerType) | Application |
| FS-BR-05 | Tên lưu trữ = `{orgId}/{yyyy}/{MM}/{attachmentId}` — **không** dùng tên file người dùng (chống path traversal); tên gốc chỉ lưu metadata (đã làm sạch, ≤ 255) | Infrastructure |
| FS-BR-06 | Tải file **chỉ qua API** có xác thực + kiểm tra tổ chức, hoặc URL ký có hạn ≤ 5 phút (S3 presigned). Không có URL public | API |
| FS-BR-07 | File nhạy cảm: mỗi lần tải/xem ghi audit (C-10) | Application |
| FS-BR-08 | Xóa: soft delete (`deleted_at`), file vật lý xóa bởi job `AttachmentPurgeService` (mỗi ngày) khi `deleted_at` quá `Storage:PurgeAfterDays` (mặc định 30, 1–3650): xóa file ở kho **trước**, rồi mới đặt `purged_at` + trả `used_bytes` (kho lỗi ⇒ bỏ qua tệp đó, ngày sau thử lại; tệp đã `purged_at` không bị xử lý lại). Dung lượng hạn mức chỉ được trả khi file vật lý đã xóa. Ẩn danh thì xóa ngay (FS-BR-09). **P1 giản lược 16/11/2026**: `PaymentProof` và `MeterPhoto` **không cho xóa** (422 `ATTACHMENT_LOCKED`) — không dò trạng thái chốt từng chỉ số; mở khóa theo trạng thái là việc sau | Application |
| FS-BR-09 | Ẩn danh hóa renter (M03) → xóa ngay (hard delete) file nhạy cảm của renter đó | Handler `RenterAnonymized` |
| FS-BR-10 | Ảnh được xóa metadata EXIF (GPS) khi lưu; tạo thumbnail 320px cho ảnh (P2) | Infrastructure |
| FS-BR-11 | Lưu `sha256` để phát hiện trùng trong cùng owner (cảnh báo) và kiểm tra toàn vẹn khi tải | Infrastructure |
| FS-BR-12 | **Chạy được không cần MinIO**: `Storage:Provider` mặc định `Local` (thư mục `Storage:Local:RootPath`, ngoài wwwroot). Chỉ khi đặt `Provider=S3` mới dùng MinIO/S3. App **không** kiểm tra kết nối MinIO lúc khởi động | Infrastructure |
| FS-BR-13 | `Provider=S3` mà thiếu `ServiceUrl`/`AccessKey`/`SecretKey`/`Bucket` ⇒ app **không khởi động** (lỗi cấu hình rõ ràng). Không tự chuyển về Local (tránh file nằm lẫn 2 nơi mà không ai biết) | Infrastructure |
| FS-BR-14 | MinIO sập/không kết nối được lúc chạy ⇒ upload/tải trả 503 `STORAGE_UNAVAILABLE`; các API khác vẫn chạy bình thường. `/health` báo storage `Degraded` (không làm app `Unhealthy`) | Api |
| FS-BR-15 | Mỗi attachment lưu `storage_provider` lúc upload; tải/xóa dùng đúng provider đã lưu ⇒ đổi cấu hình Local → S3 không làm mất file cũ. Local luôn đăng ký; S3 đăng ký khi đã cấu hình | Application |
| FS-BR-16 | `signed-url` với file Local ⇒ 422 `SIGNED_URL_NOT_SUPPORTED` (client dùng `/content`). File nhạy cảm **không bao giờ** cấp signed-url, kể cả S3 | Application |
| FS-BR-17 | Triển khai: MinIO chạy bằng Docker (compose), bucket private, volume riêng, mật khẩu lấy từ biến môi trường (compose prod không có mật khẩu mặc định). Backup bucket là bắt buộc | Ops |

### 3.1 Ánh xạ OwnerType → Category (FS-BR-03) và module dùng

| OwnerType | Category cho phép | Module | Ghi chú |
|-----------|-------------------|--------|---------|
| `Renter` | `IdCardFront`, `IdCardBack`, `Portrait` | M03 (RT-UC-01) | Nhạy cảm; mỗi category tối đa 1 file đang hiệu lực (upload mới thay cái cũ — cái cũ soft delete) |
| `Room` | `RoomPhoto` | M02 (Q3) | Không nhạy cảm; ≤ 20 ảnh/phòng |
| `Property` | `AuthorizationDoc`, `LessorIdCard`, `ComplianceDoc` | M02 (PR-BR-13) | `AuthorizationDoc`, `LessorIdCard` nhạy cảm |
| `Contract` | ⏸ **tạm hoãn (16/11/2026, chủ trọ chốt)**: `ContractScan`, `ContractAddendum`, `HandoverMinutes` | M05 (CT-UC-11) | Chưa mở upload cho HĐ — M05 giữ cờ "đã có bản ký" (CT-UC-23). Khi làm: upload `ContractScan` ⇒ tự đặt cờ; xóa scan cuối ⇒ bỏ cờ (trừ khi cờ đặt tay). Enum vẫn có sẵn giá trị để không phải đổi schema |
| `ContractAsset` | `AssetPhoto` | M05 (CT-UC-13) | |
| `MeterReading` | `MeterPhoto` | M06 (Q3) | ≤ 3 ảnh/chỉ số; chỉ số đã khóa theo phiếu chốt ⇒ không xóa ảnh (FS-BR-08) |
| `Payment` | `PaymentProof` | M08 | Không cho xóa (FS-BR-08) |
| mọi OwnerType | `Other` | | |

Category không thuộc OwnerType ⇒ 422 `CATEGORY_NOT_ALLOWED`. `Invoice` và `ResidenceRecord` (P2) chưa mở upload ở P1.

## 4. Dữ liệu

**`attachments`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | |
| owner_type | varchar(24) | N | |
| owner_id | uuid | N | (đa hình — không FK được, xem §10) |
| category | varchar(24) | N | |
| original_file_name | varchar(255) | N | đã làm sạch |
| content_type | varchar(100) | N | theo magic bytes |
| size_bytes | bigint | N | CHECK 0 < x ≤ 10485760 |
| storage_provider | varchar(16) | N | `Local`,`S3` |
| storage_key | varchar(300) | N | UNIQUE |
| sha256 | char(64) | N | |
| is_sensitive | bool | N | |
| description | varchar(300) | Y | |
| uploaded_at / uploaded_by | | N | |
| deleted_at / deleted_by | | Y | |

INDEX `(organization_id, owner_type, owner_id) WHERE deleted_at IS NULL`.
**`organization_storage_usage`**: organization_id PK, used_bytes bigint (cập nhật trong transaction khi upload/xóa; job đối soát).

## 5. Domain / Infrastructure

```
Attachment (aggregate root): Create(...), SoftDelete(now), IsSensitive từ Category
IFileStorage: SaveAsync(key, stream, contentType), OpenReadAsync(key), DeleteAsync(key), GetSignedUrlAsync(key, ttl)
  - LocalFileStorage (mặc định; dev/single server, thư mục `Storage:Local:RootPath` ngoài wwwroot; khi chạy Docker phải mount volume)
  - S3FileStorage (MinIO/AWS/Viettel/VNPT object storage — chọn nhà cung cấp lưu trữ tại VN nếu yêu cầu dữ liệu trong nước; AWSSDK.S3, `ForcePathStyle=true` cho MinIO)
IFileStorageResolver: Resolve(storageProvider) — chọn Local/S3 theo cột attachments.storage_provider (FS-BR-15); upload mới dùng `Storage:Provider`

Cấu hình: `Storage:Provider` (`Local` mặc định | `S3`), `Storage:Local:RootPath`, `Storage:S3:{ServiceUrl,Bucket,AccessKey,SecretKey,ForcePathStyle,SignedUrlTtlSeconds}`,
`Storage:OrganizationQuotaBytes` (mặc định 5 GB). Secret chỉ qua biến môi trường / user-secrets (`Storage__S3__SecretKey`).
IFileTypeInspector: Detect(stream) — magic bytes
IAttachmentOwnerResolver: ExistsAsync(ownerType, ownerId) — mỗi module đăng ký 1 hiện thực
```

## 6–7. Application & API

| Method | Route | Mô tả | Mã lỗi |
|--------|-------|-------|--------|
| POST | `/attachments` (multipart: `ownerType`, `ownerId`, `category`, `files[]`, `description?`) | Upload, stream thẳng xuống storage (không buffer toàn bộ vào RAM), giới hạn `RequestSizeLimit` | 400 `FILE_TYPE_NOT_ALLOWED`, `FILE_TOO_LARGE`, 404 `OWNER_NOT_FOUND`, 422 `CATEGORY_NOT_ALLOWED`, 507 `STORAGE_QUOTA_EXCEEDED`, 503 `STORAGE_UNAVAILABLE` |
| GET | `/attachments?ownerType=&ownerId=` | Danh sách metadata | |
| GET | `/attachments/{id}/content` | Tải (stream) — `Content-Disposition: attachment` (ảnh không nhạy cảm cho `?inline=true`), `X-Content-Type-Options: nosniff` | 404, 503 `STORAGE_UNAVAILABLE` |
| GET | `/attachments/{id}/signed-url` | URL ký 5 phút (chỉ S3, file không nhạy cảm) | 422 `SIGNED_URL_NOT_SUPPORTED` (Local hoặc file nhạy cảm) |
| DELETE | `/attachments/{id}` | Soft delete | 422 `ATTACHMENT_LOCKED` |

## 8. Validation
ownerType/category enum + ánh xạ FS-BR-03; description ≤ 300; tên file làm sạch (bỏ ký tự điều khiển, `/ \ : * ? " < > |`).

## 9. Phân quyền
Theo quyền trên owner (P1: chủ trọ và phó quản lý toàn quyền nghiệp vụ (M01 §3.3)). File nhạy cảm (ảnh giấy tờ): dữ liệu nhạy cảm (số giấy tờ đầy đủ) chỉ chủ trọ hoặc phó quản lý được chủ trọ cấp quyền (ID-BR-22).

## 10. Toàn vẹn dữ liệu

| Kịch bản | Giải pháp |
|----------|-----------|
| `owner_id` đa hình không có FK → bản ghi mồ côi / trỏ sang org khác | FS-BR-04 kiểm tra khi upload; owner trong hệ thống **không bị xóa vật lý** (C-06) nên không mồ côi; test C-01 cho mỗi OwnerType |
| Upload ghi file xong nhưng lưu DB lỗi | Ghi file trước với key mới → lưu DB; lỗi DB → xóa file (best effort) + job dọn file không có metadata (P2) |
| Lưu DB xong nhưng file lỗi | Thứ tự trên đảm bảo không có metadata trỏ file không tồn tại |
| Vượt hạn mức khi upload song song | `UPDATE organization_storage_usage SET used_bytes = used_bytes + @n WHERE used_bytes + @n <= quota` (nguyên tử) |

## 11. Bảo mật
- Không phục vụ file qua static files middleware. Không render HTML/SVG (không nằm trong whitelist).
- Mã hóa at-rest: S3 SSE hoặc mã hóa đĩa; backup có mã hóa.

## 12. Test
- Upload file `.jpg` thực chất là `.exe` → 400; vượt 10MB → 400/413; owner thuộc org khác → 404; tải file nhạy cảm sinh audit; ẩn danh renter xóa file; quota song song.
- **Kịch bản cụ thể (16/11/2026)**:
  1. Không cấu hình `Storage` ⇒ app khởi động, Provider = Local; upload ảnh JPEG 1 MB cho `Renter` category `IdCardFront` ⇒ 201, file nằm tại `{RootPath}/{orgId}/2026/11/{id}`, `storage_provider = Local`, `is_sensitive = true`.
  2. `Provider=S3` thiếu `SecretKey` ⇒ host không khởi động, thông báo nêu tên khóa thiếu (FS-BR-13).
  3. `Provider=S3` nhưng MinIO không chạy ⇒ app khởi động; `POST /attachments` ⇒ 503 `STORAGE_UNAVAILABLE`, không tạo bản ghi `attachments`, `used_bytes` không đổi; `GET /renters` vẫn 200; `/health` storage `Degraded`.
  4. File upload khi Provider=Local, sau đó đổi cấu hình sang S3 ⇒ tải file cũ vẫn 200 (FS-BR-15); file mới có `storage_provider = S3`.
  5. `signed-url` của file Local ⇒ 422 `SIGNED_URL_NOT_SUPPORTED`; file `IdCardFront` trên S3 ⇒ 422 cùng mã.
  6. `Renter` + category `RoomPhoto` ⇒ 422 `CATEGORY_NOT_ALLOWED`. Upload `IdCardFront` lần 2 cho cùng renter ⇒ bản cũ soft delete, còn đúng 1 file hiệu lực.
  7. Quota 5 MB, tổ chức đã dùng 4 MB, upload 2 MB ⇒ 507 `STORAGE_QUOTA_EXCEEDED`, `used_bytes` vẫn 4 MB; hai upload 3 MB song song trên quota 5 MB ⇒ đúng 1 thành công.
  8. Upload file `PaymentProof` rồi `DELETE` ⇒ 422 `ATTACHMENT_LOCKED`; `MeterPhoto` của chỉ số đã chốt vào phiếu ⇒ 422 `ATTACHMENT_LOCKED`.
  9. ⏸ (hoãn cùng FS-08) Upload cho `Contract` ⇒ 422 `CATEGORY_NOT_ALLOWED` / `OWNER_TYPE_NOT_SUPPORTED` cho tới khi mở.
  10. Ẩn danh renter ⇒ file `IdCardFront/IdCardBack/Portrait` bị xóa vật lý ngay, bản ghi `attachments` còn dấu xóa (FS-BR-09).
  12. (FS-11) Upload 2000 B rồi xóa (used_bytes = 2000). Đặt `deleted_at` = 31 ngày trước ⇒ chạy job ⇒ trả 1; file vật lý biến mất, `purged_at` có giá trị, `used_bytes` = 0. Chạy job lần 2 ⇒ trả 0, `used_bytes` vẫn 0.
  13. (FS-11) Xóa mềm cách đây 29 ngày ⇒ chạy job ⇒ trả 0; file vật lý còn, `used_bytes` không đổi. Tệp đang hiệu lực (chưa xóa) không bao giờ bị dọn dù upload từ lâu.
  14. (FS-11) Kho không dùng được (S3 cấu hình nhưng MinIO sập) ⇒ job không ném lỗi, trả 0, `purged_at` vẫn null, `used_bytes` không đổi.
  11. Tải `IdCardFront` ⇒ 1 dòng audit; tải `RoomPhoto` ⇒ không audit. Org B tải file của org A ⇒ 404.

## 13–14. Phụ thuộc & Task
Dùng: M01. Bị dùng: M02, M03, M05, M06, M07, M08.

| ID | Task | Ước lượng |
|----|------|-----------|
| FS-01 ✅ | Entity + EF + quota (migration `AddAttachments`) | 0.5d |
| FS-02 ✅ | `IFileStorage` Local + S3 (AWSSDK.S3, MinIO trong docker-compose dev; liên kết ký theo scheme của `ServiceUrl`) | 1d |
| FS-03 ✅ | Inspector magic bytes + EXIF strip (chỉ JPEG; PNG / WEBP / HEIC giữ nguyên) | 0.5d |
| FS-04 ✅ | Endpoints upload/download/delete + owner resolvers (Property, Room, Renter, MeterReading, Payment) | 1d |
| FS-05 ✅ | Tests (§12): `FileStorageTests` 16, `MinioStorageTests` 2 (MinIO thật), seeder demo, e2e UI | 1d |
| FS-06 ✅ | Docker (đã: MinIO trong compose dev, mẫu `.env`, `set-dev-secrets -UseMinio`, health check, hướng dẫn; compose production ✅ 16/11/2026 — [deploy.md](../guides/deploy.md), tài khoản MinIO riêng cho app): MinIO trong compose dev + compose prod (không mật khẩu mặc định), Dockerfile mount volume Local, CI, health check storage, tài liệu deploy (M12) | 0.5d |
| FS-07 ✅ | Gắn M03: ảnh CCCD mặt trước/sau, chân dung (RT-UC-01) + xóa khi ẩn danh (FS-BR-09) | 0.5d |
| FS-08 ⏸ | Gắn M05: scan HĐ / phụ lục / biên bản bàn giao / ảnh tài sản + cờ CT-UC-23 — **tạm hoãn** theo quyết định 16/11/2026 | 0.5d |
| FS-09 ✅ | Gắn M02 (ảnh phòng ✅, giấy ủy quyền / giấy tờ bên cho thuê ✅ ở tab Tài liệu của khu), M06 (ảnh công tơ — BE ✅ + UI ✅), M08 (chứng từ chuyển khoản — BE ✅ + UI ✅) | 1d |
| FS-11 ✅ | Job dọn file vật lý của tệp xóa mềm > 30 ngày + trả dung lượng hạn mức (FS-BR-08) | 0.5d |
| FS-10 ✅ | UI (repo `renting_room_ui`; đã làm người thuê / phòng / khu / chỉ số công tơ / phiếu thu, CSP cho `blob:`): component upload/xem file dùng chung + gắn vào các màn hình trên | 1.5d |

## 15. Câu hỏi mở
| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Dữ liệu phải lưu trữ tại Việt Nam? | Rà soát với Luật BVDLCN/NĐ 356; mặc định chọn object storage đặt tại VN |
| Q2 | Có nén ảnh? | P2: resize ảnh > 2560px |
