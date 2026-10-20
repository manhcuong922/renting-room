# Công tơ điện nước của phòng

Công tơ **đi theo phòng**, không gắn vào hợp đồng. Mỗi phòng có tối đa **1 công tơ đang hoạt động** cho mỗi khoản điện / nước theo
công tơ của khu ("Điện", "Nước"). Phòng tính nước **theo đầu người** thì không lắp công tơ nước (dùng dịch vụ "Nước" `PerOccupant` — [fees.md](fees.md)).

Quyền: chủ trọ và phó quản lý.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Tab **Công tơ** trong chi tiết phòng | `GET /rooms/{roomId}/meters?includeRemoved=false` |
| Lắp công tơ | `POST /rooms/{roomId}/meters` |
| Thay công tơ (hỏng / quay vòng về 0) | `POST /meters/{id}/replace` |
| **Thay công tơ hàng loạt** giữa tháng (điện lực thay cả khu) | `GET /properties/{id}/meters/replace-sheet?feeTypeId=` · `POST /properties/{id}/meters/bulk-replace` |
| Tháo công tơ (không thay) | `POST /meters/{id}/remove` |
| Lịch sử chỉ số + sửa chỉ số nhập sai | `GET /meters/{id}/readings`, `PUT /meter-readings/{id}` |
| Bước "Chỉ số nhận phòng" khi kích hoạt hợp đồng | [contracts.md](contracts.md#kích-hoạt-bàn-giao-phòng) |
| **Lưới ghi chỉ số hằng tháng** của khu | `GET /properties/{propertyId}/meter-reading-sheet?billingMonth=2026-11&floor=`, `PUT /properties/{propertyId}/meter-readings` |
| Ô "Chỉ số cuối" khi hoàn tất thanh lý | [contracts.md](contracts.md#hoàn-tất--post-contractsidliquidationcomplete) |

## Danh sách công tơ của phòng

`GET /rooms/{roomId}/meters` → **mảng**:

```json
[
  {
    "id": "…", "roomId": "…", "feeTypeId": "…", "feeTypeName": "Điện", "unit": "kWh",
    "serialNo": "E-101", "installedDate": "2026-09-01", "removedDate": null, "replacedByMeterId": null, "isActive": true,
    "latestReading": { "id": "…", "kind": "Handover", "readingDate": "2026-10-05", "value": 1250, "contractId": "…", "note": null, "version": "…" },
    "note": null, "version": "…"
  }
]
```

`includeRemoved=true` ⇒ hiện cả các **phiên bản cũ** (đã tháo / thay — `replacedByMeterId` trỏ tới công tơ mới).
`latestReading` dùng cho nút **"Dùng số mới nhất"**.

## Lắp

`POST /rooms/{roomId}/meters` — **Idempotency-Key**

```json
{ "feeTypeId": "…điện", "serialNo": "E-101", "installedDate": "2026-09-01", "initialValue": 1250, "note": null }
```

→ **201** `{ "id": "…" }`

| Lỗi | Khi nào |
|-----|---------|
| 409 `METER_ALREADY_ACTIVE` | Phòng đã có công tơ hoạt động cho khoản này — dùng **Thay công tơ** |
| 422 `FEE_NOT_METERED` | Khoản là dịch vụ, không phải điện nước theo công tơ |
| 422 `FEE_NOT_IN_PROPERTY` / `FEE_ARCHIVED` | Khoản khác khu / đã ngừng dùng |
| 422 `ROOM_ARCHIVED` | Phòng đã ngừng dùng |

## Lắp hàng loạt cho nhiều phòng (12/10/2026)

`POST /properties/{propertyId}/meters/bulk` — **Idempotency-Key**

```json
{ "feeTypeId": "…", "installedDate": "2026-10-01",
  "rows": [ { "roomId": "…", "serialNo": "E-101", "initialValue": 0 }, { "roomId": "…", "serialNo": null, "initialValue": 1250 } ] }
```

- Một khoản thu + một ngày lắp chung; mỗi dòng = 1 lệnh lắp đơn (phòng, serial, chỉ số đầu). Mỗi phòng 1 dòng, tối đa 500 dòng.
- **Tất cả hoặc không**: dòng lỗi (`ROOM_NOT_FOUND` phòng không thuộc khu, `ROOM_ARCHIVED` phòng ngừng dùng, `METER_ALREADY_ACTIVE` phòng đã có công tơ của khoản
  này) ⇒ 422 `BULK_INSTALL_INVALID` kèm `rowErrors: [ { "index", "roomId", "code", "message" } ]`, không lưu dòng nào — tô đỏ dòng theo `index`.
- → **200** `{ "installed": 18, "meterIds": ["…"] }`.
- **Lưới để nhập (13/10/2026):** `GET /properties/{propertyId}/meters/install-sheet?feeTypeId=…` → các phòng đang dùng của khu **chưa có công tơ đang
  hoạt động** của khoản thu, xếp theo tầng rồi mã phòng: `[ { "roomId": "…", "roomCode": "104", "floor": "1" } ]`. Mảng rỗng = phòng nào cũng có rồi.
  Khoản thu không thuộc khu / không tính theo công tơ → lỗi như lệnh lắp. Màn hình: chọn khoản thu → hiện lưới này → nhập serial / chỉ số đầu → lưu 1 lần.

## Thay công tơ (phiên bản)

`POST /meters/{id}/replace` — **Idempotency-Key**

```json
{ "date": "2026-10-20", "oldFinalValue": 1320, "newSerialNo": "E-102", "newInitialValue": 0, "note": "Công tơ cũ hỏng" }
```

→ **201** `{ "id": "<công tơ mới>" }`. Công tơ cũ ngừng hoạt động với **số cuối bắt buộc**; công tơ mới bắt đầu cùng ngày.
Cuối kỳ chỉ cần nhập số công tơ mới — module phiếu sẽ tự cộng phần của công tơ cũ (1.250 → 1.320).
Cách khác: không ghi thay công tơ, nhập tiền điện công tơ cũ thành **phụ thu** trên phiếu nháp.

## Thay hàng loạt (MT-UC-08)

Lưới: `GET /properties/{propertyId}/meters/replace-sheet?feeTypeId=…` → **200** mỗi công tơ đang hoạt động của khoản thu trong khu:
`[ { "meterId", "roomId", "roomCode", "floor", "serialNo", "lastValue", "lastDate" } ]`.

Lưu: `POST /properties/{propertyId}/meters/bulk-replace` — **Idempotency-Key**

```json
{ "feeTypeId": "…", "replacedOn": "2026-11-12", "note": "Điện lực thay đồng loạt",
  "rows": [ { "meterId": "…", "oldFinalValue": 4250, "newSerialNo": "EVN-101", "newInitialValue": null } ] }
```

- Một ngày thay chung; mỗi dòng = 1 lệnh thay đơn (số cuối công tơ cũ, serial mới, chỉ số đầu mới — bỏ trống = 0). Chỉ gửi phòng có thay; tối đa 500 dòng.
- **Tất cả hoặc không**: dòng lỗi (số cuối nhỏ hơn chỉ số cũ, ngày thay trước chỉ số gần nhất, công tơ không thuộc khu / khoản thu) ⇒
  422 `BULK_REPLACE_INVALID` kèm `rowErrors: [ { "index", "meterId", "code", "message" } ]`, không lưu dòng nào.
- → **200** `{ "replaced": 18, "newMeterIds": ["…"] }`. Kỳ có thay: phiếu tự cộng 2 công tơ (2 đoạn đo); nháp đã lập của phòng ⇒ "Cần tính lại".

## Tháo (không thay)

`POST /meters/{id}/remove` — `{ "date": "2026-10-20", "finalValue": 1320, "note": null }` → **200** `{ "warnings": [] }`.
Phòng đang có người thuê ⇒ cảnh báo `ROOM_HAS_OPEN_CONTRACT` (từ kỳ sau phiếu không còn dòng khoản này).

Lỗi chung khi thay / tháo: 422 `METER_REMOVED` (đã tháo), `INVALID_READING_DATE` (ngày trước chỉ số đã ghi),
`READING_NOT_MONOTONIC` (số cuối nhỏ hơn chỉ số trước), `METER_PERIOD_LOCKED` (17/10/2026 — ngày thay / tháo thuộc kỳ sử dụng đã chốt phiếu;
body có `lockedUntil`, UI gợi ý chọn từ `lockedUntil + 1`; thay hàng loạt: dòng lỗi trong `rowErrors`).

## Lịch sử & sửa chỉ số

`GET /meters/{id}/readings` → mảng (không phân trang), mới nhất trước:

```json
[ { "id": "…", "kind": "Periodic", "readingDate": "2026-10-01", "value": 1270, "contractId": "…", "note": null, "version": "5613" },
  { "id": "…", "kind": "Periodic", "readingDate": "2026-09-01", "value": 1170, "contractId": "…", "note": null, "version": "5605" } ]
```

`contractId` = hợp đồng chỉ số này thuộc về (`null` với chỉ số lắp / tháo công tơ). `kind`: `Initial` lắp · `Handover` nhận phòng · `Periodic` cuối kỳ ·
`Adhoc` kiểm tra · `Final` cuối hợp đồng · `Removal` tháo.

`PUT /meter-readings/{id}` — `{ "value": 108, "note": "Đọc lại" }` → **200** chỉ số sau khi sửa. Đã dùng cho hóa đơn đã chốt → 422 `READING_LOCKED`.
**Từ 20/10/2026**: hóa đơn **nháp** đang dùng công tơ này **tự tính lại ngay** trong cùng lệnh (giữ ô sửa tay) — áp cho sửa, hủy chỉ số và lưu lưới;
UI tải lại nháp, không còn cờ "Cần tính lại" vì chỉ số (thay / tháo / lắp công tơ vẫn chỉ gắn cờ).
Giá trị phải **≥ chỉ số trước và ≤ chỉ số sau** (422 `READING_NOT_MONOTONIC`, body có `previousValue`, `nextValue` để hiện gợi ý).
VD: chỉ số nhận phòng tự lấy 100 (số cuối người cũ), thực tế thợ sửa chữa đã dùng 8 kWh → sửa thành 108.

### Điều chỉnh chỉ số đã chốt (MT-UC-09 — viết lại 20/10/2026)

Gõ sai chỉ số, phát hiện khi hóa đơn đã chốt: `PUT` trả `READING_LOCKED` ⇒ UI hiện nút **"Điều chỉnh chỉ số đã chốt"**.

`POST /meter-readings/{id}/adjust` — **Idempotency-Key**

```json
{ "value": 1338, "reason": "Gõ nhầm 1.338 thành 1.388",
  "handling": "Immediate",
  "settlement": { "date": "2026-11-10", "method": "Cash" } }
```

- `reason` bắt buộc (≤ 300). `handling` — chỉ cần khi hóa đơn nguồn **đã có tiền** (đã thu và / hoặc đã bỏ nợ): `Immediate` (**Xử lý ngay**) hoặc
  `NextInvoice` (**Đẩy sang hóa đơn sau**). Hóa đơn **chưa thu** gửi `NextInvoice` cũng được (20/10/2026) — dùng khi tính lại bị chặn
  `ADJUSTMENT_NEGATIVE_TOTAL`. `settlement` (tùy chọn, `Immediate`) — ngày + cách trả / thu tiền; bỏ trống = hôm nay, tiền mặt.
- **Hộp thoại gợi ý cho UI**: hóa đơn nguồn chưa thu (`paidAmount = 0`) ⇒ chỉ nhập số mới + lý do. Đã thu ⇒ thêm 2 lựa chọn; HĐ đang thanh lý mà hóa
  đơn trả phòng **đã chốt** (hoặc chỉ số là số cuối lúc trả phòng) ⇒ chỉ hiện "Xử lý ngay".

**Kết quả theo trạng thái thu của hóa đơn nguồn** (VD điện 3.800đ/kWh, hóa đơn tháng 10 chốt với 1.388, đúng 1.338):

| Hóa đơn nguồn | `handling` trả về | Hệ thống làm |
|---|---|---|
| Chưa thu đồng nào | `Recalculated` | Tính lại dòng điện nước **ngay trên hóa đơn** (88 kWh = 334.400đ), tổng giảm; giữ số hóa đơn, ngày lập, hạn. Nhãn `isAdjusted`, `needsResend` |
| Đã thu + `Immediate` | `Immediate` | Tính lại như trên. Thu dư ⇒ trừ vào **phần bỏ nợ** trước, phần còn lại ghi **phiếu hoàn tiền** (`kind: Refund`, số `PC…` — [payments.md](payments.md)). Thu thiếu ⇒ hóa đơn còn nợ; có `settlement` ⇒ ghi luôn phiếu thu phần thiếu |
| Đã thu + `NextInvoice` | `NextInvoice` | Hóa đơn **giữ nguyên tổng**, nhãn `isAdjusted`. Phần chênh thành **khoản điều chỉnh** (thu dư ⇒ `Credit` = chênh − phần đã bỏ nợ; thu thiếu ⇒ `Surcharge`) vào hóa đơn sau — có nháp ⇒ gắn ngay, chưa có ⇒ chờ |

**Response 200**

```json
{ "reading": { "id": "…", "kind": "Periodic", "readingDate": "2026-11-01", "value": 1338, "contractId": "…", "note": "Gõ nhầm 1.338 thành 1.388", "version": "…" },
  "handling": "Immediate",
  "invoice": { "summary": { "id": "…", "invoiceNo": "PB2026-000012", "totalAmount": 3834000, "paidAmount": 3834000, "collectedAmount": 3834000,
                            "isAdjusted": true, "needsResend": true, … }, "lines": [ … ] },
  "charge": null,
  "payments": [ { "id": "…", "receiptNo": "PC2026-000001", "kind": "Refund", "amount": 190000, "method": "Cash", "paidAt": "2026-11-10", … } ],
  "warnings": [] }
```

- `invoice` = chi tiết hóa đơn nguồn sau điều chỉnh. `charge` = khoản điều chỉnh khi `NextInvoice` (null khi không có / chênh về 0).
  `payments` = phiếu hoàn / phiếu thu thêm / phiếu bỏ nợ bị đảo và phiếu bỏ nợ ghi lại trong lệnh.
- **Khoản điều chỉnh** ([room-charges.md](room-charges.md)): `isSystem: true` — **không sửa / hủy tay** (422 `ROOM_CHARGE_SYSTEM`), chỉ đánh dấu đã trả /
  đã thu ngay. **Mỗi dòng nguồn chỉ 1 khoản**: điều chỉnh lần sau (VD 1.338 → 1.350) **sửa chính khoản đó** (190.000đ → 144.400đ), không sinh khoản mới;
  điều chỉnh về đúng số lúc chốt ⇒ khoản tự hủy. Hóa đơn nguồn bị hủy về sau ⇒ khoản tự hủy. Khoản đã đánh dấu đã trả / thu ngay ⇒ không
  điều chỉnh tiếp được (`ADJUSTMENT_ALREADY_BILLED`); bỏ đánh dấu không bị giới hạn bù 1 tháng tiền phòng.
- Nháp kỳ sau (bắt đầu từ chỉ số này) **tự tính lại** trong cùng lệnh.
- `warnings`: `LINE_MANUALLY_EDITED` — dòng của hóa đơn đã chốt từng sửa tay: tiền chênh theo số hệ thống, số sửa tay + chênh;
  `MANUAL_AMOUNT_CLAMPED` — số sửa tay + chênh ra âm, đã đặt 0đ (chủ trọ kiểm tra lại).
- **HĐ đang thanh lý**: hóa đơn trả phòng chưa chốt ⇒ `NextInvoice` vào hóa đơn trả phòng (có thể làm hóa đơn âm — [invoices.md](invoices.md)).
- **Không làm hóa đơn tháng âm** (20/10/2026): tính lại trên hóa đơn (chưa thu, hoặc `Immediate`) mà khoản giảm / bù trên hóa đơn lớn hơn phần thu còn
  lại ⇒ 422 `ADJUSTMENT_NEGATIVE_TOTAL`, không đổi gì ⇒ UI gợi ý **"Đẩy sang hóa đơn sau"** (gửi lại `handling: "NextInvoice"`). VD tiền phòng
  3.000.000 + điện 524.400 − giảm 3.500.000 = 24.400đ, sửa 1.388 → 1.338 (−190.000) ⇒ lỗi; Đẩy sang ⇒ hóa đơn giữ tổng, khoản bù 190.000đ.

#### Sau thanh lý (HĐ đã kết thúc, 20/10/2026)

Người thuê đã đi, mới phát hiện **số cuối lúc trả phòng gõ cao** ⇒ cùng endpoint, trên chỉ số `Final` của HĐ:

```json
{ "value": 1420, "reason": "Số cuối gõ 1.470", "settlement": { "date": "2026-11-20", "method": "BankTransfer" } }
```

- Chỉ **giảm** được (tăng → 422 `ADJUSTMENT_AFTER_END_INCREASE` — số cuối gõ thấp thì cho qua). Chỉ số khác của HĐ đã kết thúc → `READING_NEXT_PERIOD_FINALIZED`.
- Chuỗi chỉ số sửa về đúng; **hóa đơn trả phòng giữ nguyên** (nhãn "Đã điều chỉnh", không "Cần gửi lại"), báo cáo các tháng cũ không đổi.
  `handling` trả về **`AfterEnd`**, `payments` rỗng.
- Phần chênh (trừ phần đã bỏ nợ của hóa đơn trả phòng) thành khoản **"Chênh lệch sau thanh lý — …"** (`charge`, `kind: Credit`, không vào hóa đơn nào):

| Body | Khoản |
|---|---|
| `settlement { date, method }` — **"Đã trả lại"** | `isSettled: true` ngày đó — báo cáo ghi tiền chi ra |
| `notReturned: true` — **"Không trả lại"** (không cần lý do) | Hủy, `cancelReason = "Không trả lại"` — báo cáo không đổi |
| Bỏ trống — **chờ xử lý** | `status: Pending`; quyết sau: `POST /room-charges/{id}/settle` (đã trả lại) hoặc `POST /room-charges/{id}/waive` (không trả lại) |

  Gửi cả `settlement` và `notReturned` → 400 `INVALID_HANDLING`. Điều chỉnh lại sửa chính khoản; khoản đã trả → `ADJUSTMENT_ALREADY_BILLED`.
  VD điện 3.500đ/kWh, số cuối 1.470 (đúng 1.420) ⇒ khoản 175.000đ. Người mới nhận phòng nhập số nhỏ hơn cũng đi đường này ([contracts.md](contracts.md#kích-hoạt-bàn-giao-phòng)).
- Lịch sử: `GET /invoices/{id}/adjustments` ([invoices.md](invoices.md#điều-chỉnh-chỉ-số--lịch-sử-và-gửi-lại)).

| Lỗi | Khi nào |
|-----|---------|
| 422 `READING_NOT_LOCKED` | Chỉ số chưa dùng cho hóa đơn đã chốt — dùng `PUT` sửa trực tiếp |
| 422 `READING_NOT_ADJUSTABLE` | Không phải chỉ số cuối của đoạn đo trên hóa đơn đã chốt (VD chỉ số đầu kỳ) |
| 422 `READING_NEXT_PERIOD_FINALIZED` | Hóa đơn kỳ sau đã chốt, bắt đầu từ chỉ số này — tổng 2 kỳ đã tự bù nhau |
| 400 `READING_UNCHANGED` | Số mới trùng số hiện tại |
| 400 `ADJUSTMENT_HANDLING_REQUIRED` | Hóa đơn đã có tiền mà chưa chọn `handling` |
| 400 `INVALID_HANDLING` / `INVALID_SETTLED_DATE` | `handling` khác 2 giá trị trên / ngày `settlement` ở tương lai |
| 422 `ADJUSTMENT_ALREADY_BILLED` | Khoản điều chỉnh lần trước đã thanh toán (đã trả / thu ngay, hoặc đã nằm trên hóa đơn đã chốt) — không điều chỉnh tiếp; phần chênh còn lại nhập khoản phụ thu / bù tay |
| 422 `REFUND_CONFIRMED` | Hóa đơn nguồn (âm) đã xác nhận "Đã hoàn" — bỏ xác nhận (`DELETE /invoices/{id}/refund`) rồi điều chỉnh |
| 422 `ADJUSTMENT_NO_NEXT_INVOICE` | `NextInvoice` mà hóa đơn trả phòng đã chốt (không còn hóa đơn sau) |
| 422 `ADJUSTMENT_NEGATIVE_TOTAL` | Tính lại làm hóa đơn tháng âm — chọn `NextInvoice` |
| 422 `ADJUSTMENT_AFTER_END_INCREASE` | HĐ đã kết thúc mà số mới lớn hơn số cuối |
| 422 `READING_NOT_MONOTONIC` | Số mới nhỏ hơn chỉ số trước / lớn hơn chỉ số sau |
| 422 `READING_ADJUSTMENT_TOO_LARGE` | Khoản điều chỉnh vượt 100.000.000đ |

## Lưới ghi chỉ số hằng tháng

`GET /properties/{propertyId}/meter-reading-sheet?billingMonth=2026-11&floor=2&feeTypeId=…` (`floor`, `feeTypeId` tùy chọn — `feeTypeId` để đi ghi
riêng điện hoặc riêng nước, 12/10/2026) — mỗi dòng = (phòng, hợp đồng, công tơ) cần chỉ số cuối kỳ
cho phiếu tháng đó. Hợp đồng **trả sau** (`Postpaid`): kỳ của tháng; **trả trước** (`Prepaid`): kỳ liền trước (kỳ đầu không có dòng).

```json
{
  "billingMonth": "2026-11",
  "rows": [
    {
      "roomId": "…", "roomCode": "101", "floor": "1", "contractId": "…", "contractNo": "HD2026-0012", "representativeName": "Trần Thị Lan",
      "meterId": "…", "serialNo": "E-101", "feeTypeId": "…", "feeTypeName": "Điện", "unit": "kWh",
      "usageStart": "2026-11-01", "usageEnd": "2026-11-30", "closingPeriodStart": "2026-11-01", "endsWithFinal": false,
      "previous": { "id": "…", "kind": "Handover", "readingDate": "2026-11-01", "value": 100 },
      "current": null, "consumption": null, "locked": false, "recentAverage": 140, "usageWarning": null,
      "invoice": { "id": "…", "status": "Draft", "isStale": false }, "moveOutDate": null
    }
  ]
}
```

- `previous` = chỉ số cũ (số cuối của phiếu trước / nhận phòng / tháng trước); `current` = số đã nhập; `consumption` = mới − cũ (tạm tính).
- `locked: true` ⇒ chỉ số đã dùng cho hóa đơn đã chốt — ô chỉ đọc (gõ sai ⇒ "Điều chỉnh chỉ số đã chốt").
- **`invoice`** (20/10/2026) = hóa đơn thường của HĐ cho kỳ đang ghi: `{ id, status: Draft | Finalized, isStale }`; `null` = chưa lập. UI dựng cột
  **"Hóa đơn"** và lọc phía client: **Chưa nhập** (`current = null`), **Chưa có hóa đơn** (`invoice = null`), **Cần tính lại** (`isStale`), **Đã chốt**.
  VD khu 34 phòng mới nhập 25 ⇒ "Tạo hóa đơn" tạo 25, bỏ qua 9 (`MISSING_READING`) — lọc "Chưa nhập" ra đúng 9 phòng.
- **`moveOutDate`** (20/10/2026): ngày dự kiến trả đã báo / ngày trả phòng ⇒ nhãn **"Trả phòng dd/MM"**; kỳ chứa ngày đó không lập hóa đơn tháng.
- **Bất thường** (MT-BR-08, không chặn lưu): `recentAverage` = trung bình 3 kỳ trước của HĐ (null khi chưa đủ 3 kỳ) — UI tô vàng ngay khi
  nhập nếu sản lượng ≥ 3 × `recentAverage` **và** tăng ≥ 50; hoặc = 0 khi phòng có người ở. Số đã lưu ⇒ server trả câu cảnh báo trong `usageWarning`
  (phiếu nháp có cảnh báo `UNUSUAL_USAGE`).
- Công tơ thay giữa kỳ: chỉ hiện công tơ mới — phần công tơ cũ hệ thống tự cộng khi tính phiếu.

`PUT /properties/{propertyId}/meter-readings` (nên gửi `Idempotency-Key`)

```json
{ "readings": [ { "meterId": "…", "contractId": "…", "closingPeriodStart": "2026-11-01", "readingDate": "2026-11-30", "value": 188 } ] }
```

→ **200** `{ "saved": 1, "warnings": [] }`. **Cả lô hoặc không gì**: có dòng lỗi ⇒ 422 `READINGS_INVALID`, body có `rowErrors: [{ index, code, message }]`
(`READING_NOT_MONOTONIC`, `READING_LOCKED`, `METER_NOT_IN_ROOM`, `NOT_PERIOD_START`…) — tô đỏ dòng theo `index`, chưa lưu dòng nào.
Gửi lại dòng đã có số ⇒ sửa số đó (nếu chưa khóa). Hóa đơn nháp đang dùng các công tơ này **tự tính lại** trong cùng lệnh (20/10/2026).

**Cảnh báo ngày ghi (12/10/2026, không chặn lưu):** `readingDate` nằm ngoài khoảng **7 ngày trước → 15 ngày sau ngày cuối kỳ** (`usageEnd`) ⇒
kết quả lưu có `warnings: [ { "index": 0, "code": "READING_DATE_FAR_FROM_PERIOD_END", "message": "Ngày ghi 06/11/2026 lệch xa ngày cuối kỳ 30/11/2026 — …" } ]`.
Số **đã được lưu**; UI hiện câu nhắc ở dòng `index` để người dùng kiểm tra có chọn nhầm tháng không.

## Sản lượng phòng trống (12/10/2026)

`GET /properties/{propertyId}/meters/vacant-usage?from=2026-01-01&to=2026-12-31` (bỏ trống: `to` = hôm nay, `from` = `to` − 1 năm)

Điện nước công tơ chạy lúc phòng **không có người thuê** (sửa chữa, thử phòng, người cũ đi chưa chốt) — không nằm trên phiếu của ai. Hệ thống lấy
mỗi **chỉ số nhận phòng** cao hơn chỉ số liền trước trên cùng công tơ, lọc theo **ngày nhận phòng**.

```json
{
  "from": "2026-01-01", "to": "2026-12-31",
  "rows": [ { "roomId": "…", "roomCode": "101", "meterId": "…", "serialNo": "E-101", "feeTypeId": "…", "feeTypeName": "Điện", "unit": "kWh",
              "fromDate": "2026-09-20", "fromValue": 1320, "toDate": "2026-10-05", "toValue": 1328, "quantity": 8 } ],
  "totals": [ { "feeTypeId": "…", "feeTypeName": "Điện", "unit": "kWh", "quantity": 8 } ]
}
```

Dùng để đối chiếu hóa đơn điện nước cả khu: tổng trên các phiếu + `totals` ≈ số nhà cung cấp tính. Khu không tồn tại → 404 `PROPERTY_NOT_FOUND`.

## Hủy chỉ số ghi nhầm (11/10/2026)

`POST /meter-readings/{id}/void` `{ "reason": "Ghi nhầm phòng" }` → **204**. Dòng bị hủy không còn trong lịch sử chỉ số (vẫn lưu trong DB kèm
lý do); hóa đơn nháp liên quan tự tính lại (20/10/2026).

| Lỗi | Khi nào |
|-----|---------|
| 422 `READING_NOT_VOIDABLE` | Chỉ hủy được chỉ số **cuối kỳ** (`Periodic`) và **kiểm tra** (`Adhoc`). Chỉ số lắp / tháo công tơ, nhận phòng, cuối hợp đồng: sửa giá trị (`PUT /meter-readings/{id}`) hoặc hủy thao tác gốc (hủy thanh lý, hủy chuyển phòng) |
| 422 `READING_LOCKED` | Chỉ số đã dùng cho phiếu đã chốt, hoặc cho khoản chốt điện nước khi chuyển phòng |
| 404 `READING_NOT_FOUND` | Không có / đã hủy |
| 400 | Thiếu `reason` |
