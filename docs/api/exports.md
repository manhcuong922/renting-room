# Xuất Excel

Quyền: chủ trọ và phó quản lý. File `.xlsx` tải về trực tiếp (không lưu trên server).
Giới hạn **5 lần/phút mỗi tài khoản** (chung với xem số giấy tờ đầy đủ và đổi mật khẩu) → 429 `TOO_MANY_REQUESTS`.

## Danh sách người thuê

Dùng để quản lý, đối chiếu đăng ký tạm trú, gửi công an khu vực. Có thể xuất theo **một hay nhiều khu**, **tầng**, **nhóm phòng**, **phòng**,
người **đang ở hôm nay** hoặc **đã ở trong một khoảng ngày**.

### Màn hình gợi ý

Nút **"Xuất Excel"** ở danh sách người thuê / danh sách khu / sơ đồ phòng → hộp thoại:

| Ô | Gửi lên | Nguồn dữ liệu cho ô chọn |
|---|---------|--------------------------|
| Khu trọ (chọn nhiều, trống = tất cả) | `propertyIds` | `GET /properties` |
| Tầng (chọn nhiều) | `floors` | các giá trị `floor` của `GET /rooms?propertyId=` |
| Nhóm phòng (chọn nhiều) | `roomGroupIds` | `GET /properties/{id}/room-groups` |
| Phòng (chọn nhiều) | `roomIds` | `GET /rooms?propertyId=` |
| Thời điểm: "Đang ở hôm nay" / "Trong khoảng ngày" | `fromDate`, `toDate` | |
| Chia sheet: theo khu / theo tầng / gộp 1 sheet | `layout` | |
| ☐ Hiện đầy đủ số giấy tờ | `includeSensitive` | Cảnh báo: dữ liệu cá nhân, thao tác được ghi log. **Ẩn** khi `canViewSensitiveData = false` (gửi `true` → 403 `SENSITIVE_DATA_FORBIDDEN`) |

Các bộ lọc kết hợp **AND** (VD khu A + tầng 2 = tầng 2 của khu A).

### `POST /exports/renters`

Không cần `Idempotency-Key` (không tạo dữ liệu).

```json
{
  "propertyIds": ["aa9daa2c-9c31-4e96-9259-6295c9e44320"],
  "floors": ["1", "2"],
  "roomGroupIds": [],
  "roomIds": [],
  "fromDate": null,
  "toDate": null,
  "layout": "SheetPerFloor",
  "includeSensitive": false
}
```

Body rỗng `{}` = tất cả khu, người đang ở hôm nay, mỗi khu 1 sheet, số giấy tờ che.

| Trường | Mặc định | Quy tắc |
|--------|----------|---------|
| `propertyIds` | tất cả khu | ≤ 100; id không thuộc tổ chức → 404 `PROPERTY_NOT_FOUND` |
| `floors` | mọi tầng | ≤ 50, khớp đúng giá trị `floor` của phòng |
| `roomGroupIds` | | ≤ 100; sai id → 404 `ROOM_GROUP_NOT_FOUND`. Phòng thuộc **bất kỳ** nhóm đã chọn |
| `roomIds` | | ≤ 500; sai id → 404 `ROOM_NOT_FOUND` |
| `fromDate`, `toDate` | hôm nay | Người có thời gian ở **giao** với khoảng `[fromDate, toDate]` (kể cả đã chuyển đi trong khoảng). Gửi 1 ngày = ngày đó. `toDate ≥ fromDate`, tối đa 10 năm |
| `layout` | `SheetPerProperty` | `SheetPerProperty` mỗi khu 1 sheet · `SheetPerFloor` mỗi tầng của mỗi khu 1 sheet · `SingleSheet` gộp |
| `includeSensitive` | `false` | `true` = số giấy tờ đầy đủ (ghi log kiểm toán) |

**200** — file `danh-sach-nguoi-thue_20261002-1530.xlsx`
(`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`).

Tải file ở web:

```ts
const res = await api.post('/exports/renters', filter, { responseType: 'blob' });
const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/.exec(res.headers['content-disposition'])?.[1] ?? 'nguoi-thue.xlsx';
const url = URL.createObjectURL(res.data);
Object.assign(document.createElement('a'), { href: url, download: decodeURIComponent(name) }).click();
URL.revokeObjectURL(url);
```

Lỗi trả JSON (ProblemDetails) như mọi API khác — kiểm tra `Content-Type` trước khi lưu file.

### Nội dung file

- Dòng 1: tiêu đề "DANH SÁCH NGƯỜI THUÊ — <khu> [— Tầng n]". Dòng 2: thời điểm lọc + giờ xuất.
- Dòng 4: tiêu đề cột (cố định khi cuộn, có nút lọc). Dữ liệu từ dòng 5.
- Mỗi dòng = **một người ở trong một hợp đồng**. Người đại diện ký nhưng không ở cùng thì không có dòng.
- Chỉ tính hợp đồng đã bàn giao (đang hiệu lực / đang thanh lý / đã kết thúc); nháp và đã hủy không tính.
- Sắp xếp: khu → tầng → phòng → hợp đồng → chủ hộ trước → người đứng tên → theo thứ tự quan hệ (vợ/chồng → cha mẹ → con → ông bà, cháu → anh chị em → …) → tên.
- Hợp đồng đang thanh lý: người ở được tính tới **ngày trả phòng** đã chốt.

| Cột | Ghi chú |
|-----|---------|
| STT · Khu · Tầng · Phòng · Số hợp đồng | |
| Vai trò | Đại diện / Người ở |
| Họ tên · Ngày sinh · Giới tính · Số điện thoại | SĐT giữ số 0 đầu |
| Loại giấy tờ · Số giấy tờ · Ngày cấp · Nơi cấp · Quốc tịch | Số giấy tờ che `********1234` trừ khi `includeSensitive` |
| Địa chỉ thường trú · Nghề nghiệp · Nơi làm việc / học tập | |
| Quan hệ với chủ hộ | Chủ hộ là người đứng tên: "Chủ hộ (người đứng tên)"; chủ hộ khác người đứng tên: "Chủ hộ"; người khác: nhãn quan hệ (+ ghi chú) |
| Người chưa thành niên | Chỉ có giá trị khi < 18 tuổi tại ngày vào ở: "Có — cha/mẹ/giám hộ đứng tên HĐ" / "Có — đã có đồng ý của cha mẹ/giám hộ" / "Có — CHƯA có đồng ý" |
| Ngày vào ở · Ngày chuyển đi | |
| Trạng thái hợp đồng · Liên hệ khẩn cấp | |

Ngày là kiểu ngày Excel (`dd/MM/yyyy`) — sắp xếp/lọc được. Chuỗi bắt đầu bằng `= + - @` luôn hiện dạng chữ (chống chèn công thức).

### Lỗi

| Code | HTTP | UI |
|------|------|----|
| `PROPERTY_NOT_FOUND` / `ROOM_GROUP_NOT_FOUND` / `ROOM_NOT_FOUND` | 404 | Tải lại danh sách lựa chọn |
| `EXPORT_TOO_LARGE` | 422 | > 20.000 dòng — thu hẹp bộ lọc |
| `VALIDATION_FAILED` (`toDate`: `INVALID_DATE_RANGE`) | 400 | Lỗi dưới ô ngày |

Xuất thông tin phòng, tiền phòng tháng, công nợ, danh sách cọc: chưa có (M10 — để sau).
