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
  "payerName": null, "reference": null, "note": null, "status": "Recorded", "reversedAt": null, "reverseReason": null,
  "allocations": [ { "invoiceId": "…", "invoiceNo": "PB2026-000101", "amount": 3378000 } ]
}
```

| Lỗi | Khi nào |
|-----|---------|
| 422 `PAYMENT_EXCEEDS_DEBT` | Số tiền lớn hơn số còn nợ của phiếu (hoặc tổng nợ của HĐ) — chưa hỗ trợ trả thừa |
| 422 `NO_OUTSTANDING_INVOICE` | HĐ không còn phiếu nào chưa thu đủ |
| 422 `INVOICE_NOT_FINALIZED` | Phiếu còn nháp / đã hủy |
| 400 `INVALID_PAID_AT` | Ngày thu ở tương lai |

## Đảo phiếu thu

`POST /payments/{id}/reverse` `{ "reason": "Nhập nhầm số tiền" }` → **200** phiếu thu (`status: "Reversed"`). Các phiếu tiền phòng liên quan
trở lại còn nợ. Đã đảo → 409 `PAYMENT_ALREADY_REVERSED`. Không xóa / sửa phiếu thu — nhập sai thì đảo rồi ghi lại.
