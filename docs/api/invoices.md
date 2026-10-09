# Phiếu tiền phòng

Phiếu báo tiền phòng hằng kỳ cho từng hợp đồng — **không phải hóa đơn GTGT**. Vòng đời: **Nháp** (sửa thoải mái) → **Chốt** (cấp số,
bất biến, gửi người thuê) → thu tiền ([payments.md](payments.md)); sai sót sau khi chốt thì **Hủy** rồi lập lại.

Quyền: chủ trọ và phó quản lý.

## Các nhóm trên phiếu

| Nhóm | `type` của dòng | Nguồn | Ví dụ |
|------|-----------------|-------|-------|
| Tiền phòng | `Rent` | Giá thuê HĐ × hệ số kỳ lẻ (kỳ chuyển tiếp: 1 tháng ± số ngày chủ trọ chọn) | 3.000.000 · "Tiền phòng (1 tháng + 4 ngày — đổi ngày chốt)" |
| Điện nước | `Metered` | Công tơ của phòng ([meters.md](meters.md)) × một giá hoặc **giá theo bậc** (bản giá mới nhất tới cuối kỳ) | Điện 88 kWh × 3.500 · "Điện (giá bậc)" |
| Dịch vụ | `Service` | Dịch vụ gắn HĐ: theo phòng / theo đầu người / theo số gói — **trọn tháng**, kỳ lẻ không chia ngày (sửa tay trên nháp nếu cần) | Nước 2 người × 20.000; Giữ xe 2 × 100.000 |
| Phụ thu | `Surcharge` | **Nhập tay**, bắt buộc lý do — 1 phiếu hoặc nhiều phòng một lúc | Thay khóa cửa 250.000 |
| Giảm trừ | `ManualDiscount` | Nhập tay (số dương, lưu âm) — không vượt phần thu | −200.000 |
| Hoàn trả | `Refund` | **Nhập tay** như phụ thu, bắt buộc lý do (số dương, lưu âm) — được làm **tổng phiếu âm** = chủ trọ trả lại người thuê | Hoàn tiền phòng 15 ngày chưa ở −1.500.000 |

- **Kỳ**: mỗi tháng thu (`billingMonth` = tháng của kỳ chuẩn của khu chứa kỳ) tối đa 1 phiếu / hợp đồng. Tiền phòng kỳ lẻ (vào giữa kỳ, trả phòng
  giữa kỳ) tính theo ngày nếu khu chọn `Daily` (`prorationFactor` trên dòng); dịch vụ luôn trọn tháng.
- **Điện nước trả sau / trả trước**: `Postpaid` — điện nước của chính kỳ; `Prepaid` — điện nước của **kỳ trước** (kỳ đầu không có),
  riêng kỳ cuối (có ngày trả phòng) gộp luôn điện nước tới chỉ số cuối.
- **Thay công tơ giữa kỳ**: hệ thống tự cộng phần công tơ cũ (`segments` có 2 đoạn). Cách khác: nhập tiền điện công tơ cũ thành phụ thu.
- **Giá theo phiên bản**: mọi khoản (điện nước, dịch vụ) lấy bản giá mới nhất có hiệu lực tới **ngày cuối** kỳ, áp cả kỳ — đổi giá giữa kỳ thì cả kỳ tính giá mới.
- **Tiền phòng thu hằng tháng** (không có đóng nhiều tháng / lần).

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Bảng phiếu theo khu + tháng (lọc tầng, trạng thái, còn nợ) | `GET /invoices?propertyId=&billingMonth=2026-11&status=&floor=&unpaidOnly=` |
| Nút **"Tạo phiếu tháng này"** (cả khu / phòng chọn / 1 tầng) | `POST /invoices/generate` |
| Nút **"Tính lại"** (phiếu chọn / cả tháng / 1 tầng) | `POST /invoices/recalculate` |
| Chi tiết phiếu nháp — sửa ô, thêm phụ thu / giảm trừ / hoàn trả | `GET /invoices/{id}`, `PUT/DELETE /invoices/{id}/lines/{lineId}`, `POST /invoices/{id}/manual-lines` |
| Nút **"Thêm phụ thu / hoàn trả cho nhiều phòng"** (phòng chọn / 1 tầng / cả khu) | `POST /invoices/manual-lines` |
| Nút **"Đã hoàn tiền"** trên phiếu Chờ hoàn | `POST /invoices/{id}/refund`, `DELETE /invoices/{id}/refund` (bỏ xác nhận) |
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
| `PREVIOUS_PERIOD_NOT_BILLED` | Chưa lập phiếu kỳ trước — lập **lần lượt từ kỳ đầu** (kỳ chứa "Tính tiền từ ngày"); UI gợi ý tạo tháng còn thiếu. HĐ nhập từ sổ cũ đặt `billingStartDate` thay vì bỏ qua kỳ đầu |
| `LATER_PERIOD_BILLED` | Đã có phiếu kỳ sau |
| `NO_PERIOD_IN_MONTH` | HĐ không có kỳ thuộc tháng thu này |
| `USE_FINAL_INVOICE` | Kỳ chứa ngày trả phòng — lập **phiếu quyết toán** thay phiếu thường |

`withIssues` = số phiếu còn **vấn đề chặn chốt** (`issues` có `severity: "Error"`): `MISSING_READING` (thiếu chỉ số — vào lưới ghi chỉ số),
`FEE_PRICE_MISSING` (khoản chưa có giá), `RENT_TERM_MISSING`, `NEGATIVE_TOTAL` (giảm trừ lớn hơn phần thu). Cảnh báo (`Warning`): `EDITED_BASE_CHANGED`,
`RENT_OVERPAID` (phiếu quyết toán: tiền phòng kỳ cuối đã thu nhiều hơn số ngày ở — message ghi số thừa; thêm dòng Hoàn trả nếu trả lại),
`UNUSUAL_USAGE` (điện nước bất thường — gấp ≥ 3 lần trung bình 3 kỳ trước và tăng ≥ 50, hoặc = 0 khi có người ở: kiểm lại chỉ số),
`TWO_RENT_PERIODS` (khu vừa đổi sang thu trước: người thuê trả 2 tháng tiền phòng gần nhau — nên báo trước).

## Chi tiết phiếu

`GET /invoices/{id}`

```json
{
  "summary": {
    "id": "…", "invoiceNo": null, "type": "Regular", "status": "Draft", "paymentStatus": null,
    "propertyId": "…", "roomId": "…", "roomCode": "101", "contractId": "…", "contractNo": "HD2026-0012", "representativeName": "Trần Thị Lan",
    "billingMonth": "2026-11", "periodStart": "2026-11-01", "periodEnd": "2026-11-30",
    "totalAmount": 3378000, "paidAmount": 0, "outstanding": 0, "refundDue": 0, "dueDate": null, "errorCount": 0, "version": "…"
  },
  "subtotal": 3378000, "discountTotal": 0, "refundTotal": 0, "refundedOn": null, "refundMethod": null, "refundNote": null, "issueDate": null,
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
| Bỏ sửa tay / xóa phụ thu, giảm trừ, hoàn trả | `DELETE /invoices/{id}/lines/{lineId}` | Dòng hệ thống về số hệ thống; dòng tay bị xóa |
| Thêm phụ thu / giảm trừ | `POST /invoices/{id}/manual-lines` `{ "type": "Surcharge", "description": "Thay khóa", "quantity": null, "unitPrice": null, "amount": 250000, "note": "Lý do", "feeTypeId": null }` | `type`: `Surcharge` / `ManualDiscount`; `note` (lý do) bắt buộc |
| Sửa phụ thu / giảm trừ / hoàn trả | `PUT /invoices/{id}/lines/{lineId}` (như trên, `note` bắt buộc) | |
| Ghi chú in trên phiếu | `PUT /invoices/{id}/note` `{ "note": "…" }` | |

Mọi thao tác trả **chi tiết phiếu** sau khi sửa. Giảm trừ không được lớn hơn phần thu (422 `NEGATIVE_TOTAL`) — trả lại tiền cho người thuê thì dùng **Hoàn trả**.
Phiếu đã chốt → 422 `INVOICE_NOT_DRAFT`.

### Thêm cho nhiều phòng

`POST /invoices/manual-lines` — cùng một dòng (phụ thu / giảm trừ / hoàn trả) vào từng phiếu **nháp** trong phạm vi:

```json
{ "invoiceIds": null, "propertyId": "…", "billingMonth": "2026-11", "roomIds": ["…", "…"], "floor": null,
  "type": "Surcharge", "description": "Sơn lại hành lang", "quantity": null, "unitPrice": null, "amount": 50000,
  "note": "Thu chung theo thông báo", "feeTypeId": null }
```

→ **200** `{ "added": 17, "results": [ { "invoiceId": "…", "roomId": "…", "roomCode": "101", "success": true, "errorCode": null, "message": null }, … ] }`.
Phạm vi: `invoiceIds`, hoặc `propertyId` + `billingMonth` (lọc `roomIds` / `floor`). Phòng chọn mà chưa có phiếu nháp → `errorCode: "NO_DRAFT_INVOICE"`;
phiếu đã chốt → `INVOICE_NOT_DRAFT`; giảm trừ vượt phần thu → `NEGATIVE_TOTAL`. Mỗi phiếu lưu riêng (lỗi 1 phiếu không ảnh hưởng phiếu khác).

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
  thu trước; đã xác nhận hoàn tiền → 422 `REFUND_CONFIRMED` — bỏ xác nhận trước) và là **phiếu mới nhất** của HĐ (422 `NOT_LATEST_INVOICE` — hủy / xóa phiếu kỳ sau trước). Hủy xong lập lại được, chỉ số được mở khóa.
- `DELETE /invoices/{id}` → 204: xóa **nháp** (cũng chỉ phiếu mới nhất).

## Trạng thái thu tiền (`paymentStatus`, chỉ phiếu đã chốt)

`Unpaid` chưa thu · `PartiallyPaid` thu một phần · `Paid` đủ (phiếu 0đ coi như đã thu) · `Overdue` quá hạn chưa thu đủ · `WrittenOff` đã bỏ nợ (không tính doanh thu) ·
`RefundPending` tổng âm, **chờ chủ trọ trả lại** người thuê · `Refunded` đã trả lại.
`outstanding` = số còn nợ (≥ 0 — phiếu âm không bù trừ nợ phiếu khác); `refundDue` = số còn phải trả lại người thuê. Gửi phiếu qua Zalo kèm mã QR: P2.

## Hoàn tiền cho người thuê

1. Thêm dòng **Hoàn trả** (`type: "Refund"`) vào phiếu nháp (thường là phiếu quyết toán khi trả phòng sớm — gợi ý từ cảnh báo `RENT_OVERPAID`).
2. Chốt phiếu. Tổng âm ⇒ `paymentStatus: "RefundPending"`, `refundDue` = số phải trả.
3. Trả tiền cho người thuê rồi bấm **"Đã hoàn tiền"**: `POST /invoices/{id}/refund` `{ "refundedOn": "2026-11-20", "method": "Cash", "note": "…" }`
   → 200 chi tiết phiếu (`Refunded`). Ngày hoàn từ ngày lập phiếu tới hôm nay (422 `INVALID_REFUND_DATE`); phiếu không âm → 422 `NOTHING_TO_REFUND`;
   đã xác nhận → 422 `REFUND_CONFIRMED`. Nhập nhầm: `DELETE /invoices/{id}/refund`. HĐ đã kết thúc → 422 `CONTRACT_NOT_BILLABLE` (không đổi được nữa).
4. Hoàn tất thanh lý bị chặn khi còn phiếu Chờ hoàn (422 `REFUND_PENDING`, `refundDue` trong body).

## Phiếu quyết toán

Lập từ hợp đồng đang thanh lý: `POST /contracts/{id}/final-invoice` ([contracts.md](contracts.md#lập-phiếu-quyết-toán--post-contractsidfinal-invoice)).
Là phiếu bình thường (sửa tay, phụ thu, tính lại, chốt, hủy như trên) cho đoạn cuối **[đầu kỳ cuối, ngày trả phòng]**, chỉ **thu phần còn thiếu**:

- **Tiền phòng** những ngày đã ở của kỳ cuối nếu chưa thu (đã thu trọn kỳ ⇒ không có dòng; thu thừa ⇒ cảnh báo `RENT_OVERPAID`, chủ trọ thêm dòng **Hoàn trả** nếu trả lại).
- **Dịch vụ** kỳ cuối nếu kỳ đó chưa có phiếu thường — **trọn tháng** (sửa số lượng / thành tiền trên nháp nếu thỏa thuận khác).
- **Điện nước** tới **chỉ số cuối** (trả trước: gồm cả kỳ trước nếu chưa thu).

Phiếu quyết toán đã chốt là điều kiện hoàn tất thanh lý; còn nợ thì chọn "Đã thu toàn bộ" / "Bỏ nợ"; còn phiếu Chờ hoàn thì xác nhận đã hoàn trước ([contracts.md](contracts.md#hoàn-tất--post-contractsidliquidationcomplete)).
