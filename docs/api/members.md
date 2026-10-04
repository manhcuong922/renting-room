# Thành viên: phó quản lý

Chủ trọ (`OrgOwner`) thêm **một hoặc nhiều phó quản lý** (`OrgManager`, mặc định tối đa 10). Phó quản lý thao tác nghiệp vụ
**y như chủ trọ** (khu, phòng, người thuê, hợp đồng) nhưng **không** quản lý thành viên.

| Thao tác | OrgOwner | OrgManager |
|----------|:--------:|:----------:|
| Xem danh sách / chi tiết thành viên | ✅ | ✅ (chỉ xem) |
| Thêm, sửa, khóa, mở khóa, gỡ, cấp lại mật khẩu | ✅ | ❌ 403 — **ẩn nút** |
| Cấp / thu hồi quyền xem dữ liệu nhạy cảm | ✅ | ❌ 403 — **ẩn nút** |
| Xem / xuất / in số giấy tờ đầy đủ | ✅ | Chỉ khi được cấp (`canViewSensitiveData`) |

## Màn hình "Thành viên"

- Bảng: Họ tên · SĐT / email · Vai trò · Trạng thái · Xem số giấy tờ (công tắc, chỉ chủ trọ bật / tắt) · Lần đăng nhập cuối · Thao tác.
- Nút **"Thêm phó quản lý"** (chỉ chủ trọ).
- Công tắc "Hiện cả người đã gỡ" → `includeRemoved=true`.
- Dòng chủ trọ: không có nút thao tác.

## Endpoint

| Method | URL | Quyền | Mô tả |
|--------|-----|-------|-------|
| GET | `/org/members?includeRemoved=false` | Thành viên | Danh sách (mảng, không phân trang) |
| GET | `/org/members/{id}` | Thành viên | Chi tiết |
| POST | `/org/members` | Chủ trọ · **Idempotency-Key** | Thêm phó quản lý → 201 + mật khẩu tạm |
| PUT | `/org/members/{id}` | Chủ trọ | Sửa họ tên / SĐT / email |
| POST | `/org/members/{id}/lock` | Chủ trọ | Khóa — đăng xuất ngay mọi phiên |
| POST | `/org/members/{id}/unlock` | Chủ trọ | Mở khóa |
| POST | `/org/members/{id}/remove` | Chủ trọ | Gỡ vĩnh viễn (không khôi phục) |
| POST | `/org/members/{id}/reset-password` | Chủ trọ | Cấp lại mật khẩu tạm |
| PUT | `/org/members/{id}/sensitive-data-access` | Chủ trọ | `{ "allowed": true }` cấp / `false` thu hồi quyền xem số giấy tờ đầy đủ → 200 thành viên |

### Danh sách

```json
[
  {
    "id": "8ceb6012-7907-4b2b-a65a-e67a9d435e13",
    "fullName": "Phó Mẫu",
    "phone": "0954191081",
    "email": null,
    "role": "OrgManager",
    "status": "Active",
    "mustChangePassword": true,
    "tempPasswordExpiresAt": "2026-10-05T13:19:05.678591+00:00",
    "lastLoginAt": null,
    "createdAt": "2026-10-02T13:19:05.754001+00:00",
    "canViewSensitiveData": false,
    "version": "938"
  }
]
```

Nhãn phụ gợi ý:
- `mustChangePassword && tempPasswordExpiresAt > now` → "Chờ kích hoạt (hết hạn dd/MM HH:mm)"
- `mustChangePassword && tempPasswordExpiresAt <= now` → "Mật khẩu tạm đã hết hạn" + gợi ý bấm **Cấp lại mật khẩu**

### Quyền xem dữ liệu nhạy cảm

`PUT /org/members/{id}/sensitive-data-access` — `{ "allowed": true }` → **200** (thành viên sau khi đổi).

- Mặc định phó quản lý **không** có quyền: vẫn làm mọi nghiệp vụ, nhưng số giấy tờ luôn che, không xuất Excel / in hợp đồng có số đầy đủ.
- Thu hồi có hiệu lực **ngay** (không cần phó quản lý đăng nhập lại). Mỗi lần cấp / thu hồi được ghi log kiểm toán.
- Dòng chủ trọ luôn `canViewSensitiveData = true`, không có công tắc (gọi với id chủ trọ → 422 `CANNOT_MODIFY_OWNER`).

### Thêm phó quản lý

`POST /org/members`

```json
{ "fullName": "Phó Mẫu", "phone": "0954191081", "email": null }
```

| Trường | Quy tắc |
|--------|---------|
| `fullName` | Bắt buộc, ≤ 200 |
| `phone` / `email` | **Ít nhất 1** (là tên đăng nhập), duy nhất toàn hệ thống |

**201**

```json
{
  "userId": "8ceb6012-7907-4b2b-a65a-e67a9d435e13",
  "username": "0954191081",
  "temporaryPassword": "3YmCRfkjYCk3",
  "expiresAt": "2026-10-05T13:19:05.6785916+00:00"
}
```

Hiện hộp thoại một lần như admin tạo chủ trọ: tên đăng nhập + mật khẩu tạm + nút sao chép + "hết hạn lúc …".
Hệ thống không tự gửi — chủ trọ tự chuyển cho phó quản lý.

| Lỗi | UI |
|-----|----|
| 409 `PHONE_TAKEN` / `EMAIL_TAKEN` | Lỗi dưới ô tương ứng |
| 422 `MANAGER_LIMIT_REACHED` | "Đã đạt số phó quản lý tối đa" — vô hiệu nút Thêm |
| 400 `errors.phone` mã `USERNAME_REQUIRED` | "Cần SĐT hoặc email" |

### Sửa

`PUT /org/members/{id}`

```json
{ "fullName": "Phó Mẫu", "phone": "0954191081", "email": "pho@example.com", "version": "938" }
```

→ 200 trả `MemberDto` mới. Đổi SĐT/email ⇒ tên đăng nhập đổi theo.

### Khóa / mở khóa / gỡ / cấp lại mật khẩu

| Nút | Hiện khi `status` | Xác nhận | Kết quả |
|-----|-------------------|----------|---------|
| Khóa | `Active` | "Phó quản lý sẽ bị đăng xuất ngay" | 204 |
| Mở khóa | `Locked` | — | 204 |
| Gỡ khỏi tổ chức | `Active`, `Locked` | **Xác nhận mạnh** (gõ tên) — không hoàn tác | 204 |
| Cấp lại mật khẩu | `Active`, `Locked` | — | 200 `{ userId, username, temporaryPassword, expiresAt }` |

Lỗi chung: 404 `MEMBER_NOT_FOUND`, 422 `CANNOT_MODIFY_OWNER` (thao tác trên chủ trọ),
409 `USER_REMOVED` / `USER_ALREADY_LOCKED` / `USER_NOT_LOCKED` → tải lại danh sách.
