# M02 — Property & Room (Khu trọ, Phòng, Nhóm phòng)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Quy ước chung: [README §6](README.md#6-quy-ước-dùng-chung-cross-cutting--bắt-buộc).

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Quản lý danh mục tài sản cho thuê: **khu trọ** (property), **phòng** (room), **nhóm phòng**
(room group — dùng để áp giảm giá/tăng giá theo nhóm), và **cài đặt thu mặc định** của khu.

**Trong phạm vi**: CRUD + archive khu/phòng; tạo phòng hàng loạt; nhóm phòng; trạng thái phòng (dẫn xuất);
cài đặt thu mặc định của khu; thông tin hợp đồng điện EVN của khu (phục vụ đối chiếu LEG-05).

**Ngoài phạm vi**: công tơ (M06), bảng giá khoản thu (M04), giấy tờ PCCC (P2, dùng M09 + nhắc việc M10).

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Khu trọ | `Property` | Một địa điểm cho thuê (1 tòa/1 dãy) có địa chỉ riêng |
| Phòng | `Room` | Đơn vị cho thuê nhỏ nhất, thuộc 1 khu |
| Giá niêm yết | `ListedRent` | Giá tham khảo của phòng; **không** dùng để tính tiền — tiền phòng lấy từ hợp đồng (snapshot) |
| Nhóm phòng | `RoomGroup` | Tập phòng trong **cùng 1 khu** (VD "Tầng 3", "Phòng hướng đường") |
| Bảo trì | `MaintenanceStatus` | Phòng trống đang sửa, không cho ký hợp đồng mới |
| Trạng thái hiển thị | `RoomDisplayStatus` | Dẫn xuất: `Archived` > `Maintenance` > `Occupied` > `Reserved` > `Vacant` |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Actor | Mô tả |
|----|-------|-------|
| PR-UC-01 | OrgOwner | Tạo / sửa khu trọ (tên, mã, địa chỉ 2 cấp, mô tả, cài đặt thu mặc định, mã KH điện EVN) |
| PR-UC-09 ✅ | OrgOwner | **Bên cho thuê** (đổi 09/10/2026 — PR-BR-17): chủ trọ khai **thông tin của mình một lần** (`PUT /org/lessor`), mọi khu dùng chung. Khu nào bên cho thuê **khác** chủ trọ (công ty, người được ủy quyền — kèm giấy ủy quyền) mới khai riêng (`PUT /properties/{id}/lessor`), bỏ riêng ⇒ `DELETE` quay về thông tin chủ trọ. Cần để in HĐ đầy đủ (Luật Nhà ở 2023 Điều 163); thiếu **không** chặn kích hoạt (CT-BR-46) |
| PR-UC-10 | OrgOwner | Khai báo tài khoản ngân hàng nhận tiền (in trên phiếu báo / mã QR), nội quy khu trọ, thông tin thửa đất (tùy chọn) |
| PR-UC-02 | OrgOwner | Archive / bỏ archive khu trọ |
| PR-UC-03 | OrgOwner | Tạo phòng đơn lẻ; tạo hàng loạt theo mẫu (VD tầng 1–5, mỗi tầng 101–108) |
| PR-UC-11 ✅ | OrgOwner | **Import phòng từ Excel** (chốt 09/10/2026, thiết kế lại 09/10): mẫu `GET /properties/{id}/rooms/import-template`. Cột: mã phòng*, tầng, diện tích, số người (loại phòng), giá niêm yết, tiền cọc mặc định, tiện nghi, ghi chú; tùy chọn seri + **chỉ số đầu kỳ hiện tại** công tơ điện / nước ⇒ công tơ tính là lắp từ **ngày chốt kỳ hiện tại của khu** (chỉ số công tơ chỉ khai ở form này — form người thuê không có). Đơn vị lưu = **từng dòng**. Chặn: mã trùng trong file / trùng phòng đã có. Luồng chung (chốt lại 09/10/2026): **xem trước** `POST …/import/preview` (multipart `file`) đọc file, trả từng dòng + lỗi theo ô — **server không giữ gì** (đóng màn là mất); người dùng sửa thẳng ô sai trên màn hình hoặc sửa file tải lại; **lưu** `POST …/import` (JSON các dòng, bắt buộc `Idempotency-Key`) ⇒ server **kiểm lại toàn bộ** theo dữ liệu lúc đó, **lưu phần hợp lệ** (mỗi đơn vị 1 transaction), trả kết quả từng đơn vị (đã lưu / bỏ qua + lý do). Import **chỉ tạo mới** — không sửa / ghi đè / kết thúc dữ liệu đang có; server bỏ qua mọi id do client gửi (tra theo mã phòng, số giấy tờ). Giới hạn: `.xlsx` ≤ 2 MB (chặn ở web server), giải nén ≤ 20 MB (chống zip bomb), ≤ 500 dòng, chỉ đọc sheet / cột của mẫu, ô công thức ⇒ lỗi (RP-BR-02), ≤ 10 lần xem trước / phút / người, chỉ chủ trọ; audit 1 dòng `Import`. |
| PR-UC-04 | OrgOwner | Sửa phòng (mã, tầng, diện tích, số người tối đa, giá niêm yết, tiện ích, mô tả). Đổi giá niêm yết khi phòng đang có người thuê → UI hỏi "Áp cho người đang thuê?" (`POST /rooms/{id}/apply-listed-rent`, CT-UC-05) |
| PR-UC-05 | OrgOwner | Đặt / bỏ trạng thái bảo trì cho phòng trống |
| PR-UC-06 | OrgOwner | Archive phòng |
| PR-UC-07 | OrgOwner | Danh sách phòng của 1/nhiều khu, lọc theo trạng thái hiển thị, tầng, nhóm; xem chi tiết (hợp đồng hiện tại, số người ở) |
| PR-UC-08 | OrgOwner | Tạo/sửa/xóa nhóm phòng; thêm/bớt phòng vào nhóm |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| PR-BR-01 | `Property.code` unique trong tổ chức; `Room.code` unique trong khu (không phân biệt hoa thường, kể cả phòng đã archive — tránh nhầm khi xuất lịch sử) | DB unique index trên `lower(code)` |
| PR-BR-02 | Trạng thái phòng **không lưu cứng** "Occupied". Tính **theo ngày** D (mặc định hôm nay, C-04): `Occupied` = tồn tại HĐ `Active`/`Liquidating` đang ở phòng này có `room_since ≤ D ≤ COALESCE(actual_end_date, ∞)` (`room_since` = ngày bắt đầu, hoặc ngày chuyển tới phòng — M05 CT-BR-14) **hoặc lịch sử chuyển phòng của HĐ có `from_date ≤ D ≤ to_date`** **hoặc HĐ `Ended` có `start_date ≤ D ≤ actual_end_date`** (ngày trả phòng vẫn tính là đang thuê — CT-BR-33); `Reserved` = không Occupied và có HĐ `Draft` (chưa hủy) của phòng; còn lại `Vacant`. **HĐ quá hạn (`end_date` đã qua) vẫn là `Occupied`** cho tới khi thanh lý (CT-BR-45); người đứng tên rời đi nhưng còn người ở vẫn `Occupied` (CT-BR-44). Phòng về `Vacant` chỉ khi chủ trọ thanh lý xong. Một phòng có thể đồng thời có HĐ cũ đang thanh lý và HĐ mới bắt đầu sau `actual_end_date` cũ | Query (M05) ✅ |
| PR-BR-03 | Chỉ đặt `Maintenance` khi phòng không có hợp đồng `Active`/`Liquidating` | Application (đọc M05) trong transaction |
| PR-BR-04 | Phòng `Maintenance` hoặc `Archived` không được **kích hoạt** hợp đồng mới | M05 kiểm tra |
| PR-BR-05 | Archive phòng / bắt đầu bảo trì chỉ khi không có hợp đồng `Draft`/`Active`/`Liquidating` **và không có HĐ `Ended` có `actual_end_date ≥ hôm nay`** (người thuê còn ở trong ngày trả phòng — CT-BR-33); archive khu chỉ khi mọi phòng đã archive hoặc archivable (archive phòng kèm theo trong cùng transaction) **Công tơ không bị tháo** khi phòng ngừng dùng / bảo trì: công tơ vẫn chạy (sửa chữa, thử phòng dùng điện) — phần dùng khi phòng trống không tính cho ai (MT-BR-14); HĐ mới chọn "dùng số mới nhất" hoặc nhập số khác khi kích hoạt (MT-BR-13) | Application ✅ |
| PR-BR-06 ✅ | **Số người (loại phòng)** `max_occupants` (đổi 09/10/2026): **tùy chọn, chỉ để mô tả** như diện tích — phần mềm **không chặn** số người ở (phòng 2 người vẫn cho 2 người lớn + 2 trẻ em tùy chủ trọ); danh sách phòng hiện số người đang ở (kèm "/ N" nếu có khai), không cảnh báo. Bỏ `ROOM_CAPACITY_EXCEEDED`, `MAX_OCCUPANTS_BELOW_CURRENT`, `overrideCapacity` | Domain |
| PR-BR-07 | Nhóm phòng chỉ chứa phòng **cùng khu** với nhóm | DB FK composite `(organization_id, property_id, room_id)` |
| PR-BR-08 | Xóa nhóm phòng: cho phép (quy tắc điều chỉnh M07 đã bỏ 08/10/2026 nên không còn tham chiếu) | Application |
| PR-BR-09 ✅ | **Cài đặt kỳ thu của khu** (chốt 08/10/2026): ngày chốt kỳ (1–28), thu trước / thu sau, tính theo ngày / trọn tháng ở kỳ lẻ, số ngày hạn thanh toán — **áp chung cho mọi phòng / HĐ của khu**, HĐ không chọn riêng; mỗi khu đặt khác nhau được. Tạo phiếu theo khu + tháng thu, lọc bớt phòng khi cần (M07). Đổi cài đặt khi khu **đã có phiếu** (K4 — chốt + code 09/10/2026): **cho đổi**, hiệu lực từ kỳ chưa lập phiếu đầu tiên của khu ⇒ một **kỳ chuyển tiếp** từ ngày sau kỳ cuối đã lập tới ngày trước ngày chốt mới; kỳ chuyển tiếp **kết thúc trước ngày chốt mới của tháng sau** và vẫn là tháng thu của mốc đổi ⇒ mỗi tháng đúng 1 kỳ (đổi khi code — chọn "gần 1 tháng nhất" làm 2 kỳ trùng tên tháng); VD 1 → 5: 01/11–04/12 dư 4 ngày; 5 → 1: 05/11–30/11 thiếu 4 ngày; 1 → 20: 01/11–19/12 dư 19 ngày (lệch tối đa ±27 ngày — số ngày tính thêm / trừ vẫn do chủ trọ chọn). Tiền trong kỳ chuyển tiếp theo BL-BR-28 (chỉ tiền phòng điều chỉnh theo ngày). Đổi thu trước ↔ thu sau: mỗi kỳ tiền phòng / dịch vụ cố định chỉ thu **một lần** (BL-BR-28). Đổi lại khi kỳ chuyển tiếp chưa lập phiếu ⇒ ghi đè lần đổi trước. Còn phiếu nháp của khu ⇒ 422 `BILLING_SETTINGS_DRAFT_INVOICES` (chốt / xóa nháp trước). **Chưa khóa** mức lệch / số lần đổi (để sau). Code: lịch kỳ thu `properties.billing_schedule` (jsonb — các mốc đổi để kỳ cũ vẫn tính đúng); `PUT /properties/{id}/billing` (+ `transitionAdjustDays`), xem trước `GET /properties/{id}/billing/preview?anchorDay=&chargeMode=` (kỳ chuyển tiếp, dư / thiếu, gợi ý, tiền từng phòng); `PUT /properties/{id}` không còn sửa cài đặt kỳ thu | Domain `BillingSchedule` + M05/M07 đọc từ khu |
| PR-BR-10 | Khu có > 20 phòng → hiển thị cảnh báo thông tin L3 (không chặn) | UI/Query |
| PR-BR-11 | Giới hạn gói (P3): tổng phòng chưa archive ≤ `organizations.max_rooms` | Application, khóa `organizations` row khi tạo |
| PR-BR-12 | **Bên cho thuê đầy đủ** = `lessor_type`, `lessor_name`, `lessor_address`, `lessor_phone` và: Cá nhân → loại + số giấy tờ, ngày sinh (≥ 18 tuổi); Tổ chức → mã số thuế, người đại diện, chức vụ. Thiếu → **cảnh báo** `LESSOR_INFO_INCOMPLETE` trên HĐ (chưa in được HĐ đầy đủ), không chặn kích hoạt (đổi 09/10/2026 — CT-BR-19, CT-BR-46) | Domain (`LessorInfo.IsComplete`) |
| PR-BR-13 | Người ký không phải chủ nhà (công ty quản lý, người được ủy quyền) → bắt buộc `authorization_doc_no` + `authorization_doc_date` | Validator |
| PR-BR-14 | Số giấy tờ bên cho thuê: chuẩn hóa + mã hóa + che khi hiển thị như số giấy tờ người thuê (C-11) | Infrastructure |
| PR-BR-15 | Sửa thông tin bên cho thuê / ngân hàng **không** làm đổi hợp đồng đã kích hoạt (hợp đồng giữ snapshot — CT-BR-19) | Thiết kế |
| PR-BR-16 ✅ | **Nhãn trên thẻ phòng / chi tiết phòng** (dẫn xuất): `currentContract.flags` của HĐ đang ở — `RepresentativeMovedOut` "Người ký đã rời đi", `NoOccupantLeft` "Không còn người ở", `ExpiredAwaitingDecision` "Quá hạn HĐ — chờ quyết định", `Holdover` "Ở tiếp chưa ký lại" (CT-BR-44/45), `MissingSignedDocument` **"Thiếu tài liệu"** (chưa có bản HĐ ký — CT-UC-23, nhãn xám, không ảnh hưởng thu tiền); `Còn nợ` (tổng phiếu đã chốt chưa thu đủ > 0) khi có M08; **`Quá hạn` (đỏ)** khi có phiếu đã chốt chưa thu đủ mà quá hạn thanh toán (ngày chốt phiếu + số ngày hạn của khu) — ✅ code 09/10/2026 (K3): `overdueAmount` / `isOverdue` trên phòng, lọc `GET /rooms?overdue=true`. Chi tiết phòng có tab **Phiếu tiền phòng** (M07 BL-UC-14) | Query |
| PR-BR-18 ✅ | Chi tiết phòng có tab **Khoản phát sinh** (M07 BL-UC-15..17): tạo phụ thu / bù cho phòng đang có người thuê, xem trạng thái (chờ vào phiếu / đã vào phiếu / đã thanh toán) | M07 |
| PR-BR-17 ✅ | **Bên cho thuê = thông tin chủ trọ** (chốt 09/10/2026): bên cho thuê mặc định lưu ở tổ chức (`organizations.default_lessor` jsonb, số giấy tờ mã hóa như PR-BR-14, nhật ký che toàn bộ). Bên cho thuê **hiệu lực** của khu = bên cho thuê riêng của khu (cột `lessor_*`) nếu có, không thì của chủ trọ — dùng khi kích hoạt (chụp vào HĐ), in HĐ nháp, cảnh báo, `lessorComplete`, xem số giấy tờ. Chi tiết khu trả `lessor` hiệu lực + `lessorInherited`. Form lần đầu gợi ý từ tên / SĐT / địa chỉ liên hệ của tổ chức (`prefill`). Khai thông tin chủ trọ: chỉ chủ trọ | Domain + Application `LessorSource` |

### 3.3 Vòng đời
- Property: `Active` ⇄ `Archived` (`archived_at`).
- Room: `Active` ⇄ `Archived`; cờ `maintenance` (bool) chỉ có nghĩa khi Active.

## 4. Dữ liệu

### 4.1 Bảng

**`properties`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id | uuid | N | PK; UNIQUE (organization_id, id) |
| organization_id | uuid | N | FK organizations |
| code | varchar(32) | N | PR-BR-01 |
| name | varchar(200) | N | |
| province_code | varchar(10) | N | FK provinces (C-12) |
| commune_code | varchar(10) | N | FK communes; CHECK thuộc province (FK composite `(province_code, commune_code)`) |
| street_address | varchar(300) | N | số nhà, ngõ, đường |
| description | text | Y | |
| billing_anchor_day | smallint | N | **1–28**, mặc định 1 — ngày chốt kỳ **chung của khu** hiện hành (PR-BR-09; = mốc cuối của `billing_schedule`) |
| charge_mode | varchar(16) | N | **Mặc định `Postpaid`** (chốt 09/10/2026) — thu sau: tiền phòng + điện nước của chính kỳ, lập phiếu đầu kỳ sau; `Prepaid`: tiền phòng kỳ này + điện nước kỳ trước. Chung cả khu |
| payment_due_days | smallint | N | 0–60, mặc định 5 — hạn = ngày chốt phiếu + số ngày; quá hạn ⇒ nhãn đỏ (PR-BR-16) |
| proration_mode | varchar(16) | N | `Daily` / `FullPeriod` — tiền phòng kỳ lẻ, chung cả khu |
| default_notice_days | smallint | N | 0–180, mặc định 30 (L4) — gợi ý khi tạo HĐ (HĐ giữ riêng) |
| billing_schedule | jsonb | N | K4: `[{effectiveFrom, anchorDay, chargeMode, adjustDays}]` — mốc đầu `0001-01-01`; mỗi lần đổi khi khu đã có phiếu thêm 1 mốc (kỳ chuyển tiếp) |
| evn_customer_code | varchar(20) | Y | mã khách hàng điện, tham chiếu đối chiếu |
| **Bên cho thuê riêng của khu** (PR-BR-12, 17 — NULL hết ⇒ dùng `organizations.default_lessor`) | | | |
| lessor_type | varchar(16) | Y | `Individual`,`Organization` |
| lessor_name | varchar(200) | Y | họ tên chủ nhà / tên tổ chức |
| lessor_id_type | varchar(16) | Y | `CitizenId`,`Passport` (cá nhân) |
| lessor_id_number_encrypted / lessor_id_number_last4 | bytea / varchar(4) | Y | PR-BR-14 |
| lessor_id_issue_date / lessor_id_issue_place | date / varchar(200) | Y | |
| lessor_date_of_birth | date | Y | cá nhân |
| lessor_address | varchar(500) | Y | địa chỉ thường trú / trụ sở |
| lessor_phone / lessor_email | varchar(15) / varchar(254) | Y | |
| lessor_tax_code | varchar(14) | Y | bắt buộc nếu Organization |
| lessor_representative_name / lessor_representative_title | varchar(200) / varchar(100) | Y | bắt buộc nếu Organization |
| authorization_doc_no / authorization_doc_date | varchar(50) / date | Y | PR-BR-13; scan giấy ủy quyền ở M09 |
| **Thửa đất (tùy chọn — Điều 163 khoản 2)** | | | |
| land_parcel_no / land_map_sheet_no | varchar(20) | Y | số thửa / số tờ bản đồ |
| ownership_certificate_no | varchar(50) | Y | số Giấy chứng nhận (không bắt buộc khi cho thuê — Điều 160) |
| **Thanh toán & nội quy** | | | |
| bank_name / bank_account_no / bank_account_name | varchar(100) / varchar(20) / varchar(200) | Y | in trên phiếu báo, VietQR (P2) |
| house_rules_text | text | Y | nội quy (giờ giấc, khách, PCCC) — đính kèm hợp đồng |
| archived_at | timestamptz | Y | |
| audit cols, xmin | | | |

**`rooms`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id | uuid | N | PK; UNIQUE (organization_id, id); UNIQUE (organization_id, property_id, id) |
| organization_id | uuid | N | |
| property_id | uuid | N | FK composite → properties |
| code | varchar(20) | N | VD `101`, `A-203` |
| floor | varchar(10) | Y | chuỗi để chứa "Trệt", "Lửng" |
| area_m2 | numeric(6,2) | Y | > 0 |
| max_occupants | smallint | Y | 1–20, mô tả loại phòng (PR-BR-06) |
| listed_rent | numeric(18,0) | Y | ≥ 0 |
| default_deposit | numeric(18,0) | Y | ≥ 0; gợi ý tiền cọc khi tạo hợp đồng |
| amenities | jsonb | N | `[]` mảng chuỗi tag (VD `"air_con"`, `"wc_private"`) |
| description | text | Y | |
| is_under_maintenance | bool | N | false |
| maintenance_note | varchar(500) | Y | |
| archived_at | timestamptz | Y | |
| audit cols, xmin | | | |

**`room_groups`**: id, organization_id, property_id (FK composite), name varchar(100), description, audit, xmin.
UNIQUE `(property_id, lower(name))`.

**`room_group_members`**: organization_id, property_id, room_group_id, room_id.
PK `(room_group_id, room_id)`. FK composite `(organization_id, property_id, room_group_id)` → room_groups và
`(organization_id, property_id, room_id)` → rooms ⇒ DB bảo đảm PR-BR-07.

### 4.2 Ràng buộc & index
- `UNIQUE (organization_id, lower(code))` trên properties; `UNIQUE (property_id, lower(code))` trên rooms.
- CHECK `billing_anchor_day BETWEEN 1 AND 28`, `max_occupants IS NULL OR max_occupants BETWEEN 1 AND 20`, `area_m2 > 0`, `listed_rent >= 0`.
- CHECK `NOT (is_under_maintenance AND archived_at IS NOT NULL)`.
- CHECK `lessor_type IS NULL OR lessor_type IN ('Individual','Organization')`;
  CHECK `lessor_type <> 'Organization' OR (lessor_tax_code IS NOT NULL AND lessor_representative_name IS NOT NULL)`.
- CHECK `default_deposit IS NULL OR default_deposit >= 0`.
- INDEX `rooms (organization_id, property_id) WHERE archived_at IS NULL`.

### 4.3 Dữ liệu dẫn xuất
- `RoomDisplayStatus` tính trong query (LEFT JOIN hợp đồng hiện hành của M05), không lưu → không thể lệch với hợp đồng.
- `CurrentOccupantCount` = số occupant đang ở của hợp đồng hiện hành (M05).

## 5. Domain model

```
Property (aggregate root)
  + Create(orgId, code, name, address, billingDefaults, now)
  + UpdateInfo(...), UpdateBillingDefaults(BillingDefaults)
  + Archive(now) / Restore()
BillingDefaults (value object): AnchorDay, ChargeMode, PaymentDueDays, ProrationMode, NoticeDays — tự validate
Address (value object): ProvinceCode, CommuneCode, StreetAddress
LessorInfo (value object): Type, Name, IdDocument?, DateOfBirth?, Address, Phone, TaxCode?, Representative?, Authorization?
  + IsComplete : bool                // PR-BR-12
  + ToSnapshot() : LessorSnapshot    // dùng khi kích hoạt hợp đồng (CT-BR-19)
BankAccount (value object): BankName, AccountNo, AccountName
Property + UpdateLessor(LessorInfo), UpdateBankAccount(BankAccount?), UpdateHouseRules(text)

Room (aggregate root — tách khỏi Property để tránh load cả khu khi sửa 1 phòng)
  + Create(orgId, propertyId, code, floor, area, maxOccupants, listedRent, ...)
  + Update(...)
  + StartMaintenance(note) / EndMaintenance()   // precondition "không có HĐ hiệu lực" kiểm ở Application
  + Archive(now)

RoomGroup (aggregate root): + Rename, + AddRoom(roomId), + RemoveRoom(roomId)
```
Code hiện tại `Room.Status/MarkOccupied/MarkAvailable` **bị loại bỏ** (PR-BR-02).

## 6. Application — Commands / Queries

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `CreatePropertyCommand` / `UpdatePropertyCommand` | Cmd | `PROPERTY_CODE_TAKEN`, `INVALID_COMMUNE` |
| `ArchivePropertyCommand` / `RestorePropertyCommand` | Cmd | `PROPERTY_HAS_ACTIVE_CONTRACTS` |
| `ListPropertiesQuery` / `GetPropertyQuery` | Qry | (kèm số phòng theo trạng thái) |
| `CreateRoomCommand` | Cmd | `ROOM_CODE_TAKEN`, `PROPERTY_ARCHIVED`, `ROOM_LIMIT_EXCEEDED` |
| `BulkCreateRoomsCommand` | Cmd | Trả danh sách mã trùng; **all-or-nothing** |
| `UpdateRoomCommand` | Cmd | `ROOM_CODE_TAKEN` |
| `StartRoomMaintenanceCommand` / `EndRoomMaintenanceCommand` | Cmd | `ROOM_OCCUPIED` |
| `ArchiveRoomCommand` / `RestoreRoomCommand` | Cmd | `ROOM_HAS_CONTRACTS` |
| `ListRoomsQuery` | Qry | filter: propertyIds[], status[], floor, groupId, search; phân trang |
| `GetRoomQuery` | Qry | kèm hợp đồng hiện hành (id, người đại diện, ngày bắt đầu), số người ở |
| `CreateRoomGroupCommand` / `UpdateRoomGroupCommand` / `DeleteRoomGroupCommand` | Cmd | `ROOM_GROUP_NAME_TAKEN`, `ROOM_GROUP_IN_USE` |
| `SetRoomGroupMembersCommand` | Cmd | Thay toàn bộ danh sách; `ROOM_NOT_IN_PROPERTY` |

## 7. API

| Method | Route | Response | Mã lỗi |
|--------|-------|----------|--------|
| GET | `/properties?includeArchived=false` | Page | |
| POST | `/properties` | 201 | 409 `PROPERTY_CODE_TAKEN`, 400 `INVALID_COMMUNE` |
| GET | `/properties/{id}` | 200 | 404 |
| PUT | `/properties/{id}` | 200 | 409 |
| POST | `/properties/{id}/archive` · `/restore` | 204 | 422 `PROPERTY_HAS_ACTIVE_CONTRACTS` |
| GET | `/rooms?propertyIds=..&status=Vacant&floor=&groupId=&search=` | Page | |
| POST | `/properties/{propertyId}/rooms` | 201 | 409 `ROOM_CODE_TAKEN` |
| POST | `/properties/{propertyId}/rooms/bulk` | 201 `{created: n}` | 409 `ROOM_CODE_TAKEN` + `errors.codes[]` |
| GET | `/rooms/{id}` | 200 | 404 |
| PUT | `/rooms/{id}` | 200 | 409 |
| POST | `/rooms/{id}/maintenance/start` · `/end` | 204 | 422 `ROOM_OCCUPIED` |
| POST | `/rooms/{id}/archive` · `/restore` | 204 | 422 `ROOM_HAS_CONTRACTS` |
| GET/POST | `/properties/{propertyId}/room-groups` | | |
| PUT/DELETE | `/room-groups/{id}` | | 409 `ROOM_GROUP_IN_USE` |
| PUT | `/room-groups/{id}/members` `{ roomIds: [] }` | 204 | 422 `ROOM_NOT_IN_PROPERTY` |
| PUT | `/properties/{id}/lessor` | 200 | 400; xem số giấy tờ đầy đủ: POST `/properties/{id}/lessor/reveal-id-number` (audit) |
| PUT | `/properties/{id}/bank-account` · `/house-rules` | 200 | |

**Ví dụ — tạo phòng hàng loạt**
```json
POST /api/v1/properties/{propertyId}/rooms/bulk
{
  "floors": [ { "floor": "1", "codes": ["101","102","103"] }, { "floor": "2", "codes": ["201","202"] } ],
  "maxOccupants": 3, "areaM2": 18.5, "listedRent": 3000000, "amenities": ["wc_private"]
}
```

## 8. Validation

| Field | Quy tắc | Mã lỗi |
|-------|---------|--------|
| property.code | `^[A-Za-z0-9-_]{1,32}$` | `INVALID_FORMAT` |
| name | 1–200 | |
| provinceCode/communeCode | tồn tại trong bảng tham chiếu & xã thuộc tỉnh | `INVALID_COMMUNE` |
| streetAddress | 1–300 | |
| anchorDay | 1–28 | ngày chốt chung của khu |
| chargeMode / prorationMode | enum hợp lệ | |
| paymentDueDays | 0–60 | |
| room.code | `^[A-Za-z0-9-_.]{1,20}$` | |
| areaM2 | null hoặc 0 < x ≤ 1000, tối đa 2 số lẻ | |
| maxOccupants | null hoặc 1–20 | |
| listedRent | null hoặc 0 ≤ x ≤ 1.000.000.000, số nguyên | |
| amenities | ≤ 30 phần tử, mỗi tag `^[a-z0-9_]{1,30}$` | |
| lessor.idNumber | như M03 (CCCD 12 số / hộ chiếu) | `INVALID_ID_NUMBER` |
| lessor.dateOfBirth | đủ 18 tuổi | `LESSOR_UNDERAGE` |
| lessor.taxCode | `^\d{10}(-\d{3})?$` | `INVALID_TAX_CODE` |
| bankAccountNo | 6–20 chữ số | `INVALID_BANK_ACCOUNT` |
| houseRulesText | ≤ 20.000 ký tự | |
| defaultDeposit | null hoặc số nguyên 0 … 1.000.000.000 | |
| bulk | ≤ 500 phòng/lần; mã trong request không trùng nhau | `DUPLICATE_IN_REQUEST` |

## 9. Phân quyền
P1: chủ trọ và phó quản lý toàn quyền nghiệp vụ (M01 §3.3); xem số giấy tờ đầy đủ của bên cho thuê: dữ liệu nhạy cảm (số giấy tờ đầy đủ) chỉ chủ trọ hoặc phó quản lý được chủ trọ cấp quyền (ID-BR-22). P3: phó quản lý theo khu được gán. SystemAdmin: không.

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Giải pháp |
|----------|-----------|
| Đặt bảo trì đồng thời với kích hoạt hợp đồng | Cả 2 lệnh `SELECT … FROM rooms WHERE id=@id FOR UPDATE` trước khi kiểm tra → tuần tự hóa |
| Archive phòng đồng thời tạo hợp đồng nháp | Như trên (khóa hàng room) |
| Tạo phòng vượt giới hạn gói khi 2 request song song | Khóa hàng `organizations` (`FOR UPDATE`) khi đếm |
| Đổi code phòng sau khi đã có phiếu | Phiếu (M07) lưu snapshot `room_code` → không ảnh hưởng |
| Nhóm phòng chứa phòng khu khác | FK composite (PR-BR-07) |

## 11. Audit & bảo mật
Audit: tạo/sửa/archive khu & phòng, đổi cài đặt thu, bảo trì, thay đổi thành viên nhóm.

## 12. Kế hoạch test
- Unit: `BillingDefaults` validate; `Room.StartMaintenance` khi archived → Failure.
- Integration: mã phòng trùng (khác hoa thường) → 409; bulk có 1 mã trùng → không tạo phòng nào;
  nhóm thêm phòng khu khác → 422; archive phòng có HĐ Active → 422; status dẫn xuất đúng khi HĐ kích hoạt/kết thúc;
  **C-01**: tổ chức B GET `/rooms/{id của A}` → 404, PUT room group với roomId của A → 422/404.

## 13. Phụ thuộc
- Dùng: M01 (`ICurrentUser`), bảng ĐVHC.
- Bị dùng bởi: M04 (khoản thu theo khu), M05 (hợp đồng theo phòng), M06 (công tơ theo phòng), M07 (kỳ thu của khu, lọc phòng / tầng khi tạo phiếu), M10.
- M02 cần **đọc** hợp đồng (M05) để tính status → dùng query interface `IRoomOccupancyReader` hiện thực ở M05 (tránh vòng phụ thuộc trong Application: interface đặt ở M02, hiện thực khi M05 xong; trước đó trả "Vacant").

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| PR-01 | Domain Property/Room/RoomGroup + VO + unit test (xóa code Room cũ) | 1d |
| PR-02 | EF config, FK composite, index, migration | 0.5d |
| PR-03 | Commands/Queries khu trọ | 1d |
| PR-04 | Commands/Queries phòng + bulk | 1d |
| PR-05 | Nhóm phòng | 0.5d |
| PR-06 | Integration tests | 1d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Có cần "loại phòng" (đơn/đôi/studio) với giá mặc định? | Không (YAGNI); dùng nhóm phòng + tag tiện ích |
| Q2 | Một phòng cho thuê theo **giường** (ký túc xá)? | Ngoài phạm vi P1; nếu cần → mỗi giường là 1 `Room` với tiền tố mã |
| Q3 | Ảnh phòng? | Dùng M09 với owner_type `Room` |
