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
- Màn hình gợi ý: lưới các phòng chưa có công tơ của khoản thu (lấy từ danh sách phòng + `GET /rooms/{id}/meters`), nhập serial / chỉ số đầu rồi lưu 1 lần.

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
`READING_NOT_MONOTONIC` (số cuối nhỏ hơn chỉ số trước).

## Lịch sử & sửa chỉ số

`GET /meters/{id}/readings` → mảng, mới nhất trước. `kind`: `Initial` lắp · `Handover` nhận phòng · `Periodic` cuối kỳ ·
`Adhoc` kiểm tra · `Final` cuối hợp đồng · `Removal` tháo.

`PUT /meter-readings/{id}` — `{ "value": 108, "note": "Đọc lại" }` → **200** chỉ số sau khi sửa. Đã dùng cho phiếu đã chốt → 422 `READING_LOCKED`.
Giá trị phải **≥ chỉ số trước và ≤ chỉ số sau** (422 `READING_NOT_MONOTONIC`, body có `previousValue`, `nextValue` để hiện gợi ý).
VD: chỉ số nhận phòng tự lấy 100 (số cuối người cũ), thực tế thợ sửa chữa đã dùng 8 kWh → sửa thành 108.

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
      "current": null, "consumption": null, "locked": false, "recentAverage": 140, "usageWarning": null
    }
  ]
}
```

- `previous` = chỉ số cũ (số cuối của phiếu trước / nhận phòng / tháng trước); `current` = số đã nhập; `consumption` = mới − cũ (tạm tính).
- `locked: true` ⇒ chỉ số đã dùng cho phiếu đã chốt — ô chỉ đọc (muốn sửa thì hủy phiếu).
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
Gửi lại dòng đã có số ⇒ sửa số đó (nếu chưa khóa). Lưu xong → **Tính lại** phiếu nháp của tháng ([invoices.md](invoices.md#tính-lại)).

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
lý do); phiếu nháp liên quan chuyển "Cần tính lại".

| Lỗi | Khi nào |
|-----|---------|
| 422 `READING_NOT_VOIDABLE` | Chỉ hủy được chỉ số **cuối kỳ** (`Periodic`) và **kiểm tra** (`Adhoc`). Chỉ số lắp / tháo công tơ, nhận phòng, cuối hợp đồng: sửa giá trị (`PUT /meter-readings/{id}`) hoặc hủy thao tác gốc (hủy thanh lý, hủy chuyển phòng) |
| 422 `READING_LOCKED` | Chỉ số đã dùng cho phiếu đã chốt, hoặc cho khoản chốt điện nước khi chuyển phòng |
| 404 `READING_NOT_FOUND` | Không có / đã hủy |
| 400 | Thiếu `reason` |
