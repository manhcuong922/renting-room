# Thu tiền

Ghi nhận tiền người thuê đã trả (tiền mặt / chuyển khoản tự kiểm) vào **phiếu tiền phòng đã chốt** ([invoices.md](invoices.md)).
Mỗi lần thu sinh **phiếu thu** số `PT2026-000045`. Thanh toán online / mã QR: làm sau khi có gửi phiếu qua Zalo (P2–P3).

Quyền: chủ trọ và quản lý khu.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Nút **"Đã thu"** trên phiếu (mặc định đủ số còn nợ, sửa được nếu thu một phần) | `POST /contracts/{contractId}/payments` với `invoiceId` |
| Thu theo hợp đồng (tự trừ phiếu cũ nhất trước) | `POST /contracts/{contractId}/payments` không `invoiceId` |
| Lịch sử thu của phòng / hợp đồng / phiếu | `GET /payments?roomId=` · `?contractId=` · `?invoiceId=` |
| **Sửa ô "Đã thu"** của hóa đơn (gõ số mới — tăng hoặc giảm, 09/11/2026) | `PUT /invoices/{id}/collected` |
| Đảo phiếu thu nhập sai (trong Lịch sử thu) | `POST /payments/{id}/reverse` |
| **"Thanh toán + bỏ phần còn lại"** trên phiếu (chủ trọ / quản lý khu) | `POST /invoices/{id}/pay-and-write-off` |
| **Bỏ nợ** phiếu (một phần / toàn bộ, chủ trọ / quản lý khu) | `POST /invoices/{id}/write-off` |
| Nhãn **"Còn nợ"** trên thẻ phòng | `outstandingAmount` của phòng ([rooms.md](rooms.md)) |

## Ghi thu

`POST /contracts/{contractId}/payments` — **Idempotency-Key**

```json
{ "amount": 3378000, "method": "Cash", "paidAt": "2026-11-07", "invoiceId": "…", "payerName": null, "reference": null, "note": null }
```

`method`: `Cash` / `BankTransfer` / `EWallet`. `reference`: mã giao dịch ngân hàng (≤ 100).

→ **201**

```json
{
  "id": "…", "receiptNo": "PT2026-000045", "contractId": "…", "amount": 3378000, "method": "Cash", "paidAt": "2026-11-07",
  "payerName": null, "reference": null, "note": null, "status": "Recorded", "kind": "Receipt", "reversedAt": null, "reverseReason": null,
  "allocations": [ { "invoiceId": "…", "invoiceNo": "PB2026-000101", "amount": 3378000 } ]
}
```

| Lỗi | Khi nào |
|-----|---------|
| 422 `PAYMENT_EXCEEDS_DEBT` | Số tiền lớn hơn số còn nợ của phiếu (hoặc tổng nợ của HĐ) — chưa hỗ trợ trả thừa |
| 422 `NO_OUTSTANDING_INVOICE` | HĐ không còn phiếu nào chưa thu đủ |
| 422 `INVOICE_NOT_FINALIZED` | Phiếu còn nháp / đã hủy |
| 400 `INVALID_PAID_AT` | Ngày thu ở tương lai, hoặc trước ngày bắt đầu HĐ quá 60 ngày |
| 422 `CONTRACT_NOT_BILLABLE` | HĐ đã kết thúc (hoàn tất thanh lý đã xử lý hết nợ) |

`kind`: `Receipt` tiền thật · `WriteOff` **bỏ nợ** — không tính doanh thu · `Refund` **phiếu hoàn tiền** (20/10/2026) — tiền chủ trọ trả lại người thuê.
Tiền cọc không đi qua phiếu thu (chỉ theo dõi trên HĐ — [contracts.md](contracts.md#tiền-cọc)).

**Phiếu hoàn tiền** (`kind: Refund`, số `PC{yyyy}-{000000}` — PM-BR-37): chỉ hệ thống tạo, khi điều chỉnh chỉ số "Xử lý ngay" làm hóa đơn đã thu
thấp hơn số người thuê trả thật ([meters.md](meters.md#điều-chỉnh-chỉ-số-đã-chốt-mt-uc-09--viết-lại-20102026)). `allocations` = hóa đơn được hoàn; hóa đơn
**giảm** `paidAmount` / `collectedAmount` đúng số hoàn. Hiện trong danh sách phiếu thu với nhãn "Hoàn tiền" (số âm trong tổng thu). **Đảo** được như
phiếu thu: hóa đơn tăng lại số đã thu — vượt tổng mới → 422 `PAYMENT_EXCEEDS_DEBT`. Điều chỉnh làm giảm phần **bỏ nợ** ⇒ phiếu bỏ nợ cũ bị
**đảo** (`Reversed`, lý do "Điều chỉnh chỉ số — giảm phần bỏ nợ") và có phiếu bỏ nợ **mới** phần còn lại (cùng ngày, cùng lý do).
Số phiếu thu `PT{năm lập phiếu}-…` (ghi bù ngày thu năm trước vẫn mang năm hiện tại).

## Trả dần và bỏ nợ (F2)

- **Trả dần**: thu một phần bằng "Đã thu" bình thường — phần còn lại vẫn là nợ (`PartiallyPaid`).
- **Thanh toán + bỏ phần còn lại** — `POST /invoices/{id}/pay-and-write-off` — **Idempotency-Key**, chủ trọ hoặc quản lý khu

  ```json
  { "amount": 2000000, "method": "Cash", "paidAt": "2026-10-09", "payerName": null, "reference": null, "reason": "Khó khăn, chủ trọ cho 1tr" }
  ```

  → **200** `{ "payment": Payment (kind Receipt), "writeOff": Payment (kind WriteOff) }` — 1 transaction; phiếu thành `WrittenOff`,
  tiền thu thật chỉ là `amount`. `amount` = số còn nợ → 422 `NOTHING_TO_WRITE_OFF` (dùng "Đã thu"); lớn hơn → 422 `PAYMENT_EXCEEDS_DEBT`.
  Phiếu sau đó: `collectedAmount` = `amount`, `writtenOffAmount` = phần bỏ, `paidAmount` = tổng phiếu. Bỏ nợ riêng (không thu) ⇒ `collectedAmount` không
  đổi; phiếu chưa thu đồng nào vẫn là `Unpaid` / `Overdue` (không thành `PartiallyPaid`).
- **Bỏ nợ riêng** — `POST /invoices/{id}/write-off` `{ "amount": null, "reason": "…" }` — **Idempotency-Key**, chủ trọ hoặc quản lý khu;
  `amount` null = toàn bộ số còn nợ → **200** phiếu thu `kind: WriteOff`.
- Được làm **bất kỳ lúc nào** HĐ còn hiệu lực / đang thanh lý (không chỉ khi trả phòng). Từ 08/11/2026 quản lý khu luôn được bỏ nợ trong khu được giao
  (bỏ quyền cấp riêng), kể cả "Bỏ nợ" khi hoàn tất thanh lý. Bỏ nợ không tính doanh thu, đảo được như phiếu thu thường.

## Sửa số đã thu (09/11/2026)

Người dùng chỉ thấy **một ô "Đã thu"** trên hóa đơn và gõ số mới; phiếu thu vẫn được ghi phía sau (thu nhiều lần, tiền mặt / chuyển khoản,
báo cáo theo ngày nhận tiền) — UI không cần hiện phiếu thu.

`PUT /invoices/{id}/collected` (Idempotency-Key tùy chọn)

```json
{ "amount": 2800000, "expectedAmount": 3000000, "reason": "Đếm lại tiền mặt thiếu", "paidAt": null, "method": null }
```

| Trường | Ghi chú |
|--------|---------|
| `amount` | Số đã thu **mới** (0 … tổng hóa đơn − bỏ nợ) |
| `expectedAmount` | `summary.collectedAmount` người dùng đang thấy — khác số hiện tại ⇒ 409 `COLLECTED_AMOUNT_CHANGED` (người khác vừa sửa → tải lại) |
| `reason` | **Bắt buộc khi giảm** (400); tăng thì là ghi chú phiếu thu (tùy chọn) |
| `paidAt`, `method` | Chỉ dùng khi **tăng** — ngày thu / cách thu của phần thêm, mặc định hôm nay / `Cash` |

→ **200** `{ "summary": { …tóm tắt hóa đơn… }, "payments": [ …phiếu vừa ghi / đảo… ] }`

- **Tăng** (VD 2tr → 3tr): ghi 1 phiếu thu 1tr vào đúng hóa đơn.
- **Giảm** (VD 3tr → 2,8tr): đảo phiếu thu **mới nhất** của hóa đơn rồi ghi lại phần còn giữ (2,8tr) với **cùng** ngày thu và cách thu; phiếu thu theo
  hợp đồng có phần của hóa đơn khác thì phần đó ghi lại nguyên. Lý do đảo = "Sửa đã thu: {reason}". Mỗi lần giảm có 1 dòng lịch sử (trước → sau, lý do,
  người sửa) — hiện ở cột "Sửa giảm" của [Excel thu tiền](exports.md#thu-tiền-theo-kỳ).
- Hóa đơn có **phiếu hoàn** (điều chỉnh chỉ số "Xử lý ngay"): giảm về **0** ⇒ đảo cả phiếu hoàn; giảm về số > 0 ⇒ giữ phiếu hoàn.
- **Quản lý khu** không giảm được tiền có ngày thu **trước ngày 1 tháng trước** → 403 `COLLECTED_PERIOD_LOCKED` ("liên hệ chủ trọ"); tăng thì luôn được.

| Code | HTTP | UI |
|------|------|----|
| `COLLECTED_AMOUNT_CHANGED` | 409 | "Số đã thu vừa thay đổi" → tải lại hóa đơn |
| `COLLECTED_AMOUNT_UNCHANGED` | 422 | Số mới bằng số cũ — đóng ô sửa |
| `PAYMENT_EXCEEDS_DEBT` | 422 | Lớn hơn tổng hóa đơn |
| `COLLECTED_BELOW_REFUNDED` | 422 | Phiếu sau đã hoàn trả từ hóa đơn này (`refunded`) — không giảm dưới số đó. Cũng trả ở `POST /payments/{id}/reverse` khi phiếu thu còn lại không đủ che dòng Hoàn trả **đã chốt** trỏ vào hóa đơn (hủy phiếu chứa dòng hoàn trước) |
| `COLLECTED_PERIOD_LOCKED` | 403 | Quản lý khu sửa tiền tháng cũ |
| `INVOICE_NOT_FINALIZED` / `INVOICE_CARRIED_OVER` / `CONTRACT_NOT_BILLABLE` | 422 | Nháp / nợ đã chuyển vào hóa đơn trả phòng / HĐ đã kết thúc |
| `VALIDATION_FAILED` (`reason`) | 400 | Giảm mà chưa nhập lý do |

## Đảo phiếu thu

`POST /payments/{id}/reverse` `{ "reason": "Nhập nhầm số tiền" }` → **200** phiếu thu (`status: "Reversed"`). Các phiếu tiền phòng liên quan
trở lại còn nợ. Đã đảo → 409 `PAYMENT_ALREADY_REVERSED`. Không xóa phiếu thu — muốn đổi số thì dùng [Sửa số đã thu](#sửa-số-đã-thu-09112026).
Đảo phiếu thu của hóa đơn có phiếu hoàn mà phiếu thu còn lại không đủ che ⇒ **đảo kèm phiếu hoàn** (lý do "Đảo kèm phiếu thu PT…") — trước
09/11/2026 lệnh này lỗi 500 / 422 và hóa đơn kẹt. Quản lý khu đảo phiếu thu có ngày thu trước ngày 1 tháng trước → 403 `COLLECTED_PERIOD_LOCKED`.

## Danh sách phiếu thu — cấu trúc trả về (bổ sung 13/10/2026)

`GET /payments?contractId=` (hoặc `roomId=`, `invoiceId=`) → **mảng, không phân trang**, mới nhất trước:

```json
[ { "id": "…", "receiptNo": "PT2026-000004", "contractId": "…", "amount": 1000000, "method": "BankTransfer", "paidAt": "2026-10-13",
    "payerName": null, "reference": "FT01A1318C2B", "note": null, "status": "Recorded", "kind": "Receipt",
    "reversedAt": null, "reverseReason": null,
    "allocations": [ { "invoiceId": "…", "invoiceNo": "PB2026-000005", "amount": 1000000 } ] } ]
```

- `status`: `Recorded` đã ghi · `Reversed` đã đảo (gạch ngang, hiện `reverseReason`). `kind`: `Receipt` tiền thu thật · `WriteOff` bỏ nợ (không tính doanh thu) · `Refund` hoàn tiền (tiền chi ra).
- `method`: `Cash` tiền mặt · `BankTransfer` chuyển khoản · `EWallet` ví điện tử.
- `allocations` = tiền này trừ vào phiếu nào, bao nhiêu (thu theo hợp đồng có thể trừ vào nhiều phiếu, cũ trước).
- Ghi thu (`POST /contracts/{id}/payments`) → 201 + đúng một phần tử như trên; đảo (`POST /payments/{id}/reverse`) → 200 + phần tử đó sau khi đảo.

## Số nợ của hợp đồng (13/10/2026)

`GET /contracts/{id}/balance` — cho màn **"thu theo hợp đồng"** và mọi chỗ cần biết một hợp đồng đang nợ bao nhiêu. Chỉ đọc, xem được cả HĐ đã kết thúc.

```json
{
  "contractId": "…", "contractNo": "HD2026-0002", "status": "Active",
  "outstanding": 6079000, "overdueAmount": 3512000, "refundDue": 0,
  "unpaidInvoices": [
    { "invoiceId": "…", "invoiceNo": "PB2026-000004", "billingMonth": "2026-08", "type": "Regular", "totalAmount": 3512000, "paidAmount": 0,
      "outstanding": 3512000, "dueDate": "2026-09-06", "isOverdue": true, "collectedAmount": 0, "writtenOffAmount": 0 },
    { "invoiceId": "…", "invoiceNo": "PB2026-000005", "billingMonth": "2026-09", "type": "Regular", "totalAmount": 3567000, "paidAmount": 1000000,
      "outstanding": 2567000, "dueDate": "2026-10-18", "isOverdue": false, "collectedAmount": 1000000, "writtenOffAmount": 0 } ],
  "draftInvoices": 0, "pendingRoomCharges": 1
}
```

| Trường | Ý nghĩa / UI |
|--------|--------------|
| `outstanding` | Tổng còn nợ = tổng phần chưa thu của các phiếu **đã chốt**. Là số mặc định và **số tối đa** của ô tiền khi thu theo hợp đồng (nhập hơn → 422 `PAYMENT_EXCEEDS_DEBT`) |
| `overdueAmount` | Phần đã quá hạn — tô đỏ |
| `refundDue` | Chủ trọ còn phải trả lại người thuê; **không** bù trừ vào nợ |
| `unpaidInvoices[]` | Từng phiếu còn nợ, cũ trước — hiện thành bảng; mỗi dòng có nút "Đã thu" cho riêng phiếu đó (`invoiceId`). Cột "Đã thu" = `collectedAmount` (thực thu), "Đã bỏ nợ" = `writtenOffAmount`; `paidAmount` = tổng hai số (18/10/2026 — PM-BR-36) |
| `draftInvoices` | Số phiếu nháp chưa chốt — chưa tính vào nợ, nhắc "còn N phiếu nháp" |
| `pendingRoomCharges` | Số khoản phát sinh chưa lên phiếu |

HĐ không tồn tại → 404. Gợi ý màn hình: tiêu đề "HĐ … còn nợ X đ", bảng phiếu còn nợ, nút **Thu tất cả** (`POST /contracts/{id}/payments` không gửi `invoiceId`).
