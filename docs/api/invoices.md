# Phiếu tiền phòng

Phiếu báo tiền phòng hằng kỳ cho từng hợp đồng — **không phải hóa đơn GTGT**. Vòng đời: **Nháp** (sửa thoải mái) → **Chốt** (cấp số,
bất biến, gửi người thuê) → thu tiền ([payments.md](payments.md)); sai sót sau khi chốt thì **Hủy** rồi lập lại.

Quyền: chủ trọ và phó quản lý.

## 4 nhóm trên phiếu

| Nhóm | `type` của dòng | Nguồn | Ví dụ |
|------|-----------------|-------|-------|
| Tiền phòng | `Rent` | Giá thuê HĐ × hệ số kỳ lẻ | 3.000.000 |
| Điện nước | `Metered` | Công tơ của phòng ([meters.md](meters.md)) × **một giá** (giá mới nhất trong kỳ) | Điện 88 kWh × 3.500 |
| Dịch vụ | `Service` | Dịch vụ gắn HĐ: theo phòng / theo đầu người / theo số gói | Nước 2 người × 20.000; Giữ xe 2 × 100.000 |
| Phụ thu | `Surcharge` | **Nhập tay**, bắt buộc lý do | Thay khóa cửa 250.000 |
| Giảm trừ | `ManualDiscount` | Nhập tay (số dương, lưu âm) | −200.000 |

- **Kỳ**: mỗi tháng thu (`billingMonth` = tháng của ngày bắt đầu kỳ) tối đa 1 phiếu / hợp đồng. Kỳ lẻ (vào giữa tháng, trả phòng giữa kỳ)
  tính theo ngày nếu HĐ chọn `Daily` (`prorationFactor` trên dòng, VD 1,0667).
- **Điện nước trả sau / trả trước**: `Postpaid` — điện nước của chính kỳ; `Prepaid` — điện nước của **kỳ trước** (kỳ đầu không có),
  riêng kỳ cuối (có ngày trả phòng) gộp luôn điện nước tới chỉ số cuối.
- **Thay công tơ giữa kỳ**: hệ thống tự cộng phần công tơ cũ (`segments` có 2 đoạn). Cách khác: nhập tiền điện công tơ cũ thành phụ thu.
- **Giá đổi giữa kỳ**: cả kỳ tính giá mới.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Bảng phiếu theo khu + tháng (lọc tầng, trạng thái, còn nợ) | `GET /invoices?propertyId=&billingMonth=2026-11&status=&floor=&unpaidOnly=` |
| Nút **"Tạo phiếu tháng này"** (cả khu / phòng chọn / 1 tầng) | `POST /invoices/generate` |
| Nút **"Tính lại"** (phiếu chọn / cả tháng / 1 tầng) | `POST /invoices/recalculate` |
| Chi tiết phiếu nháp — sửa ô, thêm phụ thu | `GET /invoices/{id}`, `PUT/DELETE /invoices/{id}/lines/{lineId}`, `POST /invoices/{id}/manual-lines` |
| Nút **Chốt** / **Chốt các phiếu đã chọn** | `POST /invoices/{id}/finalize`, `POST /invoices/finalize-batch` |
| Hủy phiếu đã chốt / xóa nháp | `POST /invoices/{id}/void`, `DELETE /invoices/{id}` |
| Tab **Phiếu** trong chi tiết phòng | `GET /rooms/{roomId}/invoices` |
| Trước khi tạo phiếu: nhập chỉ số cuối kỳ | [meters.md](meters.md#lưới-ghi-chỉ-số-hằng-tháng) |

## Tạo phiếu nháp

`POST /invoices/generate` (nên gửi `Idempotency-Key`)

```json
{ "propertyId": "…", "billingMonth": "2026-11", "roomIds": null, "floor": null, "recalculateExistingDrafts": false }
```

→ **200**

```json
{ "created": 18, "recalculated": 0, "withIssues": 3,
  "skipped": [ { "contractId": "…", "contractNo": "HD2026-0007", "roomCode": "105", "reason": "PREVIOUS_PERIOD_NOT_BILLED" } ] }
```

| `reason` bỏ qua | Ý nghĩa / UI |
|-----------------|--------------|
| `EXISTS` | Kỳ này đã có phiếu (gửi `recalculateExistingDrafts: true` để tính lại nháp) |
| `PREVIOUS_PERIOD_NOT_BILLED` | Chưa lập phiếu kỳ trước — lập lần lượt từng tháng (phiếu **đầu tiên** của HĐ thì lập tháng nào cũng được) |
| `LATER_PERIOD_BILLED` | Đã có phiếu kỳ sau |
| `NO_PERIOD_IN_MONTH` | HĐ không có kỳ bắt đầu trong tháng này |

`withIssues` = số phiếu còn **vấn đề chặn chốt** (`issues` có `severity: "Error"`): `MISSING_READING` (thiếu chỉ số — vào lưới ghi chỉ số),
`FEE_PRICE_MISSING` (khoản chưa có giá), `RENT_TERM_MISSING`, `NEGATIVE_TOTAL`. Cảnh báo (`Warning`): `EDITED_BASE_CHANGED`.

## Chi tiết phiếu

`GET /invoices/{id}`

```json
{
  "summary": {
    "id": "…", "invoiceNo": null, "status": "Draft", "paymentStatus": null,
    "propertyId": "…", "roomId": "…", "roomCode": "101", "contractId": "…", "contractNo": "HD2026-0012", "representativeName": "Trần Thị Lan",
    "billingMonth": "2026-11", "periodStart": "2026-11-01", "periodEnd": "2026-11-30",
    "totalAmount": 3378000, "paidAmount": 0, "outstanding": 0, "dueDate": null, "errorCount": 0, "version": "…"
  },
  "subtotal": 3378000, "discountTotal": 0, "issueDate": null,
  "lines": [
    { "id": "…", "type": "Rent", "isSystem": true, "feeTypeId": null, "description": "Tiền phòng", "unit": "tháng",
      "serviceFrom": "2026-11-01", "serviceTo": "2026-11-30", "quantity": 1, "unitPrice": 3000000, "prorationFactor": 1,
      "amount": 2800000, "isManuallyEdited": true, "systemQuantity": 1, "systemUnitPrice": 3000000, "systemAmount": 3000000,
      "note": "Giảm do sửa nhà", "segments": [] },
    { "id": "…", "type": "Metered", "isSystem": true, "feeTypeId": "…", "description": "Điện", "unit": "kWh",
      "quantity": 88, "unitPrice": 3500, "amount": 308000, "isManuallyEdited": false,
      "segments": [ { "meterId": "…", "startReadingId": "…", "endReadingId": "…", "startValue": 100, "endValue": 188, "consumption": 88 } ] },
    { "id": "…", "type": "Surcharge", "isSystem": false, "description": "Thay khóa cửa", "quantity": 1, "unitPrice": 250000,
      "amount": 250000, "note": "Người thuê làm hỏng khóa", "segments": [] }
  ],
  "issues": [], "note": null, "finalizedAt": null, "voidedAt": null, "voidReason": null
}
```

UI: ô có `isManuallyEdited` hiện nhãn **"Sửa tay"** và số hệ thống (`systemAmount`) bên cạnh, nút "Bỏ sửa tay".

## Sửa phiếu nháp

| Thao tác | Endpoint | Ghi chú |
|----------|----------|---------|
| Sửa tay ô của dòng hệ thống | `PUT /invoices/{id}/lines/{lineId}` `{ "quantity": null, "unitPrice": null, "amount": 2800000, "note": "…" }` | Bỏ trống `amount` ⇒ = số lượng × đơn giá × hệ số kỳ. Dòng Tiền phòng **bắt buộc ghi chú** |
| Bỏ sửa tay / xóa phụ thu, giảm trừ | `DELETE /invoices/{id}/lines/{lineId}` | Dòng hệ thống về số hệ thống; dòng tay bị xóa |
| Thêm phụ thu / giảm trừ | `POST /invoices/{id}/manual-lines` `{ "type": "Surcharge", "description": "Thay khóa", "quantity": null, "unitPrice": null, "amount": 250000, "note": "Lý do", "feeTypeId": null }` | `type`: `Surcharge` / `ManualDiscount`; `note` (lý do) bắt buộc |
| Sửa phụ thu / giảm trừ | `PUT /invoices/{id}/lines/{lineId}` (như trên, `note` bắt buộc) | |
| Ghi chú in trên phiếu | `PUT /invoices/{id}/note` `{ "note": "…" }` | |

Mọi thao tác trả **chi tiết phiếu** sau khi sửa. Tổng phiếu không được âm (422 `NEGATIVE_TOTAL`). Phiếu đã chốt → 422 `INVOICE_NOT_DRAFT`.

## Tính lại

`POST /invoices/recalculate` — một trong hai phạm vi:

```json
{ "invoiceIds": ["…", "…"], "keepManualEdits": true }
{ "propertyId": "…", "billingMonth": "2026-11", "roomIds": null, "floor": "2", "keepManualEdits": true }
```

→ **200** `{ "recalculated": 12, "withIssues": 0, "notDraft": ["…"] }`. Mặc định **giữ ô sửa tay** (số hệ thống được cập nhật bên cạnh;
khác lúc sửa ⇒ cảnh báo `EDITED_BASE_CHANGED`); `keepManualEdits: false` ⇒ tính lại từ đầu. Phụ thu / giảm trừ luôn giữ.

## Chốt

`POST /invoices/{id}/finalize` → **200** chi tiết phiếu (`invoiceNo` `PB2026-000101`, `issueDate` hôm nay, `dueDate` = hôm nay + số ngày hạn
thanh toán của HĐ). Sau khi chốt: chỉ số cuối kỳ bị khóa và thành chỉ số cũ của kỳ sau; bảng giá trước ngày cuối kỳ không sửa được.

| Lỗi | Khi nào / UI |
|-----|--------------|
| 422 `INVOICE_HAS_ISSUES` | Còn vấn đề chặn (`issues` trong body) — xử lý rồi Tính lại |
| 409 `DRAFT_STALE` | Dữ liệu nguồn (giá, chỉ số, người ở, dịch vụ) đổi sau lần tính — bấm **Tính lại** rồi chốt |
| 422 `INVOICE_NOT_DRAFT` | Đã chốt / hủy — tải lại |

`POST /invoices/finalize-batch` `{ "invoiceIds": [...] }` → **200** mảng `{ invoiceId, success, invoiceNo, errorCode, message }` — hiện kết quả từng phiếu.

## Hủy / xóa

- `POST /invoices/{id}/void` `{ "reason": "Nhập sai chỉ số" }` → 200. Chỉ phiếu **đã chốt, chưa thu tiền** (422 `INVOICE_HAS_PAYMENTS` — đảo phiếu
  thu trước) và là **phiếu mới nhất** của HĐ (422 `NOT_LATEST_INVOICE` — hủy / xóa phiếu kỳ sau trước). Hủy xong lập lại được, chỉ số được mở khóa.
- `DELETE /invoices/{id}` → 204: xóa **nháp** (cũng chỉ phiếu mới nhất).

## Trạng thái thu tiền (`paymentStatus`, chỉ phiếu đã chốt)

`Unpaid` chưa thu · `PartiallyPaid` thu một phần · `Paid` đủ (phiếu 0đ coi như đã thu) · `Overdue` quá hạn chưa thu đủ.
`outstanding` = số còn nợ. Gửi phiếu qua Zalo kèm mã QR: P2.
