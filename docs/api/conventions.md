# Quy ước chung

## Địa chỉ & định dạng

| Mục | Giá trị |
|-----|---------|
| Base URL (dev) | `http://localhost:5213/api/v1` |
| Định dạng | JSON UTF-8, tên field **camelCase** |
| Enum | Gửi/nhận dạng **chuỗi**: `"Active"`, `"CitizenId"` (không dùng số) |
| Ngày | `"2026-10-05"` (`yyyy-MM-dd`, theo giờ Việt Nam) |
| Thời điểm | ISO 8601 UTC: `"2026-10-02T13:17:51.514457+00:00"` → hiển thị đổi sang giờ VN (+07:00) |
| Tiền | Số nguyên VND: `3500000` → hiển thị `3.500.000 đ` |
| Id | UUID chuỗi |

## Xác thực

Mọi API (trừ đăng nhập / làm mới token / health) cần header:

```http
Authorization: Bearer <accessToken>
```

Xem chi tiết luồng token ở [auth.md](auth.md). Tóm tắt xử lý chung cho **mọi request**:

| Response | Ý nghĩa | UI làm gì |
|----------|---------|-----------|
| 401 `TOKEN_EXPIRED` | Access token hết hạn (15 phút) | Gọi `POST /auth/refresh` **một lần**, rồi gửi lại request |
| 401 `SESSION_REVOKED`, `INVALID_TOKEN`, `AUTHENTICATION_REQUIRED` | Phiên không còn hiệu lực | Xóa token → về màn đăng nhập |
| 403 `PASSWORD_CHANGE_REQUIRED` | Đang dùng mật khẩu tạm | Chuyển sang màn đổi mật khẩu |
| 403 `FORBIDDEN` | Sai vai trò | Ẩn chức năng; hiện "Bạn không có quyền" |

## Phân trang

Danh sách dùng query `?page=1&pageSize=20` (tối đa 100). Response:

```json
{
  "items": [ /* … */ ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 7,
  "totalPages": 1
}
```

Ngoại lệ trả **mảng** (không phân trang): `GET /org/members`, `GET /properties/{id}/room-groups`, `GET /contracts/{id}/billing-periods`.

## Cập nhật có `version` (chống ghi đè)

Các bản ghi sửa được (khu, phòng, người thuê, hợp đồng nháp, phó quản lý) có field `"version": "904"`.

1. Khi mở form sửa: lưu `version` từ response GET.
2. Gửi kèm `version` trong body `PUT`.
3. Nếu người khác đã sửa trước → **409 `CONCURRENCY_CONFLICT`** → UI báo "Dữ liệu đã thay đổi, vui lòng tải lại" và nạp lại form.

`version` là chuỗi số — gửi lại nguyên chuỗi (`"904"`) hoặc số (`904`) đều được.

## Idempotency-Key — chống tạo trùng

Các lệnh **tạo mới** bắt buộc header `Idempotency-Key` (UUID). Thiếu → 400 `IDEMPOTENCY_KEY_REQUIRED`.

```http
POST /api/v1/properties
Idempotency-Key: 7f3c1a2e-5b4d-4e8f-9a01-2b3c4d5e6f70
```

Quy tắc cho UI:

1. **Sinh UUID khi người dùng mở form / bắt đầu thao tác**, giữ nguyên khi bấm lại hoặc retry do lỗi mạng.
2. Người dùng **sửa nội dung** form rồi gửi lại → sinh UUID mới (cùng key khác nội dung → 422 `IDEMPOTENCY_KEY_REUSED`).
3. 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS` → chờ `Retry-After` giây, gửi lại cùng key.
4. Response có header `Idempotent-Replayed: true` → đây là kết quả lần trước, xử lý như thành công.

| Endpoint | Bắt buộc |
|----------|:-------:|
| `POST /admin/organizations` | ✅ |
| `POST /org/members` | ✅ |
| `POST /properties` | ✅ |
| `POST /properties/{id}/rooms`, `POST /properties/{id}/rooms/bulk` | ✅ |
| `POST /renters` | ✅ |
| `POST /contracts` | ✅ |
| `POST /contracts/{id}/activate`, `POST /admin/organizations/{id}/suspend` · `/reactivate` | tùy chọn (nên gửi) |

**Mẹo:** hook HTTP dùng chung tự gắn `Idempotency-Key` cho mọi `POST` theo "phiên thao tác" của form là đơn giản nhất.

## Tạo mới → 201

Lệnh tạo trả **201 Created** + header `Location` + body `{ "id": "…" }` (trừ các lệnh trả mật khẩu tạm — xem từng file).
Sau khi tạo, gọi `GET` theo `Location` / id để lấy bản đầy đủ.

## Lệnh hành động → 204

Các lệnh dạng `POST /…/{id}/activate`, `/cancel`, `/lock`… thành công trả **204 No Content** (không body) →
UI tải lại bản ghi để cập nhật trạng thái.

## Lỗi (RFC 9457 ProblemDetails)

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Vi phạm quy tắc nghiệp vụ",
  "status": 422,
  "detail": "Chỉ thao tác được khi hợp đồng còn là bản nháp.",
  "instance": "POST /api/v1/contracts/1de97365-…/assets",
  "code": "CONTRACT_NOT_DRAFT",
  "traceId": "00-e16ae76a3044aa693782eb795197d3e1-d27373f8d7423f00-00"
}
```

Lỗi nhập liệu (400) có thêm `errors` theo **đường dẫn field** để hiện lỗi dưới từng ô:

```json
{
  "status": 400,
  "title": "Dữ liệu không hợp lệ",
  "code": "VALIDATION_FAILED",
  "errors": {
    "renter.fullName": ["Họ tên là bắt buộc."],
    "renter.idNumber": ["Số giấy tờ không hợp lệ (CCCD: 12 chữ số; CMND: 9 chữ số; hộ chiếu: 6–20 ký tự chữ/số)."]
  }
}
```

- **Xử lý theo `code`**, có thể hiện thẳng `detail` (đã là tiếng Việt thân thiện).
- Key trong `errors` khớp đường dẫn JSON của body (`renter.fullName`, `address.streetAddress`, `occupants[0].renterId`).
- Báo lỗi cho bộ phận hỗ trợ: kèm `traceId`.
- Bảng mã lỗi đầy đủ: [errors.md](errors.md).

## Giới hạn tần suất

Vượt giới hạn → **429 `TOO_MANY_REQUESTS`** + header `Retry-After` (giây). UI: hiện thông báo, **không tự retry liên tục**.
Đăng nhập: 10 lần/phút/IP · đổi mật khẩu: 5 lần/phút · tạo/sửa (POST/PUT/DELETE): 60 lần/phút · tối đa 10 request đồng thời.

## Enum dùng chung

| Enum | Giá trị → nhãn hiển thị gợi ý |
|------|-------------------------------|
| `UserRole` | `SystemAdmin` Quản trị viên · `OrgOwner` Chủ trọ · `OrgManager` Phó quản lý |
| `UserStatus` | `Active` Hoạt động · `Locked` Đã khóa · `Removed` Đã gỡ |
| `OrganizationStatus` | `Active` Hoạt động · `Suspended` Tạm ngưng |
| `IdDocumentType` | `CitizenId` CCCD / Căn cước (12 số) · `LegacyId` CMND cũ (9 số) · `Passport` Hộ chiếu |
| `Gender` | `Male` Nam · `Female` Nữ · `Other` Khác |
| `LessorType` | `Individual` Cá nhân · `Organization` Tổ chức |
| `ChargeMode` | `Prepaid` Thu đầu kỳ (điện nước tính kỳ trước) · `Postpaid` Thu cuối kỳ |
| `ProrationMode` | `Daily` Tính theo ngày ở · `FullPeriod` Tính tròn tháng |
| `RoomDisplayStatus` | `Vacant` Trống · `Reserved` Giữ chỗ · `Occupied` Đang thuê · `Maintenance` Bảo trì · `Archived` Ngừng dùng |
| `ContractStatus` | `Draft` Nháp · `Active` Đang hiệu lực · `Liquidating` Đang thanh lý · `Ended` Đã kết thúc · `Cancelled` Đã hủy |
| `PaymentMethod` | `Cash` Tiền mặt · `BankTransfer` Chuyển khoản · `EWallet` Ví điện tử |
| `TerminationReason` | `Expired` Hết hạn · `MutualAgreement` Hai bên thỏa thuận · `LesseeUnilateral` Bên thuê đơn phương · `LessorUnilateral` Bên cho thuê đơn phương · `RoomTransfer` Chuyển phòng |
| `TerminationGround` | `RentArrears3Months` Nợ tiền thuê từ 3 tháng · `WrongPurpose` Sử dụng sai mục đích · `UnauthorizedRenovation` Tự ý đục phá, cải tạo · `Other` Khác |
| `VehicleType` | `Motorbike` Xe máy · `Bicycle` Xe đạp · `ElectricBike` Xe điện · `Car` Ô tô |

## Dữ liệu cá nhân

Số giấy tờ (CCCD / hộ chiếu) **luôn trả dạng che** `"********2345"`. Muốn xem đầy đủ: gọi endpoint `reveal-id-number` (POST) —
mỗi lần xem đều được ghi log. UI: nút "Hiện số" có biểu tượng con mắt, không tự động gọi.
