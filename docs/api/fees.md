# Khoản thu (điện, nước, dịch vụ)

Mỗi **khu** có danh mục khoản thu riêng. Mỗi **hợp đồng** chọn khoản **cố định / theo số lượng** áp cho phòng đó, kèm số lượng và giá riêng nếu cần.
**Điện, nước theo công tơ đi theo phòng** — không gắn vào hợp đồng: phòng đang có người thuê thì tính theo công tơ của phòng,
từ chỉ số ngày nhận phòng — xem [meters.md](meters.md).
Đây là dữ liệu nền cho tính tiền phiếu hằng tháng (chưa có API phiếu).

Quyền: chủ trọ và phó quản lý.

## 2 nhóm khoản thu (`group` + `chargeBasis`)

| Nhóm | `group` | `chargeBasis` | Cách tính 1 kỳ | Ví dụ |
|------|---------|---------------|----------------|-------|
| **Điện nước** | `Metered` | — | (chỉ số mới − chỉ số cũ) × **một giá hoặc giá theo bậc** — theo công tơ của **phòng** | Điện 3.500đ/kWh, Nước 15.000đ/m³ |
| **Dịch vụ** | `Service` | `PerRoom` | đơn giá × 1 | Mạng 120.000đ/phòng, Rác 20.000đ/phòng |
| | | `PerOccupant` | đơn giá × **số người ở** đầu kỳ | Nước 20.000đ/người |
| | | `PerUnit` | đơn giá × **số gói** đăng ký trên hợp đồng | Giữ xe 120.000đ/xe — 2 xe = 2 gói |

**Phụ thu** (sửa chữa do người thuê làm hỏng, lắp thêm có thu phí…) không có trong danh mục — nhập trên phiếu nháp (module phiếu, chưa có API).

**Linh hoạt theo phòng**: một khu có thể có cả "Nước" (theo công tơ) và "Nước theo người" (theo đầu người). Phòng A có công tơ nước
(không cần gắn gì vào hợp đồng), phòng B không lắp công tơ nước và gắn khoản "Nước theo người" vào hợp đồng. Không đổi được cách tính của một khoản đã tạo — tạo khoản mới rồi ngừng dùng khoản cũ.

Khu mới tạo **có sẵn** "Điện" (kWh) và "Nước" (m³) theo công tơ, chưa có giá (`autoAttach = false`) → vào nhập giá trước.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Tab **Khoản thu** trong chi tiết khu | `GET /properties/{propertyId}/fee-types?includeArchived=false` |
| Thêm khoản thu | `POST /properties/{propertyId}/fee-types` |
| Sửa tên / đơn vị / tự gắn / thứ tự | `PUT /fee-types/{id}` |
| Bảng giá (lịch sử) + thêm giá mới | `GET /fee-types/{id}`, `POST /fee-types/{id}/prices`, `DELETE /fee-types/{id}/prices/{priceId}` |
| Ngừng dùng / khôi phục | `POST /fee-types/{id}/archive` · `/restore` |
| Nút "Sao chép từ khu khác" | `POST /properties/{propertyId}/fee-types/copy-from/{sourcePropertyId}` |
| Bước "Khoản thu" trong wizard hợp đồng | xem [contracts.md](contracts.md#khoản-thu-của-hợp-đồng) |

## Danh sách

`GET /properties/{propertyId}/fee-types` → **mảng**:

```json
[
  {
    "id": "…", "propertyId": "…", "name": "Điện", "group": "Metered", "chargeBasis": null, "unit": "kWh",
    "systemCode": "ELECTRICITY", "autoAttach": false, "defaultQuantity": null, "sortOrder": 0, "isArchived": false,
    "currentPrice": { "id": "…", "effectiveFrom": "2026-10-01", "unitPrice": 3500, "note": null, "tiers": null },
    "prices": [ { "id": "…", "effectiveFrom": "2026-10-01", "unitPrice": 3500, "note": null, "tiers": null } ],
    "version": "1203"
  }
]
```

`currentPrice` = giá đang hiệu lực hôm nay (null = chưa nhập giá → hiện nhãn đỏ "Chưa có giá"). `prices` mới nhất trước.

## Tạo khoản thu

`POST /properties/{propertyId}/fee-types` — **Idempotency-Key**

```json
{
  "name": "Nước theo người",
  "group": "Service",
  "chargeBasis": "PerOccupant",
  "unit": "người",
  "autoAttach": false,
  "defaultQuantity": null,
  "sortOrder": 10,
  "initialPrice": { "effectiveFrom": "2026-10-01", "unitPrice": 20000 }
}
```

Phí giữ xe: `{ "name": "Giữ xe máy", "group": "Service", "chargeBasis": "PerUnit", "unit": "xe", "autoAttach": false, "defaultQuantity": 1, "vehicleType": "Motorbike" }`.

→ **201** `{ "id": "…", "warnings": [] }`

| Trường | Quy tắc |
|--------|---------|
| `name` | 1–100; không trùng (không phân biệt hoa thường) với khoản đang dùng trong khu (409 `FEE_NAME_TAKEN`) |
| `group` | `Metered` (điện nước) / `Service` (dịch vụ) — **không đổi được** sau khi tạo |
| `chargeBasis` | Bắt buộc khi `Service`: `PerRoom` / `PerOccupant` / `PerUnit`; `Metered` để null (400 `INVALID_CHARGE_BASIS`) |
| `unit` | 1–20 (`kWh`, `m³`, `người`, `phòng`, `xe`…) |
| `autoAttach` | Tự gắn vào hợp đồng mới của khu (vẫn bỏ được trong wizard). Khoản `Metered` luôn lưu `false` — ẩn ô này khi chọn nhóm Điện nước |
| `defaultQuantity` | Chỉ `PerUnit`: số gói mặc định 0–100 (mặc định 1 khi gắn) |
| `vehicleType` | Chỉ `PerUnit`: đánh dấu là **phí giữ xe** loại `Motorbike` / `Bicycle` / `ElectricBike` / `Car` — hợp đồng có số xe đăng ký khác số lượng phí ⇒ cảnh báo `PARKING_QUANTITY_MISMATCH` |
| `initialPrice` | Tùy chọn — như thêm giá bên dưới |

## Bảng giá

`POST /fee-types/{id}/prices`

**Một giá**:

```json
{ "effectiveFrom": "2026-11-01", "unitPrice": 3800, "note": "Tăng theo giá EVN" }
```

**Theo bậc** (chỉ điện nước theo công tơ — chủ trọ chọn kiểu nào cũng được cho từng bản giá):

```json
{ "effectiveFrom": "2026-11-01", "tiers": [ { "upTo": 50, "price": 1984 }, { "upTo": 100, "price": 2050 }, { "upTo": null, "price": 2380 } ] }
```

`upTo` là mốc **lũy kế** (0–50, 51–100, trên 100), tăng dần, bậc cuối `null`. 120 kWh = 50×1.984 + 50×2.050 + 20×2.380.
Bậc tính theo **lượng tiêu thụ của kỳ**, không quy đổi theo số ngày ở. Bản giá trả `tiers` (null = một giá); `unitPrice` = giá bậc 1.
Đổi từ một giá sang theo bậc (hoặc ngược lại) = thêm bản giá mới.

→ **201** `{ "id": "…", "warnings": [ … ] }`

| Quy tắc | Lỗi |
|---------|-----|
| `unitPrice` bắt buộc (trừ khi gửi `tiers`); ≥ 0, tối đa 2 số lẻ; theo chỉ số ≤ 100.000đ/đơn vị, loại khác ≤ 50 triệu | 400 |
| `tiers`: 1–10 bậc, `upTo` tăng dần, bậc cuối `null`, giá mỗi bậc ≤ 100.000đ | 400 `INVALID_TIERS` |
| Giá theo bậc cho dịch vụ | 400 `FEE_TIERS_METERED_ONLY` |
| Trùng ngày hiệu lực với bản giá có sẵn | 409 `FEE_PRICE_DATE_EXISTS` |
| Ngày hiệu lực thuộc kỳ đã chốt phiếu (khi có module phiếu) | 422 `FEE_PRICE_LOCKED` |
| Ngày hiệu lực quá hôm nay + 1 năm | 400 |

Cảnh báo (vẫn lưu): `ELECTRICITY_PRICE_ABOVE_THRESHOLD` — giá điện (theo bậc: bậc cao nhất) cao hơn mức tham chiếu (mặc định 3.460đ/kWh, cấu hình
`Fees:ElectricityPriceWarningThreshold`). Tiền điện thu của người thuê không được vượt giá bán lẻ (TT 60/2025/TT-BCT).

Giá không sửa — **xóa rồi thêm lại**. Giá áp cho một kỳ = bản giá mới nhất có ngày hiệu lực ≤ ngày bắt đầu kỳ.

## Ngừng dùng

`POST /fee-types/{id}/archive` → 204. Còn hợp đồng nháp / hiệu lực / thanh lý đang gắn khoản → 422 `FEE_IN_USE` (gỡ khỏi các
hợp đồng trước); khoản điện nước còn công tơ hoạt động → 422 `FEE_HAS_ACTIVE_METERS` ([meters.md](meters.md)). Khôi phục bị chặn nếu đã có khoản cùng tên đang dùng (409 `FEE_NAME_TAKEN`).

## Sao chép từ khu khác

`POST /properties/{targetId}/fee-types/copy-from/{sourceId}` — `{ "includePrices": true, "effectiveFrom": "2026-11-01" }`

→ **200** `{ "copied": 2, "skipped": ["Nước"] }`. Trùng tên bỏ qua; "Điện"/"Nước" mặc định của khu đích chưa có giá thì được
chép giá vào. `effectiveFrom` mặc định hôm nay.

## Lỗi

| Code | HTTP | UI |
|------|------|----|
| `FEE_TYPE_NOT_FOUND` / `FEE_PRICE_NOT_FOUND` | 404 | |
| `FEE_NAME_TAKEN` / `FEE_SYSTEM_CODE_TAKEN` | 409 | Ô tên |
| `FEE_PRICE_DATE_EXISTS` | 409 | Ô ngày hiệu lực |
| `FEE_PRICE_LOCKED` | 422 | Chọn ngày từ kỳ chưa chốt |
| `FEE_IN_USE` | 422 | Gỡ khỏi hợp đồng trước |
| `FEE_ALREADY_ARCHIVED` / `FEE_NOT_ARCHIVED` | 409 | Tải lại |
| `FEE_ARCHIVED` / `FEE_NOT_IN_PROPERTY` | 422 | Khi gắn vào hợp đồng: chọn khoản khác |
| `FEE_METERED_FOLLOWS_ROOM` | 400 | Khi gắn vào hợp đồng: điện / nước theo công tơ không gắn — bỏ khỏi danh sách chọn |
