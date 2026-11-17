# Ảnh và tài liệu đính kèm (M09)

Tải lên / xem / xóa ảnh và PDF gắn với một đối tượng: ảnh CCCD, ảnh chân dung (người thuê), ảnh phòng, giấy tờ của khu, ảnh công tơ,
chứng từ chuyển khoản. File lưu trên ổ đĩa máy chủ hoặc MinIO/S3 — UI **không cần biết** nơi nào, chỉ gọi các endpoint dưới đây.
Cách cấu hình: [file-storage.md](../guides/file-storage.md). Thiết kế: [09-file-storage.md](../plans/09-file-storage.md).

> **Chưa mở cho hợp đồng** (scan HĐ, phụ lục, biên bản bàn giao): `ownerType=Contract` ⇒ 422 `CATEGORY_NOT_ALLOWED`.

## Loại tài liệu theo đối tượng

| `ownerType` | `category` cho phép | Nhạy cảm | Ghi chú |
|-------------|---------------------|----------|---------|
| `Renter` | `IdCardFront`, `IdCardBack`, `Portrait`, `Other` | `IdCard*`, `Portrait` ✔ | `IdCardFront` / `IdCardBack` / `Portrait`: **chỉ giữ 1 tệp** — tải lại sẽ thay tệp cũ |
| `Room` | `RoomPhoto`, `Other` | — | tối đa 20 ảnh `RoomPhoto` |
| `Property` | `AuthorizationDoc`, `LessorIdCard`, `ComplianceDoc`, `Other` | `AuthorizationDoc`, `LessorIdCard` ✔ | `LessorIdCard` chỉ giữ 1 tệp |
| `MeterReading` | `MeterPhoto`, `Other` | — | tối đa 3 ảnh; **không xóa được** |
| `Payment` | `PaymentProof`, `Other` | — | tối đa 5 tệp; **không xóa được** |

Loại tệp nhận: JPEG, PNG, WEBP, HEIC, PDF (kiểm bằng nội dung thật, không tin đuôi file). Tối đa **10 MB / tệp**, **20 tệp / lần**.
Ảnh JPEG được xóa metadata EXIF (tọa độ GPS) khi lưu.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Khu "Tài liệu / Ảnh" trong chi tiết người thuê, phòng, khu, thanh toán | `GET /attachments?ownerType=&ownerId=` |
| Nút "Tải lên" | `POST /attachments` (multipart) |
| Xem ảnh (thumbnail, lightbox) | `GET /attachments/{id}/content?inline=true` — **cần header `Authorization`**, nên tải bằng `fetch` rồi dùng `URL.createObjectURL(blob)`; không đặt thẳng vào `<img src>` |
| Tải về | `GET /attachments/{id}/content` |
| Xóa | `DELETE /attachments/{id}` |

## Endpoint

### POST `/attachments` — tải lên

`Content-Type: multipart/form-data`. **Không** gửi `Idempotency-Key` (upload không idempotent; bấm hai lần ⇒ hai tệp, hoặc thay tệp với loại chỉ giữ 1).

| Field | Kiểu | Ghi chú |
|-------|------|---------|
| `ownerType` | text | `Renter` · `Room` · `Property` · `MeterReading` · `Payment` |
| `ownerId` | text (GUID) | |
| `category` | text | xem bảng trên |
| `description` | text, tùy chọn | ≤ 300 ký tự |
| `files` | file, lặp lại cho nhiều tệp | 1–20 tệp |

`201`:

```json
{
  "items": [
    {
      "id": "0192f0c1-7c2e-7a53-9a0e-3d5f6a1b2c3d",
      "ownerType": "Renter",
      "ownerId": "0192f0c1-1111-7a53-9a0e-3d5f6a1b2c3d",
      "category": "IdCardFront",
      "fileName": "cccd-truoc.jpg",
      "contentType": "image/jpeg",
      "sizeBytes": 482113,
      "isSensitive": true,
      "description": null,
      "uploadedAt": "2026-11-16T09:30:12+00:00"
    }
  ]
}
```

Lỗi: `400 NO_FILE` · `400 TOO_MANY_FILES` · `400 FILE_TYPE_NOT_ALLOWED` · `400 FILE_TOO_LARGE` · `404 OWNER_NOT_FOUND` ·
`422 CATEGORY_NOT_ALLOWED` · `422 TOO_MANY_ATTACHMENTS` · `507 STORAGE_QUOTA_EXCEEDED` · `503 STORAGE_UNAVAILABLE`.
Một tệp trong lô bị từ chối ⇒ **cả lô không lưu**.

### GET `/attachments?ownerType=&ownerId=` — danh sách

`200 { "items": [ … ] }` — chỉ tệp chưa xóa, cũ trước. Đối tượng không tồn tại / thuộc tổ chức khác ⇒ `404 OWNER_NOT_FOUND`.

### GET `/attachments/{id}/content` — tải nội dung

Trả nội dung tệp (stream). Mặc định `Content-Disposition: attachment`. `?inline=true` ⇒ hiển thị trực tiếp, **chỉ với ảnh không nhạy cảm**
(tệp nhạy cảm luôn là `attachment`). Tệp nhạy cảm: mỗi lần tải ghi nhật ký kiểm toán `DownloadAttachment`.
Lỗi: `404 ATTACHMENT_NOT_FOUND` · `503 STORAGE_UNAVAILABLE`.

### GET `/attachments/{id}/signed-url` — liên kết tạm 5 phút

`200 { "url": "http://…", "expiresAt": "…" }` — chỉ tệp **không nhạy cảm** đang lưu trên MinIO/S3. Còn lại ⇒ `422 SIGNED_URL_NOT_SUPPORTED`: dùng `/content`.
UI mặc định dùng `/content`; liên kết tạm chỉ cần khi muốn mở ảnh trong thẻ `<img>` không qua `fetch`.

### DELETE `/attachments/{id}` — xóa

`204`. Xóa mềm: file vật lý được job dọn sau `Storage:PurgeAfterDays` ngày (mặc định 30), lúc đó mới trả dung lượng hạn mức. Ảnh công tơ và chứng từ thanh toán ⇒ `422 ATTACHMENT_LOCKED`.
