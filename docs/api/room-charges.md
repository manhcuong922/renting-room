# Khoản phát sinh theo phòng

Phụ thu hoặc bù cho người thuê **xảy ra giữa tháng**: tạo ngay lúc xảy ra, cuối tháng tự có trên phiếu tiền phòng. Plan: M07 BL-UC-15..17, BL-BR-30..37.

Ví dụ: ngày 3/3 phòng 301 hỏng khóa, quản lý khu thuê thợ thay ⇒ tạo phụ thu "Thay khóa 250k"; khu mất nước 2 ngày ⇒ tạo khoản bù 50k.

Quyền: chủ trọ và phó quản lý.

## Quy tắc chính

- **Chỉ tạo cho phòng đang có người thuê** tại ngày phát sinh (phòng trống → 422 `ROOM_CHARGE_NO_TENANT`). Khoản gắn với HĐ đang ở phòng ngày đó — kể cả HĐ sau đó đã chuyển sang phòng khác.
- **Trạng thái thanh toán** (`isSettled`):
  - `false` — chưa thanh toán: phụ thu cộng vào phiếu, bù trừ vào phiếu.
  - `true` — phụ thu người thuê **đã trả ngay** / bù chủ trọ **đã trả ngay**: vẫn hiện trên phiếu cho đầy đủ nhưng **không tính vào tổng**.
- **Vào phiếu nào**: phiếu nháp của HĐ có ngày cuối kỳ ≥ ngày phát sinh.
  - Nháp đã có ⇒ gắn ngay. Chưa có ⇒ khoản **chờ**, tự vào khi lập / tính lại nháp.
  - Kỳ chứa ngày phát sinh đã chốt ⇒ vào phiếu kỳ kế tiếp. Đang trả phòng ⇒ vào phiếu quyết toán.
- **Khóa theo phiếu**: khoản đã nằm trên phiếu đã chốt thì không đánh dấu / hủy được (422 `ROOM_CHARGE_LOCKED`). Hủy phiếu hoặc xóa nháp ⇒ khoản quay về chờ.
- **Thêm tay trên nháp** (`POST /invoices/{id}/manual-lines` loại `Surcharge` / `ManualDiscount`) cũng tạo khoản phát sinh của phòng; xóa dòng đó ⇒ khoản bị hủy.

## Trạng thái hiển thị (`status`)

| `status` | Ý nghĩa |
|----------|---------|
| `Pending` | Chờ vào phiếu |
| `OnDraft` | Đang trên phiếu nháp — còn đánh dấu / hủy được |
| `Billed` | Đã vào phiếu đã chốt (`invoiceNo`, `billingMonth`) — khóa |
| `Cancelled` | Đã hủy (`cancelReason`) |

## Tạo khoản

`POST /rooms/{roomId}/charges` — **Idempotency-Key**

```json
{ "kind": "Surcharge", "description": "Thay khóa cửa", "amount": 250000, "incurredOn": "2026-03-03", "reason": "Người thuê làm hỏng khóa",
  "settled": false, "settledOn": null, "method": null }
```

`kind`: `Surcharge` (phụ thu) / `Credit` (bù). `settled: true` ⇒ đã thanh toán ngay (`settledOn` bỏ trống = ngày phát sinh, `method` bỏ trống = tiền mặt).

→ **201**

```json
{ "id": "…", "roomId": "…", "roomCode": "301", "contractId": "…", "kind": "Surcharge", "description": "Thay khóa cửa", "amount": 250000,
  "incurredOn": "2026-03-03", "reason": "Người thuê làm hỏng khóa", "isSettled": false, "settledOn": null, "settledMethod": null,
  "status": "Pending", "invoiceId": null, "invoiceNo": null, "billingMonth": null, "cancelReason": null, "createdAt": "…" }
```

## Các thao tác khác

| Thao tác | Endpoint |
|----------|----------|
| Danh sách của phòng | `GET /rooms/{roomId}/charges?status=` |
| Danh sách theo khu / khoảng ngày | `GET /room-charges?propertyId=&roomId=&status=&from=&to=` |
| Đánh dấu đã thanh toán / đã hoàn | `POST /room-charges/{id}/settle` `{ "settledOn": "2026-03-05", "method": "Cash" }` |
| Bỏ đánh dấu (nhập nhầm) | `DELETE /room-charges/{id}/settle` |
| Hủy khoản | `POST /room-charges/{id}/cancel` `{ "reason": "Ghi nhầm phòng" }` |

Đánh dấu / bỏ đánh dấu / hủy khoản đang trên nháp ⇒ dòng trên nháp và tổng nháp đổi theo ngay.

## Trên phiếu

Dòng của khoản phát sinh có `roomChargeId` và `isSettled` ([invoices.md](invoices.md)). Dòng `isSettled: true` hiện kèm ghi chú "đã thu / đã trả", không cộng vào `totalAmount`.
Khoản bù chưa trả mà lớn hơn phần thu của phiếu ⇒ chưa đưa vào, nháp có cảnh báo `ROOM_CHARGE_NOT_ATTACHED`, khoản chờ phiếu sau.

## Lỗi

| Lỗi | Khi nào |
|-----|---------|
| 422 `ROOM_CHARGE_NO_TENANT` | Phòng không có người thuê tại ngày phát sinh |
| 422 `ROOM_CHARGE_NO_OPEN_INVOICE` | HĐ đã chốt phiếu quyết toán / đã kết thúc |
| 422 `ROOM_CHARGE_LOCKED` | Khoản đã nằm trên phiếu đã chốt |
| 422 `ROOM_CHARGE_CANCELLED` | Khoản đã hủy |
| 422 `ROOM_CHARGE_NOT_SETTLED` | Bỏ đánh dấu khi chưa đánh dấu |
| 400 `INVALID_INCURRED_DATE` / `INVALID_SETTLED_DATE` | Ngày ở tương lai |
