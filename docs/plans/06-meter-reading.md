# M06 — Meter Reading (Công tơ & Ghi chỉ số)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Kỳ thu: C-05. Pháp lý: L9 (điện), L10 (nước).

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Quản lý **công tơ** của từng phòng cho các khoản thu nhóm `Metered` (M04) và **chỉ số** theo thời gian,
cung cấp **sản lượng tiêu thụ chính xác, không chồng lấn, không bỏ sót** cho M07. Có **màn hình riêng** nhập chỉ số cuối kỳ
cho cả khu (yêu cầu nghiệp vụ: "tiền điện tháng trước và tháng này").

**Trong phạm vi**: lắp/gỡ/thay công tơ; chỉ số ban đầu, bàn giao, định kỳ, cuối (thanh lý), tháo; màn hình ghi chỉ số hàng loạt;
cảnh báo bất thường; khóa chỉ số đã dùng cho phiếu đã chốt; ảnh chụp công tơ (M09, tùy chọn).

**Ngoài phạm vi**: công tơ tổng chia theo người/phòng (P3); đọc công tơ IoT; OCR ảnh.

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Công tơ | `Meter` | Thiết bị đo của **1 phòng** cho **1 khoản thu Metered** |
| Chỉ số | `MeterReading` | Giá trị đọc được tại một ngày |
| Loại chỉ số | `ReadingKind` | `Initial` (lắp mới), `Handover` (bàn giao HĐ), `Periodic` (cuối kỳ), `Final` (thanh lý), `Removal` (tháo công tơ), `Adhoc` (kiểm tra, không dùng tính tiền) |
| Kỳ sử dụng | `UsagePeriod` | Khoảng thời gian tiêu thụ được tính tiền trên 1 phiếu. `Postpaid`: = kỳ của phiếu; `Prepaid`: = kỳ **liền trước** kỳ của phiếu |
| Đoạn đo | `UsageSegment` | (chỉ số đầu, chỉ số cuối) trên **một** công tơ; 1 dòng phiếu có thể gồm nhiều đoạn khi thay công tơ |
| Tháng thu | `BillingMonth` | Tháng của `PeriodStart` của phiếu (C-05) |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| MT-UC-01 | Lắp công tơ cho phòng (khoản thu Metered, số serial, ngày lắp, chỉ số ban đầu) — có thể lắp hàng loạt cho cả khu |
| MT-UC-02 | Thay công tơ (hỏng / quay vòng về 0): chỉ số tháo của công tơ cũ + chỉ số ban đầu công tơ mới trong **một** lệnh |
| MT-UC-03 | Gỡ công tơ (không thay) |
| MT-UC-04 | **Màn hình ghi chỉ số**: chọn khu + tháng thu (+ lọc khoản thu, tầng) → lưới mỗi dòng = (phòng, công tơ, HĐ): chỉ số cũ (ngày), ô nhập chỉ số mới, sản lượng tạm tính, cảnh báo; lưu hàng loạt |
| MT-UC-05 | Ghi chỉ số bàn giao (gọi từ M05 khi kích hoạt) và chỉ số cuối (khi thanh lý) |
| MT-UC-06 | Sửa/hủy chỉ số chưa bị khóa |
| MT-UC-07 | Xem lịch sử chỉ số của công tơ, biểu đồ sản lượng |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| MT-BR-01 | Mỗi `(room, fee_type)` có tối đa **1 công tơ đang hoạt động** | DB partial unique |
| MT-BR-02 | Công tơ chỉ gắn khoản thu `Metered`, cùng khu với phòng | Domain + FK composite `(org, property_id, fee_type_id)`, `(org, property_id, room_id)` |
| MT-BR-03 | **Đơn điệu**: theo thứ tự `(reading_date, sequence)`, giá trị chỉ số của **cùng một công tơ** không giảm. Chèn chỉ số lùi ngày phải ≥ chỉ số liền trước và ≤ chỉ số liền sau | Application, khóa `meters` row `FOR UPDATE` |
| MT-BR-04 | Chỉ số `Periodic` gắn `(contract_id, closing_period_start)`; tối đa 1 bản chưa hủy cho mỗi `(meter, contract, closing_period_start)`. `Final` tối đa 1 / (meter, contract). `Handover` tối đa 1 / (meter, contract) | DB partial unique |
| MT-BR-05 | `reading_date` của `Periodic` nằm trong `[usage_end − 7 ngày, usage_end + 15 ngày]` → ngoài khoảng: **cảnh báo** (không chặn). `reading_date` ≤ hôm nay + 1 | Application |
| MT-BR-06 | Chỉ số đã được dùng trong dòng phiếu **Finalized** chưa Void → **khóa**: không sửa/hủy (422 `READING_LOCKED`). Nếu chỉ dùng trong phiếu Draft → cho sửa, phiếu Draft bị đánh dấu `is_stale` | Application (query M07) |
| MT-BR-07 | Ghi chỉ số hàng loạt là **all-or-nothing**: lỗi bất kỳ dòng → không lưu dòng nào, trả lỗi theo từng dòng | Transaction |
| MT-BR-08 | Cảnh báo bất thường (không chặn): sản lượng âm (đã bị chặn bởi BR-03), = 0 khi phòng có người ở, > 3× trung bình 3 kỳ gần nhất, > ngưỡng tuyệt đối (mặc định điện 1.500 kWh, nước 100 m³ / kỳ) | Application |
| MT-BR-09 | Thay công tơ: chỉ số `Removal` của công tơ cũ và `Initial` của công tơ mới cùng ngày; công tơ cũ `removed_date` = ngày đó. Không được thay nếu ngày thay < chỉ số cuối cùng đã ghi của công tơ cũ | Domain |
| MT-BR-10 | Gỡ công tơ khi HĐ đang có đăng ký khoản Metered đó → yêu cầu thay công tơ mới hoặc bỏ đăng ký trước (422) | Application |
| MT-BR-11 | HĐ có đăng ký khoản Metered thì phòng **phải có công tơ hoạt động** cho khoản đó khi kích hoạt HĐ (bổ sung CT-BR-02) | M05 Application |
| MT-BR-12 | **Chuỗi liên tục**: chỉ số đầu của đoạn đo kỳ N = chỉ số cuối của đoạn đo kỳ N−1 (cùng HĐ, cùng công tơ) **trên phiếu chưa Void**; kỳ đầu tiên dùng chỉ số `Handover` (hoặc `Initial` nếu công tơ lắp sau khi kích hoạt). Không có khoảng hở hoặc chồng lấn | Domain (`MeterUsageCalculator`) + DB unique trên end reading |

### 3.3 Thuật toán sản lượng (dùng bởi M07)

```
Input: contract C, fee type F, usage period U = [Us, Ue], isFinal
1. meters = công tơ của room(C) với fee F có khoảng hoạt động giao U (tối đa n, sắp theo installed_date)
2. Với mỗi meter m (theo thứ tự):
   start = (a) end_reading của đoạn đo gần nhất của (C, m) trên phiếu chưa Void
           (b) nếu không có: reading Handover của (C, m)
           (c) nếu không có: reading Initial của m (công tơ lắp/thay sau khi HĐ kích hoạt)
   end   = nếu m bị tháo trong U: reading Removal của m
           ngược lại nếu isFinal: reading Final của (C, m)
           ngược lại: reading Periodic (C, m, closing_period_start = Us)
   thiếu end → lỗi MISSING_READING(meter m); start.value > end.value → lỗi (không thể xảy ra nhờ BR-03)
   segment = (m, start, end, end.value − start.value)
3. quantity = Σ segment.consumption   (làm tròn 2 số lẻ)
Output: segments[], quantity
```

Bước 2(a) "đoạn đo gần nhất" chỉ đúng vì M07 bắt buộc **lập phiếu tuần tự** (BL-BR-21) và **hủy theo LIFO** (BL-BR-22):
không bao giờ tồn tại phiếu kỳ sau khi kỳ trước chưa có phiếu, nên đoạn gần nhất luôn là đoạn của kỳ liền trước.

Ví dụ thay công tơ giữa kỳ: cũ từ 1.250 → tháo 1.320 (70); mới 0 → cuối kỳ 45 (45) ⇒ 115 kWh.

### 3.4 Màn hình ghi chỉ số — cách xác định dòng

Với khu K và tháng thu M, mỗi HĐ `Active`/`Liquidating` của K có đăng ký khoản Metered F đang hiệu lực:
- `Postpaid`: kỳ sử dụng U = kỳ có `PeriodStart` thuộc tháng M.
- `Prepaid`: U = kỳ **liền trước** kỳ có `PeriodStart` thuộc tháng M (bỏ qua nếu kỳ đó trước `start_date` của HĐ).
- HĐ `Liquidating` mà `actual_end_date` ∈ U → dòng hiển thị yêu cầu chỉ số `Final` thay vì `Periodic`.
- Dòng hiển thị **rõ khoảng U** (VD "05/10 – 04/11") để tránh nhầm tháng.

## 4. Dữ liệu

**`meters`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | |
| property_id, room_id, fee_type_id | uuid | N | FK composite (MT-BR-02) |
| serial_no | varchar(50) | Y | |
| installed_date | date | N | |
| removed_date | date | Y | ≥ installed_date |
| note | varchar(300) | Y | |
| audit, xmin | | | |

UNIQUE `(room_id, fee_type_id) WHERE removed_date IS NULL` (MT-BR-01).

**`meter_readings`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | |
| meter_id | uuid | N | FK composite |
| kind | varchar(12) | N | ReadingKind |
| reading_date | date | N | |
| sequence | bigint | N | `GENERATED ALWAYS AS IDENTITY` — phân định thứ tự khi cùng ngày |
| value | numeric(12,2) | N | ≥ 0 |
| contract_id | uuid | Y | bắt buộc với Handover/Periodic/Final |
| closing_period_start | date | Y | bắt buộc với Periodic |
| photo_attachment_id | uuid | Y | M09 |
| note | varchar(300) | Y | |
| voided_at | timestamptz | Y | |
| void_reason | varchar(300) | Y | |
| recorded_at / recorded_by | | N | |
| updated_at/by, xmin | | | |

CHECK:
- `kind NOT IN ('Handover','Periodic','Final') OR contract_id IS NOT NULL`
- `(kind = 'Periodic') = (closing_period_start IS NOT NULL)`
- `value >= 0`

UNIQUE (partial, `WHERE voided_at IS NULL`):
- `(meter_id, contract_id, closing_period_start)` WHERE kind = 'Periodic'
- `(meter_id, contract_id)` WHERE kind IN ('Handover') ; riêng `(meter_id, contract_id)` WHERE kind = 'Final'
- `(meter_id)` WHERE kind = 'Initial'; `(meter_id)` WHERE kind = 'Removal'

INDEX `(meter_id, reading_date, sequence) WHERE voided_at IS NULL`.

Đoạn đo trên phiếu lưu ở M07 (`invoice_meter_segments`) với `start_reading_id`, `end_reading_id`.

## 5. Domain model

```
Meter (aggregate root; readings là aggregate riêng để tránh load toàn lịch sử)
  + Install(room, feeType, serial, date, initialValue) : (Meter, Reading Initial)
  + Remove(date, finalValue) : Reading Removal
  + static Replace(old, date, oldFinal, newSerial, newInitial) : (Reading Removal, Meter new, Reading Initial)
MeterReading
  + Record(meter, kind, date, value, contract?, closingPeriodStart?)
  + Correct(value, reason) / Void(reason)
MonotonicityGuard (domain service): Validate(prev?, next?, candidate)
MeterUsageCalculator (domain service): Calculate(contract, feeType, usagePeriod, isFinal, readings, priorSegments) — thuần, unit test bảng
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `InstallMeterCommand` / `BulkInstallMetersCommand` | Cmd | `METER_ALREADY_ACTIVE`, `FEE_NOT_METERED` |
| `ReplaceMeterCommand` | Cmd | `READING_NOT_MONOTONIC`, `INVALID_REPLACE_DATE` |
| `RemoveMeterCommand` | Cmd | `METER_IN_USE_BY_CONTRACT` |
| `GetMeterReadingSheetQuery` | Qry | propertyId, billingMonth (`yyyy-MM`), feeTypeIds?, floor? → rows (§3.4) |
| `SaveMeterReadingsCommand` (Idempotency-Key) | Cmd | upsert theo khóa MT-BR-04; trả `rowErrors[]`, `warnings[]`, `staleDraftInvoiceIds[]` |
| `RecordHandoverReadingsCommand` / `RecordFinalReadingsCommand` | Cmd (nội bộ, gọi từ M05) | |
| `CorrectReadingCommand` / `VoidReadingCommand` | Cmd | `READING_LOCKED`, `READING_NOT_MONOTONIC` |
| `ListMeterReadingsQuery` | Qry | theo meter, khoảng ngày |

## 7. API

| Method | Route | Mã lỗi |
|--------|-------|--------|
| GET | `/rooms/{roomId}/meters?includeRemoved=` | |
| POST | `/rooms/{roomId}/meters` | 409 `METER_ALREADY_ACTIVE` |
| POST | `/properties/{propertyId}/meters/bulk` | |
| POST | `/meters/{id}/replace` · `/remove` | 422 |
| GET | `/properties/{propertyId}/meter-reading-sheet?billingMonth=2026-10&feeTypeIds=…` | |
| PUT | `/properties/{propertyId}/meter-readings` (bulk upsert, Idempotency-Key) | 422 `rowErrors` |
| PUT | `/meter-readings/{id}` · POST `/meter-readings/{id}/void` | 422 `READING_LOCKED` |
| GET | `/meters/{id}/readings?from=&to=` | |

**Ví dụ — lưới ghi chỉ số**
```json
GET /api/v1/properties/{pid}/meter-reading-sheet?billingMonth=2026-11
{
  "billingMonth": "2026-11",
  "rows": [
    {
      "roomCode": "101", "contractId": "…", "contractNo": "HD2026-0012", "representativeName": "Trần Thị Lan",
      "chargeMode": "Prepaid",
      "meterId": "…", "feeTypeName": "Điện", "unit": "kWh",
      "usagePeriod": { "start": "2026-10-05", "end": "2026-11-04" },
      "readingKind": "Periodic",
      "previous": { "readingId": "…", "value": 1250, "date": "2026-10-05", "kind": "Handover" },
      "current": null,
      "averageConsumption": null,
      "locked": false
    }
  ]
}
```

**Ví dụ — lưu hàng loạt**
```json
PUT /api/v1/properties/{pid}/meter-readings
Idempotency-Key: 9f1c…
{
  "readings": [
    { "meterId": "…", "contractId": "…", "kind": "Periodic", "closingPeriodStart": "2026-10-05", "readingDate": "2026-11-04", "value": 1338 }
  ]
}
→ 200 { "saved": 1, "warnings": [ { "meterId": "…", "code": "CONSUMPTION_SPIKE", "consumption": 88, "average": 25 } ], "staleDraftInvoiceIds": [] }
→ 422 { "code": "READINGS_INVALID", "rowErrors": [ { "index": 0, "code": "READING_NOT_MONOTONIC", "previousValue": 1350 } ] }
```

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| value | 0 ≤ x ≤ 99.999.999,99; tối đa 2 số lẻ |
| readingDate | ≤ hôm nay + 1; ≥ installed_date của meter; < removed_date (nếu có) với kind khác Removal |
| kind | trong bulk chỉ cho `Periodic`/`Final`/`Adhoc` |
| closingPeriodStart | phải là `PeriodStart` hợp lệ của HĐ (C-05) |
| contractId | HĐ của đúng phòng của meter; trạng thái Active/Liquidating |
| bulk | ≤ 1.000 dòng; không trùng khóa trong request |
| billingMonth | `yyyy-MM` |

## 9. Phân quyền
OrgOwner toàn quyền; OrgManager (P3) ghi chỉ số khu được gán (thường là người đi ghi).

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Giải pháp |
|----------|-----------|
| 2 người ghi chỉ số cùng công tơ cùng lúc | Khóa `meters` row `FOR UPDATE` (sắp theo id khi bulk để tránh deadlock) + unique MT-BR-04 |
| Sửa chỉ số sau khi phiếu đã chốt | MT-BR-06 khóa; muốn sửa → Void phiếu (M07) → sửa → lập lại |
| Chỉ số bị dùng 2 lần (2 phiếu) | `invoice_meter_segments` UNIQUE `(end_reading_id) WHERE voided = false` (M07) |
| Khoảng hở chỉ số giữa 2 kỳ | MT-BR-12: start luôn lấy từ end của đoạn trước — không lấy từ "chỉ số gần nhất theo ngày" |
| Void phiếu → đoạn đo được giải phóng | M07 đặt `voided = true` trên segment trong cùng transaction với Void phiếu |
| Lưu bulk bị retry | Idempotency-Key; upsert theo khóa nghiệp vụ nên retry cũng an toàn |
| Công tơ quay vòng (99999 → 00000) | Xử lý như **thay công tơ** (Removal 99999 + Initial 0) — tài liệu hướng dẫn người dùng |

## 11. Audit
Audit sửa/hủy chỉ số (giá trị cũ → mới, lý do), thay/gỡ công tơ.

## 12. Kế hoạch test
- Unit `MeterUsageCalculator` (bảng): kỳ đầu dùng Handover; kỳ thường; thay công tơ giữa kỳ; công tơ lắp sau khi kích hoạt (dùng Initial); Final; thiếu end → lỗi; sau Void phiếu kỳ trước → start lùi về đoạn trước nữa.
- Unit `MonotonicityGuard`: chèn lùi ngày giữa 2 chỉ số.
- Integration: sheet Prepaid vs Postpaid ra đúng U; bulk 1 dòng lỗi → không lưu dòng nào; sửa chỉ số đã khóa → 422; sửa chỉ số của draft → draft stale;
  2 request ghi song song cùng khóa → 1 thành công, 1 cập nhật (upsert) không trùng; **C-01**.

## 13. Phụ thuộc
- Dùng: M02 (room), M04 (fee Metered), M05 (HĐ, chế độ thu, kỳ), M09 (ảnh).
- Bị dùng: M05 (bàn giao/cuối), M07 (`IMeterUsageProvider`), M10 (báo cáo sản lượng, đối chiếu EVN).

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| MT-01 | Domain Meter/Reading + guards + `MeterUsageCalculator` + unit test bảng | 2d |
| MT-02 | EF + partial unique + migration | 0.5d |
| MT-03 | Install/Replace/Remove (+bulk) | 1d |
| MT-04 | Reading sheet query (§3.4) — tối ưu 1–2 query cho cả khu | 1.5d |
| MT-05 | Bulk save + warnings + stale draft | 1.5d |
| MT-06 | Correct/Void + lock check | 0.5d |
| MT-07 | Tests | 1.5d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Nhiều phòng dùng chung 1 công tơ? | P3: `meter_allocations` chia theo tỉ lệ/đầu người |
| Q2 | Có hệ số nhân (CT ratio)? | Không cần với nhà trọ |
| Q3 | Bắt buộc ảnh chụp công tơ? | Tùy chọn theo cài đặt khu (P2) |
