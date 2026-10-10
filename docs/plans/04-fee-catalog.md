# M04 — Fee Catalog (Danh mục khoản thu & Bảng giá)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L9, L10.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Định nghĩa các **khoản thu** (ngoài tiền phòng) của từng khu trọ và **giá theo thời gian hiệu lực**,
chia **2 nhóm** (chốt 04/10/2026, cho dễ hiểu). Đây là "nguồn sự thật" về cách tính cho M05 (đăng ký), M06 (chỉ số), M07 (tính tiền).

| Nhóm | Code | Cách tính 1 kỳ | Ví dụ | Màn hình đặc thù |
|------|------|----------------|-------|------------------|
| **Điện nước** (theo công tơ) | `Metered` | (chỉ số mới − chỉ số cũ) × **một giá** hoặc **theo bậc** (FE-BR-15) — đi theo **công tơ của phòng**, không gắn vào HĐ | Điện (kWh), Nước (m³), Nước nóng | Màn hình ghi chỉ số (M06) |
| **Dịch vụ** (không theo công tơ) | `Service` + cách tính `PerRoom` / `PerOccupant` / `PerUnit` | đơn giá × 1 (theo phòng) · × số người ở (theo đầu người) · × số gói đăng ký (theo số lượng) — gắn vào HĐ | Mạng 120.000/phòng; Nước 20.000/người; Rác; Giữ xe 120.000/xe — 2 xe = 2 gói | Chọn dịch vụ + số gói trên HĐ (M05) |
| Phụ thu | — | **Không thuộc danh mục khoản thu**: nhập tay trên phiếu nháp (M07 BL-BR-23) — sửa chữa do người thuê làm hỏng, bồi thường khi trả phòng, khoản phát sinh đã thỏa thuận |

Tiền phòng **không** phải khoản thu trong danh mục — nó đi theo hợp đồng (M05).

Phòng tính nước **theo đầu người** dùng khoản Dịch vụ "Nước" `PerOccupant` và **không cần công tơ nước**; khi đó nước giống dịch vụ giữ xe.

> ✅ **Đã chuyển code (04/10/2026)** từ 3 nhóm cũ: `Fixed/PerRoom` → `Service/PerRoom`, `Fixed/PerOccupant` → `Service/PerOccupant`, `Quantity` → `Service/PerUnit` (migration `TwoFeeGroupsAndHoldover` chuyển cả dữ liệu và bản chụp giá lúc ký).

**Ngoài phạm vi P1**: phí tối thiểu; khoản thu dùng chung chia theo đầu người từ 1 công tơ tổng.

**Trạng thái (07/10/2026)**: ✅ đã code — danh mục, bảng giá **theo phiên bản** (một giá hoặc theo bậc — FE-BR-15; giá áp cho kỳ = bản mới nhất tới ngày cuối kỳ — FE-BR-10), seed Điện/Nước, sao chép danh mục,
gắn khoản **dịch vụ** vào HĐ (M05 `contract_fees`); điện nước theo công tơ đi theo phòng (FE-BR-17); khóa giá theo phiếu đã chốt (FE-BR-07 — `FeePriceLockReader`); chặn ngừng dùng khoản theo chỉ số còn công tơ (FE-BR-12). Đổi giá ảnh hưởng nháp ⇒ nháp "Cần tính lại" (FE-BR-08/09, M07 BL-BR-20).

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Khoản thu | `FeeType` | Một loại phí của **một khu trọ** |
| Bảng giá | `FeePrice` | Đơn giá của khoản thu, hiệu lực từ `effective_from` đến trước bản giá kế tiếp |
| Cách tính (Dịch vụ) | `ChargeBasis` | `PerRoom` (theo phòng) / `PerOccupant` (theo đầu người) / `PerUnit` (theo số gói, VD số xe) |
| Tự gắn | `AutoAttach` | Tự thêm vào HĐ mới tạo ở khu này (chủ trọ vẫn bỏ được). Chỉ nhóm Dịch vụ — `Metered` luôn false (FE-BR-17) |
| Mã hệ thống | `SystemCode` | `ELECTRICITY`, `WATER` — seed khi tạo khu, phục vụ báo cáo/đối chiếu |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| FE-UC-01 ✅ | Khi tạo khu: seed `Điện` (Metered, kWh, system code `ELECTRICITY`) và `Nước` (Metered, m³, `WATER`) chưa có giá, `auto_attach = false` (đi theo công tơ của phòng, không gắn vào HĐ) |
| FE-UC-02 ✅ | Tạo khoản thu mới thuộc 1 nhóm; khai báo đơn vị, cơ sở tính, auto-attach, thứ tự hiển thị |
| FE-UC-03 ✅ | Thêm bảng giá mới (đơn giá + ngày hiệu lực) |
| FE-UC-04 ✅ | Sửa tên/đơn vị/thứ tự hiển thị/auto-attach (không đổi nhóm) |
| FE-UC-05 ✅ | Ngừng dùng (archive) khoản thu |
| FE-UC-06 ✅ | Sao chép danh mục khoản thu từ khu A sang khu B |
| FE-UC-07 ✅ | Xem lịch sử giá |
| FE-UC-08 ✅ | **Phòng đang dùng dịch vụ** (chốt 09/10/2026) (thay cho "tự gắn theo nhóm phòng"): chọn 1 dịch vụ ⇒ danh sách phòng của khu đang dùng / chưa dùng (số gói, giá riêng, từ kỳ nào) ⇒ chọn phòng **thêm hoặc bớt bằng tay** hàng loạt — áp từ kỳ chưa chốt đầu tiên của từng phòng (CT-BR-06), kết quả từng phòng. VD lắp điều hòa cho 6 phòng tầng 2 ⇒ thêm "Phí điều hòa" cho 6 phòng một lúc. **Code 09/10/2026**: `GET /fee-types/{id}/usage` (mọi phòng của khu: HĐ đang ở, đang dùng?, số gói, giá riêng, từ ngày), `POST /fee-types/{id}/usage { action: Add|Remove, contractIds, quantity?, unitPriceOverride? }` → kết quả từng HĐ (HĐ lỗi không làm hỏng HĐ khác) |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| FE-BR-01 | Tên khoản thu unique trong khu (không phân biệt hoa thường) trong số khoản chưa archive | DB partial unique |
| FE-BR-02 | `group` **bất biến** sau khi tạo (đổi nhóm làm sai lệch đăng ký & phiếu cũ). Cần đổi → tạo khoản mới, archive khoản cũ | Domain |
| FE-BR-03 | `charge_basis` bắt buộc khi group = `Service`, NULL với `Metered`. `default_quantity` chỉ cho `PerUnit` | CHECK |
| FE-BR-04 | Tối đa 1 khoản thu chưa archive cho mỗi `system_code` trong khu | DB partial unique |
| FE-BR-05 | Đơn giá ≥ 0 (cho phép 0 = miễn phí). Metered đơn giá ≤ 100.000đ/đơn vị (chặn nhập nhầm thêm số 0); Dịch vụ ≤ 50.000.000đ | Validator |
| FE-BR-06 | Unique `(fee_type_id, effective_from)` | DB |
| FE-BR-07 ✅ | **Không thêm/sửa/xóa bảng giá có `effective_from` ≤ ngày cuối của bất kỳ dòng phiếu đã chốt (Finalized, chưa Void) dùng khoản thu này** — tránh tạo mâu thuẫn giữa giá trong bảng và giá trên phiếu đã chốt | Application (query M07) |
| FE-BR-08 ✅ | Chỉ xóa được bản giá chưa từng được dùng bởi phiếu đã chốt (FE-BR-07); bản giá đang dùng ở **nháp** thì vẫn xóa / sửa được và nháp có kỳ kết thúc từ ngày hiệu lực của bản giá được đánh dấu "Cần tính lại" (M07 BL-BR-20) | Application |
| FE-BR-09 ✅ | Thêm / sửa / xóa bản giá ảnh hưởng phiếu **nháp** ⇒ nháp có kỳ kết thúc từ ngày hiệu lực của giá được đánh dấu "Cần tính lại" (đã làm 09/10/2026 cùng M07 BL-BR-20 — tự động khi lưu bảng giá; khoản thu vừa tạo kèm giá ban đầu thì bỏ qua). Chốt vẫn tính lại và báo `DRAFT_STALE` nếu lệch (BL-BR-12) | Infrastructure (M07) |
| FE-BR-10 ✅ | **Giá theo phiên bản** (đổi 07/10/2026): giá áp cho 1 dòng phiếu = bản giá có `effective_from` lớn nhất ≤ **ngày cuối khoảng tính của dòng** (`service_to`: cuối kỳ của phiếu với dịch vụ, cuối kỳ sử dụng U với điện nước), áp **cả dòng** — không chia nửa kỳ giá cũ / nửa kỳ giá mới (chủ trọ báo trước khi tăng giá). Áp cho mọi khoản (điện nước, dịch vụ); giá riêng của HĐ (dịch vụ) ưu tiên | Domain (`InvoiceCalculator` dùng `FeeType.ResolvePrice`) |
| FE-BR-11 ✅ | Không có bản giá hiệu lực tới ngày cuối khoảng tính (và không có giá riêng) → dòng phiếu lỗi `FEE_PRICE_MISSING`, draft không chốt được | M07 |
| FE-BR-12 | **Đã đổi khi code**: ngừng dùng khoản thu chỉ khi **không còn HĐ nháp / hiệu lực / thanh lý đang gắn** (đăng ký chưa kết thúc) → 422 `FEE_IN_USE`; gỡ khỏi HĐ trước (`DELETE /contracts/{id}/fees/{feeTypeId}`). Lý do: tự kết thúc đăng ký ở "kỳ chưa lập phiếu" cần M07 và dễ gây bất ngờ khi tính tiền. Khoản ngừng dùng không gắn được vào HĐ mới. Khoản `Metered` còn công tơ hoạt động → 422 `FEE_HAS_ACTIVE_METERS` ✅ (gỡ / thay công tơ trước) | Application |
| FE-BR-13 ✅ | **Cảnh báo pháp lý (L9)**: đơn giá điện (theo bậc ⇒ bậc cao nhất) > ngưỡng cấu hình (`Fees:ElectricityPriceWarningThreshold`, mặc định 3.460đ/kWh) → `warnings[]` `ELECTRICITY_PRICE_ABOVE_THRESHOLD`, không chặn | Application |
| FE-BR-14 ✅ | `PerOccupant` (VD nước theo người): **số gói tự áp = số người đang ở tại đầu kỳ**, giống số xe với phí giữ xe (chốt 09/10/2026); người vào / ra giữa kỳ áp từ kỳ sau. Chủ trọ sửa số lượng trên phiếu nháp nếu muốn tính khác (BL-BR-07) | M07 |
| FE-BR-15 ✅ | **Hai kiểu giá cho khoản theo công tơ** (đổi lại 04/10/2026 — linh hoạt nhiều mô hình trọ): mỗi bản giá là **một giá** (`unit_price`) ✅ hoặc **theo bậc** (`tiers = [{upTo, price}]`, `upTo` lũy kế tăng dần, bậc cuối `null`; VD điện bậc EVN, nước bậc theo m³). Thành tiền theo bậc = Σ phần sản lượng × giá bậc, làm tròn tới đồng; bậc chỉ là **mức giá theo lượng tiêu thụ** của kỳ (tổng mọi đoạn đo — BL-BR-05), **không quy đổi theo số ngày** (tháng vào ở / trả phòng giữa kỳ vẫn dùng nguyên mốc bậc). Chủ trọ chọn **một giá hoặc theo bậc cho từng bản giá** ⇒ khu này tính điện 1 giá, khu khác tính bậc; đổi kiểu giá = thêm bản giá mới. Dịch vụ chỉ một giá. Giá riêng theo HĐ chỉ cho dịch vụ | Domain `FeePrice.Amount` |
| FE-BR-16 ✅ | **Linh hoạt theo phòng**: một khu có thể có nhiều khoản cùng loại khác cách tính (VD "Nước" theo công tơ và "Nước theo người" — `Service/PerOccupant`); phòng tính nước theo người thì gắn khoản "Nước theo người" vào HĐ và **không lắp công tơ nước** (M06) | M05 `contract_fees` + M06 |
| FE-BR-17 ✅ | **Điện nước theo công tơ đi theo phòng** (04/10/2026): khoản `Metered` **không gắn vào HĐ** (gắn → 400 `FEE_METERED_FOLLOWS_ROOM`), không tự gắn. Mọi HĐ đang hiệu lực của phòng được tính theo công tơ đang hoạt động của phòng, bắt đầu từ chỉ số ngày nhận phòng (M06 MT-BR-13). Bản chụp giá lúc ký (CT-BR-19) và bản in ghi giá điện nước của khu tại ngày bắt đầu | Application `ContractFeeRules` |

## 4. Dữ liệu

**`fee_types`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | UNIQUE (organization_id, id), UNIQUE (organization_id, property_id, id) |
| property_id | uuid | N | FK composite |
| name | varchar(100) | N | |
| group | varchar(16) | N | `Metered`,`Service` |
| charge_basis | varchar(16) | Y | `PerRoom`,`PerOccupant`,`PerUnit` (đổi từ `fixed_basis`) |
| unit | varchar(20) | N | `kWh`, `m³`, `tháng`, `người`, `xe`… |
| system_code | varchar(20) | Y | `ELECTRICITY`,`WATER` |
| auto_attach | bool | N | |
| default_quantity | numeric(12,2) | Y | chỉ `PerUnit`; mặc định 1 khi gắn |
| vehicle_type | varchar(16) | Y | chỉ `PerUnit` — đánh dấu phí giữ xe theo loại xe (M05 CT-BR-22) ✅ |
| sort_order | int | N | 0 |
| archived_at | timestamptz | Y | |
| audit, xmin | | | |

CHECK `(group = 'Service') = (charge_basis IS NOT NULL)`; CHECK `charge_basis = 'PerUnit' OR default_quantity IS NULL`;
CHECK `system_code IS NULL OR group = 'Metered'`.
UNIQUE `(property_id, lower(name)) WHERE archived_at IS NULL`; UNIQUE `(property_id, system_code) WHERE archived_at IS NULL AND system_code IS NOT NULL`.

**`fee_prices`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | |
| fee_type_id | uuid | N | FK composite |
| effective_from | date | N | |
| unit_price | numeric(18,2) | N | ≥ 0 |
| tiers | jsonb | Y | bậc giá — chỉ khoản `Metered`; NULL = một giá (FE-BR-15). ✅ thêm lại (migration `RentCycleTiersWriteOff`) |
| note | varchar(300) | Y | |
| created_at/by | | | (không có updated — sửa = xóa + thêm, theo FE-BR-07/08) |

UNIQUE `(fee_type_id, effective_from)`.

ngưỡng cảnh báo giá điện đọc từ cấu hình `Fees:ElectricityPriceWarningThreshold` (mặc định 3.460đ/kWh = bậc 6 chưa VAT); P2 chuyển sang bảng do admin sửa.

Tên cột nhóm là `fee_group` (tránh từ khóa SQL `group`); thêm `name_normalized` (chữ thường) cho unique không phân biệt hoa thường.

## 5. Domain model

```
FeeType (aggregate root, chứa danh sách FeePrice)
  + Create(orgId, propertyId, name, group, fixedBasis?, unit, autoAttach, defaultQty?)
  + Rename / UpdateDisplay(unit, sortOrder, autoAttach, defaultQty)
  + AddPrice(effectiveFrom, unitPrice | tiers, note, lockedUntil)    // một giá hoặc theo bậc (FE-BR-15); lockedUntil = ngày cuối dòng phiếu đã chốt (FE-BR-07)
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
P1: chủ trọ và phó quản lý toàn quyền nghiệp vụ (M01 §3.3). P3: cấu hình cho phép chỉ chủ trọ sửa giá.

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
- Unit: `ResolvePrice` với nhiều bản giá (trước bản đầu → null; đúng ngày hiệu lực; giữa 2 bản); `AddPrice` với `lockedUntil`; một giá cho mọi sản lượng; Metered không tự gắn.
- Integration: tạo khu → có Điện/Nước; thêm giá trùng ngày → 409; thêm giá hồi tố trước kỳ đã chốt → 422; thêm giá ảnh hưởng draft → draft stale;
  archive Metered còn công tơ → 422; cảnh báo giá điện; **C-01**.

## 13. Phụ thuộc
- Dùng: M02. Bị dùng: M05 (`contract_fees`), M06 (công tơ gắn fee type Metered), M07 (giá), M10.
- Interface `IFinalizedUsageReader` (hiện thực ở M07) cung cấp `lockedUntil` theo fee type.

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| FE-01 ✅ | Domain FeeType/FeePrice + resolver + unit test (bậc thang đã bỏ 04/10/2026) | 1d |
| FE-02 ✅ | EF + migration `AddFeeCatalogAndSensitiveAccess` (EXCLUDE `contract_fees`, seed Điện/Nước `auto_attach = false` cho khu cũ) + seed khi tạo khu | 0.5d |
| FE-03 ✅ | Commands/Queries + copy + gắn vào HĐ (M05) | 1d |
| FE-04 | Tích hợp lock/stale với M07 (sau khi M07 có bảng) | 0.5d |
| FE-05 ✅ | Tests: 12 unit + 5 integration | 1d |
| FE-06 ✅ | Đổi 3 nhóm → 2 nhóm: enum `FeeGroup { Metered, Service }` + `ChargeBasis { PerRoom, PerOccupant, PerUnit }` (thay `fixedBasis`); API tạo khoản thu nhận `group` + `chargeBasis` (bắt buộc khi `Service`); `defaultQuantity`, `vehicleType` chỉ `PerUnit`; số lượng gắn HĐ chỉ `PerUnit`; DTO khoản thu / khoản thu của HĐ / `utilityPrices` trả `chargeBasis`. Migration đổi tên cột `fixed_basis` → `charge_basis`, chuyển dữ liệu `Fixed` → `Service` (giữ basis), `Quantity` → `Service/PerUnit`, sửa CHECK, chuyển JSON `utility_price_snapshot` | 0.5d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Có cần giá khác nhau theo phòng cho cùng 1 khoản? | Có — qua `unit_price_override` trên đăng ký HĐ (M05), không nhân bản khoản thu |
| Q2 ✅ | Tính giá điện bậc thang như EVN? | **Cả hai** là lựa chọn của nhóm theo công tơ: một giá (chính, test chủ yếu) hoặc theo bậc. **Không** chia định mức theo số người ở (TT 60/2025) — mỗi phòng tính 1 hộ (chốt 08/10/2026) |
| Q3 ✅ | Phí tối thiểu (VD nước tối thiểu 3 m³)? | **Không hỗ trợ** (chốt 08/10/2026): dùng bao nhiêu thu bấy nhiêu, dùng 0 thu 0 — kể cả phòng thuê mà không ở |
