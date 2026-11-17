# Thành viên: quản lý khu

**Đổi 08/11/2026** (thay "phó quản lý"): chủ trọ (`OrgOwner`) thêm **một hoặc nhiều quản lý khu** (`OrgManager`, mặc định tối đa 10) và **giao khu**
cho từng người. Quản lý khu làm **mọi việc như chủ trọ nhưng chỉ trong các khu được giao** — tuyển khách, lập / in / ký thay hợp đồng, chỉ số, phiếu,
thu tiền, bỏ nợ, "Không hoàn", xem / xuất / in số giấy tờ đầy đủ, nhập Excel. Dữ liệu khu khác **coi như không tồn tại** (mở → 404, danh sách / số tổng
hợp / xuất Excel không gồm). Chủ trọ thấy mọi khu và theo dõi qua [nhật ký](audit.md).

| Thao tác | OrgOwner | OrgManager |
|----------|:--------:|:----------:|
| Nghiệp vụ khu, phòng, người thuê, hợp đồng, chỉ số, phiếu, thu tiền, bỏ nợ, số giấy tờ đầy đủ, nhập / xuất Excel | ✅ mọi khu | ✅ **khu được giao** |
| Xem danh sách / chi tiết thành viên | ✅ | ✅ (chỉ xem) |
| **Chỉ chủ trọ** (quản lý → 403, **ẩn nút**): thành viên (thêm, sửa, **giao khu**, khóa, gỡ, cấp lại mật khẩu); tài khoản ngân hàng nhận tiền (quản lý chỉ xem); bên cho thuê của khu / tổ chức gồm người ký thay + giấy ủy quyền (quản lý xem, in theo); tạo khu, ngừng / khôi phục khu; tạo / sửa / ngừng mẫu hợp đồng (quản lý vẫn dùng); thông tin tổ chức; giữ dữ liệu; ẩn danh tay; nhật ký, nhật ký đăng nhập. **Sau này**: mã QR, thanh toán online, liên kết / đối soát ngân hàng | ✅ | ❌ |

**Hồ sơ người thuê** (không gắn khu): quản lý thấy hồ sơ có hợp đồng ở khu được giao hoặc do chính mình tạo. Người thuê cũ của khu khác quay lại:
nhập CCCD khi tạo hồ sơ ⇒ 409 `RENTER_ID_NUMBER_EXISTS` kèm `existingRenterName` + `existingIdNumberLast4` ⇒ UI "Đã có hồ sơ: Nguyễn Thị Hoa, ***4567 —
dùng hồ sơ này" (mở `existingRenterId`, từ đó quản lý thấy và dùng được hồ sơ — [renters.md](renters.md)); hợp đồng / lịch sử ở khu khác vẫn ẩn.

## Màn hình "Thành viên"

- Bảng: Họ tên · SĐT / email · Vai trò · Trạng thái · **Khu được giao** (tên khu; chủ trọ: "Tất cả") · Lần đăng nhập cuối · Thao tác.
- Nút **"Thêm quản lý khu"** (chỉ chủ trọ) — form có ô **chọn khu** (nhiều khu).
- Thao tác dòng quản lý: Sửa · **Giao khu** · Khóa / Mở khóa · Cấp lại mật khẩu · Gỡ.
- Công tắc "Hiện cả người đã gỡ" → `includeRemoved=true`.
- Dòng chủ trọ: không có nút thao tác.

## Endpoint

| Method | URL | Quyền | Mô tả |
|--------|-----|-------|-------|
| GET | `/org/members?includeRemoved=false` | Thành viên | Danh sách (mảng, không phân trang) |
| GET | `/org/members/{id}` | Thành viên | Chi tiết |
| POST | `/org/members` | Chủ trọ · **Idempotency-Key** | Thêm quản lý khu (kèm `propertyIds`) → 201 + mật khẩu tạm |
| PUT | `/org/members/{id}` | Chủ trọ | Sửa họ tên / SĐT / email |
| PUT | `/org/members/{id}/properties` | Chủ trọ | **Giao khu** `{ "propertyIds": [...] }` — thay cả danh sách → 200 thành viên |
| POST | `/org/members/{id}/lock` | Chủ trọ | Khóa — đăng xuất ngay mọi phiên |
| POST | `/org/members/{id}/unlock` | Chủ trọ | Mở khóa |
| POST | `/org/members/{id}/remove` | Chủ trọ | Gỡ vĩnh viễn (không khôi phục) |
| POST | `/org/members/{id}/reset-password` | Chủ trọ | Cấp lại mật khẩu tạm |

Bỏ 08/11/2026: `PUT /org/members/{id}/sensitive-data-access`, `PUT /org/members/{id}/write-off-permission` (quản lý khu luôn có hai quyền này trong khu được giao).

### Danh sách

```json
[
  {
    "id": "8ceb6012-7907-4b2b-a65a-e67a9d435e13",
    "fullName": "Trần Minh",
    "phone": "0954191081",
    "email": null,
    "role": "OrgManager",
    "status": "Active",
    "mustChangePassword": true,
    "tempPasswordExpiresAt": "2026-10-05T13:19:05.678591+00:00",
    "lastLoginAt": null,
    "createdAt": "2026-10-02T13:19:05.754001+00:00",
    "canViewSensitiveData": true,
    "version": "938",
    "canWriteOff": true,
    "propertyIds": ["0b9c3f9e-1a2b-4c5d-8e7f-001122334455"]
  }
]
```

- `propertyIds`: khu được giao (`[]` = chưa giao khu nào — đăng nhập vào thấy trống, UI nhắc chủ trọ giao khu). **Bỏ trống / `null` khi tạo ⇒ giao tất cả khu đang hoạt động** (cùng mặc định chọn sẵn của UI — chốt 14/11/2026, 99 O-29); muốn không giao khu nào phải gửi `[]`. Dòng chủ trọ `null` = mọi khu.
- `canViewSensitiveData` / `canWriteOff`: luôn `true` với chủ trọ và quản lý khu (giữ để UI cũ không vỡ).

Nhãn phụ gợi ý:
- `mustChangePassword && tempPasswordExpiresAt > now` → "Chờ kích hoạt (hết hạn dd/MM HH:mm)"
- `mustChangePassword && tempPasswordExpiresAt <= now` → "Mật khẩu tạm đã hết hạn" + gợi ý bấm **Cấp lại mật khẩu**

### Giao khu

`PUT /org/members/{id}/properties` — `{ "propertyIds": ["…khu A", "…khu C"] }` → **200** (thành viên sau khi đổi).

- Thay **cả danh sách**; `[]` = bỏ hết khu. Tối đa 200 khu. Có hiệu lực **ngay** ở phiên đang mở của người đó (không cần đăng nhập lại).
- Khu không thuộc tổ chức / đã ngừng sử dụng / id lạ → 422 `PROPERTY_ASSIGNMENT_INVALID`; gọi với id chủ trọ → 422 `CANNOT_MODIFY_OWNER`.
- Tạo khu mới **không** tự giao cho quản lý nào — chủ trọ giao sau. Ngừng sử dụng khu không bỏ giao khu (khôi phục thì quyền còn nguyên).
- Nhật ký ghi danh sách khu cũ → mới.

### Thêm quản lý khu

`POST /org/members`

```json
{ "fullName": "Trần Minh", "phone": "0954191081", "email": null, "propertyIds": ["…khu A"] }
```

| Trường | Quy tắc |
|--------|---------|
| `fullName` | Bắt buộc, ≤ 200 |
| `phone` / `email` | **Ít nhất 1** (là tên đăng nhập), duy nhất toàn hệ thống |
| `propertyIds` | Khu được giao, ≤ 200, khu đang dùng của tổ chức. **Không gửi** = mọi khu đang dùng; `[]` = chưa giao khu nào |

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
Hệ thống không tự gửi — chủ trọ tự chuyển cho quản lý khu (Zalo / tin nhắn). Đăng nhập lần đầu bắt buộc đổi mật khẩu; mật khẩu tạm hết hạn sau 72 giờ.

| Lỗi | UI |
|-----|----|
| 409 `PHONE_TAKEN` / `EMAIL_TAKEN` | Lỗi dưới ô tương ứng |
| 422 `MANAGER_LIMIT_REACHED` | "Đã đạt số quản lý khu tối đa" — vô hiệu nút Thêm |
| 422 `PROPERTY_ASSIGNMENT_INVALID` | Lỗi dưới ô chọn khu, tải lại danh sách khu |
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
| Khóa | `Active` | "Quản lý khu sẽ bị đăng xuất ngay" | 204 |
| Mở khóa | `Locked` | — | 204 |
| Gỡ khỏi tổ chức | `Active`, `Locked` | **Xác nhận mạnh** (gõ tên) — không hoàn tác | 204 |
| Cấp lại mật khẩu | `Active`, `Locked` | — | 200 `{ userId, username, temporaryPassword, expiresAt }` |

Lỗi chung: 404 `MEMBER_NOT_FOUND`, 422 `CANNOT_MODIFY_OWNER` (thao tác trên chủ trọ),
409 `USER_REMOVED` / `USER_ALREADY_LOCKED` / `USER_NOT_LOCKED` → tải lại danh sách.

## Thời gian giữ dữ liệu người thuê

`GET /org/data-retention` (chủ trọ, quản lý khu) → `{ "retentionMonths": 36, "autoAnonymize": true, "warnings": [] }`

`PUT /org/data-retention` `{ "retentionMonths": 60, "autoAnonymize": true }` — **chỉ chủ trọ**. `retentionMonths` 36–120 (400 nếu ngoài khoảng).
Người thuê rời đi quá số tháng này (không còn HĐ đang chạy, không nợ) được ẩn danh tự động mỗi đêm ([renters.md](renters.md)).
`autoAnonymize: false` ⇒ tắt hẳn; response có cảnh báo `AUTO_ANONYMIZE_DISABLED` — UI hiện hộp xác nhận "bạn tự chịu trách nhiệm lưu giữ dữ liệu cá nhân". Mọi thay đổi ghi nhật ký (ai, lúc nào).

## Thông tin tổ chức (11/10/2026)

| Method | Route | Ai | Ghi chú |
|--------|-------|----|---------|
| GET | `/org` | Chủ trọ, quản lý khu | `{ id, code, name, contactName, contactPhone, contactEmail, taxCode, address }` |
| PUT | `/org` | **Chỉ chủ trọ** (quản lý khu → 403) | Body như dưới → 200 trả thông tin sau khi sửa. `code` (mã tổ chức) không đổi được |

```json
{ "name": "Nhà trọ Hoa Sữa", "contactName": "Chị Hoa", "contactPhone": "0912345678", "contactEmail": null, "taxCode": null, "address": "12 Tạ Quang Bửu" }
```

`name` bắt buộc (≤ 200); `contactPhone` đúng dạng SĐT Việt Nam; `taxCode` ≤ 14; `address` ≤ 500. Tên tổ chức mới hiện ngay ở `GET /me`
(`organization.name`).

## Nhật ký đăng nhập (12/10/2026)

`GET /org/login-events?userId=&success=&page=1&pageSize=20` — **chỉ chủ trọ** (quản lý khu → 403). Mỗi lần đăng nhập thành công / thất bại vào một tài
khoản của tổ chức là 1 dòng, mới nhất trước.

```json
{ "items": [ { "id": "…", "userId": "…", "fullName": "Phó Quản Lý", "success": false, "reason": "WrongPassword", "ipAddress": "113.190.1.2",
               "occurredAt": "2026-10-12T02:15:00+00:00" } ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1 }
```

| `reason` | Nghĩa |
|----------|-------|
| `Success` | Đăng nhập thành công |
| `WrongPassword` | Sai mật khẩu |
| `LockedOut` | Tài khoản đang bị khóa / tạm khóa do thử sai nhiều lần |
| `TemporaryPasswordExpired` | Mật khẩu tạm đã hết hạn |
| `OrganizationSuspended` | Tổ chức đang tạm ngưng |

- Lọc: `userId` (1 tài khoản), `success=true|false`.
- Lần thử với tên đăng nhập **không tồn tại** không thuộc tổ chức nào nên không hiện ở đây. Hệ thống không lưu tên đăng nhập người ta gõ.
- Nhật ký giữ **180 ngày**, sau đó tự xóa.
- Gợi ý UI: tab "Nhật ký đăng nhập" trong màn hình Thành viên; dòng thất bại tô đỏ nhạt.
