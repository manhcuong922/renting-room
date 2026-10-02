# M04 — Fee Catalog (Danh mục khoản thu & Bảng giá)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L9, L10.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Định nghĩa các **khoản thu** (ngoài tiền phòng) của từng khu trọ và **giá theo thời gian hiệu lực**,
chia 3 nhóm tính tiền khác nhau. Đây là "nguồn sự thật" về cách tính cho M05 (đăng ký), M06 (chỉ số), M07 (tính tiền).

| Nhóm | Code | Cách tính 1 kỳ | Ví dụ | Màn hình đặc thù |
|------|------|----------------|-------|------------------|
| Theo chỉ số | `Metered` | (chỉ số mới − chỉ số cũ) × đơn giá | Điện (kWh), Nước (m³), Nước nóng | Màn hình ghi chỉ số (M06) |
| Dịch vụ cố định | `Fixed` | đơn giá × 1 (theo phòng) hoặc × số người ở (theo người) | Rác, Wifi, Vệ sinh chung, Nước theo đầu người | — |
| Dịch vụ theo số lượng | `Quantity` | đơn giá × số lượng đăng ký | Giữ xe máy (2 xe = 2), Máy giặt | Đăng ký số lượng trên HĐ (M05) |

Tiền phòng **không** phải khoản thu trong danh mục — nó đi theo hợp đồng (M05).

**Ngoài phạm vi P1**: giá bậc thang (tiered) cho điện — P2 (schema đã chừa `tiers`); phí tối thiểu; khoản thu dùng chung chia theo đầu người từ 1 công tơ tổng.

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Khoản thu | `FeeType` | Một loại phí của **một khu trọ** |
| Bảng giá | `FeePrice` | Đơn giá của khoản thu, hiệu lực từ `effective_from` đến trước bản giá kế tiếp |
| Cơ sở tính (Fixed) | `FixedBasis` | `PerRoom` / `PerOccupant` |
| Tự gắn | `AutoAttach` | Tự thêm vào HĐ mới tạo ở khu này (chủ trọ vẫn bỏ được) |
| Mã hệ thống | `SystemCode` | `ELECTRICITY`, `WATER` — seed khi tạo khu, phục vụ báo cáo/đối chiếu |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| FE-UC-01 | Khi tạo khu: seed `Điện` (Metered, kWh, system code `ELECTRICITY`) và `Nước` (Metered, m³, `WATER`) chưa có giá, `auto_attach = true` |
| FE-UC-02 | Tạo khoản thu mới thuộc 1 nhóm; khai báo đơn vị, cơ sở tính, auto-attach, thứ tự hiển thị |
| FE-UC-03 | Thêm bảng giá mới (đơn giá + ngày hiệu lực) |
| FE-UC-04 | Sửa tên/đơn vị/thứ tự hiển thị/auto-attach (không đổi nhóm) |
| FE-UC-05 | Ngừng dùng (archive) khoản thu |
| FE-UC-06 | Sao chép danh mục khoản thu từ khu A sang khu B |
| FE-UC-07 | Xem lịch sử giá |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| FE-BR-01 | Tên khoản thu unique trong khu (không phân biệt hoa thường) trong số khoản chưa archive | DB partial unique |
| FE-BR-02 | `group` **bất biến** sau khi tạo (đổi nhóm làm sai lệch đăng ký & phiếu cũ). Cần đổi → tạo khoản mới, archive khoản cũ | Domain |
| FE-BR-03 | `fixed_basis` bắt buộc khi group = `Fixed`, NULL với nhóm khác | CHECK |
| FE-BR-04 | Tối đa 1 khoản thu chưa archive cho mỗi `system_code` trong khu | DB partial unique |
| FE-BR-05 | Đơn giá ≥ 0 (cho phép 0 = miễn phí). Metered đơn giá ≤ 100.000đ/đơn vị (chặn nhập nhầm thêm số 0); Fixed/Quantity ≤ 50.000.000đ | Validator |
| FE-BR-06 | Unique `(fee_type_id, effective_from)` | DB |
| FE-BR-07 | **Không thêm/sửa/xóa bảng giá có `effective_from` ≤ ngày cuối của bất kỳ dòng phiếu đã chốt (Finalized, chưa Void) dùng khoản thu này** — tránh tạo mâu thuẫn giữa giá trong bảng và giá trên phiếu đã chốt | Application (query M07) |
| FE-BR-08 | Chỉ xóa được bản giá chưa từng được dùng bởi phiếu chưa Void (kể cả Draft → draft được đánh dấu cần tính lại) | Application |
| FE-BR-09 | Khi thêm bản giá ảnh hưởng phiếu **Draft** → các draft liên quan được đánh dấu `is_stale = true` (M07 yêu cầu tính lại trước khi chốt) | Domain event → M07 |
| FE-BR-10 | Giá áp cho 1 dòng phiếu = bản giá có `effective_from` lớn nhất ≤ **ngày bắt đầu khoảng dịch vụ của dòng** (`service_from`). Không chia giá trong một kỳ | Domain (M07 dùng `IFeePriceResolver`) |
| FE-BR-11 | Không có bản giá hiệu lực tại `service_from` → dòng phiếu lỗi `FEE_PRICE_MISSING`, draft không chốt được | M07 |
| FE-BR-12 | Archive khoản thu: các đăng ký trên HĐ (M05) **kết thúc từ kỳ kế tiếp chưa lập phiếu**; không ảnh hưởng phiếu đã có. Archive `Metered` khi còn công tơ đang hoạt động gắn khoản này → 422 (phải gỡ công tơ trước) | Application |
| FE-BR-13 | **Cảnh báo pháp lý (L9)**: đơn giá điện > ngưỡng cấu hình hệ thống (mặc định = giá bán lẻ bậc cao nhất hiện hành, admin cập nhật) → trả `warnings[]` trong response, không chặn | Application |
| FE-BR-14 | `PerOccupant`: số người = số occupant **đang ở tại ngày `service_from`** của kỳ (snapshot vào dòng phiếu). Thay đổi người ở giữa kỳ áp dụng từ kỳ sau | M07 |

## 4. Dữ liệu

**`fee_types`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | UNIQUE (organization_id, id), UNIQUE (organization_id, property_id, id) |
| property_id | uuid | N | FK composite |
| name | varchar(100) | N | |
| group | varchar(16) | N | `Metered`,`Fixed`,`Quantity` |
| fixed_basis | varchar(16) | Y | `PerRoom`,`PerOccupant` |
| unit | varchar(20) | N | `kWh`, `m³`, `tháng`, `người`, `xe`… |
| system_code | varchar(20) | Y | `ELECTRICITY`,`WATER` |
| auto_attach | bool | N | |
| default_quantity | numeric(12,2) | Y | chỉ Quantity; mặc định 1 khi auto-attach |
| sort_order | int | N | 0 |
| archived_at | timestamptz | Y | |
| audit, xmin | | | |

CHECK `(group = 'Fixed') = (fixed_basis IS NOT NULL)`; CHECK `group = 'Quantity' OR default_quantity IS NULL`;
CHECK `system_code IS NULL OR group = 'Metered'`.
UNIQUE `(property_id, lower(name)) WHERE archived_at IS NULL`; UNIQUE `(property_id, system_code) WHERE archived_at IS NULL AND system_code IS NOT NULL`.

**`fee_prices`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | |
| fee_type_id | uuid | N | FK composite |
| effective_from | date | N | |
| unit_price | numeric(18,2) | N | ≥ 0 |
| tiers | jsonb | Y | P2: `[{ "upTo": 50, "price": 1984 }, { "upTo": null, "price": 3460 }]` |
| note | varchar(300) | Y | |
| created_at/by | | | (không có updated — sửa = xóa + thêm, theo FE-BR-07/08) |

UNIQUE `(fee_type_id, effective_from)`.

**`system_settings`** (toàn hệ thống, admin): `electricity_price_warning_threshold numeric(18,2)`.

## 5. Domain model

```
FeeType (aggregate root, chứa danh sách FeePrice)
  + Create(orgId, propertyId, name, group, fixedBasis?, unit, autoAttach, defaultQty?)
  + Rename / UpdateDisplay(unit, sortOrder, autoAttach, defaultQty)
  + AddPrice(effectiveFrom, unitPrice, lockedUntil)    // lockedUntil = ngày cuối dòng phiếu đã chốt (FE-BR-07)
  + RemovePrice(priceId, isUsed)
  + ResolvePrice(date) : FeePrice?                       // FE-BR-10
  + Archive(now)
IFeePriceResolver (Application): ResolveAsync(feeTypeId, serviceFrom) — batch theo khu để tránh N+1
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `CreateFeeTypeCommand` | Cmd | `FEE_NAME_TAKEN`, `PROPERTY_ARCHIVED` |
| `UpdateFeeTypeCommand` | Cmd | `FEE_NAME_TAKEN`, `FEE_GROUP_IMMUTABLE` |
| `ArchiveFeeTypeCommand` | Cmd | `FEE_HAS_ACTIVE_METERS` |
| `AddFeePriceCommand` | Cmd | `FEE_PRICE_DATE_EXISTS`, `FEE_PRICE_LOCKED` (kèm `lockedUntil`) → response kèm `warnings[]`, `staleDraftCount` |
| `DeleteFeePriceCommand` | Cmd | `FEE_PRICE_IN_USE` |
| `CopyFeeCatalogCommand` | Cmd | sourcePropertyId, targetPropertyId, includePrices, effectiveFrom; bỏ qua tên đã tồn tại, trả danh sách bỏ qua |
| `ListFeeTypesQuery` | Qry | theo khu, kèm giá hiện hành (theo hôm nay) |
| `GetFeeTypeQuery` | Qry | kèm lịch sử giá |

## 7. API

| Method | Route | Mã lỗi |
|--------|-------|--------|
| GET | `/properties/{propertyId}/fee-types?includeArchived=` | |
| POST | `/properties/{propertyId}/fee-types` | 409 `FEE_NAME_TAKEN` |
| GET / PUT | `/fee-types/{id}` | 422 `FEE_GROUP_IMMUTABLE` |
| POST | `/fee-types/{id}/archive` | 422 `FEE_HAS_ACTIVE_METERS` |
| POST | `/fee-types/{id}/prices` | 409 `FEE_PRICE_DATE_EXISTS`, 422 `FEE_PRICE_LOCKED` |
| DELETE | `/fee-types/{id}/prices/{priceId}` | 422 `FEE_PRICE_IN_USE` |
| POST | `/properties/{targetId}/fee-types/copy-from/{sourceId}` | |

```json
POST /api/v1/properties/{propertyId}/fee-types
{ "name": "Giữ xe máy", "group": "Quantity", "unit": "xe", "autoAttach": false, "defaultQuantity": 1,
  "initialPrice": { "effectiveFrom": "2026-10-01", "unitPrice": 100000 } }

POST /api/v1/fee-types/{id}/prices
{ "effectiveFrom": "2026-11-01", "unitPrice": 3800 }
→ 201 { "id": "...", "warnings": [ { "code": "ELECTRICITY_PRICE_ABOVE_THRESHOLD", "threshold": 3460 } ], "staleDraftCount": 0 }
```

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| name | 1–100, trim |
| group | enum; bắt buộc khi tạo |
| fixedBasis | bắt buộc ⇔ group = Fixed |
| unit | 1–20 |
| defaultQuantity | Quantity: 0 < x ≤ 100, tối đa 2 số lẻ |
| unitPrice | FE-BR-05; tối đa 2 số lẻ |
| effectiveFrom | ngày hợp lệ; không quá hôm nay + 366 ngày |
| sortOrder | 0–1000 |

## 9. Phân quyền
OrgOwner toàn quyền; OrgManager (P3) chỉ đọc (giá là quyết định kinh doanh của chủ).

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Giải pháp |
|----------|-----------|
| Thêm giá hồi tố đè lên kỳ đã chốt phiếu | FE-BR-07: tính `lockedUntil` trong cùng transaction, khóa `fee_types` row `FOR UPDATE`; lệnh chốt phiếu (M07) cũng khóa các `fee_types` liên quan theo thứ tự id ⇒ không race |
| Đổi tên khoản thu sau khi phiếu đã chốt | Phiếu lưu snapshot tên/đơn vị (C-06) |
| Đổi nhóm làm hỏng dữ liệu đăng ký | FE-BR-02 |
| Draft tính theo giá cũ sau khi có giá mới | `is_stale` (FE-BR-09) + M07 kiểm tra lại giá khi chốt |
| Copy danh mục giữa 2 khu khác tổ chức | FK composite + kiểm tra 2 khu cùng org |

## 11. Audit
Audit: tạo/sửa/archive khoản thu, thêm/xóa giá.

## 12. Kế hoạch test
- Unit: `ResolvePrice` với nhiều bản giá (trước bản đầu → null; đúng ngày hiệu lực; giữa 2 bản); `AddPrice` với `lockedUntil`.
- Integration: tạo khu → có Điện/Nước; thêm giá trùng ngày → 409; thêm giá hồi tố trước kỳ đã chốt → 422; thêm giá ảnh hưởng draft → draft stale;
  archive Metered còn công tơ → 422; cảnh báo giá điện; **C-01**.

## 13. Phụ thuộc
- Dùng: M02. Bị dùng: M05 (`contract_fees`), M06 (công tơ gắn fee type Metered), M07 (giá), M10.
- Interface `IFinalizedUsageReader` (hiện thực ở M07) cung cấp `lockedUntil` theo fee type.

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| FE-01 | Domain FeeType/FeePrice + resolver + unit test | 1d |
| FE-02 | EF + migration + seed khi tạo khu (handler `PropertyCreated`) | 0.5d |
| FE-03 | Commands/Queries + copy | 1d |
| FE-04 | Tích hợp lock/stale với M07 (sau khi M07 có bảng) | 0.5d |
| FE-05 | Tests | 1d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Có cần giá khác nhau theo phòng cho cùng 1 khoản? | Có — qua `unit_price_override` trên đăng ký HĐ (M05), không nhân bản khoản thu |
| Q2 | Tính giá điện bậc thang như EVN? | P2 (`tiers`), áp theo tổng sản lượng từng phòng |
| Q3 | Phí tối thiểu (VD nước tối thiểu 3 m³)? | Chưa hỗ trợ; dùng điều chỉnh thủ công |
