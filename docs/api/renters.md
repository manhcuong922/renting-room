# Người thuê / người ở

Hồ sơ người thuê dùng chung trong tổ chức: một người có thể là **đại diện ký hợp đồng** hoặc **người ở cùng** ở nhiều hợp đồng
(qua các thời điểm). Người thuê **không đăng nhập**. Mỗi số giấy tờ (theo loại) chỉ có **một hồ sơ** trong tổ chức.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Danh sách / tìm kiếm người thuê | `GET /renters` (tên, SĐT) · `POST /renters/search` (có số giấy tờ) |
| Form tạo hồ sơ (cũng dùng làm modal "Thêm nhanh" trong wizard hợp đồng) | `POST /renters` |
| Chi tiết / sửa hồ sơ | `GET /renters/{id}`, `PUT /renters/{id}` |
| Nút 👁 xem số giấy tờ đầy đủ | `POST /renters/{id}/reveal-id-number` |

## Tìm kiếm

**Theo tên / SĐT:** `GET /renters?q=lan&page=1&pageSize=20`

**Theo số giấy tờ:** `POST /renters/search` — số giấy tờ đi trong **body**, không đặt trên URL (URL bị ghi vào log proxy,
lịch sử trình duyệt, header `Referer`). Không cần `Idempotency-Key` (chỉ đọc).

```json
{ "idNumber": "036202012345", "idType": "CitizenId", "q": null, "page": 1, "pageSize": 20 }
```

| Trường | Cách khớp |
|--------|-----------|
| `q` | Một phần **họ tên, không cần dấu** (`tran thi` khớp "Trần Thị Lan") **hoặc** đúng SĐT (`0987 654 321`, `+84987654321`) |
| `idNumber` | **Khớp chính xác** toàn bộ số giấy tờ (không tìm một phần — dữ liệu được mã hóa). Chỉ có ở `POST /renters/search` |
| `idType` | Kèm `idNumber` để thu hẹp; bỏ trống = tìm mọi loại |

Sắp theo tên. Response (cả hai endpoint): trang `RenterDto` (như chi tiết bên dưới).

**Ô chọn người thuê (autocomplete) trong wizard hợp đồng** — gợi ý:
1. Người dùng gõ ≥ 2 ký tự → debounce 300ms → `GET /renters?q=…&pageSize=10`.
2. Hiện `fullName` · `dateOfBirth` · `phone` · `idNumberMasked`.
3. Không thấy → nút "Thêm người thuê mới" mở modal tạo → tạo xong tự chọn.
4. Gõ đủ 9/12 chữ số → có thể gọi thêm `POST /renters/search` với `{ "idNumber": … }` để tìm chính xác.

## Tạo hồ sơ

`POST /renters` — **Idempotency-Key** — body **phẳng**:

```json
{
  "fullName": "Trần Thị Lan",
  "dateOfBirth": "2002-04-15",
  "gender": "Female",
  "phone": "0987654321",
  "email": null,
  "nationality": "VN",
  "idType": "CitizenId",
  "idNumber": "036202012345",
  "idIssueDate": "2021-05-10",
  "idIssuePlace": "Cục CS QLHC về TTXH",
  "permanentAddress": "Xã Hải Hậu, Nam Định",
  "occupation": "Sinh viên",
  "workplace": "ĐH Bách khoa",
  "emergencyContactName": "Trần Văn Nam (bố)",
  "emergencyContactPhone": "0912345678",
  "note": null
}
```

→ **201** `{ "id": "…" }`

| Trường | Bắt buộc | Quy tắc |
|--------|:-------:|---------|
| `fullName` | ✅ | ≤ 200 |
| `dateOfBirth` | ✅ | Từ 1900 tới hôm nay. (Người **ký** hợp đồng phải đủ 18 — kiểm tra khi tạo HĐ) |
| `gender` | ✅ | `Male` / `Female` / `Other` |
| `idType` | ✅ | `CitizenId` / `LegacyId` / `Passport` |
| `idNumber` | ✅ | CCCD 12 số · CMND 9 số · hộ chiếu 6–20 chữ/số (bỏ qua khoảng trắng). **Trẻ em chưa có thẻ**: nhập **số định danh cá nhân** 12 số (trên giấy khai sinh / VNeID của cha mẹ) với `idType = CitizenId`; ngày cấp, nơi cấp để trống |
| `idIssueDate` | | Sau ngày sinh, không ở tương lai |
| `phone` | | Di động VN hoặc dạng `+mã nước` |
| `nationality` | | Mã ISO 2 chữ (`VN`, `KR`, `CN`…), mặc định `VN` |
| `email` | | Hợp lệ |
| `idIssuePlace`, `occupation`, `workplace`, `emergencyContactName` | | ≤ 200 |
| `permanentAddress` | | ≤ 500 — nơi thường trú (in vào hợp đồng) |
| `emergencyContactPhone` | | ≤ 20 |
| `note` | | ≤ 2000 |

UI: `idType = Passport` → gợi ý chọn `nationality` khác VN; nhãn ô số đổi theo loại ("Số CCCD (12 số)", "Số hộ chiếu").

| Lỗi | UI |
|-----|----|
| 409 `RENTER_ID_NUMBER_EXISTS` | "Số giấy tờ đã có hồ sơ" → body có `existingRenterId` → mở / đề xuất **dùng hồ sơ đó** (một người được đứng tên nhiều phòng; là người ở thì chỉ ở 1 phòng) |
| 400 `VALIDATION_FAILED` | Key `errors` trùng tên field body: `fullName`, `idNumber`… |

## Chi tiết

`GET /renters/{id}`

```json
{
  "id": "95e22443-fc6d-4211-b15f-be7e30de5780",
  "fullName": "Trần Thị Lan",
  "dateOfBirth": "2002-04-15",
  "gender": "Female",
  "phone": "0987654321",
  "email": null,
  "nationality": "VN",
  "idType": "CitizenId",
  "idNumberMasked": "********2345",
  "idIssueDate": null,
  "idIssuePlace": null,
  "permanentAddress": "Nam Định",
  "occupation": null,
  "workplace": null,
  "emergencyContactName": null,
  "emergencyContactPhone": null,
  "note": null,
  "createdAt": "2026-10-02T13:19:06.745243+00:00",
  "version": "950"
}
```

Tab "Lịch sử thuê" trong chi tiết: `GET /contracts?renterId={id}` — các hợp đồng người này **đứng tên hoặc ở cùng**
(xem [contracts.md](contracts.md)).

## Sửa hồ sơ

`PUT /renters/{id}` — body **bọc trong `renter`** + `version`:

```json
{
  "renter": { "fullName": "Trần Thị Lan", "dateOfBirth": "2002-04-15", "gender": "Female", "idType": "CitizenId", "idNumber": null, "phone": "0987654321", "permanentAddress": "Nam Định" },
  "version": "950"
}
```

- `idNumber: null` = **giữ số cũ** (form sửa để trống ô số, placeholder = `idNumberMasked`).
- Đổi `idType` thì bắt buộc nhập số mới → 400 `ID_NUMBER_REQUIRED`.
- Key lỗi validation có tiền tố: `renter.fullName`, `renter.idNumber`.
- Sửa hồ sơ **không đổi** thông tin đã in trên hợp đồng đã kích hoạt (hợp đồng giữ bản chụp lúc ký).

## Xem số giấy tờ

`POST /renters/{id}/reveal-id-number` → `{ "idNumber": "036202012345" }`. Không có quyền dữ liệu nhạy cảm → 403 `SENSITIVE_DATA_FORBIDDEN` (ẩn nút khi `canViewSensitiveData = false`).
Mỗi lần gọi được ghi log kiểm toán. Không cache, không tự gọi; ẩn lại sau ~30 giây.
Giới hạn **5 lần/phút mỗi tài khoản**, dùng chung với xem số giấy tờ bên cho thuê, xuất Excel và đổi mật khẩu → 429 `TOO_MANY_REQUESTS` (khóa nút theo `Retry-After`).
