# Mẫu hợp đồng

Mỗi tổ chức tự tạo **mẫu hợp đồng** theo **loại** (thuê phòng trọ / thuê nhà nguyên căn), gồm:

- **Tiêu đề** in trên hợp đồng (VD "HỢP ĐỒNG THUÊ PHÒNG TRỌ").
- **Điều khoản**: danh sách mục `heading` + `body`, ví dụ "Trách nhiệm của bên A".
- **Trường tùy biến**: các ô nhập thêm riêng cho loại hợp đồng, ví dụ "Cách tính tiền điện", "Tiền nước đ/người", "Số tầng".

Khi tạo hợp đồng chọn mẫu, server **chép** tiêu đề, điều khoản và định nghĩa trường vào hợp đồng. Hệ quả:

- Sửa hoặc ngừng dùng mẫu **không đổi** hợp đồng đã kích hoạt.
- Bản **nháp** nhận nội dung mới của mẫu ở lần sửa nháp kế tiếp.

> Trường tùy biến chỉ để **ghi nhận thỏa thuận** in trên hợp đồng. Việc tự tính tiền điện/nước vào phiếu thu thuộc module khoản thu (chưa có API).

Quyền: chủ trọ và phó quản lý.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Danh sách mẫu (nhóm theo loại) | `GET /contract-templates?type=&includeArchived=false` |
| Tạo mẫu: nút "Bắt đầu từ mẫu gợi ý" | `GET /contract-templates/presets` → `POST /contract-templates` |
| Sửa mẫu: trình soạn điều khoản + trình dựng trường | `GET /contract-templates/{id}`, `PUT /contract-templates/{id}` |
| Ngừng dùng / khôi phục | `POST /contract-templates/{id}/archive` · `/restore` |
| Wizard hợp đồng: bước "Chọn mẫu" + form trường tùy biến | xem [contracts.md](contracts.md) |

## Loại hợp đồng (`contractType`)

| Giá trị | Nhãn | Tiêu đề mặc định |
|---------|------|------------------|
| `RoomRental` | Thuê phòng trọ | HỢP ĐỒNG THUÊ PHÒNG TRỌ |
| `WholeHouseRental` | Thuê nhà nguyên căn | HỢP ĐỒNG THUÊ NHÀ |

Thuê nhà nguyên căn: khai báo căn nhà là một **khu** có **một phòng** (cả căn), rồi lập hợp đồng cho phòng đó như bình thường.

## Hợp đồng không cọc (`noDeposit`)

"Không cọc" là **chính sách cọc** của mẫu, dùng được cho cả thuê phòng lẫn thuê nhà (không phải loại hợp đồng riêng).

| `noDeposit` | Khi tạo / sửa hợp đồng dùng mẫu |
|-------------|----------------------------------|
| `false` (mặc định) | Cọc = `depositAmount` gửi lên, không gửi thì lấy cọc mặc định của phòng |
| `true` | Cọc **luôn = 0**, bỏ qua cọc mặc định của phòng. Gửi `depositAmount` > 0 → 400 `DEPOSIT_NOT_ALLOWED` |

UI: khi chọn mẫu `noDeposit = true` thì **ẩn / khóa ô tiền cọc**, hiện nhãn "Không cọc".
Xem nhóm hợp đồng không cọc: `GET /contracts?hasDeposit=false` (gồm cả hợp đồng không dùng mẫu nhưng cọc = 0).

## Kiểu trường tùy biến (`type`)

| `type` | Ô nhập gợi ý | Giá trị gửi lên trong `customFields` |
|--------|--------------|--------------------------------------|
| `Text` | input 1 dòng | chuỗi ≤ 500 |
| `LongText` | textarea | chuỗi ≤ 5000 |
| `Number` | input số (kèm `unit`, VD m²) | số |
| `Money` | input tiền | số nguyên 0 – 1 tỷ |
| `Date` | date picker | `"yyyy-MM-dd"` |
| `Boolean` | checkbox / switch | `true` / `false` |
| `Select` | dropdown từ `options` | một chuỗi trong `options` |

Định nghĩa trường:

```json
{ "key": "water_unit_price", "label": "Đơn giá nước", "type": "Money", "required": true, "options": null, "unit": "đ", "hint": "VD 20.000đ/người" }
```

| Thuộc tính | Quy tắc |
|-----------|---------|
| `key` | Chữ thường không dấu, số, `_`; bắt đầu bằng chữ; ≤ 40; **không trùng** trong mẫu. Là khóa trong `customFields` |
| `label` | ≤ 100 — nhãn hiển thị |
| `required` | Bắt buộc nhập khi tạo hợp đồng |
| `options` | Chỉ với `Select`: 1–30 lựa chọn, không trùng, mỗi cái ≤ 100. Kiểu khác gửi `null` |
| `unit` | ≤ 20 — hậu tố cạnh ô nhập (`đ/kWh`, `m²`) |
| `hint` | ≤ 200 — placeholder / chú thích |

Tối đa 50 trường và 30 điều khoản mỗi mẫu. Mỗi điều khoản có `heading` (≤ 200) và `body` (≤ 10.000, xuống dòng bằng `\n`).

## Mẫu gợi ý

`GET /contract-templates/presets` → mảng 3 mẫu (không lưu trong DB), dùng làm điểm xuất phát:

| Mẫu | Điều khoản | Trường |
|-----|-----------|--------|
| Thuê phòng trọ | Trách nhiệm bên A · bên B · chung · Giải quyết tranh chấp | Cách tính điện (giá nhà nước / cố định), đơn giá điện, thời điểm trả tiền điện; cách tính nước (đầu người / m³ / trọn gói), đơn giá nước, thời điểm trả; có wifi; dịch vụ khác |
| Thuê phòng trọ **không cọc** (`noDeposit: true`) | Trách nhiệm bên A · bên B · **Thanh toán (không đặt cọc)**: trả tiền thuê trước đầu kỳ, chậm trả quá N ngày bên A được chấm dứt · chung · Giải quyết tranh chấp | **Số ngày chậm trả tối đa** (bắt buộc) + các trường điện nước như trên |
| Thuê nhà nguyên căn | Đặc điểm nhà · Trách nhiệm bên A · bên B · Chấm dứt · Giải quyết tranh chấp | Diện tích đất, diện tích sàn, số tầng, giấy tờ sở hữu, mục đích sử dụng, kỳ trả tiền thuê, cho thuê lại + các trường điện nước như trên |

UI: chọn preset → mở form tạo mẫu đã điền sẵn → người dùng sửa tên, điều khoản, thêm/bớt trường → `POST`.

## Tạo mẫu

`POST /contract-templates` — **Idempotency-Key**

```json
{
  "name": "Thuê trọ khu Quang Minh",
  "contractType": "RoomRental",
  "title": "HỢP ĐỒNG THUÊ PHÒNG TRỌ",
  "clauses": [
    { "heading": "Trách nhiệm của bên A", "body": "- Tạo mọi điều kiện thuận lợi để bên B thực hiện theo hợp đồng.\n- Cung cấp nguồn điện, nước, wifi cho bên B sử dụng." },
    { "heading": "Trách nhiệm của bên B", "body": "- Thanh toán đầy đủ các khoản tiền theo đúng thỏa thuận.\n- Nếu cho khách ở qua đêm phải báo và được chủ nhà đồng ý." }
  ],
  "fields": [
    { "key": "electricity_pricing", "label": "Cách tính tiền điện", "type": "Select", "required": true,
      "options": ["Theo giá nhà nước (bậc thang EVN)", "Đơn giá cố định theo kWh"], "unit": null, "hint": null },
    { "key": "water_unit_price", "label": "Tiền nước", "type": "Money", "required": true, "options": null, "unit": "đ/người", "hint": null },
    { "key": "wifi_included", "label": "Có wifi", "type": "Boolean", "required": false, "options": null, "unit": null, "hint": null }
  ],
  "noDeposit": false
}
```

→ **201** `{ "id": "…" }`. Lỗi validation theo đường dẫn body: `name`, `clauses[0].body`, `fields[1].key`, `fields[0].options`…

## Chi tiết / danh sách

`GET /contract-templates/{id}` (danh sách trả **mảng** cùng dạng):

```json
{
  "id": "…",
  "name": "Thuê trọ khu Quang Minh",
  "contractType": "RoomRental",
  "title": "HỢP ĐỒNG THUÊ PHÒNG TRỌ",
  "clauses": [ { "heading": "…", "body": "…" } ],
  "fields": [ { "key": "water_unit_price", "label": "Tiền nước", "type": "Money", "required": true, "options": null, "unit": "đ/người", "hint": null } ],
  "noDeposit": false,
  "isArchived": false,
  "createdAt": "2026-10-02T14:20:00+00:00",
  "version": "1021"
}
```

## Sửa / ngừng dùng

- `PUT /contract-templates/{id}`: body như khi tạo + `"version"` → 200 trả mẫu. Gửi **toàn bộ** điều khoản và trường (thay thế hết).
- `POST …/archive` → 204: mẫu bị ẩn khỏi danh sách mặc định, **không chọn được cho hợp đồng mới**. Bản nháp đang dùng mẫu vẫn sửa được.
- `POST …/restore` → 204.

## Lỗi

| Code | HTTP | UI |
|------|------|----|
| `CONTRACT_TEMPLATE_NOT_FOUND` | 404 | |
| `CONTRACT_TEMPLATE_NAME_TAKEN` | 409 | Lỗi dưới ô tên |
| `CONTRACT_TEMPLATE_ARCHIVED` | 422 | Mẫu đã ngừng dùng — chọn mẫu khác |
| `DEPOSIT_NOT_ALLOWED` | 400 | Mẫu không cọc mà gửi tiền cọc > 0 — khóa ô tiền cọc |
| `CONTRACT_TEMPLATE_ALREADY_ARCHIVED` / `CONTRACT_TEMPLATE_NOT_ARCHIVED` | 409 | Tải lại |
| `CONCURRENCY_CONFLICT` | 409 | Tải lại form |
