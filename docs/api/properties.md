# Khu trọ (property)

Một tổ chức có **nhiều khu trọ**. Mỗi khu có: địa chỉ, cài đặt thu tiền mặc định, **bên cho thuê** (người đứng tên trên hợp đồng),
tài khoản ngân hàng nhận tiền, nội quy. Quyền: chủ trọ và phó quản lý.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Danh sách khu (thẻ hoặc bảng) | `GET /properties` |
| Form tạo khu | `POST /properties` |
| Chi tiết khu — tab **Thông tin & cài đặt thu** | `GET /properties/{id}`, `PUT /properties/{id}` |
| Cài đặt tổ chức — **Thông tin chủ trọ** (khai 1 lần) | `GET` / `PUT /org/lessor`, `POST /org/lessor/reveal-id-number` |
| Tab **Bên cho thuê** của khu (chỉ khi khác chủ trọ) | `PUT` / `DELETE /properties/{id}/lessor`, `POST /properties/{id}/lessor/reveal-id-number` |
| Tab **Ngân hàng** | `PUT /properties/{id}/bank-account` |
| Tab **Nội quy** | `PUT /properties/{id}/house-rules` |
| Tab **Phòng**, **Nhóm phòng** | xem [rooms.md](rooms.md) |
| Nút Ngừng sử dụng / Khôi phục | `POST /properties/{id}/archive` · `/restore` |

### Wizard "khu mới" gợi ý

```
Bước 1  Thông tin khu + địa chỉ + cài đặt thu   POST /properties
Bước 2  Bên cho thuê — mặc định dùng thông tin chủ trọ (GET /org/lessor); khác chủ trọ mới PUT /properties/{id}/lessor
Bước 3  Ngân hàng (tùy chọn)                     PUT  /properties/{id}/bank-account
Bước 4  Tạo phòng hàng loạt                      POST /properties/{id}/rooms/bulk
```

## Danh sách

`GET /properties?search=&includeArchived=false&page=1&pageSize=20`

```json
{
  "items": [
    {
      "id": "aa9daa2c-9c31-4e96-9259-6295c9e44320",
      "code": "KMAU",
      "name": "Khu Mẫu",
      "addressText": "Số 5 ngõ 10, Phường Cầu Giấy, Hà Nội",
      "roomCount": 12,
      "occupiedRoomCount": 9,
      "lessorComplete": true,
      "isArchived": false
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

- Thẻ khu: tên, địa chỉ, `occupiedRoomCount/roomCount` (thanh tỉ lệ lấp đầy).
- `lessorComplete = false` (xét bên cho thuê hiệu lực — riêng của khu hoặc thông tin chủ trọ) → cảnh báo vàng "Chưa đủ thông tin bên cho thuê —
  chưa in được hợp đồng đầy đủ" + link sang **Thông tin chủ trọ**. Không chặn kích hoạt / thu tiền.
- `isArchived = true` → làm mờ, nhãn "Ngừng sử dụng".

## Tạo khu

`POST /properties` — **Idempotency-Key**

```json
{
  "code": "KMAU",
  "name": "Khu Mẫu",
  "address": {
    "streetAddress": "Số 5 ngõ 10",
    "communeName": "Phường Cầu Giấy",
    "provinceName": "Hà Nội",
    "communeCode": null,
    "provinceCode": null
  },
  "description": null,
  "evnCustomerCode": null,
  "land": { "parcelNo": null, "mapSheetNo": null, "ownershipCertificateNo": null },
  "billing": { "anchorDay": 1, "chargeMode": "Postpaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30 }
}
```

→ **201** `{ "id": "…" }`

| Trường | Bắt buộc | Quy tắc / ý nghĩa |
|--------|:-------:|-------------------|
| `code` | ✅ | ≤ 32, chữ số `. _ / -`; duy nhất trong tổ chức (tự viết hoa). **Không sửa được** sau khi tạo |
| `name` | ✅ | ≤ 200 |
| `address.streetAddress` | ✅ | Số nhà, ngõ, đường (≤ 300) |
| `address.communeName` / `provinceName` | ✅ | Xã/phường, tỉnh/thành — **địa chỉ 2 cấp** (không còn quận/huyện từ 07/2025) |
| `address.communeCode` / `provinceCode` | | Mã ĐVHC (chưa có API danh mục — để null) |
| `evnCustomerCode` | | Mã khách hàng điện lực (≤ 20) |
| `land.*` | | Số thửa, số tờ bản đồ, số giấy chứng nhận — in vào hợp đồng |
| `billing` | | Bỏ trống = mặc định như ví dụ (chốt ngày 1, **thu sau**). **Mọi hợp đồng của khu dùng chung** (HĐ không chọn riêng) |

### Cài đặt kỳ thu (`billing`)

| Trường | Miền giá trị | Nhãn UI gợi ý |
|--------|--------------|---------------|
| `anchorDay` | **1–28** | "Ngày chốt kỳ thu" — tháng nào cũng có ngày chốt |
| `chargeMode` | `Prepaid` / `Postpaid` | "Thu tiền phòng đầu kỳ / cuối kỳ" |
| `paymentDueDays` | 0–60 | "Hạn đóng sau ngày chốt (ngày)" |
| `prorationMode` | `Daily` / `FullPeriod` | "Tháng lẻ tính theo ngày ở / tính tròn tháng" |
| `noticeDays` | 0–180 | "Số ngày báo trước khi trả phòng" (mặc định 30 — báo trước 1 tháng) — **gợi ý** khi tạo HĐ, HĐ giữ riêng |

## Chi tiết

`GET /properties/{id}`

```json
{
  "id": "aa9daa2c-9c31-4e96-9259-6295c9e44320",
  "code": "KMAU",
  "name": "Khu Mẫu",
  "address": { "streetAddress": "Số 5 ngõ 10", "communeName": "Phường Cầu Giấy", "provinceName": "Hà Nội", "communeCode": null, "provinceCode": null },
  "addressText": "Số 5 ngõ 10, Phường Cầu Giấy, Hà Nội",
  "description": null,
  "evnCustomerCode": null,
  "land": { "parcelNo": null, "mapSheetNo": null, "ownershipCertificateNo": null },
  "billing": { "anchorDay": 1, "chargeMode": "Postpaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30, "changes": [] },
  "lessor": {
    "type": "Individual",
    "name": "Nguyễn Văn Chủ",
    "address": "Số 1 Láng Hạ, Hà Nội",
    "phone": "0911222333",
    "email": null,
    "idType": "CitizenId",
    "idNumberMasked": "********2345",
    "idIssueDate": null,
    "idIssuePlace": null,
    "dateOfBirth": "1980-01-01",
    "taxCode": null,
    "representativeName": null,
    "representativeTitle": null,
    "authorizationDocNo": null,
    "authorizationDocDate": null,
    "isComplete": true
  },
  "bankAccount": { "bankName": "Vietcombank", "accountNo": "0123456789", "accountName": "NGUYEN VAN CHU" },
  "houseRulesText": null,
  "isArchived": false,
  "createdAt": "2026-10-02T13:19:05.994125+00:00",
  "version": "944"
}
```

`lessor` = bên cho thuê **hiệu lực**: riêng của khu, không có thì thông tin chủ trọ (`lessorInherited: true` → UI ghi "Dùng thông tin
chủ trọ" + nút "Khai riêng cho khu này"). `null` khi cả hai đều chưa khai. `bankAccount` = `null` khi chưa có.

## Sửa thông tin khu

`PUT /properties/{id}` → 200 trả chi tiết mới.

```json
{
  "name": "Khu Mẫu",
  "address": { "streetAddress": "Số 5 ngõ 10", "communeName": "Phường Cầu Giấy", "provinceName": "Hà Nội", "communeCode": null, "provinceCode": null },
  "description": "Gần ĐH Quốc gia",
  "evnCustomerCode": "PD0100123456",
  "land": null,
  "version": "944"
}
```

Cài đặt kỳ thu **không** sửa ở đây — dùng [Cài đặt kỳ thu](#cài-đặt-kỳ-thu--đổi-ngày-chốt).

## Cài đặt kỳ thu / đổi ngày chốt

Chi tiết khu có `billing`:

```json
"billing": {
  "anchorDay": 5, "chargeMode": "Postpaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30,
  "changes": [ { "effectiveFrom": "2026-11-01", "transitionEnd": "2026-12-04", "anchorDay": 5, "chargeMode": "Postpaid", "deviationDays": 4, "adjustDays": 4 } ]
}
```

`changes` = các lần đổi khi khu **đã có phiếu** — mỗi lần có một **kỳ chuyển tiếp** `[effectiveFrom, transitionEnd]` (vẫn là tháng thu của
`effectiveFrom`), dài / ngắn hơn 1 tháng `deviationDays` ngày; tiền phòng kỳ đó = 1 tháng ± `adjustDays` ngày.

**Xem trước** — `GET /properties/{id}/billing/preview?anchorDay=5&chargeMode=Postpaid`:

```json
{ "effectiveFrom": "2026-11-01", "transitionEnd": "2026-12-04", "transitionDays": 34, "baseDays": 30, "deviationDays": 4,
  "suggestedAdjustDays": 4, "hasDraftInvoices": false,
  "rooms": [ { "contractId": "…", "roomCode": "101", "monthlyRent": 3000000, "perDay": 100000, "suggestedAmount": 400000 } ] }
```

- `effectiveFrom` = đầu kỳ chưa lập phiếu đầu tiên của khu; `null` ⇒ khu chưa có phiếu, đổi là áp lại từ đầu (không có kỳ chuyển tiếp).
- UI hiện: "Kỳ chuyển tiếp 01/11–04/12 **dư 4 ngày** (≈ 400.000đ/phòng). Tính thêm: [ 4 ] ngày" — ô số ngày điền sẵn `suggestedAdjustDays`
  (lệch ≤ 3 ngày ⇒ 0), cho sửa trong 0..dư (hoặc thiếu..0 khi kỳ ngắn hơn). Chỉ tiền phòng điều chỉnh; điện nước theo chỉ số thật; dịch vụ trọn tháng.
- `hasDraftInvoices: true` ⇒ phải chốt / xóa phiếu nháp của khu trước.

**Lưu** — `PUT /properties/{id}/billing` → 200 chi tiết khu:

```json
{ "anchorDay": 5, "chargeMode": "Postpaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30, "transitionAdjustDays": 4 }
```

| Lỗi | Khi nào |
|-----|---------|
| 422 `BILLING_SETTINGS_DRAFT_INVOICES` | Đổi ngày chốt / thu trước–thu sau khi khu còn phiếu nháp |
| 400 `TRANSITION_ADJUST_OUT_OF_RANGE` | `transitionAdjustDays` ngoài 0..dư (hoặc thiếu..0) |

- Hạn thanh toán, tính kỳ lẻ, báo trước: áp ngay (phiếu đã chốt giữ số tiền cũ).
- Đổi lại khi kỳ chuyển tiếp chưa lập phiếu ⇒ ghi đè lần đổi trước; đổi về đúng cài đặt cũ ⇒ bỏ kỳ chuyển tiếp.
- Đổi thu sau → thu trước: phiếu đầu tiên thu trước có cảnh báo `TWO_RENT_PERIODS` (người thuê trả 2 tháng tiền phòng gần nhau).

## Bên cho thuê

**Thông tin chủ trọ** (khai 1 lần, mọi khu dùng chung):

- `GET /org/lessor` → `{ "lessor": { …như dưới, có isComplete } | null, "prefill": { "name", "phone", "address" } }` — `prefill` lấy từ
  thông tin liên hệ của tổ chức để điền sẵn form lần đầu.
- `PUT /org/lessor` (body như dưới) → 200 cùng dạng `GET`. **Chỉ chủ trọ** (phó quản lý 403).
- `POST /org/lessor/reveal-id-number` → `{ "idNumber" }` (quyền / giới hạn như xem số giấy tờ bên dưới).

**Bên cho thuê riêng của khu** — chỉ khi khác chủ trọ (công ty quản lý, người được ủy quyền):

- `PUT /properties/{id}/lessor` → 200 trả chi tiết khu (`lessorInherited: false`).
- `DELETE /properties/{id}/lessor` → 200 trả chi tiết khu — bỏ khai riêng, quay về thông tin chủ trọ.

Sửa / bỏ không ảnh hưởng HĐ đã kích hoạt (HĐ giữ bản chụp lúc kích hoạt).

**Cá nhân** (chủ nhà đứng tên):

```json
{
  "type": "Individual",
  "name": "Nguyễn Văn Chủ",
  "address": "Số 1 Láng Hạ, Hà Nội",
  "phone": "0911222333",
  "email": null,
  "idType": "CitizenId",
  "idNumber": "001080012345",
  "idIssueDate": "2021-06-01",
  "idIssuePlace": "Cục CS QLHC về TTXH",
  "dateOfBirth": "1980-01-01",
  "taxCode": null,
  "representativeName": null,
  "representativeTitle": null,
  "authorizationDocNo": null,
  "authorizationDocDate": null
}
```

**Tổ chức** (công ty): `type = "Organization"`, bắt buộc `taxCode`, `representativeName`, `representativeTitle`.

| Trường | Cá nhân | Tổ chức | Ghi chú |
|--------|:------:|:------:|---------|
| `name`, `address`, `phone` | ✅ | ✅ | SĐT di động VN |
| `idType` | ✅ | | `CitizenId` / `LegacyId` / `Passport` |
| `idNumber` | ✅ lần đầu | | CCCD 12 số · CMND 9 số · hộ chiếu 6–20 chữ/số. **Gửi `null` = giữ số cũ**; đổi `idType` thì phải nhập lại số (`ID_NUMBER_REQUIRED`) |
| `dateOfBirth` | ✅ | | Phải đủ 18 tuổi (`LESSOR_UNDERAGE`) |
| `taxCode` | | ✅ | 10 số hoặc `10 số-3 số` |
| `representativeName`, `representativeTitle` | | ✅ | |
| `authorizationDocNo` + `authorizationDocDate` | | | Giấy ủy quyền — có số thì phải có ngày |

UI:
- Đổi radio Cá nhân/Tổ chức → ẩn/hiện nhóm trường.
- Ô số giấy tờ khi đã có dữ liệu: hiện `idNumberMasked` dạng placeholder, để trống = giữ nguyên; nút 👁 gọi reveal.
- `isComplete` dùng để hiện tích xanh trên tab.

### Xem số giấy tờ đầy đủ

`POST /properties/{id}/lessor/reveal-id-number` → `{ "idNumber": "001080012345" }` — số của bên cho thuê **hiệu lực** của khu (mỗi lần gọi được ghi log kiểm toán). Không có quyền dữ liệu nhạy cảm → 403 `SENSITIVE_DATA_FORBIDDEN`. Giới hạn 5 lần/phút mỗi tài khoản (chung với xem số giấy tờ người thuê, xuất Excel) → 429.

## Ngân hàng

`PUT /properties/{id}/bank-account`

```json
{ "bankName": "Vietcombank", "accountNo": "0123456789", "accountName": "NGUYEN VAN CHU" }
```

Nhập thì phải đủ 3 trường; số tài khoản 6–20 chữ số. Gửi cả 3 `null` = xóa. → 200 chi tiết khu.

## Nội quy

`PUT /properties/{id}/house-rules` — `{ "text": "1. Giữ trật tự sau 22h…" }` (≤ 20.000 ký tự, `null` = xóa).
Nội quy được **chụp lại vào hợp đồng lúc kích hoạt**; sửa sau không ảnh hưởng hợp đồng đã ký.

## Ngừng sử dụng / khôi phục

| Endpoint | Điều kiện | Kết quả |
|----------|-----------|---------|
| `POST /properties/{id}/archive` | Không còn hợp đồng Nháp / Hiệu lực / Đang thanh lý (`PROPERTY_HAS_ACTIVE_CONTRACTS`) | 204 — khu và **mọi phòng** chuyển "Ngừng sử dụng" |
| `POST /properties/{id}/restore` | Đang ngừng | 204 — chỉ khôi phục khu; phòng khôi phục riêng từng phòng |

## Lỗi thường gặp

| Code | HTTP | UI |
|------|------|----|
| `PROPERTY_CODE_TAKEN` | 409 | Lỗi dưới ô Mã |
| `PROPERTY_NOT_FOUND` | 404 | Trang "Không tìm thấy" |
| `PROPERTY_ARCHIVED` | 422 | Khu đã ngừng — không tạo phòng / hợp đồng |
| `PROPERTY_NOT_ARCHIVED` | 409 | Tải lại |
| `PROPERTY_HAS_ACTIVE_CONTRACTS` | 422 | "Còn hợp đồng đang mở, không thể ngừng" |
| `ID_NUMBER_REQUIRED` | 400 | Lỗi dưới ô số giấy tờ |
| `CONCURRENCY_CONFLICT` | 409 | Tải lại form |
