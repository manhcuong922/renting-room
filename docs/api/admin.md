# Quản trị nền tảng (SystemAdmin)

Admin **chỉ quản lý tài khoản**: tạo tổ chức chủ trọ, tạm ngưng, cấp lại mật khẩu, khóa phó quản lý.
Admin **không** thấy khu trọ / phòng / người thuê / hợp đồng (API trả 403). Đừng làm menu đó cho admin.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Danh sách tổ chức (tìm kiếm, lọc trạng thái) | `GET /admin/organizations` |
| Tạo tổ chức + tài khoản chủ trọ | `POST /admin/organizations` |
| Chi tiết tổ chức (thông tin + danh sách tài khoản) | `GET /admin/organizations/{id}` |
| Hộp thoại tạm ngưng / kích hoạt lại | `POST …/{id}/suspend`, `POST …/{id}/reactivate` |
| Nút trên từng tài khoản: Cấp lại mật khẩu / Khóa / Mở khóa | `POST /admin/users/{userId}/reset-password` · `/lock` · `/unlock` |

## Danh sách tổ chức

`GET /admin/organizations?search=&status=Active&page=1&pageSize=20`

| Query | Ý nghĩa |
|-------|---------|
| `search` | Tìm theo mã / tên (≤ 100 ký tự) |
| `status` | `Active` \| `Suspended` (bỏ trống = tất cả) |

```json
{
  "items": [
    {
      "id": "e22cfc70-ced8-4d0f-8939-9ebe33f43246",
      "code": "MAU-9080",
      "name": "Nhà trọ Mẫu",
      "status": "Active",
      "contactName": null,
      "contactPhone": null,
      "createdAt": "2026-10-02T13:19:04.940602+00:00"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 7, "totalPages": 1
}
```

Cột gợi ý: Mã · Tên · Liên hệ · Trạng thái (badge xanh/xám) · Ngày tạo.

## Tạo tổ chức

`POST /admin/organizations` — **bắt buộc `Idempotency-Key`**

```json
{
  "code": "MAU-9080",
  "name": "Nhà trọ Mẫu",
  "contactName": "Nguyễn Văn Chủ",
  "contactPhone": "0946781528",
  "contactEmail": null,
  "taxCode": null,
  "address": "Số 1 Láng Hạ, Hà Nội",
  "note": null,
  "owner": { "fullName": "Chủ Mẫu", "phone": "0946781528", "email": null }
}
```

| Trường | Bắt buộc | Quy tắc |
|--------|:-------:|---------|
| `code` | ✅ | 3–32 ký tự chữ, số, `-`; duy nhất toàn hệ thống |
| `name` | ✅ | ≤ 200 |
| `contactPhone` / `contactEmail` | | SĐT di động VN / email hợp lệ |
| `taxCode` | | 10 số hoặc `10 số-3 số` |
| `note` | | ≤ 2000 |
| `owner.fullName` | ✅ | ≤ 200 |
| `owner.phone` / `owner.email` | **ít nhất 1** | Là tên đăng nhập của chủ trọ; duy nhất toàn hệ thống |

**201**

```json
{
  "organizationId": "e22cfc70-ced8-4d0f-8939-9ebe33f43246",
  "ownerUserId": "33c8ef92-18d4-41b9-9810-b05ed908c59d",
  "username": "0946781528",
  "temporaryPassword": "66V64DMMRSxg"
}
```

> ⚠ **Mật khẩu tạm chỉ hiện đúng lần này** (không xem lại được). UI: hộp thoại hiện `username` + `temporaryPassword`, nút "Sao chép",
> ghi chú "Hết hạn sau 72 giờ, chủ trọ phải đổi ở lần đăng nhập đầu". Hệ thống **không gửi email/SMS** — admin tự gửi cho chủ trọ.
> Gửi lại cùng `Idempotency-Key` (mất mạng) sẽ nhận lại đúng mật khẩu đó.

| Lỗi | UI |
|-----|----|
| 409 `ORG_CODE_TAKEN` | Lỗi dưới ô Mã |
| 409 `PHONE_TAKEN` / `EMAIL_TAKEN` | Lỗi dưới ô SĐT / email chủ trọ |
| 400 `VALIDATION_FAILED` — `errors["owner.phone"]` mã `USERNAME_REQUIRED` | "Chủ trọ phải có SĐT hoặc email" |

## Chi tiết tổ chức

`GET /admin/organizations/{id}`

```json
{
  "id": "e22cfc70-ced8-4d0f-8939-9ebe33f43246",
  "code": "MAU-9080",
  "name": "Nhà trọ Mẫu",
  "status": "Active",
  "suspendedReason": null,
  "contactName": null, "contactPhone": null, "contactEmail": null,
  "taxCode": null, "note": null,
  "createdAt": "2026-10-02T13:19:04.940602+00:00",
  "version": "930",
  "users": [
    {
      "id": "33c8ef92-18d4-41b9-9810-b05ed908c59d",
      "fullName": "Chủ Mẫu",
      "phone": "0946781528",
      "email": null,
      "role": "OrgOwner",
      "status": "Active",
      "mustChangePassword": true,
      "lastLoginAt": null
    }
  ]
}
```

Bố cục gợi ý: thẻ thông tin tổ chức + banner đỏ nếu `status = Suspended` (hiện `suspendedReason`) + bảng tài khoản.
Trong bảng: `mustChangePassword = true` → nhãn "Chưa đổi mật khẩu tạm"; `lastLoginAt = null` → "Chưa đăng nhập".

## Tạm ngưng / kích hoạt lại

| Endpoint | Body | Kết quả |
|----------|------|---------|
| `POST /admin/organizations/{id}/suspend` | `{ "reason": "Hết hạn hợp đồng dịch vụ" }` (bắt buộc, ≤ 500) | 204 — **mọi tài khoản trong tổ chức bị đăng xuất ngay**, không đăng nhập được |
| `POST /admin/organizations/{id}/reactivate` | — | 204 |

Lỗi: 409 `ORG_ALREADY_SUSPENDED`, 409 `ORG_NOT_SUSPENDED`, 404 `ORGANIZATION_NOT_FOUND`.
UI: hộp thoại xác nhận có ô lý do; nút đổi theo `status`.

## Thao tác trên tài khoản

| Nút | Endpoint | Hiện khi | Kết quả |
|-----|----------|----------|---------|
| Cấp lại mật khẩu | `POST /admin/users/{id}/reset-password` | Mọi tài khoản chưa gỡ | 200 `{ userId, username, temporaryPassword, expiresAt }` — hiện 1 lần như khi tạo; phiên cũ bị đăng xuất |
| Khóa | `POST /admin/users/{id}/lock` | `role = OrgManager`, `status = Active` | 204 |
| Mở khóa | `POST /admin/users/{id}/unlock` | `status = Locked` | 204 |

- Chủ trọ **không khóa được** (422 `CANNOT_LOCK_OWNER`) → dùng tạm ngưng tổ chức. Ẩn nút Khóa trên dòng chủ trọ.
- 409 `USER_ALREADY_LOCKED` / `USER_NOT_LOCKED` / `USER_REMOVED` → tải lại danh sách.
