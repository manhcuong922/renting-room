# Bảng mã lỗi

Mọi lỗi có dạng ProblemDetails (xem [conventions.md](conventions.md#lỗi-rfc-9457-problemdetails)). **Xử lý theo `code`**, không theo câu chữ.
`detail` là câu tiếng Việt có thể hiện thẳng cho người dùng (trừ lỗi 5xx).

## Xử lý chung trong interceptor

| HTTP | Hành động mặc định |
|------|--------------------|
| 400 `VALIDATION_FAILED` | Gắn `errors[field]` vào từng ô form; field không có trên form → toast |
| 400 khác | Toast `detail` |
| 401 | Xem nhóm Xác thực bên dưới (refresh hoặc về đăng nhập) |
| 403 | `PASSWORD_CHANGE_REQUIRED` → màn đổi mật khẩu; còn lại → "Bạn không có quyền" |
| 404 | Trang/hộp thoại "Không tìm thấy" (dữ liệu tổ chức khác cũng trả 404) |
| 409 | Toast `detail` + **tải lại dữ liệu** |
| 413 | "Dữ liệu quá lớn" |
| 422 | Toast/hộp thoại `detail` (lỗi nghiệp vụ — thường cần người dùng làm bước khác trước) |
| 423 | Tài khoản bị khóa → đăng xuất |
| 429 | Khóa nút theo `Retry-After` |
| 5xx | "Đã có lỗi xảy ra" + `traceId` để báo hỗ trợ |

## Hạ tầng (mọi endpoint)

| Code | HTTP | Ý nghĩa / UI |
|------|------|--------------|
| `VALIDATION_FAILED` | 400 | Dữ liệu form sai — xem `errors` |
| `INVALID_REQUEST` | 400 | JSON sai cú pháp / sai kiểu (VD gửi enum `"active"` sai chữ hoa, ngày sai định dạng) — lỗi lập trình |
| `BAD_REQUEST` | 400 | Lỗi chung |
| `IDEMPOTENCY_KEY_REQUIRED` | 400 | Thiếu header `Idempotency-Key` — lỗi lập trình |
| `INVALID_IDEMPOTENCY_KEY` | 400 | Key không phải 8–64 ký tự `A-Z a-z 0-9 - _` |
| `IDEMPOTENCY_REQUEST_IN_PROGRESS` | 409 | Lần gửi trước còn chạy — chờ `Retry-After` rồi gửi lại cùng key |
| `IDEMPOTENCY_REPLAY_UNAVAILABLE` | 409 | Đã xử lý nhưng không trả lại được kết quả cũ — tải lại danh sách để kiểm tra |
| `IDEMPOTENCY_KEY_REUSED` | 422 | Cùng key, khác nội dung — sinh key mới |
| `CONCURRENCY_CONFLICT` | 409 | Người khác vừa sửa (`version` cũ) — tải lại form |
| `DUPLICATE_VALUE`, `OVERLAP_CONFLICT`, `REFERENCE_CONFLICT` | 409 | Ràng buộc dữ liệu (hiếm) — tải lại |
| `CONSTRAINT_VIOLATION` | 422 | Dữ liệu vi phạm ràng buộc — báo hỗ trợ kèm `traceId` |
| `NOT_FOUND` | 404 | Sai URL |
| `METHOD_NOT_ALLOWED` | 405 | Sai method |
| `PAYLOAD_TOO_LARGE` | 413 | Body > 1 MB |
| `UNSUPPORTED_MEDIA_TYPE` | 415 | Thiếu `Content-Type: application/json` |
| `TOO_MANY_REQUESTS` | 429 | Vượt giới hạn tần suất |
| `INTERNAL_ERROR` | 500 | Lỗi hệ thống |

## Xác thực & tài khoản

| Code | HTTP | UI |
|------|------|----|
| `AUTHENTICATION_REQUIRED` | 401 | Thiếu token → đăng nhập |
| `TOKEN_EXPIRED` | 401 | Refresh 1 lần rồi gửi lại |
| `INVALID_TOKEN` | 401 | Đăng nhập lại |
| `SESSION_REVOKED` | 401 | Phiên bị thu hồi (đổi mật khẩu, bị khóa, đăng xuất mọi nơi, tổ chức tạm ngưng) → đăng nhập lại |
| `INVALID_REFRESH_TOKEN` | 401 | Đăng nhập lại |
| `INVALID_CREDENTIALS` | 401 | Sai tài khoản/mật khẩu hoặc đang bị khóa tạm 15 phút |
| `TEMPORARY_PASSWORD_EXPIRED` | 401 | Mật khẩu tạm quá 72h — xin cấp lại |
| `PASSWORD_CHANGE_REQUIRED` | 403 | Chuyển màn đổi mật khẩu |
| `FORBIDDEN` | 403 | Sai vai trò |
| `ORGANIZATION_SUSPENDED` | 403 | Tổ chức tạm ngưng |
| `ACCOUNT_LOCKED` | 423 | Khóa do nhập sai mật khẩu hiện tại nhiều lần |
| `WEAK_PASSWORD` | 400 | Mật khẩu yếu / chứa tên đăng nhập |
| `INVALID_CURRENT_PASSWORD` | 422 | Sai mật khẩu hiện tại |
| `PASSWORD_REUSED` | 422 | Mật khẩu mới trùng cũ |

## Quản trị & thành viên

| Code | HTTP | UI |
|------|------|----|
| `ORG_CODE_TAKEN` | 409 | Ô mã tổ chức |
| `PHONE_TAKEN` / `EMAIL_TAKEN` | 409 | Ô SĐT / email |
| `ORGANIZATION_NOT_FOUND` / `USER_NOT_FOUND` / `MEMBER_NOT_FOUND` | 404 | Tải lại |
| `ORG_ALREADY_SUSPENDED` / `ORG_NOT_SUSPENDED` | 409 | Tải lại |
| `USER_ALREADY_LOCKED` / `USER_NOT_LOCKED` / `USER_REMOVED` | 409 | Tải lại |
| `MANAGER_LIMIT_REACHED` | 422 | Đã đủ số phó quản lý |
| `CANNOT_MODIFY_OWNER` / `CANNOT_LOCK_OWNER` | 422 | Không thao tác trên chủ trọ — ẩn nút |

## Khu trọ & phòng

| Code | HTTP | UI |
|------|------|----|
| `PROPERTY_NOT_FOUND` / `ROOM_NOT_FOUND` / `ROOM_GROUP_NOT_FOUND` | 404 | |
| `PROPERTY_CODE_TAKEN` / `ROOM_CODE_TAKEN` / `ROOM_GROUP_NAME_TAKEN` | 409 | Ô mã / tên |
| `PROPERTY_ARCHIVED` | 422 | Khu ngừng dùng — khôi phục khu trước |
| `PROPERTY_NOT_ARCHIVED` / `ROOM_NOT_ARCHIVED` | 409 | Tải lại |
| `PROPERTY_HAS_ACTIVE_CONTRACTS` / `ROOM_HAS_CONTRACTS` | 422 | Còn hợp đồng đang mở |
| `ROOM_ARCHIVED` | 422 | Phòng ngừng dùng |
| `ROOM_OCCUPIED` | 422 | Phòng đang có HĐ — không bảo trì được |
| `ROOM_ALREADY_UNDER_MAINTENANCE` / `ROOM_NOT_UNDER_MAINTENANCE` | 409 | Tải lại |
| `MAX_OCCUPANTS_BELOW_CURRENT` | 422 | Sức chứa < số người đang ở |
| `ROOM_NOT_IN_PROPERTY` | 422 | Chọn phòng cùng khu |
| `ID_NUMBER_REQUIRED` | 400 | Đổi loại giấy tờ thì nhập lại số |

## Người thuê

| Code | HTTP | UI |
|------|------|----|
| `RENTER_NOT_FOUND` | 404 | |
| `RENTER_ID_NUMBER_EXISTS` | 409 | Tìm theo số giấy tờ → đề xuất dùng hồ sơ cũ |
| `ID_NUMBER_REQUIRED` | 400 | Ô số giấy tờ |

## Hợp đồng

| Code | HTTP | UI |
|------|------|----|
| `CONTRACT_NOT_FOUND` / `OCCUPANT_NOT_FOUND` / `ASSET_NOT_FOUND` / `VEHICLE_NOT_FOUND` | 404 | Tải lại |
| `CONTRACT_NO_TAKEN` | 409 | Ô số hợp đồng |
| `CONTRACT_NOT_DRAFT` / `CONTRACT_NOT_ACTIVE` / `CONTRACT_NOT_LIQUIDATING` / `CONTRACT_NOT_EDITABLE` | 422 | Trạng thái đã đổi — tải lại |
| `MONTHLY_RENT_REQUIRED` | 400 | Ô giá thuê |
| `ROOM_UNAVAILABLE` | 422 | Phòng bảo trì / ngừng dùng |
| `ROOM_PERIOD_OVERLAP` | 409 | Phòng đã có HĐ trùng thời gian |
| `START_DATE_IN_FUTURE` | 422 | Chỉ kích hoạt khi bàn giao (≤ ngày mai) |
| `LESSOR_INFO_INCOMPLETE` | 422 | Link sang tab Bên cho thuê của khu |
| `NO_OCCUPANT` | 422 | Thêm ít nhất 1 người ở |
| `ROOM_CAPACITY_EXCEEDED` | 422 | Vượt sức chứa (thêm người ở: hỏi xác nhận → `overrideCapacity`) |
| `REPRESENTATIVE_PHONE_REQUIRED` | 422 | Link sửa hồ sơ người đại diện |
| `REPRESENTATIVE_UNDERAGE` | 422 | Chọn người đại diện khác |
| `OCCUPANCY_OVERLAP` | 409 | Người này đang ở trong hợp đồng |
| `OCCUPANT_LIVES_ELSEWHERE` | 409 | Người này đang ở phòng khác — ghi chuyển đi ở HĐ cũ trước |
| `OCCUPANT_ALREADY_MOVED_OUT` | 409 | Đã ghi chuyển đi — thêm lại như người ở mới nếu quay lại |
| `DEPOSIT_TOO_HIGH` | 400 | Tiền cọc > 12 tháng tiền thuê |
| `CONTRACT_EXPIRED_EXTEND_FIRST` | 422 | Ngày vào ở sau ngày hết hạn — hiện nút "Gia hạn" |
| `VEHICLE_OWNER_NOT_IN_CONTRACT` | 422 | Kết thúc đăng ký xe của người không còn thuộc HĐ trước |
| `HOUSEHOLD_HEAD_NOT_OCCUPANT` | 400 | Chủ hộ phải là một người ở |

Cảnh báo mềm (không phải lỗi, nằm trong `warnings` của response 2xx): `DEPOSIT_ABOVE_THREE_MONTHS`, `PLATE_FORMAT_UNUSUAL`.
| `RELATIONSHIP_REQUIRED` / `RELATIONSHIP_NOTE_REQUIRED` | 400 | Chọn / ghi rõ quan hệ với người đứng tên |
| `RELATIONSHIP_GENDER_MISMATCH` / `RELATIONSHIP_AGE_MISMATCH` | 400 (422 khi kích hoạt) | Quan hệ không khớp giới tính / tuổi |
| `SPOUSE_UNDER_MARRIAGE_AGE` / `MULTIPLE_SPOUSES` | 400 (422 khi kích hoạt) | Vợ chồng chưa đủ tuổi kết hôn / hơn 1 vợ chồng |
| `GUARDIAN_CONSENT_REQUIRED` | 400 (422 khi kích hoạt) | Người < 18 tuổi cần đồng ý của cha mẹ / giám hộ |
| `DATE_OUTSIDE_CONTRACT` / `INVALID_END_DATE` | 422 | Ô ngày |
| `NOT_PERIOD_START` | 422 | Chọn ngày bắt đầu kỳ thu |
| `PERIOD_ALREADY_BILLED` | 422 | Kỳ đã lập phiếu |
| `RENT_TERM_EXISTS` | 409 | Đã có phụ lục từ ngày này |
| `CANNOT_EXTEND_INDEFINITE` | 422 | HĐ không thời hạn |
| `TERMINATION_GROUND_REQUIRED` | 422 | Chọn căn cứ chấm dứt |
| `LIQUIDATION_BEFORE_END_DATE` | 422 | Chưa tới ngày trả phòng |
| `PLATE_ALREADY_REGISTERED` | 409 | Biển số đang gửi ở HĐ khác |
| `VEHICLE_ALREADY_ENDED` | 409 | Tải lại |

## Xuất Excel

| Code | HTTP | UI |
|------|------|----|
| `EXPORT_TOO_LARGE` | 422 | > 20.000 dòng — thu hẹp bộ lọc |
| `VALIDATION_FAILED` với `toDate` (`INVALID_DATE_RANGE`) | 400 | Lỗi dưới ô ngày |

## Mẫu hợp đồng

| Code | HTTP | UI |
|------|------|----|
| `CONTRACT_TEMPLATE_NOT_FOUND` | 404 | |
| `CONTRACT_TEMPLATE_NAME_TAKEN` | 409 | Ô tên mẫu |
| `CONTRACT_TEMPLATE_ARCHIVED` | 422 | Mẫu ngừng dùng — chọn mẫu khác |
| `DEPOSIT_NOT_ALLOWED` | 400 | Mẫu không cọc — khóa ô tiền cọc |
| `CONTRACT_TEMPLATE_ALREADY_ARCHIVED` / `CONTRACT_TEMPLATE_NOT_ARCHIVED` | 409 | Tải lại |
| `VALIDATION_FAILED` với key `contract.customFields.<key>` | 400 | Lỗi dưới ô trường tùy biến tương ứng (`REQUIRED`, sai kiểu, key không có trong mẫu) |
