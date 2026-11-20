# Gửi phiếu và nhắc nợ qua Zalo Bot / email (M11)

Thiết kế: [../plans/11-notification.md](../plans/11-notification.md). Tạo bot và lấy token: [../guides/zalo-bot-setup.md](../guides/zalo-bot-setup.md).
Quyền: chủ trọ và quản lý khu (trong khu được giao). SystemAdmin → 403.

## Cách hoạt động (để dựng màn hình)

1. **Máy chủ** có token Zalo (`ZaloBot:Token`) và/hoặc SMTP (`Email:Smtp`) thì kênh đó "đã cấu hình" — xem `GET /notifications/channels`.
2. **Người thuê liên kết Zalo**: chủ trọ tạo mã (`POST /renters/{id}/zalo-link-code`) → đưa mã cho người thuê → người thuê nhắn mã cho bot → hồ sơ có `zaloLinked = true`.
   Người thuê nhắn `HUY` cho bot để ngừng nhận tin.
3. **Khu bật tự gửi** (`PUT /properties/{id}/notification-settings`): chốt phiếu ⇒ hệ thống tự gửi cho **người đứng tên hợp đồng** trên mọi kênh họ có
   (Zalo nếu đã liên kết, email nếu hồ sơ có email). Quá hạn mà còn nợ ⇒ tự nhắc theo các mốc đã cài.
4. Tin đi qua hàng đợi: lỗi tạm thì tự thử lại (1 → 5 → 30 → 120 phút, tối đa 5 lần). Tin **tự động** chỉ gửi trong **08:00–20:00 giờ VN**; gửi lại thủ công thì gửi ngay.

Mặc định **tắt** ở mọi khu — không có tin nào tự đi cho tới khi chủ trọ bật.

Nội dung tin chỉ có: tên khu, mã phòng, tên người đứng tên, tháng thu, các dòng tiền, tổng, còn nợ, hạn nộp, tài khoản ngân hàng của khu.
**Không** có số giấy tờ, ngày sinh, số điện thoại.

## Endpoint

| Method | Route | Mô tả |
|---|---|---|
| GET | `/notifications/channels` | Kênh nào dùng được |
| GET | `/notifications/summary?propertyId=&billingMonth=` | Số tin theo trạng thái của khu + tháng thu |
| GET | `/invoices/{id}/notifications` | Lịch sử gửi của một phiếu |
| POST | `/invoices/{id}/notifications/resend` | Gửi lại phiếu đã chốt (202) |
| POST | `/renters/{id}/zalo-link-code` | Tạo mã liên kết Zalo |
| DELETE | `/renters/{id}/zalo-link` | Hủy liên kết Zalo (204) |
| PUT | `/properties/{id}/notification-settings` | Bật / tắt tự gửi, mốc nhắc nợ |
| POST | `/properties/{id}/announcements` | Gửi thông báo chung của khu qua Zalo (202) |
| GET | `/properties/{id}/announcements` | Các thông báo chung đã gửi của khu |

### `GET /notifications/channels`

```json
{ "zalo": { "configured": true, "botName": "Bot Nha Tro Hoa Mai" }, "email": { "configured": false } }
```

`configured = false` ⇒ ẩn / làm mờ nút liên kết Zalo (hoặc hiện "Máy chủ chưa cấu hình Zalo"). Kênh chưa cấu hình bị bỏ qua khi tạo tin.

### `POST /renters/{id}/zalo-link-code`

```json
{ "code": "K7XM2QPD", "expiresAt": "2026-11-16T05:00:00Z", "botName": "Bot Nha Tro Hoa Mai" }
```

- Mã 8 ký tự (không có `0 O 1 I L`), **hạn 48 giờ**, dùng một lần. Tạo mã mới ⇒ mã cũ chưa dùng hết hiệu lực.
- Mã thô **chỉ trả một lần** (server chỉ lưu hash) — hiện rõ kèm tên bot để chủ trọ đọc / gửi cho người thuê; mất mã thì tạo lại.
- Lỗi: 404 `RENTER_NOT_FOUND`, 422 `RENTER_ANONYMIZED`, 409 `ZALO_BOT_NOT_CONFIGURED`.
- Hồ sơ người thuê (`GET /renters/{id}`) có thêm `zaloLinked` (bool) và `zaloLinkedAt`. Chat id Zalo **không** trả ra API.

### `DELETE /renters/{id}/zalo-link`

204. Tin Zalo đang chờ của người đó bị hủy. 404 `RENTER_NOT_FOUND`.

### `PUT /properties/{id}/notification-settings`

```json
{ "notifyOnFinalize": true, "overdueReminderDays": [1, 3, 7] }
```

- `overdueReminderDays`: tối đa 3 mốc, mỗi mốc 1–60 (ngày **sau hạn nộp**), không trùng, tăng dần; `[]` = không nhắc. Sai ⇒ 400 `INVALID_REMINDER_DAYS`.
- Trả về chi tiết khu; hai trường nằm trong `billing.notifyOnFinalize`, `billing.overdueReminderDays`.
- Job lỡ ngày chạy bù: chỉ gửi **mốc lớn nhất** đã tới hạn, không dồn nhiều tin một lúc.

### `GET /invoices/{id}/notifications`

```json
[{ "id": "…", "channel": "Zalo", "kind": "InvoiceIssued", "step": 0, "status": "Sent",
   "attempts": 1, "lastError": null, "sentAt": "2026-11-14T05:00:02Z", "createdAt": "2026-11-14T05:00:00Z" }]
```

- `channel`: `Zalo` | `Email`. `kind`: `InvoiceIssued` | `OverdueReminder` (`step` = số ngày sau hạn) | `ManualResend` (`step` = lần gửi lại thứ mấy).
  (`Announcement` = thông báo chung — không nằm trong lịch sử của phiếu, xem `GET /properties/{id}/announcements`.)
- `status`: `Pending` (chờ / chờ thử lại) · `Sending` · `Sent` · `Failed` · `Skipped` (nhắc nợ nhưng phiếu đã hết nợ / đã hủy) · `Cancelled` (hủy phiếu / hủy liên kết).
- `lastError`: mã ngắn (VD `HTTP_500`, `RATE_LIMITED`, `NETWORK`, `HTTP_403`) — không chứa token hay nội dung tin.

### `POST /invoices/{id}/notifications/resend`

Body tùy chọn: `{ "channels": ["Zalo"] }` (bỏ trống = mọi kênh người đứng tên có). Hỗ trợ `Idempotency-Key`. Trả **202** và danh sách tin vừa tạo.

| Mã | HTTP | Khi nào |
|---|---|---|
| `INVOICE_NOT_FOUND` | 404 | Không có phiếu (hoặc của tổ chức / khu khác) |
| `INVOICE_NOT_FINALIZED` | 409 | Phiếu còn nháp |
| `INVOICE_VOIDED` | 409 | Phiếu đã hủy |
| `INVALID_CHANNEL` | 400 | Kênh không phải `Zalo` / `Email`, hoặc trùng |
| `NO_NOTIFICATION_CHANNEL` | 422 | Người đứng tên chưa có kênh dùng được — gợi ý chủ trọ liên kết Zalo / thêm email |

### `GET /notifications/summary`

`billingMonth` là ngày đầu tháng (`2026-11-01`) — khác ⇒ 400 `INVALID_BILLING_MONTH`.

```json
{ "total": 12, "pending": 2, "sent": 9, "failed": 1, "skipped": 0, "cancelled": 0, "noChannel": 1 }
```

`noChannel` = phiếu đã chốt (tổng > 0) của khu bật tự gửi mà không có dòng nào — người đứng tên chưa có kênh. Dùng để nhắc chủ trọ gửi tay.

### `POST /properties/{id}/announcements` — thông báo chung (tăng giá điện, cắt nước, bảo trì…)

Gửi cho **người đứng tên** của HĐ đang hiệu lực (`Active` / `Liquidating`) ở các phòng của khu, **chỉ qua Zalo** (đã liên kết), **gửi ngay — không giới hạn 08:00–20:00**.
Người ở cùng không nhận. Cùng một chat Zalo (một người đứng tên 2 phòng, hoặc nhiều hồ sơ gắn một chat) ⇒ đúng 1 tin. Hỗ trợ `Idempotency-Key`. Đã gửi thì **không thu hồi được**.

```json
{ "title": "Tăng giá điện từ 01/12/2026", "body": "Giá điện tăng lên 3.800đ/kWh, áp dụng từ kỳ thu tháng 12/2026.", "roomIds": null }
```

- `title` 1–100 ký tự, `body` 1–1500 ký tự (bỏ khoảng trắng đầu / cuối). `roomIds` bỏ trống / `[]` = mọi phòng đang thuê; có thì ≤ 500, không trùng.
- Trả **202**:

```json
{ "id": "…", "title": "Tăng giá điện từ 01/12/2026", "roomCount": 12, "queued": 9,
  "noChannelRooms": [{ "roomId": "…", "roomCode": "P102" }, { "roomId": "…", "roomCode": "P205" }] }
```

`roomCount` = số phòng có người đứng tên trong phạm vi gửi (phòng trống không tính); `queued` = số tin đã xếp; `noChannelRooms` = phòng mà người đứng tên chưa liên kết Zalo
⇒ hiện cho chủ trọ nhắn tay.

Tin người thuê nhận:

```
[Khu A — Thu sau] Tăng giá điện từ 01/12/2026

Giá điện tăng lên 3.800đ/kWh, áp dụng từ kỳ thu tháng 12/2026.

(Nhắn HUY để ngừng nhận tin.)
```

| Mã | HTTP | Khi nào |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Tiêu đề / nội dung rỗng hoặc quá dài; `roomIds` trùng / quá 500 |
| `PROPERTY_NOT_FOUND` | 404 | Không có khu (hoặc của tổ chức khác / ngoài khu được giao) |
| `ROOM_NOT_FOUND` | 404 | Có phòng trong `roomIds` không thuộc khu |
| `ZALO_BOT_NOT_CONFIGURED` | 409 | Máy chủ chưa có token Zalo |
| `NO_NOTIFICATION_CHANNEL` | 422 | Không người đứng tên nào trong phạm vi đã liên kết Zalo — **không lưu** thông báo |

### `GET /properties/{id}/announcements?page=&pageSize=`

Trang chuẩn (`items`, `page`, `pageSize`, `totalCount`, `totalPages`), mới nhất trước:

```json
{ "items": [{ "id": "…", "title": "Tăng giá điện từ 01/12/2026", "body": "…", "allRooms": true, "roomCount": 12, "recipientCount": 9,
  "pending": 0, "sent": 8, "failed": 1, "cancelled": 0, "createdAt": "2026-11-20T08:00:00Z" }], "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1 }
```

`pending` gồm cả đang gửi. Tin quá 180 ngày bị dọn khỏi hàng đợi ⇒ các số đếm về 0, thông báo vẫn còn trong danh sách. 404 `PROPERTY_NOT_FOUND`.

## Bot Zalo nói gì

Bot chỉ hiểu **mã liên kết** và lệnh `HUY`. Mã sai và mã hết hạn cùng một câu trả lời. Một cuộc chat nhập sai quá 5 lần / giờ thì bot im lặng tới hết giờ.
