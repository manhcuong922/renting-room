# Nhập từ Excel

Dùng **một lần khi chuyển khu đang chạy từ sổ ghi chép sang phần mềm**: tạo mới hàng loạt phòng và người thuê đang ở. Import **chỉ tạo mới** —
không sửa, ghi đè hay kết thúc dữ liệu đã có (việc đó làm ở màn phòng / người thuê / HĐ). Chỉ chủ trọ.

| Bước | Phòng (PR-UC-11) | Người thuê đang ở (RT-UC-10) |
|------|------------------|------------------------------|
| Tải mẫu | `GET /properties/{id}/rooms/import-template` | `GET /properties/{id}/tenancies/import-template` |
| Xem trước | `POST /properties/{id}/rooms/import/preview` | `POST /properties/{id}/tenancies/import/preview` |
| Lưu | `POST /properties/{id}/rooms/import` | `POST /properties/{id}/tenancies/import` |

Nhập **phòng trước**, rồi người thuê.

## Luồng

1. **Xem trước** — `multipart/form-data`, field `file`: server đọc file, kiểm từng dòng theo dữ liệu hiện tại, trả kết quả. **Không lưu gì,
   server không giữ gì** — đóng màn là mất.
2. Người dùng **sửa thẳng ô sai trên màn** (hoặc sửa file rồi tải lại).
3. **Lưu** — gửi lại mảng `row` (đã sửa) dạng JSON, **bắt buộc `Idempotency-Key`** (chặn bấm 2 lần). Server **kiểm lại toàn bộ**, lưu phần
   hợp lệ, trả kết quả từng đơn vị. Đơn vị lỗi giữ lại trên màn để sửa tiếp rồi lưu lại (đơn vị đã lưu gửi lại sẽ bị bỏ qua — không nhân đôi).

FE nên nhắc xem trước lại nếu quá 15 phút không thao tác (dữ liệu có thể đã đổi). Bước lưu có thể mất vài giây — hiện thanh tiến trình.

**Giới hạn**: `.xlsx` ≤ 2 MB (giải nén ≤ 20 MB), ≤ 500 dòng, ≤ 10 lần xem trước / phút / người. Server tìm dòng tiêu đề theo mẫu (phía trên
được có dòng tiêu đề / ghi chú), chỉ đọc đúng các cột của mẫu, bỏ qua cột thừa. Ô có **công thức** ⇒ lỗi `FORMULA_NOT_ALLOWED`.
Server **bỏ qua mọi id** client gửi — tra phòng theo mã phòng, người theo số giấy tờ.

Lỗi cả file (400): `IMPORT_FILE_INVALID` (không phải xlsx), `IMPORT_FILE_TOO_LARGE`, `IMPORT_TEMPLATE_MISMATCH` (thiếu cột theo mẫu), `IMPORT_TOO_MANY_ROWS`.

## Phòng

Cột: **Mã phòng***, Tầng, Diện tích (m²), Số người (loại phòng — chỉ mô tả), Giá niêm yết, Tiền cọc mặc định, Tiện nghi (mã cách nhau dấu phẩy:
`air_con, wifi`), Ghi chú, Seri + **Chỉ số đầu kỳ** công tơ điện / nước. Có chỉ số ⇒ lắp công tơ, tính là lắp từ **ngày chốt kỳ hiện tại của khu**
(số chủ trọ ghi sổ đầu kỳ). Chỉ số công tơ **chỉ khai ở form này**.

Xem trước → 200:

```json
{
  "validCount": 3, "errorCount": 1, "fileErrors": [],
  "rows": [
    { "row": { "rowNumber": 4, "code": "101", "floor": "1", "areaM2": 20, "maxOccupants": null, "listedRent": 3000000, "defaultDeposit": 3000000,
               "amenities": ["air_con", "wifi"], "description": null, "electricitySerial": "E-101", "electricityReading": 1250,
               "waterSerial": null, "waterReading": null },
      "errors": [], "warnings": [] },
    { "row": { "rowNumber": 5, "code": "301", "...": "…" },
      "errors": [ { "code": "ROOM_CODE_TAKEN", "message": "Khu đã có phòng 301.", "column": "Mã phòng" } ], "warnings": [] }
  ]
}
```

Lưu `{ "rows": [ { …row… } ] }` → 200 (đơn vị = **từng dòng**):

```json
{ "saved": 3, "skipped": 1,
  "units": [ { "key": "101", "rowNumbers": [4], "outcome": "Saved", "errors": [] },
             { "key": "301", "rowNumbers": [5], "outcome": "Invalid", "errors": [ { "code": "ROOM_CODE_TAKEN", "…": "…" } ] } ] }
```

`outcome`: `Saved` · `Invalid` (không hợp lệ, chưa lưu) · `Failed` (hợp lệ nhưng lỗi lúc lưu do dữ liệu vừa đổi — chưa lưu).
Chặn: mã trùng trong file (`DUPLICATE_IN_FILE`), trùng phòng đã có (`ROOM_CODE_TAKEN`) và mọi quy tắc của tạo phòng / lắp công tơ.

## Người thuê đang ở

**1 sheet, mỗi dòng 1 người**; các dòng cùng mã phòng = 1 hợp đồng; đơn vị lưu = **phòng** (1 người lỗi ⇒ cả phòng bỏ qua, không tạo hồ sơ
cho người chỉ thuộc phòng lỗi).

| Cột | Bắt buộc | Ghi chú |
|-----|:-------:|---------|
| Mã phòng | ✅ | Phòng đã có, **đang trống** |
| Vai trò | ✅ | `Đứng tên` · `Đứng tên (không ở)` · `Ở cùng` — mỗi phòng đúng 1 người đứng tên, ≥ 1 người ở |
| Họ tên, Ngày sinh, Giới tính (Nam / Nữ / Khác) | ✅ | |
| Loại giấy tờ (CCCD / CMND / Hộ chiếu), Số giấy tờ | ✅ từ 14 tuổi và với người đứng tên | Dưới 14 tuổi để trống được |
| SĐT, Quốc tịch, Quê quán / thường trú, Nghề nghiệp | | |
| Quan hệ với người đứng tên | | Với `Ở cùng` — VD Vợ, Chồng, Con, Cùng ở thuê; thiếu ⇒ cảnh báo |
| Ngày vào ở | ✅ | ≤ hôm nay |
| Giá thuê, Tiền cọc | Giá ✅ ở dòng đứng tên | Dòng khác ghi thì bỏ qua (cảnh báo) |

Không có chỉ số công tơ, dịch vụ, xe: chỉ số nhận phòng = **số mới nhất** của công tơ (khai ở import phòng); dịch vụ thêm sau ở
"Phòng đang dùng dịch vụ" ([fees.md](fees.md)) — khoản "tự gắn" vẫn tự gắn; xe thêm ở màn phương tiện.

Mỗi phòng lưu: dùng lại hồ sơ cùng số giấy tờ (**không sửa** hồ sơ) hoặc tạo mới → HĐ (ngày bắt đầu = ngày vào ở sớm nhất; **tính tiền từ** đầu kỳ
hiện tại của khu, vào ở giữa kỳ ⇒ từ ngày vào ở; "Thiếu tài liệu") → kích hoạt.

Xem trước → 200: như form phòng (`rows[]` có `errors`, `warnings`) + `rooms[]`:

```json
{ "validRooms": 2, "invalidRooms": 1, "fileErrors": [],
  "rows": [ { "row": { "rowNumber": 2, "roomCode": "101", "role": "Representative", "fullName": "Phạm Văn Hùng", "...": "…" }, "errors": [], "warnings": [] } ],
  "rooms": [ { "roomCode": "101", "rowNumbers": [2, 3, 4], "isValid": true, "startDate": "2024-10-09", "billingStartDate": "2026-10-01",
               "errors": [], "warnings": [] },
             { "roomCode": "103", "rowNumbers": [7], "isValid": false,
               "errors": [ { "code": "ROOM_HAS_CONTRACTS", "message": "Phòng 103 đang có HĐ HD2026-0004 của …" } ], "warnings": [] } ] }
```

Lưu `{ "rows": [ { …row… } ] }` → 200 dạng như form phòng, `key` = mã phòng.

**Hỗ trợ**: gia đình / ở ghép vào phòng trống · người đứng tên không ở · **1 người đứng tên nhiều phòng** (ở thật tối đa 1 phòng) · trẻ < 14 tuổi
không giấy tờ · người đã có hồ sơ mà không đang ở HĐ mở (dùng lại; khác thông tin ⇒ cảnh báo `RENTER_DATA_DIFFERS`) · hộ chiếu · người đứng tên
< 18 tuổi / quan hệ chưa hợp lý (cảnh báo trên HĐ).

| Lỗi | Khi nào |
|-----|---------|
| `ROOM_NOT_FOUND` | Phòng không có / đã ngừng dùng — import phòng trước |
| `ROOM_HAS_CONTRACTS` | Phòng đã có HĐ nháp / đang ở / đang thanh lý — thêm người, ký lại, thanh lý ở màn HĐ |
| `START_DATE_IN_FUTURE` | Ngày vào ở sau hôm nay — người sắp vào thì tạo HĐ nháp |
| `OCCUPANT_LIVES_ELSEWHERE` | Một người **ở** 2 phòng (trong file, hoặc đang ở phòng khác) |
| `REPRESENTATIVE_ID_REQUIRED`, `REQUIRED` (số giấy tờ) | Người đứng tên / người ≥ 14 tuổi thiếu giấy tờ |
| `REPRESENTATIVE_COUNT`, `NO_OCCUPANT` | Phòng không có / có 2 người đứng tên; không có ai ở |
| `PERSON_MISMATCH`, `DUPLICATE_IN_ROOM` | Cùng số giấy tờ khác họ tên / ngày sinh; 1 người 2 lần trong 1 phòng |
| `INVALID_FORMAT` | Vai trò, giới tính, loại giấy tờ, quan hệ sai danh mục |

Cảnh báo: `RENTER_EXISTS`, `RENTER_DATA_DIFFERS`, `RELATIONSHIP_REQUIRED`, `IGNORED_FOR_OCCUPANT`, `STALE_METER_READING` (chỉ số công tơ ghi lần cuối
trước kỳ tính tiền > 31 ngày — cập nhật chỉ số trước, nếu không phiếu đầu gộp điện nước nhiều tháng).

**Không nhập**: người đã rời đi (lịch sử), nợ cũ, cọc đã thu.
