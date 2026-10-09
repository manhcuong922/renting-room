# Phòng & nhóm phòng

## Trạng thái phòng — server tính, UI chỉ hiển thị

`status` **không sửa trực tiếp**. Server suy ra từ hợp đồng và cờ bảo trì / ngừng dùng, theo thứ tự ưu tiên:

| Ưu tiên | `status` | Điều kiện | Màu gợi ý |
|:------:|----------|-----------|-----------|
| 1 | `Archived` — Ngừng dùng | Phòng (hoặc khu) đã ngừng | Xám |
| 2 | `Maintenance` — Bảo trì | Đang bảo trì | Cam |
| 3 | `Occupied` — Đang thuê | Có hợp đồng Hiệu lực / Đang thanh lý bao gồm hôm nay (kể cả **ngày trả phòng**) | Đỏ / xanh dương |
| 4 | `Reserved` — Giữ chỗ | Có hợp đồng Nháp, hoặc HĐ đã kích hoạt nhưng ngày bắt đầu ở tương lai | Vàng |
| 5 | `Vacant` — Trống | Còn lại | Xanh lá |

Khi `Occupied`, `currentContract` chứa tóm tắt hợp đồng (người đại diện, số người đang ở, ngày hết hạn) và `flags` — nhãn cần xử lý trên ô
phòng: `RepresentativeMovedOut` "Người ký đã rời đi", `NoOccupantLeft` "Không còn người ở", `ExpiredAwaitingDecision` "Quá hạn — chờ quyết định",
`Holdover` "Ở tiếp chưa ký lại" (chi tiết: [contracts.md](contracts.md#cần-xử-lý-flags)). Hợp đồng quá hạn **vẫn là `Occupied`**.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| **Sơ đồ phòng** theo khu: lưới nhóm theo `floor`, ô màu theo `status` | `GET /rooms?propertyId={id}&pageSize=100` |
| Danh sách phòng (lọc khu, trạng thái, tầng, nhóm, tìm mã) | `GET /rooms` |
| Tạo một phòng | `POST /properties/{propertyId}/rooms` |
| Tạo hàng loạt theo tầng | `POST /properties/{propertyId}/rooms/bulk` |
| Chi tiết phòng (thông số + hợp đồng hiện hành + nút thao tác) | `GET /rooms/{id}` |
| Sửa phòng | `PUT /rooms/{id}` |
| Bảo trì / ngừng dùng / khôi phục | `POST /rooms/{id}/maintenance/start` · `/maintenance/end` · `/archive` · `/restore` |
| Nhóm phòng của khu (dùng cho giảm giá theo nhóm sau này) | `/properties/{propertyId}/room-groups`, `/room-groups/{id}` |

Ô phòng trên sơ đồ gợi ý: `code` to · giá niêm yết · `currentContract.representativeName` · `currentContract.occupantCount` 👤 (kèm "/ maxOccupants" nếu có khai — chỉ mô tả loại phòng, không cảnh báo).
Bấm ô Trống → nút "Tạo hợp đồng" (mở wizard với `roomId` điền sẵn).

## Danh sách

`GET /rooms?propertyId=&status=&floor=&groupId=&search=&overdue=&page=1&pageSize=20`

| Query | Ý nghĩa |
|-------|---------|
| `propertyId` | Lọc theo khu |
| `status` | `Vacant` · `Reserved` · `Occupied` · `Maintenance` · `Archived`. **Mặc định ẩn phòng ngừng dùng** — muốn xem phải lọc `Archived` |
| `floor` | Đúng giá trị tầng (VD `1`, `Tầng trệt`) |
| `groupId` | Phòng thuộc nhóm |
| `search` | Một phần mã phòng (không phân biệt hoa thường) |
| `overdue` | `true` = phòng có phiếu **quá hạn thanh toán** chưa thu đủ (bộ lọc nhanh "Quá hạn") |

Sắp xếp sẵn theo mã khu → mã phòng.

```json
{
  "items": [
    {
      "id": "4153f47f-3ad8-43f1-afea-a40e51e24566",
      "propertyId": "aa9daa2c-9c31-4e96-9259-6295c9e44320",
      "propertyCode": "KMAU",
      "code": "101",
      "floor": "1",
      "areaM2": 18.5,
      "maxOccupants": 2,
      "listedRent": 3500000,
      "defaultDeposit": null,
      "amenities": ["air_con", "wc_private"],
      "description": null,
      "status": "Occupied",
      "maintenanceNote": null,
      "currentContract": {
        "id": "274fe373-ac0f-4870-a032-d95777d23204",
        "contractNo": "HD2026-0001",
        "representativeName": "Trần Thị Lan",
        "startDate": "2026-10-02",
        "endDate": "2027-10-02",
        "occupantCount": 1,
        "flags": []
      },
      "version": "946",
      "outstandingAmount": 2378000
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 2, "totalPages": 1
}
```

`outstandingAmount` = tổng còn nợ của các phiếu đã chốt của phòng (mọi hợp đồng) — > 0 thì hiện nhãn **"Còn nợ"** + số tiền trên ô phòng
([payments.md](payments.md)).
`overdueAmount` / `isOverdue` = phần còn nợ của phiếu đã **quá hạn thanh toán** (ngày chốt phiếu + số ngày hạn của khu) ⇒ nhãn **đỏ "Quá hạn"**.

`GET /rooms/{id}` trả đúng một phần tử như trên.

## Tạo một phòng

`POST /properties/{propertyId}/rooms` — **Idempotency-Key**

```json
{
  "code": "101",
  "spec": {
    "floor": "1",
    "areaM2": 18.5,
    "maxOccupants": 2,
    "listedRent": 3500000,
    "defaultDeposit": 3500000,
    "amenities": ["air_con", "wc_private"],
    "description": "Cửa sổ hướng Nam"
  }
}
```

→ **201** `{ "id": "…" }`

| Trường | Bắt buộc | Quy tắc |
|--------|:-------:|---------|
| `code` | ✅ | ≤ 20, chữ số `. _ / -`, tự viết hoa; duy nhất trong khu (`ROOM_CODE_TAKEN`) |
| `spec.floor` | | ≤ 10 ký tự (dùng để nhóm sơ đồ) |
| `spec.areaM2` | | 0 < x ≤ 1000, tối đa 2 số lẻ |
| `spec.maxOccupants` | | `null` hoặc 1–20 — **số người theo loại phòng, chỉ để mô tả**; không giới hạn số người ở (PR-BR-06) |
| `spec.listedRent` | | Giá niêm yết — **gợi ý** khi tạo hợp đồng, sửa không ảnh hưởng HĐ đã có |
| `spec.defaultDeposit` | | Tiền cọc gợi ý |
| `spec.amenities` | | Tối đa 30 mã, mỗi mã `a-z 0-9 _` (VD `air_con`, `wc_private`, `water_heater`, `balcony`). UI tự map mã → nhãn/icon |
| `spec.description` | | ≤ 2000 |

Lỗi validation trả key `spec.areaM2`, `spec.maxOccupants`… khớp đúng body.

## Tạo hàng loạt

`POST /properties/{propertyId}/rooms/bulk` — **Idempotency-Key**

```json
{
  "floors": [
    { "floor": "1", "codes": ["101", "102", "103"] },
    { "floor": "2", "codes": ["201", "202", "203"] }
  ],
  "maxOccupants": 2,
  "areaM2": 18.5,
  "listedRent": 3500000,
  "defaultDeposit": null,
  "amenities": ["wc_private"]
}
```

→ **201** `{ "created": 6, "roomIds": ["…", "…"] }`

- Tối đa 500 phòng / lần; thông số áp chung cho mọi phòng, sửa riêng sau.
- **Tất cả hoặc không**: một mã trùng (với phòng đã có) → 409 `ROOM_CODE_TAKEN`, `detail` liệt kê mã trùng, không phòng nào được tạo.
- Trùng mã ngay trong danh sách gửi lên → 400 `DUPLICATE_IN_REQUEST`.
- UI gợi ý: chọn số tầng + số phòng mỗi tầng + mẫu mã (`{tầng}{01..n}`) → sinh preview → người dùng sửa → gửi.

## Sửa phòng

`PUT /rooms/{id}` — `{ "code": "101A", "spec": { … }, "version": "946" }` → 200 trả phòng.

- Đổi mã trùng phòng khác trong khu → 409 `ROOM_CODE_TAKEN`.
- Đổi `listedRent` khi phòng đang có người thuê → **không** tự đổi giá HĐ. UI hỏi "Áp giá mới cho người đang thuê?" → có thì gọi
  `POST /rooms/{id}/apply-listed-rent` → 204 (đổi giá HĐ đang hiệu lực từ kỳ chưa lập phiếu đầu tiên; lỗi 400 `LISTED_RENT_REQUIRED`,
  422 `CONTRACT_NOT_ACTIVE` — xem [contracts.md](contracts.md#áp-giá-niêm-yết-mới-cho-người-đang-thuê)).

## Thao tác trạng thái

| Nút | Endpoint | Hiện khi | Lỗi |
|-----|----------|----------|-----|
| Bắt đầu bảo trì | `POST /rooms/{id}/maintenance/start` body `{ "note": "Sửa điện" }` (tùy chọn, ≤ 500) | `Vacant`, `Reserved` | 422 `ROOM_OCCUPIED` nếu có HĐ hiệu lực |
| Kết thúc bảo trì | `POST /rooms/{id}/maintenance/end` | `Maintenance` | 409 `ROOM_NOT_UNDER_MAINTENANCE` |
| Ngừng sử dụng | `POST /rooms/{id}/archive` | Không có HĐ nháp/hiệu lực/thanh lý | 422 `ROOM_HAS_CONTRACTS` |
| Khôi phục | `POST /rooms/{id}/restore` | `Archived` | 422 `PROPERTY_ARCHIVED` (khôi phục khu trước) |

Tất cả trả **204** → tải lại phòng. Trong **ngày trả phòng** của hợp đồng vừa kết thúc, phòng vẫn tính là đang thuê → không ngừng dùng / bảo trì được tới hôm sau.

Ảnh hưởng tới hợp đồng: phòng **ngừng dùng** không tạo được hợp đồng (`ROOM_ARCHIVED`); phòng **bảo trì** vẫn tạo được bản nháp
nhưng **không kích hoạt được** (`ROOM_UNAVAILABLE`) cho tới khi kết thúc bảo trì.

## Nhóm phòng

Nhóm = tập phòng **trong cùng khu** do người dùng tự đặt (VD "Tầng 1", "Phòng có ban công"). Một phòng có thể thuộc nhiều nhóm.
Hiện dùng để lọc; sau này dùng cho giảm giá theo nhóm.

| Method | URL | Body | Trả về |
|--------|-----|------|--------|
| GET | `/properties/{propertyId}/room-groups` | — | Mảng nhóm |
| POST | `/properties/{propertyId}/room-groups` | `{ "name": "Tầng 1", "description": null }` | 201 nhóm |
| PUT | `/room-groups/{id}` | `{ "name": "Tầng 1 (mặt đường)", "description": null }` | 200 nhóm |
| PUT | `/room-groups/{id}/members` | `{ "roomIds": ["…", "…"] }` — **thay toàn bộ** danh sách (≤ 500) | 200 nhóm |
| DELETE | `/room-groups/{id}` | — | 204 (phòng không bị xóa) |

```json
{
  "id": "bc809455-8783-4408-a557-08cfe5704556",
  "propertyId": "aa9daa2c-9c31-4e96-9259-6295c9e44320",
  "name": "Tầng 1",
  "description": null,
  "roomIds": ["4153f47f-3ad8-43f1-afea-a40e51e24566"]
}
```

UI chọn phòng cho nhóm: danh sách checkbox phòng của khu (`GET /rooms?propertyId=`) → gửi mảng đã chọn.
Lỗi: 409 `ROOM_GROUP_NAME_TAKEN` (tên ≤ 100, duy nhất trong khu), 422 `ROOM_NOT_IN_PROPERTY`, 404 `ROOM_GROUP_NOT_FOUND`.
