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

## Thay công tơ (phiên bản)

`POST /meters/{id}/replace` — **Idempotency-Key**

```json
{ "date": "2026-10-20", "oldFinalValue": 1320, "newSerialNo": "E-102", "newInitialValue": 0, "note": "Công tơ cũ hỏng" }
```

→ **201** `{ "id": "<công tơ mới>" }`. Công tơ cũ ngừng hoạt động với **số cuối bắt buộc**; công tơ mới bắt đầu cùng ngày.
Cuối kỳ chỉ cần nhập số công tơ mới — module phiếu sẽ tự cộng phần của công tơ cũ (1.250 → 1.320).
Cách khác: không ghi thay công tơ, nhập tiền điện công tơ cũ thành **phụ thu** trên phiếu nháp.

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

`GET /properties/{propertyId}/meter-reading-sheet?billingMonth=2026-11&floor=2` — mỗi dòng = (phòng, hợp đồng, công tơ) cần chỉ số cuối kỳ
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

→ **200** `{ "saved": 1 }`. **Cả lô hoặc không gì**: có dòng lỗi ⇒ 422 `READINGS_INVALID`, body có `rowErrors: [{ index, code, message }]`
(`READING_NOT_MONOTONIC`, `READING_LOCKED`, `METER_NOT_IN_ROOM`, `NOT_PERIOD_START`…) — tô đỏ dòng theo `index`, chưa lưu dòng nào.
Gửi lại dòng đã có số ⇒ sửa số đó (nếu chưa khóa). Lưu xong → **Tính lại** phiếu nháp của tháng ([invoices.md](invoices.md#tính-lại)).
