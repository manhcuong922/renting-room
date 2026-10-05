# Khu trọ (property)

Một tổ chức có **nhiều khu trọ**. Mỗi khu có: địa chỉ, cài đặt thu tiền mặc định, **bên cho thuê** (người đứng tên trên hợp đồng),
tài khoản ngân hàng nhận tiền, nội quy. Quyền: chủ trọ và phó quản lý.

## Màn hình

| Màn hình | Endpoint |
|----------|----------|
| Danh sách khu (thẻ hoặc bảng) | `GET /properties` |
| Form tạo khu | `POST /properties` |
| Chi tiết khu — tab **Thông tin & cài đặt thu** | `GET /properties/{id}`, `PUT /properties/{id}` |
| Tab **Bên cho thuê** | `PUT /properties/{id}/lessor`, `POST /properties/{id}/lessor/reveal-id-number` |
| Tab **Ngân hàng** | `PUT /properties/{id}/bank-account` |
| Tab **Nội quy** | `PUT /properties/{id}/house-rules` |
| Tab **Phòng**, **Nhóm phòng** | xem [rooms.md](rooms.md) |
| Nút Ngừng sử dụng / Khôi phục | `POST /properties/{id}/archive` · `/restore` |

### Wizard "khu mới" gợi ý

```
Bước 1  Thông tin khu + địa chỉ + cài đặt thu   POST /properties
Bước 2  Bên cho thuê                             PUT  /properties/{id}/lessor        ⚠ thiếu thì không kích hoạt được hợp đồng
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
- `lessorComplete = false` → cảnh báo vàng "Chưa khai báo đủ bên cho thuê — chưa ký được hợp đồng" + link sang tab Bên cho thuê.
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
  "billingDefaults": { "anchorDay": 1, "chargeMode": "Prepaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30, "rentCycleMonths": 1 }
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
| `billingDefaults` | | Bỏ trống = mặc định như ví dụ. **Là giá trị gợi ý khi tạo hợp đồng mới** |

### Cài đặt thu (`billingDefaults`)

| Trường | Miền giá trị | Nhãn UI gợi ý |
|--------|--------------|---------------|
| `anchorDay` | 1–31 | "Ngày chốt kỳ thu" — ngày 31 tự hiểu là cuối tháng với tháng ngắn |
| `chargeMode` | `Prepaid` / `Postpaid` | "Thu tiền phòng đầu kỳ / cuối kỳ" |
| `paymentDueDays` | 0–60 | "Hạn đóng sau ngày chốt (ngày)" |
| `prorationMode` | `Daily` / `FullPeriod` | "Tháng lẻ tính theo ngày ở / tính tròn tháng" |
| `noticeDays` | 0–180 | "Số ngày báo trước khi trả phòng" (mặc định 30 — báo trước 1 tháng) |
| `rentCycleMonths` | 1 / 2 / 3 / 6 / 12 | "Đóng tiền phòng mỗi … tháng" (bỏ trống = 1) — 400 `INVALID_RENT_CYCLE` |

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
  "billingDefaults": { "anchorDay": 1, "chargeMode": "Prepaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30, "rentCycleMonths": 1 },
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

`lessor` = `null` khi chưa khai báo; `bankAccount` = `null` khi chưa có.

## Sửa thông tin khu

`PUT /properties/{id}` → 200 trả chi tiết mới.

```json
{
  "name": "Khu Mẫu",
  "address": { "streetAddress": "Số 5 ngõ 10", "communeName": "Phường Cầu Giấy", "provinceName": "Hà Nội", "communeCode": null, "provinceCode": null },
  "description": "Gần ĐH Quốc gia",
  "evnCustomerCode": "PD0100123456",
  "land": null,
  "billingDefaults": { "anchorDay": 5, "chargeMode": "Prepaid", "paymentDueDays": 5, "prorationMode": "Daily", "noticeDays": 30, "rentCycleMonths": 1 },
  "version": "944"
}
```

`billingDefaults` bắt buộc ở PUT. Đổi cài đặt thu **chỉ áp cho hợp đồng tạo sau** — hợp đồng cũ giữ cài đặt riêng.
UI nên ghi chú điều này cạnh nhóm cài đặt.

## Bên cho thuê

`PUT /properties/{id}/lessor` → 200 trả chi tiết khu.

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

`POST /properties/{id}/lessor/reveal-id-number` → `{ "idNumber": "001080012345" }` (mỗi lần gọi được ghi log kiểm toán). Không có quyền dữ liệu nhạy cảm → 403 `SENSITIVE_DATA_FORBIDDEN`.

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
