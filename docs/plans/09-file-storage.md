# M09 — File Storage (Ảnh & Tài liệu đính kèm)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L2 (lưu bản HĐ), L11 (dữ liệu cá nhân), LEG-06.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Cho phép upload/tải **ảnh và tài liệu** gắn với các đối tượng nghiệp vụ nhằm **lưu trữ thông tin**
(scan HĐ, ảnh CCCD, xác nhận tạm trú, ảnh công tơ, biên bản bàn giao, ảnh phòng, chứng từ chuyển khoản) — an toàn, có kiểm soát truy cập.

**Ngoài phạm vi**: chỉnh sửa/ký tài liệu, OCR (P3), quét virus nâng cao (P2: ClamAV).

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Tệp đính kèm | `Attachment` | Metadata 1 file + vị trí lưu |
| Chủ sở hữu | `OwnerType` + `OwnerId` | `Property`, `Room`, `Renter`, `Contract`, `ResidenceRecord` (P2), `MeterReading`, `Invoice`, `Payment`, `DepositTransaction`, `ContractAsset` |
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
| FS-BR-08 | Xóa: soft delete (`deleted_at`), file vật lý xóa bởi job sau 30 ngày. File gắn với đối tượng tài chính đã chốt (Invoice Finalized, Payment) **không** cho xóa | Application |
| FS-BR-09 | Ẩn danh hóa renter (M03) → xóa ngay (hard delete) file nhạy cảm của renter đó | Handler `RenterAnonymized` |
| FS-BR-10 | Ảnh được xóa metadata EXIF (GPS) khi lưu; tạo thumbnail 320px cho ảnh (P2) | Infrastructure |
| FS-BR-11 | Lưu `sha256` để phát hiện trùng trong cùng owner (cảnh báo) và kiểm tra toàn vẹn khi tải | Infrastructure |

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
  - LocalFileStorage (dev/single server, thư mục ngoài wwwroot)
  - S3FileStorage (MinIO/AWS/Viettel/VNPT object storage — chọn nhà cung cấp lưu trữ tại VN nếu yêu cầu dữ liệu trong nước)
IFileTypeInspector: Detect(stream) — magic bytes
IAttachmentOwnerResolver: ExistsAsync(ownerType, ownerId) — mỗi module đăng ký 1 hiện thực
```

## 6–7. Application & API

| Method | Route | Mô tả | Mã lỗi |
|--------|-------|-------|--------|
| POST | `/attachments` (multipart: `ownerType`, `ownerId`, `category`, `files[]`, `description?`) | Upload, stream thẳng xuống storage (không buffer toàn bộ vào RAM), giới hạn `RequestSizeLimit` | 400 `FILE_TYPE_NOT_ALLOWED`, `FILE_TOO_LARGE`, 404 `OWNER_NOT_FOUND`, 422 `CATEGORY_NOT_ALLOWED`, 507 `STORAGE_QUOTA_EXCEEDED` |
| GET | `/attachments?ownerType=&ownerId=` | Danh sách metadata | |
| GET | `/attachments/{id}/content` | Tải (stream) — `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff` | 404 |
| GET | `/attachments/{id}/signed-url` | URL ký 5 phút (S3) | |
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

## 13–14. Phụ thuộc & Task
Dùng: M01. Bị dùng: M02, M03, M05, M06, M07, M08.

| ID | Task | Ước lượng |
|----|------|-----------|
| FS-01 | Entity + EF + quota | 0.5d |
| FS-02 | `IFileStorage` Local + S3 (MinIO trong docker-compose dev) | 1d |
| FS-03 | Inspector magic bytes + EXIF strip | 0.5d |
| FS-04 | Endpoints upload/download/delete + owner resolvers | 1d |
| FS-05 | Tests | 1d |

## 15. Câu hỏi mở
| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Dữ liệu phải lưu trữ tại Việt Nam? | Rà soát với Luật BVDLCN/NĐ 356; mặc định chọn object storage đặt tại VN |
| Q2 | Có nén ảnh? | P2: resize ảnh > 2560px |
