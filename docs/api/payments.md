# Thu tiền

Ghi nhận tiền người thuê đã trả (tiền mặt / chuyển khoản tự kiểm) vào **phiếu tiền phòng đã chốt** ([invoices.md](invoices.md)).
Mỗi lần thu sinh **phiếu thu** số `PT2026-000045`. Thanh toán online / mã QR: làm sau khi có gửi phiếu qua Zalo (P2–P3).

Quyền: chủ trọ và phó quản lý.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Nút **"Đã thu"** trên phiếu (mặc định đủ số còn nợ, sửa được nếu thu một phần) | `POST /contracts/{contractId}/payments` với `invoiceId` |
| Thu theo hợp đồng (tự trừ phiếu cũ nhất trước) | `POST /contracts/{contractId}/payments` không `invoiceId` |
| Lịch sử thu của phòng / hợp đồng / phiếu | `GET /payments?roomId=` · `?contractId=` · `?invoiceId=` |
| Đảo phiếu thu nhập sai | `POST /payments/{id}/reverse` |
| **"Thanh toán + bỏ phần còn lại"** trên phiếu (chủ trọ / phó quản lý được cấp quyền) | `POST /invoices/{id}/pay-and-write-off` |
| **Bỏ nợ** phiếu (một phần / toàn bộ, chủ trọ / phó quản lý được cấp quyền) | `POST /invoices/{id}/write-off` |
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

`kind`: `Receipt` tiền thật · `WriteOff` **bỏ nợ** — không tính doanh thu. Tiền cọc không đi qua phiếu thu (chỉ theo dõi trên HĐ — [contracts.md](contracts.md#tiền-cọc)).
Số phiếu thu `PT{năm lập phiếu}-…` (ghi bù ngày thu năm trước vẫn mang năm hiện tại).

## Trả dần và bỏ nợ (F2)

- **Trả dần**: thu một phần bằng "Đã thu" bình thường — phần còn lại vẫn là nợ (`PartiallyPaid`).
- **Thanh toán + bỏ phần còn lại** — `POST /invoices/{id}/pay-and-write-off` — **Idempotency-Key**, chủ trọ hoặc phó quản lý **được cấp quyền**

  ```json
  { "amount": 2000000, "method": "Cash", "paidAt": "2026-10-09", "payerName": null, "reference": null, "reason": "Khó khăn, chủ trọ cho 1tr" }
  ```

  → **200** `{ "payment": Payment (kind Receipt), "writeOff": Payment (kind WriteOff) }` — 1 transaction; phiếu thành `WrittenOff`,
  tiền thu thật chỉ là `amount`. `amount` = số còn nợ → 422 `NOTHING_TO_WRITE_OFF` (dùng "Đã thu"); lớn hơn → 422 `PAYMENT_EXCEEDS_DEBT`.
- **Bỏ nợ riêng** — `POST /invoices/{id}/write-off` `{ "amount": null, "reason": "…" }` — **Idempotency-Key**, chủ trọ hoặc phó quản lý **được cấp quyền**;
  `amount` null = toàn bộ số còn nợ → **200** phiếu thu `kind: WriteOff`.
- Được làm **bất kỳ lúc nào** HĐ còn hiệu lực / đang thanh lý (không chỉ khi trả phòng). Phó quản lý chưa được chủ trọ cấp quyền bỏ nợ
  ([members.md](members.md#quyền-bỏ-nợ)) → 403 `WRITE_OFF_NOT_ALLOWED`, kể cả "Bỏ nợ" khi hoàn tất thanh lý. Bỏ nợ không tính doanh thu, đảo được như phiếu thu thường.

## Đảo phiếu thu

`POST /payments/{id}/reverse` `{ "reason": "Nhập nhầm số tiền" }` → **200** phiếu thu (`status: "Reversed"`). Các phiếu tiền phòng liên quan
trở lại còn nợ. Đã đảo → 409 `PAYMENT_ALREADY_REVERSED`. Không xóa / sửa phiếu thu — nhập sai thì đảo rồi ghi lại.
