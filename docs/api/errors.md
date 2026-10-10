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
| `SENSITIVE_DATA_FORBIDDEN` | 403 | Phó quản lý chưa được cấp quyền xem số giấy tờ đầy đủ / xuất đầy đủ — ẩn nút theo `canViewSensitiveData`, báo "Cần chủ trọ cấp quyền" |

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
| `ROOM_NOT_IN_PROPERTY` | 422 | Chọn phòng cùng khu |
| `ID_NUMBER_REQUIRED` | 400 | Đổi loại giấy tờ thì nhập lại số |

## Người thuê

| Code | HTTP | UI |
|------|------|----|
| `RENTER_NOT_FOUND` | 404 | (cả khi chọn hồ sơ đã ẩn danh cho HĐ mới) |
| `RENTER_ANONYMIZED` | 422 | Hồ sơ đã ẩn danh — không sửa / xem số giấy tờ / ẩn danh lại; thuê lại thì tạo hồ sơ mới |
| `RENTER_HAS_ACTIVE_CONTRACT` | 422 | Ẩn danh: người này còn HĐ nháp / hiệu lực / thanh lý |
| `RENTER_HAS_UNSETTLED_INVOICES` | 422 | Ẩn danh: HĐ người này đứng tên còn phiếu chưa thu đủ / chờ hoàn |
| `RENTER_ID_NUMBER_EXISTS` | 409 | Body có `existingRenterId` → đề xuất dùng hồ sơ cũ |
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
| `NO_OCCUPANT` | 422 | Thêm ít nhất 1 người ở |
| `OCCUPANCY_OVERLAP` | 409 | Người này đang ở trong hợp đồng |
| `OCCUPANT_LIVES_ELSEWHERE` | 409 | Người này đang ở phòng khác — ghi chuyển đi ở HĐ cũ trước |
| `OCCUPANT_ALREADY_MOVED_OUT` | 409 | Đã ghi chuyển đi — thêm lại như người ở mới nếu quay lại |
| `DEPOSIT_TOO_HIGH` | 400 | Tiền cọc > 12 tháng tiền thuê |
| `NO_DEPOSIT` | 422 | Đánh dấu hoàn cọc cho HĐ không cọc |
| `INVALID_REFUND_AMOUNT` | 400 | Hoàn cọc: số tiền âm / lớn hơn cọc, hoặc ít hơn cọc mà không ghi lý do |
| `INVALID_REFUND_DATE` | 400 | Hoàn cọc / hoàn trả: ngày không hợp lệ |
| `DEPOSIT_ALREADY_REFUNDED` | 422 | Cọc đã hoàn / đã chuyển — bỏ đánh dấu trước |
| `DEPOSIT_NOT_REFUNDED` | 422 | Bỏ đánh dấu hoàn cọc khi chưa đánh dấu |
| `ROOM_TRANSFER_SAME_ROOM` | 400 | Chuyển phòng: trùng phòng hiện tại |
| `ROOM_TRANSFER_OTHER_PROPERTY` | 422 | Chuyển phòng sang khu khác — thanh lý + HĐ mới |
| `ROOM_TRANSFER_ROOM_UNAVAILABLE` | 422 | Phòng mới không trống từ ngày chuyển / bảo trì / ngừng dùng |
| `ROOM_TRANSFER_INVALID_DATE` | 400 | Ngày chuyển không sau ngày vào phòng hiện tại hoặc ở tương lai |
| `ROOM_TRANSFER_ALREADY_BILLED` | 422 | Điện nước phòng cũ đã lập phiếu tới sau ngày chuyển |
| `CONTRACT_EXPIRED_EXTEND_FIRST` | 422 | Ngày vào ở sau ngày hết hạn — hiện nút "Gia hạn" |
| `VEHICLE_OWNER_NOT_IN_CONTRACT` | 422 | Kết thúc đăng ký xe của người không còn thuộc HĐ trước |
| `HOUSEHOLD_HEAD_NOT_OCCUPANT` | 400 | Chủ hộ phải là một người ở |

| `LISTED_RENT_REQUIRED` | 400 | Áp giá niêm yết: phòng chưa có giá niêm yết |
| `DATE_OUTSIDE_CONTRACT` / `INVALID_END_DATE` | 422 | Ô ngày |
| `NOT_PERIOD_START` | 422 | Chọn ngày bắt đầu kỳ thu |
| `PERIOD_ALREADY_BILLED` | 422 | Kỳ đã lập phiếu |
| `RENT_TERM_EXISTS` | 409 | Đã có phụ lục từ ngày này |
| `CANNOT_EXTEND_INDEFINITE` | 422 | HĐ không thời hạn |
| `EXPIRED_REASON_INVALID` | 422 | "Hết hạn" chỉ cho HĐ có thời hạn, trả phòng từ ngày hết hạn — chọn lý do khác |
| `INDEFINITE_GROUND_ONLY` | 422 | Căn cứ "HĐ không thời hạn" dùng sai cho HĐ có thời hạn |
| `ABANDONED_NOTE_REQUIRED` | 422 | Bỏ đi không báo: nhập ghi chú |
| `CONTRACT_NOT_EXPIRED` | 422 | "Ở tiếp chưa ký lại" chỉ khi đã quá ngày hết hạn |
| `HOLDOVER_ALREADY` | 409 | Đã ghi nhận ở tiếp — tải lại |
| `RESIGN_REPRESENTATIVE_NOT_OCCUPANT` | 422 | Ký lại: chọn người đứng tên trong số người còn ở |
| `RESIGN_NO_OCCUPANT_LEFT` | 422 | Ký lại: không còn ai ở — thanh lý |
| `LIQUIDATION_BEFORE_END_DATE` | 422 | Chưa tới ngày trả phòng |
| `PLATE_ALREADY_REGISTERED` | 409 | Biển số đang gửi ở HĐ khác |
| `VEHICLE_ALREADY_ENDED` | 409 | Tải lại |

Cảnh báo mềm của HĐ (không phải lỗi, nằm trong `warnings` của response 2xx / chi tiết HĐ): `DEPOSIT_ABOVE_THREE_MONTHS`, `PLATE_FORMAT_UNUSUAL`;
giấy tờ / pháp lý (09/10/2026 — trước là lỗi chặn): `LESSOR_INFO_INCOMPLETE`, `REPRESENTATIVE_PHONE_MISSING`, `REPRESENTATIVE_UNDERAGE`,
`RELATIONSHIP_REQUIRED` / `RELATIONSHIP_NOTE_REQUIRED` / `RELATIONSHIP_GENDER_MISMATCH` / `RELATIONSHIP_AGE_MISMATCH`,
`SPOUSE_UNDER_MARRIAGE_AGE` / `MULTIPLE_SPOUSES`, `GUARDIAN_CONSENT_REQUIRED`, `SIGNED_DOCUMENT_MISSING`.

## Xuất Excel

| Code | HTTP | UI |
|------|------|----|
| `EXPORT_TOO_LARGE` | 422 | > 20.000 dòng — thu hẹp bộ lọc |
| `VALIDATION_FAILED` với `toDate` (`INVALID_DATE_RANGE`) | 400 | Lỗi dưới ô ngày |

## Công tơ

| Code | HTTP | UI |
|------|------|----|
| `METER_NOT_FOUND` / `READING_NOT_FOUND` | 404 | Tải lại |
| `METER_ALREADY_ACTIVE` | 409 | Phòng đã có công tơ khoản này — dùng "Thay công tơ" |
| `FEE_NOT_METERED` | 422 | Chỉ lắp công tơ cho điện nước theo công tơ |
| `METER_REMOVED` | 422 | Công tơ đã tháo |
| `INVALID_READING_DATE` | 422 | Ngày ngoài thời gian công tơ hoạt động / trước chỉ số đã ghi |
| `READING_NOT_MONOTONIC` | 422 | Chỉ số phải ≥ `previousValue` và ≤ `nextValue` (trong body) |
| `HANDOVER_READING_REQUIRED` | 422 | Kích hoạt: nhập chỉ số nhận phòng cho các công tơ trong `meterIds` |
| `FINAL_READING_REQUIRED` | 422 | Hoàn tất thanh lý: nhập chỉ số cuối cho các công tơ trong `meterIds` |
| `METER_NOT_IN_ROOM` | 422 | Công tơ không thuộc phòng / không hoạt động tại ngày đó — tải lại danh sách |

Cảnh báo mềm: `ROOM_WITHOUT_METER` (HĐ hiệu lực, phòng chưa có công tơ điện), `ROOM_HAS_OPEN_CONTRACT` (tháo công tơ khi phòng đang thuê).

## Phiếu tiền phòng & thu tiền

| Code | HTTP | UI |
|------|------|----|
| `INVOICE_NOT_FOUND` / `INVOICE_LINE_NOT_FOUND` / `PAYMENT_NOT_FOUND` | 404 | Tải lại |
| `INVOICE_NOT_DRAFT` | 422 | Phiếu đã chốt / hủy — chỉ đọc |
| `INVOICE_NOT_FINALIZED` | 422 | Phiếu chưa chốt — chốt trước khi thu / hủy |
| `INVOICE_HAS_ISSUES` | 422 | Còn thiếu chỉ số / giá (`issues` trong body) — xử lý rồi Tính lại |
| `DRAFT_STALE` | 409 | Dữ liệu nguồn đã đổi — bấm Tính lại rồi chốt |
| `NEGATIVE_TOTAL` | 422 | Giảm trừ lớn hơn phần thu — bớt giảm trừ; trả lại tiền cho người thuê thì dùng dòng Hoàn trả |
| `NOTE_REQUIRED` | 400 | Nhập lý do / ghi chú (phụ thu, sửa tiền phòng) |
| `INVOICE_HAS_PAYMENTS` | 422 | Đảo phiếu thu trước khi hủy phiếu |
| `ROOM_CHARGE_NO_TENANT` | 422 | Khoản phát sinh: phòng không có người thuê tại ngày phát sinh |
| `ROOM_CHARGE_NO_OPEN_INVOICE` | 422 | Khoản phát sinh: HĐ đã chốt phiếu quyết toán / đã kết thúc |
| `ROOM_CHARGE_LOCKED` | 422 | Khoản phát sinh đã nằm trên phiếu đã chốt — không đánh dấu / hủy |
| `REFUND_SOURCE_INVALID` | 422 | Phiếu nguồn của dòng hoàn trả không phải phiếu đã chốt của cùng HĐ |
| `REFUND_EXCEEDS_PAID` | 422 | Hoàn trả vượt số người thuê đã trả thật cho phiếu nguồn trừ các lần hoàn trước (`available`) |
| `REFUND_NOT_BULK` | 400 | Thêm hoàn trả hàng loạt — hoàn từng phiếu (chọn phiếu nguồn) |
| `REFUND_CONFIRMED` | 422 | Phiếu đã xác nhận hoàn tiền — bỏ xác nhận (`DELETE /invoices/{id}/refund`) trước khi hủy / xác nhận lại |
| `REFUND_NOT_CONFIRMED` | 422 | Phiếu chưa xác nhận hoàn tiền |
| `NOTHING_TO_REFUND` | 422 | Phiếu không có khoản phải trả lại người thuê (tổng không âm) |
| `INVALID_REFUND_DATE` | 400 | Ngày hoàn từ ngày lập phiếu tới hôm nay |
| `NO_DRAFT_INVOICE` | — | (kết quả từng phòng của `POST /invoices/manual-lines`) Phòng chưa có phiếu nháp tháng này |
| `INVOICE_SAVE_FAILED` | — | (kết quả từng phòng của `POST /invoices/manual-lines`) Không lưu được phiếu đó — tải lại rồi thử lại; `CONCURRENCY_CONFLICT`: phiếu vừa được sửa |
| `NOT_LATEST_INVOICE` | 422 | Hủy / xóa phiếu kỳ sau trước |
| `INVOICE_EXISTS` | 409 | Kỳ đã có phiếu (tạo song song) — tải lại |
| `INVALID_BILLING_MONTH` | 400 | Tháng thu dạng yyyy-MM |
| `READINGS_INVALID` | 422 | Lưu chỉ số: tô đỏ dòng theo `rowErrors[].index`, chưa lưu dòng nào |
| `READING_LOCKED` | 422 | Chỉ số đã dùng cho phiếu đã chốt — hủy phiếu trước |
| `PAYMENT_EXCEEDS_DEBT` | 422 | Số tiền lớn hơn số còn nợ |
| `WRITE_OFF_NOT_ALLOWED` | 403 | Bỏ nợ (riêng / kèm thanh toán / khi hoàn tất thanh lý) — phó quản lý chưa được chủ trọ cấp quyền |
| `NOTHING_TO_WRITE_OFF` | 422 | "Thanh toán + bỏ phần còn lại" mà số trả = số còn nợ — dùng "Đã thu" |
| `NO_OUTSTANDING_INVOICE` | 422 | Không còn phiếu nào chưa thu đủ |
| `PAYMENT_ALREADY_REVERSED` | 409 | Phiếu thu đã đảo — tải lại |
| `FINAL_INVOICE_EXISTS` | 409 | Đã có phiếu quyết toán — mở phiếu đó |
| `FINAL_INVOICE_REQUIRED` | 422 | Hoàn tất thanh lý: lập + chốt phiếu quyết toán trước |
| `FINAL_INVOICE_FINALIZED` | 422 | Hủy thanh lý: hủy phiếu quyết toán đã chốt trước |
| `INVOICE_DRAFT_EXISTS` | 409 | Còn phiếu nháp — chốt hoặc xóa |
| `PREVIOUS_PERIOD_NOT_BILLED` | 422 | Lập phiếu các kỳ trước trước |
| `CONTRACT_HAS_DEBT` | 422 | Hoàn tất thanh lý: còn nợ `outstanding` — chọn "Đã thu toàn bộ" / "Bỏ nợ" |
| `REFUND_PENDING` | 422 | Hoàn tất thanh lý: còn `refundDue` phải trả lại người thuê — xác nhận đã hoàn trên phiếu trước |
| `CONTRACT_NOT_BILLABLE` | 422 | HĐ đã kết thúc — không ghi / đảo phiếu thu, không xác nhận / bỏ xác nhận hoàn tiền |
| `INVOICE_AFTER_END_DATE` | 422 | Bắt đầu thanh lý / ký lại: còn phiếu kỳ sau ngày trả phòng — hủy / xóa trước |

Cảnh báo trên phiếu (`issues`, `severity: "Warning"`): `EDITED_BASE_CHANGED` (ô sửa tay mà số hệ thống tính lại đã đổi).

## Khoản thu

| Code | HTTP | UI |
|------|------|----|
| `FEE_TYPE_NOT_FOUND` / `FEE_PRICE_NOT_FOUND` / `CONTRACT_FEE_NOT_FOUND` | 404 | |
| `FEE_NAME_TAKEN` / `FEE_SYSTEM_CODE_TAKEN` / `FEE_PRICE_DATE_EXISTS` | 409 | Ô tên / ngày |
| `FEE_PRICE_LOCKED` | 422 | Ngày thuộc kỳ đã chốt phiếu |
| `FEE_TIERS_METERED_ONLY` | 400 | Giá theo bậc chỉ cho điện nước theo công tơ |
| `FEE_METERED_FOLLOWS_ROOM` | 400 | Điện / nước theo công tơ đi theo phòng — không gắn vào hợp đồng |
| `FEE_IN_USE` | 422 | Gỡ khỏi hợp đồng trước khi ngừng dùng |
| `FEE_HAS_ACTIVE_METERS` | 422 | Còn công tơ đang hoạt động — tháo / thay công tơ trước khi ngừng dùng |
| `FEE_ARCHIVED` / `FEE_NOT_IN_PROPERTY` | 422 | Chọn khoản khác khi gắn vào hợp đồng |
| `INVALID_QUANTITY` | 400 | Dịch vụ không theo số gói — bỏ trống số lượng |
| `INVALID_CHARGE_BASIS` | 400 | Điện nước không chọn cách tính dịch vụ / dịch vụ phải chọn cách tính |
| `CONTRACT_FEE_LATER_CHANGE_EXISTS` | 409 | Đã có thay đổi từ kỳ sau |

Cảnh báo mềm: `ELECTRICITY_PRICE_ABOVE_THRESHOLD`, `PARKING_QUANTITY_MISMATCH` (số xe đăng ký ≠ số lượng phí giữ xe), `LESSOR_TERMINATION_SHORT_NOTICE` (bên cho thuê đơn phương chấm dứt, báo trước < 30 ngày; HĐ không thời hạn < 90 ngày), `LESSEE_TERMINATION_SHORT_NOTICE` (bên thuê báo trước chưa đủ), `LESSEE_ABANDONED` (bỏ đi không báo — hướng dẫn xử lý).

## Mẫu hợp đồng

| Code | HTTP | UI |
|------|------|----|
| `CONTRACT_TEMPLATE_NOT_FOUND` | 404 | |
| `CONTRACT_TEMPLATE_NAME_TAKEN` | 409 | Ô tên mẫu |
| `CONTRACT_TEMPLATE_ARCHIVED` | 422 | Mẫu ngừng dùng — chọn mẫu khác |
| `DEPOSIT_NOT_ALLOWED` | 400 | Mẫu không cọc — khóa ô tiền cọc |
| `CONTRACT_TEMPLATE_ALREADY_ARCHIVED` / `CONTRACT_TEMPLATE_NOT_ARCHIVED` | 409 | Tải lại |
| `VALIDATION_FAILED` với key `contract.customFields.<key>` | 400 | Lỗi dưới ô trường tùy biến tương ứng (`REQUIRED`, sai kiểu, key không có trong mẫu) |

## Kỳ thu, nhập Excel (09/10/2026)

| Code | HTTP | UI |
|------|------|----|
| `REPRESENTATIVE_ID_REQUIRED` | 422 | Người đứng tên chưa có số giấy tờ — bổ sung ở hồ sơ |
| `ID_NUMBER_REQUIRED` | 400 | Từ 14 tuổi phải có số giấy tờ / đổi loại giấy tờ phải nhập số mới |
| `INVALID_BILLING_START_DATE` | 400 | "Tính tiền từ ngày" ngoài thời gian HĐ / trước hôm nay quá 1 năm |
| `BILLING_SETTINGS_DRAFT_INVOICES` | 422 | Chốt / xóa phiếu nháp của khu trước khi đổi ngày chốt |
| `TRANSITION_ADJUST_OUT_OF_RANGE` | 400 | Số ngày điều chỉnh kỳ chuyển tiếp ngoài 0..dư (thiếu..0) |
| `IMPORT_FILE_INVALID` / `IMPORT_FILE_TOO_LARGE` / `IMPORT_TEMPLATE_MISMATCH` / `IMPORT_TOO_MANY_ROWS` | 400 | Lỗi cả file — tải mẫu mới, sửa file |

Cảnh báo phiếu (`issues`, `Warning`): `UNUSUAL_USAGE`, `TWO_RENT_PERIODS`. Cảnh báo HĐ: `OCCUPANT_ID_MISSING`.
Lỗi / cảnh báo từng dòng import nằm trong kết quả xem trước / lưu — xem [imports.md](imports.md).
