# Khoản phát sinh theo phòng

Phụ thu hoặc bù cho người thuê **xảy ra giữa tháng**: tạo ngay lúc xảy ra, cuối tháng tự có trên phiếu tiền phòng. Plan: M07 BL-UC-15..17, BL-BR-30..37.

Ví dụ: ngày 3/3 phòng 301 hỏng khóa, quản lý khu thuê thợ thay ⇒ tạo phụ thu "Thay khóa 250k"; khu mất nước 2 ngày ⇒ tạo khoản bù 50k.

Quyền: chủ trọ và phó quản lý.

## Quy tắc chính

- **Chỉ tạo cho phòng đang có người thuê** tại ngày phát sinh (phòng trống → 422 `ROOM_CHARGE_NO_TENANT`). Khoản gắn với HĐ đang ở phòng ngày đó — kể cả HĐ sau đó đã chuyển sang phòng khác.
- **Giới hạn khoản bù** (11/10/2026): khoản bù **chưa trả** cộng các khoản bù chưa trả khác của HĐ (chưa nằm trên phiếu đã chốt) không vượt
  **một tháng tiền phòng** → 422 `ROOM_CHARGE_CREDIT_TOO_HIGH` (`limit` = tiền phòng). Bù nhiều hơn thì trả trực tiếp rồi tạo khoản với
  "đã trả ngay" — khoản đã trả không bị giới hạn. Phụ thu không bị giới hạn này.
- **Khoản hệ thống** (`isSystem = true`) — **chỉ còn dữ liệu cũ** (17/10/2026: chuyển phòng không tạo khoản này nữa, sản lượng phòng cũ cộng vào
  dòng điện nước của phiếu): khoản chốt điện nước của phòng cũ khi chuyển phòng trước 17/10/2026 — kèm `feeTypeId`, `quantity`, `unitPrice`.
  Chỉ đánh dấu / bỏ đánh dấu "đã thu ngay"; sửa / hủy → 422 `ROOM_CHARGE_SYSTEM`.
- **Khoản điều chỉnh chỉ số** (`isSystem = true`, 20/10/2026 — [meters.md](meters.md#điều-chỉnh-chỉ-số-đã-chốt-mt-uc-09--viết-lại-20102026)): tạo khi
  điều chỉnh chỉ số đã chốt chọn "Đẩy sang hóa đơn sau"; kèm `feeTypeId`. Mỗi dòng hóa đơn nguồn tối đa 1 khoản — đổi số tiền bằng cách điều chỉnh
  lại chỉ số; **không sửa / hủy tay** (422 `ROOM_CHARGE_SYSTEM`), chỉ đánh dấu đã trả / đã thu ngay; không tính vào giới hạn khoản bù; hóa đơn nguồn bị
  hủy ⇒ khoản tự hủy.
- **Trạng thái thanh toán** (`isSettled`):
  - `false` — chưa thanh toán: phụ thu cộng vào phiếu, bù trừ vào phiếu.
  - `true` — phụ thu người thuê **đã trả ngay** / bù chủ trọ **đã trả ngay**: vẫn hiện trên phiếu cho đầy đủ nhưng **không tính vào tổng**.
- **Vào phiếu nào**: phiếu nháp của HĐ có ngày cuối kỳ ≥ ngày phát sinh.
  - Nháp đã có ⇒ gắn ngay. Chưa có ⇒ khoản **chờ**, tự vào khi lập / tính lại nháp.
  - Kỳ chứa ngày phát sinh đã chốt ⇒ vào phiếu kỳ kế tiếp. Đang trả phòng ⇒ vào hóa đơn trả phòng.
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
  "status": "Pending", "invoiceId": null, "invoiceNo": null, "billingMonth": null, "cancelReason": null, "createdAt": "…",
  "isSystem": false, "feeTypeId": null, "quantity": null, "unitPrice": null }
```

## Các thao tác khác

| Thao tác | Endpoint |
|----------|----------|
| Danh sách của phòng | `GET /rooms/{roomId}/charges?status=` |
| Danh sách theo khu / khoảng ngày | `GET /room-charges?propertyId=&roomId=&status=&from=&to=` |
| Đánh dấu đã thanh toán / đã hoàn | `POST /room-charges/{id}/settle` `{ "settledOn": "2026-03-05", "method": "Cash" }` |
| Bỏ đánh dấu (nhập nhầm) | `DELETE /room-charges/{id}/settle` — khoản **bù** bỏ đánh dấu sẽ quay lại chờ trừ vào phiếu nên cũng phải trong giới hạn 1 tháng tiền phòng: vượt → 422 `ROOM_CHARGE_CREDIT_TOO_HIGH`, khoản giữ nguyên "đã thanh toán" (13/10/2026) |
| Hủy khoản | `POST /room-charges/{id}/cancel` `{ "reason": "Ghi nhầm phòng" }` |

Đánh dấu / bỏ đánh dấu / hủy khoản đang trên nháp ⇒ dòng trên nháp và tổng nháp đổi theo ngay.

## Trên phiếu

Dòng của khoản phát sinh có `roomChargeId` và `isSettled` ([invoices.md](invoices.md)). Dòng `isSettled: true` hiện kèm ghi chú "đã thu / đã trả", không cộng vào `totalAmount`.
Khoản bù chưa trả mà lớn hơn phần thu của **phiếu tháng** ⇒ chưa đưa vào, nháp có cảnh báo `ROOM_CHARGE_NOT_ATTACHED`, khoản chờ phiếu sau.
**Hóa đơn trả phòng** (20/10/2026) luôn nhận khoản bù, kể cả làm hóa đơn âm ⇒ Chờ hoàn ([invoices.md](invoices.md#hóa-đơn-trả-phòng)).

## Lỗi

| Lỗi | Khi nào |
|-----|---------|
| 422 `ROOM_CHARGE_NO_TENANT` | Phòng không có người thuê tại ngày phát sinh |
| 422 `ROOM_CHARGE_NO_OPEN_INVOICE` | HĐ đã chốt hóa đơn trả phòng / đã kết thúc |
| 422 `ROOM_CHARGE_LOCKED` | Khoản đã nằm trên phiếu đã chốt |
| 422 `ROOM_CHARGE_CANCELLED` | Khoản đã hủy |
| 422 `ROOM_CHARGE_NOT_SETTLED` | Bỏ đánh dấu khi chưa đánh dấu |
| 400 `INVALID_INCURRED_DATE` / `INVALID_SETTLED_DATE` | Ngày ở tương lai |

## Cấu trúc trả về (bổ sung 13/10/2026)

`GET /rooms/{roomId}/charges` và `GET /room-charges?…` → **mảng, không phân trang**, mới nhất trước:

```json
[ { "id": "…", "roomId": "…", "roomCode": "103", "contractId": "…", "kind": "Credit", "description": "Mất nước 1 ngày", "amount": 50000,
    "incurredOn": "2026-09-30", "reason": "Sự cố bồn nước", "isSettled": false, "settledOn": null, "settledMethod": null,
    "status": "OnDraft", "invoiceId": "…", "invoiceNo": null, "billingMonth": "2026-09", "cancelReason": null,
    "createdAt": "2026-10-12T19:17:12.651281+00:00", "isSystem": false, "feeTypeId": null, "quantity": null, "unitPrice": null } ]
```

| `status` | Nghĩa | Nút |
|----------|-------|-----|
| `Pending` | Chờ lên phiếu (chưa có phiếu của kỳ) | Hủy · Đánh dấu / bỏ đánh dấu đã thanh toán (không có lệnh sửa — nhập sai thì hủy rồi tạo lại) |
| `OnDraft` | Đang nằm trên phiếu nháp (`invoiceId`, `billingMonth`; `invoiceNo` còn `null`) | Như trên — phiếu nháp tự cập nhật |
| `Billed` | Đã vào phiếu đã chốt (`invoiceNo`) | Chỉ xem (422 `ROOM_CHARGE_LOCKED`) |
| `Cancelled` | Đã hủy (`cancelReason`) | Chỉ xem, gạch ngang |

- `isSystem: true` = khoản chốt điện nước khi chuyển phòng của dữ liệu trước 17/10/2026 (`feeTypeId`, `quantity`, `unitPrice` có giá trị): không hủy tay.
- **Kết quả thao tác:** tạo → 201 + khoản vừa tạo; đánh dấu đã thanh toán (`POST …/settle`), bỏ đánh dấu (`DELETE …/settle`), hủy
  (`POST …/cancel`) → **200 + chính khoản đó sau khi đổi** (cùng cấu trúc trên).

## Màn hình gợi ý

- **Tab "Khoản phát sinh" trong chi tiết phòng:** danh sách của phòng + nút "Thêm phụ thu" / "Thêm khoản bù" (chọn đã thanh toán ngay hay chưa).
- **Trang theo khu:** bảng lọc theo trạng thái / khoảng ngày (`GET /room-charges?propertyId=`), nhãn màu theo `status`, biểu tượng khóa cho `isSystem`.
- Trên phiếu nháp, dòng có `roomChargeId` là khoản phát sinh: thao tác tại màn khoản phát sinh, không sửa trên phiếu.
