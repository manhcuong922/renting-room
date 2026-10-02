# Quy ước gọi API (dành cho frontend / mobile)

> Áp dụng cho mọi endpoint `/api/v1/*`. Thử nhanh bằng [`renting_room.http`](../../renting_room/renting_room.http) hoặc Swagger `http://localhost:5213/swagger`.

## 1. Xác thực

| Bước | Endpoint | Ghi chú |
|------|----------|---------|
| Đăng nhập | `POST /auth/login` `{ username, password }` | `username` = SĐT (nhận cả `+84…`, có dấu cách/chấm) hoặc email |
| Gọi API | Header `Authorization: Bearer <accessToken>` | Access token sống **15 phút** |
| Làm mới | `POST /auth/refresh` `{ refreshToken }` | Refresh token **dùng 1 lần** — luôn lưu cặp token MỚI từ response |
| Đăng xuất | `POST /auth/logout` `{ refreshToken }` | Luôn trả 204 |
| Đăng xuất mọi thiết bị | `POST /auth/logout-all` | Access token hiện tại cũng mất hiệu lực ngay |

Xử lý phía client:

- `mustChangePassword: true` trong response đăng nhập → chuyển ngay sang màn **đổi mật khẩu**
  (mọi API nghiệp vụ trả 403 `PASSWORD_CHANGE_REQUIRED` cho tới khi đổi).
- 401 `TOKEN_EXPIRED` → gọi `/auth/refresh` **một lần** rồi gửi lại request. Nhiều request cùng hết hạn: chỉ cho **một** lời gọi refresh
  chạy, các request khác chờ kết quả đó (refresh song song cùng token chỉ 1 cái thành công).
- 401 `SESSION_REVOKED` / `INVALID_REFRESH_TOKEN` → xóa token, về màn đăng nhập (đã đổi mật khẩu / bị đăng xuất / tổ chức bị tạm ngưng).
- Phiên đăng nhập tối đa **90 ngày** kể cả khi refresh liên tục.

## 2. Idempotency-Key — chống tạo trùng

Thao tác **tạo mới** (và sau này: thanh toán, cọc, tạo phiếu) yêu cầu header:

```http
POST /api/v1/rooms
Idempotency-Key: 7f3c1a2e-5b4d-4e8f-9a01-2b3c4d5e6f70
```

Quy tắc cho client:

1. **Sinh một UUID mới khi người dùng bắt đầu một thao tác** (mở form / bấm nút lần đầu) — KHÔNG sinh lại khi retry.
2. Retry (mất mạng, timeout, bấm 2 lần) → gửi lại **cùng key, cùng body**. Server trả lại đúng kết quả lần đầu, không tạo thêm.
3. Người dùng sửa form rồi gửi lại → **sinh key mới** (cùng key + body khác ⇒ 422).

| Response | Ý nghĩa | Client làm gì |
|----------|---------|---------------|
| 2xx, không có header `Idempotent-Replayed` | Thực thi lần đầu | Bình thường |
| 2xx + `Idempotent-Replayed: true` | Đã xử lý trước đó, đây là kết quả cũ | Xử lý như thành công |
| 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS` | Lần gửi trước đang chạy | Chờ `Retry-After` giây rồi gửi lại cùng key |
| 422 `IDEMPOTENCY_KEY_REUSED` | Dùng lại key cho nội dung khác | Lỗi lập trình phía client — sinh key mới |
| 400 `IDEMPOTENCY_KEY_REQUIRED` / `INVALID_IDEMPOTENCY_KEY` | Thiếu / sai định dạng (8–64 ký tự `A-Z a-z 0-9 - _`) | Sửa client |

Endpoint đang áp dụng:

| Endpoint | Bắt buộc? |
|----------|-----------|
| `POST /admin/organizations` | ✅ (retry nhận lại đúng mật khẩu tạm của lần đầu) |
| `POST /rooms` | ✅ |
| `POST /admin/organizations/{id}/suspend`, `/reactivate` | Tùy chọn |

Key có hiệu lực 24 giờ, phạm vi theo từng tài khoản (2 người dùng trùng key không ảnh hưởng nhau).

## 3. Giới hạn tần suất (rate limit)

Vượt giới hạn → **429** `TOO_MANY_REQUESTS` kèm header `Retry-After` (giây). Client: hiện thông báo, **không retry ngay**,
đợi đúng `Retry-After`. Không retry tự động vòng lặp.

| Giới hạn (mặc định) | Áp cho | Theo |
|---------------------|--------|------|
| 10 / phút | `POST /auth/login` | IP (IPv6 theo dải /64) |
| 30 / phút | `POST /auth/refresh`, `/auth/logout` | IP |
| 5 / phút | `POST /auth/change-password`, `/auth/logout-all` | Người dùng |
| 300 / phút | Mọi request đã đăng nhập | Người dùng |
| 60 / phút | Mọi request chưa đăng nhập | IP |
| 60 / phút | Request ghi (POST/PUT/PATCH/DELETE) | Người dùng / IP |
| 10 request **đồng thời** | Mọi request | Người dùng / IP |

Cửa sổ thời gian **trượt** (không reset cứng đầu mỗi phút). Body request tối đa **1 MB** (vượt → 413 `PAYLOAD_TOO_LARGE`).

Ngoài ra: nhập sai mật khẩu **5 lần liên tiếp** → tài khoản tạm khóa 15 phút (trong thời gian khóa, đăng nhập vẫn chỉ báo
`INVALID_CREDENTIALS` — không phân biệt để tránh lộ thông tin).

## 4. Định dạng lỗi (RFC 9457 ProblemDetails)

Mọi lỗi có `Content-Type: application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Dữ liệu không hợp lệ",
  "status": 400,
  "detail": "Một hoặc nhiều trường dữ liệu không hợp lệ.",
  "instance": "POST /api/v1/auth/login",
  "code": "VALIDATION_FAILED",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": { "username": ["Tên đăng nhập là bắt buộc."] }
}
```

- **Xử lý theo `code`**, không theo câu chữ `detail` (câu chữ có thể đổi).
- `errors` chỉ có khi `code = VALIDATION_FAILED`; key là tên field camelCase giống body gửi lên → hiển thị lỗi dưới từng ô.
- Báo lỗi cho bộ phận hỗ trợ: gửi kèm `traceId`.

Mã lỗi thường gặp:

| HTTP | `code` | Ý nghĩa |
|------|--------|---------|
| 400 | `VALIDATION_FAILED`, `INVALID_REQUEST` | Dữ liệu sai / JSON sai cú pháp |
| 401 | `AUTHENTICATION_REQUIRED`, `TOKEN_EXPIRED`, `INVALID_TOKEN`, `SESSION_REVOKED`, `INVALID_CREDENTIALS`, `INVALID_REFRESH_TOKEN` | Xem mục 1 |
| 403 | `FORBIDDEN`, `PASSWORD_CHANGE_REQUIRED`, `ORGANIZATION_SUSPENDED` | Không có quyền |
| 404 | `NOT_FOUND`, `ROOM_NOT_FOUND`, `ORGANIZATION_NOT_FOUND`… | Không tồn tại (hoặc thuộc tổ chức khác) |
| 409 | `PHONE_TAKEN`, `EMAIL_TAKEN`, `ORG_CODE_TAKEN`, `CONCURRENCY_CONFLICT`, `IDEMPOTENCY_REQUEST_IN_PROGRESS`… | Xung đột dữ liệu |
| 413 | `PAYLOAD_TOO_LARGE` | Body quá lớn |
| 422 | `INVALID_CURRENT_PASSWORD`, `PASSWORD_REUSED`, `IDEMPOTENCY_KEY_REUSED`… | Vi phạm quy tắc nghiệp vụ |
| 423 | `ACCOUNT_LOCKED` | Đổi mật khẩu khi tài khoản đang tạm khóa |
| 429 | `TOO_MANY_REQUESTS` | Mục 3 |
| 500 | `INTERNAL_ERROR` | Lỗi hệ thống — gửi `traceId` cho hỗ trợ |

## 5. Cấu hình cho người vận hành (`appsettings.json` / biến môi trường)

| Khóa | Mặc định | Ghi chú |
|------|----------|---------|
| `RateLimiting:Enabled` | `true` | Chỉ tắt khi debug cục bộ |
| `RateLimiting:{Login,Refresh,Sensitive,Authenticated,Anonymous,Write}:PermitLimit` / `:WindowSeconds` | xem mục 3 / `60` | Biến môi trường: `RateLimiting__Login__PermitLimit=20` |
| `RateLimiting:MaxConcurrentRequestsPerClient` | `10` | |
| `Idempotency:RetentionHours` | `24` | |
| `Idempotency:InProgressTimeoutSeconds` | `120` | Phải lớn hơn thời gian xử lý request dài nhất |
| `Limits:MaxRequestBodyBytes` | `1048576` | |
| `DataProtection:KeysPath` | trống | **Bắt buộc khi chạy nhiều instance / container**: thư mục dùng chung, bền vững (mất khóa ⇒ không trả lại được response idempotency cũ) |
| `ReverseProxy:KnownProxies` / `KnownNetworks` | trống | **Bắt buộc khi đứng sau nginx / load balancer**, nếu không mọi client bị tính chung 1 IP |
