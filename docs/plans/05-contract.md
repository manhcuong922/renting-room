# M05 — Contract (Hợp đồng, Người ở, Phụ lục, Thanh lý)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L1, L2, L4, LEG-01. Kỳ thu: C-05.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Lưu trữ **thông tin hợp đồng thuê** giữa chủ trọ và người thuê cho **một phòng**, là nguồn sự thật cho:
phòng đang có người (M02), ai đang ở (M03), giá thuê theo thời gian, khoản thu đăng ký, cài đặt kỳ thu (M07), tiền cọc thỏa thuận (M08).

**Trong phạm vi**: tạo nháp, kích hoạt (bàn giao), người ở cùng vào/ra, phụ lục giá & gia hạn, đăng ký khoản thu,
báo trả phòng, thanh lý (orchestration), hủy nháp, file scan (M09).

**Bổ sung (02–03/10/2026)**: **mẫu hợp đồng** theo loại (thuê phòng trọ / thuê nhà nguyên căn) gồm tiêu đề, điều khoản, trường tùy biến;
**hợp đồng không cọc**; **quan hệ người ở với người đứng tên** (phục vụ đăng ký tạm trú chung hộ), đồng ý của cha mẹ / người giám hộ
cho người chưa thành niên; chặn một người ở 2 phòng cùng lúc.

**Ngoài phạm vi**: in hợp đồng ra PDF/DOCX từ mẫu (P2 — CT-UC-15), ký điện tử.

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Hợp đồng | `Contract` | Thỏa thuận thuê 1 phòng trong một khoảng thời gian |
| Bên thuê (đại diện) | `RepresentativeRenter` | Người đứng tên ký HĐ — có thể **không** ở (VD phụ huynh ký cho con) |
| Người ở | `Occupant` / `contract_occupants` | Người thực tế ở phòng, có ngày vào/ra riêng |
| Điều khoản giá | `RentTerm` | Giá thuê tháng hiệu lực từ đầu một kỳ |
| Đăng ký khoản thu | `ContractFee` | Khoản thu (M04) áp cho HĐ, có số lượng/giá riêng, hiệu lực theo kỳ |
| Ngày kết thúc thỏa thuận | `end_date` | Hạn HĐ ghi trên giấy (NULL = không thời hạn) |
| Ngày kết thúc thực tế | `actual_end_date` | **Ngày cuối cùng tính tiền** (bao gồm), đặt khi thanh lý |
| Thanh lý | `Liquidation` | Quy trình kết thúc: chỉ số cuối, phiếu quyết toán, cấn trừ/hoàn cọc |
| Loại hợp đồng | `ContractType` | `RoomRental` (thuê phòng trọ) · `WholeHouseRental` (thuê nhà nguyên căn — khai báo là 1 khu có 1 phòng) |
| Mẫu hợp đồng | `ContractTemplate` / `contract_templates` | Của tổ chức: tên, loại, tiêu đề, điều khoản, định nghĩa trường tùy biến, chính sách cọc |
| Điều khoản | `ContractClause` | Một mục văn bản `{heading, body}` (VD "Trách nhiệm của bên A") |
| Trường tùy biến | `CustomFieldDefinition` | Ô nhập thêm theo mẫu: `key`, `label`, `type` (Text/LongText/Number/Money/Date/Boolean/Select), `required`, `options`, `unit`, `hint` |
| Hợp đồng không cọc | `no_deposit` | Mẫu có cờ này ⇒ `deposit_amount = 0`. "Nhóm không cọc" = mọi HĐ có `deposit_amount = 0` |
| Quan hệ với người đứng tên | `OccupantRelationship` | Quan hệ của người ở với người ký HĐ — dùng làm "quan hệ với chủ hộ" khi đăng ký tạm trú chung hộ |
| Đồng ý của người giám hộ | `guardian_consent` | Người ở < 18 tuổi đã có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (Luật Cư trú Điều 28) |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| CT-UC-01 | Tạo HĐ nháp: chọn phòng, người đại diện, người ở, ngày bắt đầu, thời hạn (bỏ trống = không thời hạn), giá thuê, tiền cọc, cài đặt kỳ thu (theo khu — không chọn riêng), khoản thu **cố định / theo số lượng** (auto-attach từ M04, chỉnh được — điện nước theo công tơ đi theo phòng, FE-BR-17), điều khoản (text) |
| CT-UC-02 | Sửa HĐ nháp (mọi trường) / hủy nháp |
| CT-UC-03 ✅ | **Kích hoạt** (bàn giao phòng): nhập chỉ số bàn giao cho các công tơ (M06) |
| CT-UC-04 | Thêm người ở (ngày vào, **quan hệ với người đứng tên**, đồng ý của người giám hộ nếu < 18 tuổi) / ghi nhận người ở rời đi (ngày ra) |
| CT-UC-05 | **Sửa giá thuê / dịch vụ trực tiếp trên HĐ** (chốt 09/10/2026) — không nhập ngày: hệ thống tự áp từ **kỳ chưa chốt đầu tiên** và giữ lịch sử phía sau (`contract_rent_terms`, `contract_fees` theo khoảng hiệu lực) ⇒ hủy / lập lại phiếu tháng cũ vẫn ra giá cũ. Gia hạn `end_date`; đổi tiền cọc thỏa thuận. Sửa **giá niêm yết** của phòng (PR-UC-04) → UI hỏi "Áp giá mới cho người đang thuê?" ⇒ `POST /rooms/{id}/apply-listed-rent` đổi giá HĐ đang ở từ kỳ chưa chốt đầu tiên (không tự đổi — giá thỏa thuận có thể khác giá niêm yết) |
| CT-UC-06 ✅ | Đổi đăng ký khoản thu từ kỳ X (thêm/bỏ khoản, đổi số lượng xe, giá riêng) — nháp qua `fees[]`, đang hiệu lực qua `PUT/DELETE /contracts/{id}/fees/{feeTypeId}` |
| CT-UC-07 | Báo trả phòng: ghi nhận ngày báo, ngày dự kiến trả; cảnh báo nếu < `notice_days` |
| CT-UC-08 ✅ | **Trả phòng**: (1) bắt đầu thanh lý — đặt `actual_end_date` (báo trước đủ 1 tháng hoặc trả ngay); (2) ghi tình trạng tài sản; (3) **lập phiếu quyết toán** — nhập chỉ số cuối công tơ, nháp sửa được, bồi thường thêm làm phụ thu (M07 BL-UC-11); (4) chốt phiếu quyết toán; (5) **hoàn tất** theo CT-BR-12; (6) khi trả lại cọc ⇒ bấm "Đã hoàn trả cọc" (M08 PM-BR-33 — không bắt buộc trước khi hoàn tất) |
| CT-UC-09 | Hủy thanh lý (quay về Active) khi chưa chốt phiếu quyết toán |
| CT-UC-10 | Danh sách HĐ: lọc khu, phòng, trạng thái, sắp hết hạn (≤ 30 ngày), quá hạn, người thuê |
| CT-UC-11 | Đính kèm file scan HĐ, phụ lục, biên bản bàn giao (M09) |
| CT-UC-12 ✅ | **Chuyển phòng**: HĐ **đi theo người thuê** — giữ nguyên số HĐ, người ở, xe, dịch vụ, cọc, kỳ thu; chỉ đổi phòng từ ngày D sang **phòng trống cùng khu** (HĐ thường không ghi cụ thể mã phòng). Trong 1 thao tác: chỉ số cuối các công tơ phòng cũ tại D (bắt buộc), chỉ số nhận phòng các công tơ phòng mới tại D (bỏ trống = số mới nhất), (tùy chọn) giá thuê mới áp từ kỳ chưa chốt đầu tiên (như CT-UC-05). Khác khu ⇒ thanh lý + HĐ mới |
| CT-UC-13 | Ghi **tài sản bàn giao** (điều hòa, giường, chìa khóa…) và tình trạng; khi trả phòng ghi tình trạng lúc trả — bồi thường (nếu có) là phụ thu trên phiếu quyết toán (CT-BR-23) |
| CT-UC-14 | **Phương tiện của phòng** (chốt 09/10/2026): loại (xe máy / xe đạp / xe điện / ô tô), hiệu – màu, biển số, chủ xe (tùy chọn) — quản lý theo phòng (gắn với lượt thuê đang ở của phòng nên người đi thì xe kết thúc theo). Đối chiếu số xe với số gói phí giữ xe (CT-BR-22, cảnh báo) |
| CT-UC-15 ✅ | In hợp đồng ra **.docx** (`GET /contracts/{id}/document`) từ bản chụp lúc ký + mẫu + khoản thu + tiền bằng chữ; nháp có dòng "BẢN NHÁP". Còn lại P2: PDF, phụ lục, biên bản bàn giao riêng |
| CT-UC-16 ✅ | Quản lý **mẫu hợp đồng**: tạo từ mẫu gợi ý (thuê trọ / thuê trọ không cọc / thuê nhà nguyên căn), sửa tiêu đề, điều khoản, trường tùy biến, ngừng dùng / khôi phục |
| CT-UC-17 ✅ | Tạo / sửa HĐ nháp **theo mẫu**: chép tiêu đề, điều khoản, định nghĩa trường vào HĐ; nhập giá trị trường tùy biến (VD cách tính điện, tiền nước/người, wifi); ghi đè tiêu đề / điều khoản cho riêng HĐ |
| CT-UC-18 ✅ | Lọc **nhóm hợp đồng không cọc / có cọc** trong danh sách HĐ |
| CT-UC-20 ✅ | **Dữ liệu cần xem lại** (`GET /contracts/data-review`): quan hệ người ở không còn hợp lý, người đứng tên < 18 tuổi tại ngày ký, một người ở 2 phòng, khoản thu chưa có giá |
| CT-UC-19 ✅ | Khai **quan hệ người ở với người đứng tên** (vợ, chồng, con, cha mẹ, cùng ở thuê…) — dữ liệu để lập tờ khai tạm trú chung hộ và xuất danh sách (M10 E1) |
| CT-UC-21 ✅ | **Ký lại cho người còn ở** — `POST /contracts/{id}/re-sign` `{ handoverDate, representativeRenterId, endDate? }`: trong 1 transaction (1) HĐ cũ bắt đầu thanh lý tại `handoverDate` = X (lý do `MutualAgreement`, ghi chú "Ký lại cho người còn ở — HĐ mới …"); (2) tạo **HĐ nháp mới** từ X+1, `previous_contract_id` = HĐ cũ, chép: giá thuê hiện hành, cài đặt kỳ thu, ngày báo trước, phương thức thanh toán, cọc thỏa thuận + điều khoản cọc, mẫu / tiêu đề / điều khoản / trường tùy biến, khoản dịch vụ đang áp tại X+1, **người ở còn ở sau X** (vào ở X+1; giữ quan hệ nếu chủ hộ còn ở, ngược lại xóa quan hệ để khai lại so với người đứng tên mới), xe đang đăng ký của người đứng tên mới / người ở được chép. `endDate` bỏ trống = không thời hạn. Người đứng tên mới phải là một người còn ở (422 `RESIGN_REPRESENTATIVE_NOT_OCCUPANT`); không còn ai ở sau X → 422 `RESIGN_NO_OCCUPANT_LEFT`; lỗi thanh lý như CT-BR-11. → 201 `{ id: <HĐ nháp mới>, warnings }`. Chủ trọ xem lại nháp rồi kích hoạt như bình thường; body thêm `transferDeposit` (mặc định `true`) — có ⇒ cọc của HĐ cũ ghi **"Đã chuyển sang HĐ mới"** (M08 PM-BR-34); `false` ⇒ HĐ cũ vẫn "Đang giữ cọc" để hoàn sau |
| CT-UC-23 ✅ | **Bản HĐ đã ký** (chốt 09/10/2026): `PUT /contracts/{id}/signed-document` `{ hasSignedDocument, note }` — đánh dấu đã có bản ký (giấy / ảnh / PDF) + ghi nơi cất; chưa có ⇒ cờ **"Thiếu tài liệu"** (`MissingSignedDocument`, CT-BR-46). Lọc `GET /contracts?missingSignedDocument=true`. HĐ import mặc định **thiếu**. Khi có M09 (upload file) tải bản scan lên sẽ tự đánh dấu |
| CT-UC-22 ✅ | **Quyết định khi HĐ hết hạn mà người thuê vẫn ở** (CT-BR-45): (1) Gia hạn / ký phụ lục — `POST /contracts/{id}/extend` (xóa trạng thái ở tiếp); (2) **Ở tiếp, chưa ký lại** — `POST /contracts/{id}/holdover` `{ note? }` → 204; HĐ chưa quá hạn → 422 `CONTRACT_NOT_EXPIRED`, đã ghi nhận → 409 `HOLDOVER_ALREADY`; (3) **Không cho thuê tiếp** → `POST /contracts/{id}/liquidation/start` |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| CT-BR-01 | Một phòng **không thể có 2 HĐ chồng lấn thời gian** trong các trạng thái `Active/Liquidating/Ended`. Khoảng chiếm phòng = `[start_date, COALESCE(actual_end_date, 'infinity')]` | **DB EXCLUDE constraint** (btree_gist) |
| CT-BR-02 | Kích hoạt yêu cầu (chỉ phần **thu tiền**): phòng không archive/bảo trì; ≥ 1 người ở; giá thuê > 0; `start_date` ≤ hôm nay + 1 (kích hoạt = bàn giao thực tế; cho phép lùi ngày khi nhập HĐ cũ). **Đổi 04/10/2026**: không còn bắt HĐ đăng ký công tơ. Khi M06 có: màn hình kích hoạt **bắt nhập chỉ số nhận phòng** cho từng công tơ đang hoạt động của phòng tại ngày vào ở — nút "Dùng số mới nhất" (chỉ số cuối của HĐ trước / chỉ số tháng trước) hoặc nhập số khác ≥ số đó (MT-BR-13); phòng chưa có công tơ điện → **cảnh báo**, không chặn (phòng tính nước theo người không cần công tơ nước). **Đổi 09/10/2026**: người đứng tên thiếu SĐT → chỉ cảnh báo `REPRESENTATIVE_PHONE_MISSING` (CT-BR-46) | Domain + Application |
| CT-BR-03 | `end_date` (nếu có) > `start_date`. Hết `end_date` mà chưa thanh lý → HĐ **vẫn Active** (cờ dẫn xuất `IsOverdue`), vẫn lập phiếu bình thường — không tự kết thúc | Thiết kế |
| CT-BR-04 ✅ | **Cài đặt kỳ thu lấy theo khu** (đổi 08/10/2026 — M02 PR-BR-09): ngày chốt, thu trước / thu sau, tính theo ngày / trọn tháng, số ngày hạn là của khu, HĐ **không chọn riêng** (code 09/10/2026: bỏ cột trên HĐ, ô `billing` khi tạo HĐ không còn). Kỳ lẻ khi vào / trả phòng giữa kỳ: theo C-05. **K5**: `billing_start_date` "Tính tiền từ ngày" (mặc định = `start_date`, trong thời gian HĐ, ≤ 1 năm trước hôm nay; có ô này thì `start_date` được tới 10 năm trước — HĐ nhập từ sổ cũ); kích hoạt ghi **chỉ số nhận phòng tại ngày tính tiền** | Domain |
| CT-BR-05 | Giá thuê theo `contract_rent_terms`; bản đầu tiên `effective_from = start_date`. Phụ lục giá chỉ cho hợp đồng **Active** (nháp sửa trực tiếp). Bản mới phải có `effective_from` là **ngày bắt đầu một kỳ** (C-05) và ≥ kỳ đầu tiên chưa khóa: kỳ sau ngày cuối của **dòng Tiền phòng** đã chốt mới nhất; dịch vụ khóa theo kỳ phiếu đã chốt | Domain — ✅ khóa theo phiếu đã chốt |
| CT-BR-06 | `contract_fees`: mỗi `(contract, fee_type)` các khoảng hiệu lực không chồng lấn; `effective_from` là đầu kỳ, cùng quy tắc khóa như CT-BR-05. Khoản thu phải thuộc **cùng khu** với HĐ | DB EXCLUDE + FK composite `(organization_id, property_id, fee_type_id)` |
| CT-BR-07 | `quantity`: Quantity > 0; Fixed = 1. Khoản `Metered` không gắn vào HĐ (FE-BR-17). `unit_price_override` ≥ 0 hoặc NULL (dùng giá danh mục) | CHECK + Application |
| CT-BR-08 | Người ở: `move_in_date` ≥ `start_date` của HĐ; `move_out_date` (nếu có) ≥ `move_in_date` và ≤ `actual_end_date` (nếu có). Cùng 1 renter không có 2 khoảng ở chồng lấn trong cùng HĐ. Đã ghi chuyển đi thì **không ghi lại** (409 `OCCUPANT_ALREADY_MOVED_OUT`) — muốn ở tiếp thì thêm lại như người ở mới | Domain + DB EXCLUDE |
| CT-BR-09 ✅ | **Không giới hạn số người ở theo sức chứa** — số người do chủ trọ quyết định (PR-BR-06). **Người đứng tên phải có giấy tờ** (RT-BR-01) ⇒ 422 `REPRESENTATIVE_ID_REQUIRED` khi tạo / sửa nháp, ký lại | Application |
| CT-BR-11 ✅ | Bắt đầu thanh lý: `actual_end_date` ≥ `start_date` và ≥ `move_in_date` muộn nhất; ≤ hôm nay + 60 ngày; **không tồn tại phiếu Regular chưa Void có `period_start` > `actual_end_date`** (phải hủy trước — tránh thu tiền kỳ sau khi đã trả phòng); không tồn tại chỉ số `Periodic` chưa hủy có `closing_period_start` > `actual_end_date`. Trong trạng thái `Liquidating` M07 **không** tạo phiếu định kỳ cho kỳ bắt đầu sau `actual_end_date` | Domain + Application |
| CT-BR-12 ✅ | **Hoàn tất thanh lý**: (1) có **chỉ số cuối** mọi công tơ (nhập khi lập phiếu quyết toán); (2) **phiếu quyết toán đã chốt**; (3) không còn phiếu **Chờ hoàn** (`REFUND_PENDING`); (4) **còn nợ** ⇒ **cảnh báo** + chủ trọ chọn **"Đã thu toàn bộ"** (M08 PM-UC-14) hoặc **"Bỏ nợ"** (lý do bắt buộc, theo quyền — PM-BR-16) — hoặc hủy để thu sau. **Cọc không chặn** (chỉ nhắc "Chưa hoàn cọc" — M08 PM-BR-35). Xong ⇒ `Ended`, đóng người ở và xe tại `actual_end_date` | Application (transaction) |
| CT-BR-13 | Không xóa HĐ khác `Draft`. Nháp chỉ **hủy** (`Cancelled`, lý do) — giữ lại lịch sử. Cọc giữ chỗ (nếu có) theo dõi bằng "Đã hoàn trả cọc" như HĐ khác (M08 PM-BR-33) | Domain |
| CT-BR-14 ✅ | **Chuyển phòng** (CT-UC-12, đổi 10/10/2026) — `POST /contracts/{id}/transfer-room`: HĐ `Active` (không đang thanh lý); D ≤ hôm nay, D > ngày vào phòng hiện tại; phòng mới **cùng khu**, khác phòng hiện tại, không ngừng dùng / bảo trì, **trống từ D** (không HĐ nào chiếm từ D — kể cả HĐ nháp giữ chỗ, kể cả lịch sử chuyển phòng) → 422 `ROOM_TRANSFER_*`. Không được có đoạn đo điện nước đã lập phiếu kết thúc sau D trên công tơ phòng cũ → 422 `ROOM_TRANSFER_ALREADY_BILLED` (hủy phiếu đó trước). Lưu: `contracts.room_id` = phòng mới, `room_since` = D; lịch sử `contract_room_moves (room_id, from_date, to_date = D)`. Ngày D tính là đang thuê ở **cả 2 phòng** (như ngày trả phòng — CT-BR-33); phòng cũ trống từ D+1. Bản HĐ đã ký giữ phòng lúc ký; chi tiết HĐ hiện "Phòng hiện tại" + lịch sử. Cảnh báo `RESIDENCE_ROOM_CHANGED`: cập nhật số phòng trên đăng ký tạm trú nếu cần | Domain + Application + DB |
| CT-BR-15 | `contract_no`: người dùng nhập hoặc tự sinh `HD{yyyy}-{seq:0000}`; unique trong tổ chức | DB unique |
| CT-BR-16 ✅ | **Báo trước khi trả phòng**: người thuê phải báo trước `notice_days` (mặc định 30 — "báo trước 1 tháng"). Báo muộn / không báo ⇒ cảnh báo `LESSEE_TERMINATION_SHORT_NOTICE` và chủ trọ **vẫn trả phòng ngay được**; phạt theo điều khoản (nếu có) nhập thành phụ thu trên phiếu quyết toán | Application |
| CT-BR-17 | `deposit_amount` (thỏa thuận) ≥ 0; 0 = không cọc. Cọc chỉ là **thông tin theo dõi** (M08 PM-BR-32): trạng thái `Holding` / `Refunded` / `Transferred`, ngày và số tiền đã hoàn, ghi chú. Chi tiết HĐ / phòng hiện "Có cọc X — đang giữ / đã hoàn ngày …" | Domain |
| CT-BR-18 | Người đứng tên **nên** đủ 18 tuổi tại ngày ký (`signed_date`, nếu trống dùng ngày bắt đầu) — BLDS 2015 Điều 117. **Đổi 09/10/2026: chỉ cảnh báo** `REPRESENTATIVE_UNDERAGE` (bản giấy cần người giám hộ ký thay), không chặn kích hoạt. Người ở có thể dưới 18 tuổi | Application `ContractPaperWarnings` |
| CT-BR-19 | Khu chưa có **thông tin bên cho thuê đầy đủ** (PR-BR-12) → **chỉ cảnh báo** `LESSOR_INFO_INCOMPLETE` "chưa in được HĐ đầy đủ" (đổi 09/10/2026 — trước đây chặn kích hoạt); bên cho thuê = bên cho thuê riêng của khu, không có thì **thông tin chủ trọ** (PR-BR-17); chưa khai cả hai ⇒ `signing_snapshot` NULL. Khi kích hoạt chụp **snapshot bất biến**: `signing_snapshot` (bên cho thuê, bên thuê, phòng, ngân hàng), `utility_price_snapshot` (đơn giá điện, nước, dịch vụ tại ngày bắt đầu), `house_rules_snapshot` — sửa khu sau đó không đổi hợp đồng đã ký | Application |
| CT-BR-20 | `effective_date` mặc định = `signed_date` (Luật Nhà ở 2023 Điều 164: không thỏa thuận thì hiệu lực từ lúc ký); ràng buộc `signed_date ≤ effective_date` (có thể sau ngày bàn giao: dọn vào trước, ký sau). Không ghi ngày ký: HĐ nhập lại (bắt đầu trong quá khứ) lấy ngày bắt đầu, HĐ mới lấy hôm nay | Validator + CHECK |
| CT-BR-21 ✅ | Kết thúc phải có `termination_reason`: `Expired`, `MutualAgreement`, `LesseeUnilateral`, `LessorUnilateral`, `RoomTransfer`, `Abandoned` (CT-BR-41). `Expired` chỉ khi HĐ **có thời hạn** và `actual_end_date ≥ end_date` → ngược lại 422 `EXPIRED_REASON_INVALID`. `LessorUnilateral` **nên** chọn `termination_ground` (tùy chọn từ 09/10/2026 — trước đây bắt buộc) thuộc Điều 172 khoản 2 (`RentArrears3Months`, `WrongPurpose`, `UnauthorizedRenovation`), `IndefiniteTermNotice` (CT-BR-42) hoặc `Other` + mô tả; báo trước < 30 ngày → cảnh báo `LESSOR_TERMINATION_SHORT_NOTICE` (tính `actual_end_date − liquidation_started_on`). Bên thuê đơn phương (`LesseeUnilateral`) mà chưa báo trước / báo < `notice_days` → cảnh báo `LESSEE_TERMINATION_SHORT_NOTICE` (xử lý cọc theo điều khoản) | Domain + Application |
| CT-BR-22 ✅ | Tổng số xe đăng ký đang hiệu lực theo loại ≠ số lượng khoản phí giữ xe tương ứng → **cảnh báo** `PARKING_QUANTITY_MISMATCH` (không chặn). Khoản phí giữ xe = khoản `Quantity` có `fee_types.vehicle_type` | Domain `ContractWarnings` (chi tiết HĐ + đăng ký xe) |
| CT-BR-23 ✅ | Tài sản bàn giao — lúc trả phòng **chỉ ghi tình trạng** (`condition_at_return`). **Bồi thường = phụ thu** chủ trọ thêm trên nháp phiếu quyết toán (M07 BL-BR-23) — không có cơ chế riêng | Domain |
| CT-BR-24 | Hợp đồng là giấy tờ chứng minh chỗ ở hợp pháp để đăng ký tạm trú, không cần công chứng (NĐ 154/2024) ⇒ phải in được đầy đủ thông tin 2 bên + địa chỉ phòng | Thiết kế (CT-UC-15) |
| CT-BR-25 | **Mẫu hợp đồng** theo tổ chức (`contract_templates`: tên duy nhất, loại `RoomRental`/`WholeHouseRental`, tiêu đề, điều khoản jsonb, định nghĩa trường jsonb). Tạo/sửa nháp **chép** tiêu đề, điều khoản, định nghĩa trường vào hợp đồng (`title`, `clauses`, `custom_field_definitions`) ⇒ sửa/ngừng mẫu không đổi HĐ đã kích hoạt. Mẫu ngừng dùng không chọn được cho HĐ mới; nháp đang gắn mẫu vẫn sửa được | Application + FK composite `(organization_id, template_id)` |
| CT-BR-26 | `custom_field_values` khớp định nghĩa: đủ trường bắt buộc, đúng kiểu (Text/LongText/Number/Money/Date/Boolean/Select), không có key lạ. Không dùng mẫu ⇒ không có trường tùy biến. Trường chỉ **ghi nhận thỏa thuận** — tính tiền điện/nước vẫn thuộc M04 | Application (lỗi 400 theo `contract.customFields.<key>`) |
| CT-BR-27 | **Hợp đồng không cọc**: mẫu có `no_deposit = true` ⇒ `deposit_amount = 0` (bỏ qua cọc mặc định của phòng; gửi > 0 → 400 `DEPOSIT_NOT_ALLOWED`). Nhóm không cọc = mọi HĐ có `deposit_amount = 0` (lọc `hasDeposit=false`). Mẫu gợi ý không cọc bù rủi ro bằng trả trước đầu kỳ + quyền chấm dứt khi chậm trả quá N ngày | Application |
| CT-BR-28 | Một phòng nhiều người ở, **một người đứng tên**. Mỗi người ở không đứng tên khai **quan hệ với người đứng tên** (`contract_occupants.relationship_type`) theo danh mục "quan hệ với chủ hộ" TT 55/2021/TT-BCA Điều 6 (sửa bởi TT 66/2023/TT-BCA); "Khác" phải ghi rõ (`relationship`). **Đổi 09/10/2026**: chưa khai quan hệ → cảnh báo `RELATIONSHIP_REQUIRED` kèm tên người, không chặn | Application `ContractPaperWarnings` + Domain `OccupantRelationshipRules` |
| CT-BR-29 | Quan hệ phải hợp lý: giới tính khớp nhãn (vợ/mẹ/con dâu… nữ; chồng/cha/con rể… nam; giới tính "Khác" bỏ qua); cha mẹ đẻ, ông bà, cụ lớn tuổi hơn; con đẻ, cháu nội ngoại, chắt nhỏ tuổi hơn; vợ chồng tối đa 1 người cùng thời gian, cả hai đủ tuổi kết hôn (nam 20, nữ 18 — Luật HN&GĐ 2014 Điều 8). **Đổi 09/10/2026: chỉ cảnh báo** (`RELATIONSHIP_GENDER_MISMATCH`, `RELATIONSHIP_AGE_MISMATCH`…) trên HĐ nháp / đang ở, không chặn tạo / kích hoạt / thêm người | Domain (hàm thuần) → cảnh báo |
| CT-BR-30 | Người ở **chưa thành niên** (< 18 tuổi tại ngày vào ở) phải có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (`guardian_consent`, Luật Cư trú 2020 Điều 28), trừ khi người đứng tên là cha/mẹ/người giám hộ (con đẻ, con nuôi, người được giám hộ). **Đổi 09/10/2026: chỉ cảnh báo** `GUARDIAN_CONSENT_REQUIRED` | Domain → cảnh báo |
| CT-BR-31 | Một người **không ở 2 phòng cùng lúc**: thêm người ở vào HĐ đang hiệu lực / kích hoạt HĐ bị chặn nếu người đó đang ở HĐ khác (Active/Liquidating/Ended) có khoảng ở giao nhau; HĐ đang thanh lý tính tới ngày trả phòng; chuyển đi ngày X – vào ở ngày X hợp lệ | Application → 409 `OCCUPANT_LIVES_ELSEWHERE` |
| CT-BR-32 | Tiền cọc ≤ 12 tháng tiền thuê (chặn nhập thừa số 0) → 400 `DEPOSIT_TOO_HIGH` | Application |
| CT-BR-33 | Phòng / khu còn **người thuê trong ngày trả phòng** (HĐ `Ended` có `actual_end_date ≥ hôm nay`) ⇒ không ngừng dùng phòng / khu, không bắt đầu bảo trì — khớp trạng thái "Đang thuê" | Application |
| CT-BR-34 | Hủy thanh lý: người ở chưa ghi chuyển đi trở lại "đang ở" ⇒ kiểm CT-BR-31 (không đang ở phòng khác) | Application → 409 `OCCUPANT_LIVES_ELSEWHERE` |
| CT-BR-35 | Dời ngày bắt đầu HĐ nháp về sau ⇒ xe đã đăng ký giữ dời `registered_from` theo (không giữ xe trước ngày bắt đầu HĐ) | Domain |
| CT-BR-36 ✅ | **Chủ hộ** (`household_head_renter_id`): chọn trong số người ở, mặc định = người đứng tên. Quan hệ của người ở (CT-BR-28..30) khai so với **chủ hộ**; chủ hộ không khai quan hệ. Chủ hộ không phải người ở → 400 `HOUSEHOLD_HEAD_NOT_OCCUPANT`. Chỉ đặt ở bản nháp | Application + Domain |
| CT-BR-37 ✅ | Thêm người ở với ngày vào **sau `end_date`** khi HĐ đang hiệu lực (quá hạn / sắp hết hạn) → 422 `CONTRACT_EXPIRED_EXTEND_FIRST` — gia hạn (phụ lục) trước | Domain |
| CT-BR-38 ✅ | Sửa nháp làm chủ xe (`contract_vehicles.renter_id`) không còn là người đứng tên / người ở → 422 `VEHICLE_OWNER_NOT_IN_CONTRACT`; kết thúc đăng ký xe đó trước | Domain |
| CT-BR-39 ✅ | **Cảnh báo mềm** (không chặn) trả trong `warnings` của tạo HĐ / đăng ký xe / chi tiết HĐ: `DEPOSIT_ABOVE_THREE_MONTHS` (cọc > 3 tháng tiền thuê ban đầu), `PLATE_FORMAT_UNUSUAL` (biển số sau chuẩn hóa không khớp `^[0-9]{2}[A-Z]{1,2}[0-9]?[0-9]{4,5}$`) | Domain `ContractWarnings` |
| CT-BR-40 ✅ | Ghi chú nội bộ (`note`) sửa được ở mọi trạng thái trừ `Cancelled` qua `PUT /contracts/{id}/note`; nội dung đã ký (điều khoản, giá, trường tùy biến) chỉ đổi qua phụ lục | Domain |
| CT-BR-41 ✅ | **Trả phòng bất chợt / bỏ đi không báo** (`Abandoned`): `actual_end_date` được lùi về quá khứ (ngày phát hiện hoặc ngày cuối còn ở, ≥ `start_date` và ≥ ngày vào ở muộn nhất); ghi chú bắt buộc (ngày phát hiện, đồ để lại, người chứng kiến) → 422 `ABANDONED_NOTE_REQUIRED`. Cảnh báo `LESSEE_ABANDONED` nhắc: lập biên bản kiểm kê đồ để lại có người làm chứng, chốt chỉ số công tơ ngày phát hiện (M06 `Final`), khai báo xóa tạm trú (LEG-04), xử lý cọc theo điều khoản (ghi "Đã hoàn trả cọc" với số tiền thực trả, có thể 0 — M08 PM-BR-33), nợ không đòi được → bỏ nợ (PM-BR-16). Phiếu kỳ sau `actual_end_date` phải hủy trước (CT-BR-11) | Domain + Application |
| CT-BR-42 ✅ | **HĐ không thời hạn** (`end_date` NULL — Luật Nhà ở 2023 Điều 171, BLDS Điều 474): không gia hạn (422 `CANNOT_EXTEND_INDEFINITE`), không vào danh sách sắp hết hạn / quá hạn, không chọn lý do `Expired`. Bên cho thuê chấm dứt bằng căn cứ `IndefiniteTermNotice` (chỉ cho HĐ không thời hạn → 422 `INDEFINITE_GROUND_ONLY`): được hẹn ngày trả phòng tới **90 ngày** sau ngày bắt đầu thanh lý (thay mức 60 ngày của CT-BR-11); báo trước < 90 ngày → cảnh báo `LESSOR_TERMINATION_SHORT_NOTICE`. Bên thuê chấm dứt: như CT-BR-21 (báo trước `notice_days`) | Domain |
| CT-BR-43 ✅ | Bản in (CT-UC-15): ngoài thỏa thuận lúc ký, in cả khoản gắn **sau khi ký** dù hiệu lực từ đúng ngày bắt đầu (trước đây bị bỏ sót). Người không có quyền dữ liệu nhạy cảm (ID-BR-22) nhận bản in có số giấy tờ ở dạng che | Application |
| CT-BR-44 ✅ | **Người đứng tên rời đi khi còn người ở**: người đứng tên là người ở của HĐ và đã chuyển đi (`move_out_date ≤ hôm nay`) trong khi còn người ở khác (`move_out_date` trống hoặc > hôm nay) → HĐ **vẫn hiệu lực**, phòng vẫn "Đang thuê"; HĐ có cờ `RepresentativeMovedOut` + cảnh báo `REPRESENTATIVE_MOVED_OUT` "Phòng còn người ở nhưng người ký hợp đồng đã rời đi — cần ký hợp đồng mới" cho tới khi ký lại (CT-UC-21) hoặc thanh lý. Phân biệt: **trả phòng 1 người** = ghi chuyển đi người đó; **trả cả phòng** = thanh lý (mọi người ra). HĐ đang hiệu lực mà không còn ai ở → cờ `NoOccupantLeft` + cảnh báo `NO_OCCUPANT_LEFT` "Không còn người ở — thanh lý?"; phòng chỉ về "Trống" khi thanh lý xong (PR-BR-02). Người đứng tên không phải người ở (VD bố ký cho con) → không cảnh báo. Cờ là **dẫn xuất** theo ngày (domain `Contract.Flags(today)`), trả trong `flags` của danh sách HĐ, chi tiết HĐ và `currentContract` của phòng | Domain + Application |
| CT-BR-45 ✅ | **Hết hạn mà vẫn ở** (`end_date < hôm nay`, HĐ `Active`): phòng **vẫn "Đang thuê"** (không bao giờ tự về Trống); cờ `ExpiredAwaitingDecision` + cảnh báo `CONTRACT_EXPIRED_DECISION_NEEDED` cho tới khi chủ trọ chọn CT-UC-22. Chọn "Ở tiếp, chưa ký lại" → lưu `holdover_since` (= hôm nay) + `holdover_note`, cờ đổi thành `Holdover` + cảnh báo `HOLDOVER_SIGN_ADDENDUM` "Đang ở tiếp từ dd/MM chưa ký lại — nên ký phụ lục gia hạn" (vẫn tính tiền theo điều khoản cũ, kỳ thu tiếp tục — CT-BR-03; lịch kỳ thu mặc định hiện tới 12 tháng sau hôm nay); nhắc lặp mỗi 30 ngày thuộc nhắc việc P2. Gia hạn → xóa `holdover_since`. Lý do: BLDS 2015 **không** tự gia hạn HĐ hết hạn; người thuê vẫn ở và trả tiền là giao dịch mới xác lập bằng hành vi — khuyến nghị ký phụ lục (HĐ thuê nhà phải lập văn bản — Điều 163). Ràng buộc: chỉ áp cho HĐ **đã ký** (đã kích hoạt); thêm người ở / đổi giá sau `end_date` vẫn yêu cầu gia hạn trước (CT-BR-37). Phòng chỉ về "Trống" khi chủ trọ thu lại phòng (thanh lý xong) | Domain + Application |
| CT-BR-46 ✅ | **HĐ trong phần mềm là hồ sơ thuê để tính tiền** (chốt 09/10/2026) — bản có giá trị pháp lý là bản giấy. **Bắt buộc** chỉ phần thu tiền: phòng, người đứng tên, ngày bắt đầu, giá thuê > 0, kỳ thu, khoản thu, chỉ số bàn giao. Phần giấy tờ / pháp lý chỉ **cảnh báo** (`warnings[]` khi tạo nháp và trong chi tiết HĐ nháp / đang ở / đang thanh lý): `LESSOR_INFO_INCOMPLETE`, `REPRESENTATIVE_PHONE_MISSING`, `REPRESENTATIVE_UNDERAGE`, quan hệ người ở (CT-BR-28..30, mỗi dòng có tên người), `SIGNED_DOCUMENT_MISSING` (cờ `MissingSignedDocument` khi `has_signed_document = false`, CT-UC-23). Vẫn giữ mẫu HĐ + in HĐ trong app (CT-UC-15/16) | Application `ContractPaperWarnings` + Domain |
| CT-BR-47 ✅ | **Tính tiền kỳ có chuyển phòng** (CT-UC-12): tiền phòng, dịch vụ không đổi (theo HĐ). Điện nước kỳ chứa D gồm **2 phòng**: công tơ phòng cũ từ chỉ số đầu kỳ tới **chỉ số cuối tại D** + công tơ phòng mới từ **chỉ số nhận phòng tại D** tới cuối kỳ (cùng cơ chế cộng nhiều công tơ của MT-BR-15). Phiếu thuộc phòng hiện tại của HĐ lúc lập. Nháp đã lập ⇒ "Cần tính lại" (BL-BR-20) | Domain (M07) |

### 3.3 Vòng đời

```mermaid
stateDiagram-v2
  [*] --> Draft : Create
  Draft --> Cancelled : Cancel(reason)
  Draft --> Active : Activate (bàn giao)
  Active --> Liquidating : StartLiquidation(actualEndDate)
  Liquidating --> Active : CancelLiquidation (chưa chốt phiếu quyết toán)
  Liquidating --> Ended : CompleteLiquidation
  Ended --> [*]
  Cancelled --> [*]
```

| Từ → Đến | Lệnh | Điều kiện | Tác động phụ (cùng transaction) |
|----------|------|-----------|--------------------------------|
| Draft → Active | `ActivateContract` | CT-BR-01, 02 | Ghi chỉ số nhận phòng (M06) ✅ |
| Draft → Cancelled | `CancelContract` | — | |
| Active → Liquidating | `StartLiquidation` | CT-BR-11 | Đặt `actual_end_date` (EXCLUDE kiểm lại) |
| Liquidating → Active | `CancelLiquidation` | Phiếu `Final` chưa Finalized (draft bị xóa) | `actual_end_date = NULL` (EXCLUDE kiểm lại: nếu phòng đã có HĐ mới kích hoạt sau ngày trả → 409 `ROOM_PERIOD_OVERLAP`); hủy (void) các chỉ số `Final` chưa khóa của HĐ |
| Liquidating → Ended | `CompleteLiquidation` | CT-BR-12 | Đóng người ở và xe; (nếu chọn) phiếu thu "Đã thu toàn bộ" / bỏ nợ (M08) |

## 4. Dữ liệu

**`contracts`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | UNIQUE (organization_id, id); UNIQUE (organization_id, property_id, id) (cho FK của `contract_fees`, `invoices`) |
| property_id | uuid | N | = khu của phòng (FK composite `(organization_id, property_id, room_id)` → rooms) |
| room_id | uuid | N | |
| contract_no | varchar(30) | N | CT-BR-15 |
| status | varchar(16) | N | `Draft`,`Active`,`Liquidating`,`Ended`,`Cancelled` |
| representative_renter_id | uuid | N | FK composite renters |
| signed_date | date | Y | |
| start_date | date | N | ngày bàn giao (HĐ nhập từ sổ cũ: ngày bắt đầu thật) |
| billing_start_date | date | N | K5 — "Tính tiền từ ngày" (mặc định = `start_date`); CHECK `start_date ≤ billing_start_date ≤ end_date` |
| end_date | date | Y | thỏa thuận |
| actual_end_date | date | Y | ngày cuối tính tiền (bao gồm) |
| notice_given_date / planned_move_out_date | date | Y | CT-UC-07 |
| notice_days | smallint | N | snapshot |
| deposit_amount | numeric(18,0) | N | thỏa thuận, ≥ 0 |
| terms_text | text | Y | điều khoản quyền/nghĩa vụ (LEG-01) |
| note | text | Y | |
| previous_contract_id | uuid | Y | chuyển phòng / tái ký |
| holdover_since | date | Y | CT-BR-45 — ngày chủ trọ chọn "ở tiếp, chưa ký lại"; xóa khi gia hạn |
| holdover_note | varchar(500) | Y | CT-BR-45 |
| activated_at / ended_at / cancelled_at | timestamptz | Y | |
| cancel_reason | varchar(500) | Y | |
| **Bổ sung theo Luật Nhà ở 2023 Điều 163–164, 172** | | | |
| effective_date | date | Y | CT-BR-20; NOT NULL khi Active |
| signed_place | varchar(200) | Y | nơi ký |
| copies_count | smallint | N | số bản, mặc định 2 (mỗi bên 1) |
| payment_methods | varchar(20)[] | N | mặc định `{Cash,BankTransfer}` |
| deposit_terms | text | Y | điều kiện hoàn / mất cọc (in trên HĐ) |
| deposit_status | varchar(12) | N | `Holding` / `Refunded` / `Transferred` (M08 PM-BR-32) |
| deposit_refunded_on / deposit_refunded_amount / deposit_note | date / numeric(18,0) / varchar(500) | Y | "Đã hoàn trả cọc" (PM-BR-33) |
| signing_snapshot | jsonb | Y | CT-BR-19 — bản chụp lúc ký: bên cho thuê, bên thuê (người đứng tên), phòng (mã, tầng, diện tích, số người loại phòng), ngân hàng, tên & địa chỉ khu (số giấy tờ lưu dạng mã hóa). NULL khi chưa khai cả thông tin chủ trọ lẫn bên cho thuê riêng của khu (PR-BR-17, CT-BR-46) |
| utility_price_snapshot | jsonb | Y | CT-BR-19 ✅ — chụp khi kích hoạt: `[{feeTypeId, name, group, fixedBasis, unit, quantity, unitPrice, isOverride}]` (điện nước theo công tơ của khu + khoản gắn HĐ đang áp tại ngày bắt đầu; `unitPrice` null = lúc ký chưa có giá). API `utilityPrices`; bản in dùng làm thỏa thuận gốc |
| house_rules_snapshot | text | Y | CT-BR-19 |
| has_signed_document | bool | N | CT-UC-23 — mặc định false (HĐ import / tạo mới đều "Thiếu tài liệu") |
| signed_document_note | varchar(300) | Y | CT-UC-23 — nơi cất bản giấy / ghi chú |
| termination_reason | varchar(24) | Y | CT-BR-21; NOT NULL khi Ended |
| termination_ground | varchar(32) | Y | CT-BR-21 |
| termination_note | varchar(1000) | Y | |
| **Bổ sung — văn bản theo mẫu (CT-BR-25..27)** | | | |
| template_id | uuid | Y | FK composite `(organization_id, template_id)` → `contract_templates`, RESTRICT |
| contract_type | varchar(24) | N | `RoomRental` (mặc định cho dữ liệu cũ) / `WholeHouseRental` |
| title | varchar(200) | Y | tiêu đề in trên HĐ; NULL (dữ liệu cũ) ⇒ tiêu đề mặc định theo loại |
| clauses | jsonb | Y | `[{heading, body}]` — chép từ mẫu, ghi đè được khi nháp |
| custom_field_definitions | jsonb | Y | bản chụp định nghĩa trường của mẫu lúc lưu nháp |
| custom_field_values | jsonb | Y | `{key: value}` đã chuẩn hóa kiểu (số, chuỗi, bool, `yyyy-MM-dd`) |
| household_head_renter_id | uuid | Y | CT-BR-36 — FK composite `(organization_id, household_head_renter_id)` → renters; NULL ⇒ người đứng tên là chủ hộ |
| audit, xmin | | | |

Ràng buộc:
- CHECK `end_date IS NULL OR end_date > start_date`; CHECK `actual_end_date IS NULL OR actual_end_date >= start_date`.
- CHECK `status NOT IN ('Liquidating','Ended') OR actual_end_date IS NOT NULL`.
- CHECK `deposit_amount >= 0`, `copies_count BETWEEN 1 AND 10`.
- CHECK `effective_date IS NULL OR signed_date IS NULL OR effective_date >= signed_date`.
- CHECK `status NOT IN ('Active','Liquidating','Ended') OR (effective_date IS NOT NULL AND signing_snapshot IS NOT NULL)`.
- CHECK `status <> 'Ended' OR termination_reason IS NOT NULL`.
- UNIQUE `(organization_id, contract_no)`.
- **EXCLUDE** `USING gist (room_id WITH =, daterange(start_date, actual_end_date, '[]') WITH &&) WHERE (status IN ('Active','Liquidating','Ended'))`
  (`daterange(x, NULL)` = vô hạn phải → đúng nghĩa "đang thuê").
- INDEX `(organization_id, property_id, status)`, `(organization_id, representative_renter_id)`, `(organization_id, end_date) WHERE status='Active'`.

**`contract_rent_terms`**: id, organization_id, contract_id, effective_from date, monthly_rent numeric(18,0) > 0, addendum_no varchar(30) null, note, created_at/by.
UNIQUE `(contract_id, effective_from)`.

**`contract_fees`**: id, organization_id, contract_id, property_id, fee_type_id, quantity numeric(12,2), unit_price_override numeric(18,2) null,
effective_from date, effective_to date null (bao gồm), created_at/by.
- FK composite `(organization_id, property_id, fee_type_id)` → fee_types; `(organization_id, property_id, contract_id)` → contracts (cần UNIQUE tương ứng).
- CHECK `effective_to IS NULL OR effective_to >= effective_from`; CHECK `quantity > 0`.
- EXCLUDE `USING gist (contract_id WITH =, fee_type_id WITH =, daterange(effective_from, effective_to, '[]') WITH &&)`.

**`contract_occupants`**: id, organization_id, contract_id, renter_id, move_in_date, move_out_date null, expected_end_date null,
relationship varchar(50) null (ghi chú quan hệ, bắt buộc khi loại = `Other`), **relationship_type varchar(24) null** (CT-BR-28; NULL với
người đứng tên và dữ liệu cũ), **guardian_consent boolean not null default false** (CT-BR-30), note, audit, xmin.
- EXCLUDE `USING gist (contract_id WITH =, renter_id WITH =, daterange(move_in_date, move_out_date, '[]') WITH &&)`.

**`contract_liquidations`**: id, organization_id, contract_id UNIQUE, actual_end_date, final_invoice_id, deposit_received_total,
deposit_deducted_total, deposit_refunded_total, deposit_forfeited_total, note, completed_at, completed_by.
(Ảnh chụp số liệu tại thời điểm hoàn tất — để in biên bản, không dùng để tính lại.)

**`contract_assets`** (tài sản bàn giao — CT-UC-13): id, organization_id, contract_id, name varchar(200), quantity smallint > 0,
condition_at_handover varchar(500), condition_at_return varchar(500) null, value_estimate numeric(18,0) null,
ảnh (M09 owner_type `ContractAsset`), note, audit, xmin.
- Sửa `condition_at_handover` chỉ khi Draft; `condition_at_return` chỉ khi Liquidating.

**`contract_vehicles`** (phương tiện của phòng — CT-UC-14; danh sách xe hiển thị theo phòng): id, organization_id, contract_id, renter_id null (chủ xe),
vehicle_type varchar(16) (`Motorbike`,`Bicycle`,`ElectricBike`,`Car`), plate_number varchar(20) null (chuẩn hóa: bỏ khoảng trắng, chữ hoa),
brand_color varchar(100) null, registered_from date, registered_to date null, note, audit, xmin.
- UNIQUE `(organization_id, plate_number) WHERE registered_to IS NULL AND plate_number IS NOT NULL` — 1 biển số chỉ đăng ký giữ ở 1 nơi trong tổ chức.

**`contract_number_sequences`**: organization_id, year, last_value — cấp số bằng `UPDATE … RETURNING` trong transaction.

**`contract_templates`** (CT-BR-25): id, organization_id, name varchar(100), contract_type varchar(24), title varchar(200),
clauses jsonb not null (`[]`), field_definitions jsonb not null (`[]`), no_deposit boolean not null default false,
archived_at timestamptz null, audit, xmin.
- UNIQUE `(organization_id, name)` (`ux_contract_templates_name` → 409 `CONTRACT_TEMPLATE_NAME_TAKEN`); UNIQUE `(organization_id, id)` cho FK composite.
- Không xóa vật lý (HĐ tham chiếu) — chỉ ngừng dùng.
- Mẫu gợi ý (thuê trọ, thuê trọ không cọc, thuê nhà nguyên căn) nằm trong code, không seed vào DB.

### Dẫn xuất (không lưu)
- `IsOverdue` = Active ∧ end_date < hôm nay. `IsExpiringSoon` = Active ∧ end_date ∈ [hôm nay, +30].
- `CurrentRent(date)` = rent term có `effective_from` lớn nhất ≤ date.
- `ActiveOccupants(date)` = occupants có `move_in ≤ date ≤ COALESCE(move_out, ∞)`.
- Ngày ra hiệu lực của người ở = `COALESCE(move_out_date, contracts.actual_end_date)` — HĐ đang thanh lý chỉ ghi `move_out_date` khi hoàn tất (R-80).

## 5. Domain model

```
Contract (aggregate root: RentTerms, Fees, Occupants là entity con)
  + CreateDraft(room, representative, startDate, endDate?, rent, deposit, BillingSettings, fees[], occupants[], terms)
  + UpdateDraft(...) / Cancel(reason)
  + Activate(now, today)                                   // CT-BR-02 phần nội tại
  + AddOccupant(renterId, moveIn, expectedEnd?) / EndOccupancy(occupantId, moveOut)
  + ChangeRent(effectiveFrom, amount, firstOpenPeriodStart) // CT-BR-05
  + ChangeFee(feeTypeId, qty, priceOverride?, effectiveFrom, firstOpenPeriodStart) // đóng khoảng cũ = effectiveFrom - 1
  + RemoveFee(feeTypeId, effectiveFrom, firstOpenPeriodStart)
  + Extend(newEndDate)
  + GiveNotice(noticeDate, plannedMoveOut) : Warnings
  + StartLiquidation(actualEndDate, today) / CancelLiquidation() / CompleteLiquidation(LiquidationSnapshot, now)
  + BillingSchedule : BillingPeriodCalculator(anchor, start, actualEnd)  // C-05
Domain events (P2 — chưa dùng; P1 xử lý đồng bộ trong cùng lệnh): ContractActivated, OccupancyStarted, OccupancyEnded, ContractRentChanged, ContractFeesChanged, ContractEnded
```
`firstOpenPeriodStart` do Application lấy từ M07 (`IInvoiceLockReader`) → domain thuần, test được.

```
ContractTemplate (aggregate riêng)
  + Create(name, type, title, clausesJson, fieldDefinitionsJson, noDeposit) / Update(...) / Archive(now) / Restore()
Contract.ApplyDraft(..., ContractDocument)   // TemplateId, ContractType, Title, Clauses, CustomFieldDefinitions, CustomFieldValues
OccupantRelationshipRules.Check(representative, [(OccupantInput, PersonFacts)]) : [OccupantRuleViolation(index, field, error)]
  // CT-BR-28..30 — hàm thuần, Application dùng để sinh cảnh báo (ContractPaperWarnings), không chặn
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `CreateContractDraftCommand` | Cmd | `ROOM_ARCHIVED`, `FEE_NOT_IN_PROPERTY`, `CONTRACT_NO_TAKEN` |
| `UpdateContractDraftCommand` / `CancelContractCommand` | Cmd | `CONTRACT_NOT_DRAFT` |
| `ActivateContractCommand` (Idempotency-Key) | Cmd | `ROOM_UNAVAILABLE`, `ROOM_PERIOD_OVERLAP` (từ EXCLUDE 23P01), `HANDOVER_READING_REQUIRED`, `NO_OCCUPANT` (SĐT, tuổi người đứng tên, bên cho thuê chỉ cảnh báo — CT-BR-46) |
| `AddOccupantCommand` / `EndOccupancyCommand` | Cmd | `OCCUPANCY_OVERLAP`, `DATE_OUTSIDE_CONTRACT` |
| `ChangeRentCommand` / `ExtendContractCommand` / `ChangeDepositAgreementCommand` | Cmd | `NOT_PERIOD_START`, `PERIOD_ALREADY_BILLED` |
| `ChangeContractFeeCommand` / `RemoveContractFeeCommand` | Cmd | như trên + `FEE_ARCHIVED` |
| `GiveNoticeCommand` | Cmd | trả `warnings` |
| `StartLiquidationCommand` | Cmd | `INVALID_END_DATE`, `ROOM_PERIOD_OVERLAP` |
| `GetLiquidationPreviewQuery` | Qry | tổng hợp: phiếu chưa thanh toán, số dư cọc, credit, chỉ số cuối còn thiếu, phiếu quyết toán dự kiến |
| `CancelLiquidationCommand` | Cmd | `FINAL_INVOICE_FINALIZED` |
| `CompleteLiquidationCommand` | Cmd | `LIQUIDATION_NOT_SETTLED` (kèm danh sách điều kiện chưa đạt) |
| `TransferRoomCommand` (10/10/2026) | Cmd | `{ toRoomId, date, oldRoomReadings[{meterId, value}], newRoomReadings[{meterId, value?}], monthlyRent?, note? }` — CT-BR-14; lỗi `ROOM_TRANSFER_SAME_ROOM`, `ROOM_TRANSFER_OTHER_PROPERTY`, `ROOM_TRANSFER_ROOM_UNAVAILABLE`, `ROOM_TRANSFER_INVALID_DATE`, `ROOM_TRANSFER_ALREADY_BILLED`, `CONTRACT_NOT_ACTIVE`, `FINAL_READING_REQUIRED` |
| `Add/Update/RemoveContractAssetCommand`, `RecordAssetReturnConditionCommand` | Cmd | `CONTRACT_NOT_DRAFT`, `CONTRACT_NOT_LIQUIDATING` |
| `RegisterContractVehicleCommand` / `EndContractVehicleCommand` | Cmd | `PLATE_ALREADY_REGISTERED`; trả `warnings` (CT-BR-22) |
| `SetSignedDocumentCommand` | Cmd | `CONTRACT_NOT_EDITABLE` (đã hủy) — CT-UC-23 |
| `ApplyListedRentCommand` | Cmd | `LISTED_RENT_REQUIRED`, `CONTRACT_NOT_ACTIVE` — CT-UC-05 |
| `ListContractsQuery` / `GetContractQuery` | Qry | filter: propertyIds, roomId, renterId, status, expiringWithinDays, overdue, **hasDeposit** |
| (`Create/UpdateContractDraftCommand` bổ sung) | | `CONTRACT_TEMPLATE_NOT_FOUND`, `CONTRACT_TEMPLATE_ARCHIVED`, `DEPOSIT_NOT_ALLOWED`; 400 theo `contract.customFields.<key>`; 400 theo `contract.occupants[i].relationshipType` / `.guardianConsent` |
| (`AddOccupantCommand` / `ActivateContractCommand` bổ sung) | | `RELATIONSHIP_REQUIRED`, `RELATIONSHIP_NOTE_REQUIRED`, `RELATIONSHIP_GENDER_MISMATCH`, `RELATIONSHIP_AGE_MISMATCH`, `SPOUSE_UNDER_MARRIAGE_AGE`, `MULTIPLE_SPOUSES`, `GUARDIAN_CONSENT_REQUIRED` (400 khi nhập, 422 kèm tên người khi kích hoạt), 409 `OCCUPANT_LIVES_ELSEWHERE` |
| `List/Get/Create/Update ContractTemplate`, `ChangeContractTemplateStateCommand`, `GetContractTemplatePresetsQuery` | Cmd/Qry | `CONTRACT_TEMPLATE_NAME_TAKEN`, `CONTRACT_TEMPLATE_NOT_FOUND`, `CONTRACT_TEMPLATE_ALREADY_ARCHIVED`, `CONTRACT_TEMPLATE_NOT_ARCHIVED` |

## 7. API

| Method | Route | Mã lỗi |
|--------|-------|--------|
| GET | `/contracts?propertyIds=&status=&roomId=&renterId=&expiringWithinDays=30&overdue=true` | |
| POST | `/contracts` | 409 `CONTRACT_NO_TAKEN`, 422 |
| GET / PUT | `/contracts/{id}` (PUT chỉ khi Draft; nội dung đã ký đổi qua phụ lục) | 409 `CONCURRENCY_CONFLICT`, 422 `CONTRACT_NOT_DRAFT` |
| PUT | `/contracts/{id}/note` `{ note }` — ghi chú nội bộ, mọi trạng thái trừ đã hủy (CT-BR-40) | 422 `CONTRACT_NOT_EDITABLE` |
| POST | `/contracts/{id}/cancel` | |
| POST | `/contracts/{id}/activate` body tùy chọn `{ "handoverReadings": [...] }` | 409 `ROOM_PERIOD_OVERLAP`, `OCCUPANT_LIVES_ELSEWHERE`, 422 |
| POST | `/contracts/{id}/occupants`; POST `/contracts/{id}/occupants/{occId}/end` | 409 `OCCUPANCY_OVERLAP`, `OCCUPANT_LIVES_ELSEWHERE` |
| POST | `/contracts/{id}/rent-terms` | 422 `NOT_PERIOD_START`, `PERIOD_ALREADY_BILLED` |
| POST | `/contracts/{id}/extend` | |
| POST | `/contracts/{id}/holdover` `{ note? }` — ở tiếp chưa ký lại (CT-UC-22) | 422 `CONTRACT_NOT_EXPIRED`, 409 `HOLDOVER_ALREADY` |
| POST | `/contracts/{id}/re-sign` `{ handoverDate, representativeRenterId, endDate? }` — ký lại cho người còn ở (CT-UC-21) → 201 HĐ nháp mới | 422 `RESIGN_NO_OCCUPANT_LEFT`, `RESIGN_REPRESENTATIVE_NOT_OCCUPANT` |
| PUT | `/contracts/{id}/fees/{feeTypeId}` `{quantity, unitPriceOverride, effectiveFrom}`; DELETE `…?effectiveFrom=` | |
| POST | `/contracts/{id}/notice` | |
| POST | `/contracts/{id}/liquidation/start` · `/cancel` · `/complete`; GET `/contracts/{id}/liquidation/preview` | 422 `LIQUIDATION_NOT_SETTLED` |
| GET | `/contracts/{id}/billing-periods?from=&to=` | Danh sách kỳ (C-05) để UI hiển thị/chọn `effectiveFrom` |
| GET / POST | `/contracts/{id}/assets`; PUT / DELETE `/contracts/{id}/assets/{assetId}`; POST `/contracts/{id}/assets/{assetId}/return` | 422 `CONTRACT_NOT_DRAFT` |
| GET / POST | `/contracts/{id}/vehicles`; POST `/contracts/{id}/vehicles/{vehicleId}/end` | 409 `PLATE_ALREADY_REGISTERED` |
| GET | `/contracts/{id}/print?template=contract\|addendum\|handover` (P2) | File PDF/DOCX từ mẫu |
| GET | `/contracts?hasDeposit=false` | Nhóm HĐ không cọc (CT-UC-18) |
| GET | `/contract-templates?type=&includeArchived=`; GET `/contract-templates/presets`; GET `/contract-templates/{id}` | |
| POST | `/contract-templates` (Idempotency-Key) | 409 `CONTRACT_TEMPLATE_NAME_TAKEN` |
| PUT | `/contract-templates/{id}` (`version`) | 409 `CONCURRENCY_CONFLICT` |
| POST | `/contract-templates/{id}/archive` · `/restore` | 409 |

**Ví dụ — tạo HĐ nháp**
```json
POST /api/v1/contracts
{
  "roomId": "…", "representativeRenterId": "…",
  "startDate": "2026-10-05", "endDate": "2027-10-04", "signedDate": "2026-10-03",
  "monthlyRent": 3500000, "depositAmount": 3500000,
  "billing": { "anchorDay": 5, "chargeMode": "Prepaid", "prorationMode": "Daily", "paymentDueDays": 5 },
  "fees": [
    { "feeTypeId": "…điện", "quantity": 1 },
    { "feeTypeId": "…nước", "quantity": 1 },
    { "feeTypeId": "…giữ xe", "quantity": 2 },
    { "feeTypeId": "…wifi", "quantity": 1, "unitPriceOverride": 50000 }
  ],
  "occupants": [
    { "renterId": "…người đứng tên", "moveInDate": "2026-10-05" },
    { "renterId": "…", "relationshipType": "Wife" },
    { "renterId": "…", "relationshipType": "Child" },
    { "renterId": "…", "relationshipType": "NephewNiece", "relationship": "Cháu gọi bằng cậu", "guardianConsent": true }
  ],
  "templateId": "…mẫu thuê trọ",
  "customFields": { "overnight_guest_policy": "Phải báo và được chủ nhà đồng ý", "gate_closing_time": "23:00", "pets_allowed": false },
  "termsText": "…"
}
```

**Ví dụ — kích hoạt**
```json
POST /api/v1/contracts/{id}/activate
Idempotency-Key: 5b0d…
{
  "handoverReadings": [ { "meterId": "…", "value": 1250 }, { "meterId": "…", "value": 310.5 } ],
  "depositReceived": { "amount": 3500000, "method": "BankTransfer", "receivedAt": "2026-10-05" }
}
```

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| startDate | bắt buộc; ≥ hôm nay − 365 (cho nhập HĐ cũ khi chuyển đổi dữ liệu) |
| endDate | > startDate; ≤ startDate + 10 năm |
| monthlyRent | số nguyên, 1 ≤ x ≤ 1.000.000.000 |
| depositAmount | số nguyên, 0 ≤ x ≤ 12 × monthlyRent (`DEPOSIT_TOO_HIGH`); > 3 tháng → cảnh báo `DEPOSIT_ABOVE_THREE_MONTHS` (CT-BR-39) |
| billing.anchorDay | 1–31; mặc định = ngày của startDate nếu không truyền và khu không có mặc định |
| fees[] | feeTypeId không trùng; thuộc khu; chưa archive; quantity theo CT-BR-07 |
| occupants[] | ≥ 1 khi kích hoạt; renterId không trùng; moveInDate ≥ startDate |
| contractNo | null hoặc `^[A-Za-z0-9\-/._]{1,30}$` |
| signedDate / effectiveDate | CT-BR-20: signedDate ≤ effectiveDate ≤ endDate; signedDate ≤ hôm nay |
| representative | < 18 tuổi tại signedDate ⇒ cảnh báo (CT-BR-18), không chặn |
| copiesCount | 1–10 |
| paymentMethods | ≥ 1 phần tử, thuộc `Cash`,`BankTransfer`,`EWallet` |
| depositTerms | ≤ 5.000 ký tự |
| terminationReason / terminationGround | CT-BR-21 — ground tùy chọn |
| asset | name 1–200, quantity 1–100, compensationValue 0 … 1.000.000.000 |
| vehicle.plateNumber | chuẩn hóa (bỏ khoảng trắng, dấu, chữ hoa); sai định dạng biển số VN → cảnh báo `PLATE_FORMAT_UNUSUAL`, không chặn (CT-BR-39) |
| householdHeadRenterId | null hoặc một trong `occupants[].renterId` (CT-BR-36) |
| termsText | ≤ 50.000 ký tự |
| templateId | thuộc tổ chức; chưa ngừng dùng (trừ khi là mẫu đang gắn với bản nháp) |
| title | ≤ 200; null ⇒ tiêu đề mẫu / theo loại |
| clauses[] | ≤ 30; heading 1–200, body 1–10.000 |
| customFields | ≤ 50 key; khớp định nghĩa của mẫu: đủ trường bắt buộc, đúng kiểu (Text ≤ 500, LongText ≤ 5.000, Number \|x\| ≤ 10¹², Money nguyên 0…1 tỷ, Date `yyyy-MM-dd`, Boolean, Select ∈ options), không key lạ; không dùng mẫu ⇒ phải rỗng |
| depositAmount (mẫu không cọc) | null hoặc 0; > 0 → `DEPOSIT_NOT_ALLOWED` |
| occupants[].relationshipType | thiếu / không hợp lý (CT-BR-28..30) ⇒ **cảnh báo**; chủ hộ phải là người ở (vẫn chặn) |
| occupants[].guardianConsent | bắt buộc `true` khi < 18 tuổi tại ngày vào ở, trừ quan hệ `Child` / `AdoptedChild` / `Ward` |
| template.fields[] | ≤ 50; key `^[a-z][a-z0-9_]{0,39}$` không trùng; label 1–100; Select: 1–30 options không trùng, ≤ 100 ký tự; kiểu khác không có options; unit ≤ 20; hint ≤ 200 |
| handoverReadings[].value | ≥ chỉ số gần nhất của công tơ (M06) |

## 9. Phân quyền
P1: chủ trọ và phó quản lý toàn quyền nghiệp vụ (M01 §3.3); in hợp đồng có số giấy tờ đầy đủ: dữ liệu nhạy cảm (số giấy tờ đầy đủ) chỉ chủ trọ hoặc phó quản lý được chủ trọ cấp quyền (ID-BR-22) — người khác nhận bản che số. P3: phân quyền theo khu, quyền `Liquidate` riêng.

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Giải pháp |
|----------|-----------|
| 2 HĐ cùng phòng kích hoạt đồng thời | EXCLUDE constraint (CT-BR-01) — DB là chốt chặn cuối; map lỗi `23P01` → 409 `ROOM_PERIOD_OVERLAP` |
| Phòng bị đặt bảo trì đồng thời kích hoạt | Khóa `rooms` row `FOR UPDATE` (thứ tự khóa: room → contract) |
| Đổi giá thuê cho kỳ đã chốt phiếu | CT-BR-05 với `firstOpenPeriodStart` tính trong cùng transaction; lệnh chốt phiếu (M07) khóa hàng `contracts` `FOR SHARE`/`FOR UPDATE` ⇒ tuần tự hóa |
| Đổi ngày chốt sau khi có phiếu | M02 PR-BR-09 + M07 BL-BR-28 (K4 — kỳ chuyển tiếp, ✅ code 09/10/2026) |
| Kích hoạt bị retry (mạng) | Idempotency-Key + kiểm tra trạng thái (Active → trả kết quả cũ) |
| Thêm người ở đồng thời | Khóa `contracts` row khi thêm occupant |
| Hoàn tất thanh lý trong khi có thanh toán mới | Khóa contract + đọc số dư trong cùng transaction; M08 khi ghi thanh toán cũng khóa contract ⇒ tuần tự |
| HĐ `Ended` bị sửa | Domain chặn mọi lệnh trừ đính kèm file / ghi chú |
| Sửa / ngừng dùng mẫu khi đang có HĐ dùng mẫu | HĐ chép văn bản + định nghĩa trường vào chính nó ⇒ HĐ đã kích hoạt không đổi; nháp nhận nội dung mới ở lần sửa kế tiếp (giá trị cũ có thể không còn hợp lệ → 400 theo field) |
| Hồ sơ người thuê bị sửa (ngày sinh, giới tính) sau khi lập nháp | Chi tiết HĐ tính lại cảnh báo CT-BR-28..30 theo hồ sơ hiện tại (kèm tên người); không chặn kích hoạt |
| Hai lệnh song song thêm cùng một người vào 2 phòng khác nhau | ✅ Khóa logic theo người thuê (`pg_advisory_xact_lock`, thứ tự id tăng dần) trong cùng transaction trước khi kiểm CT-BR-31 — áp cho thêm người ở, kích hoạt, hủy thanh lý; test gửi song song |

## 11. Audit & bảo mật
Audit toàn bộ lệnh thay đổi. File HĐ (M09) có dữ liệu cá nhân → tải có audit.

## 12. Kế hoạch test
- Unit: chuyển trạng thái hợp lệ/không hợp lệ; `ChangeRent` không phải đầu kỳ / kỳ đã chốt; `ChangeFee` đóng khoảng cũ đúng; `CurrentRent(date)`; `ActiveOccupants(date)`.
- Integration: 2 HĐ chồng lấn cùng phòng → 409 (cả khi gửi song song bằng `Task.WhenAll`); HĐ mới bắt đầu = actual_end_date cũ + 1 → OK, = actual_end_date → 409;
  kích hoạt thiếu chỉ số bàn giao → 422; kích hoạt sinh bản ghi cư trú; thanh lý thiếu điều kiện → 422 liệt kê; thanh lý đủ → Ended, occupants đóng;
  **C-01**: HĐ của org A dùng fee type của org B → lỗi FK/422.
- Đã có (02–03/10/2026): HĐ theo mẫu chép văn bản + giá trị trường có kiểu; sửa mẫu không đổi HĐ đã ký; trường tùy biến sai → 400 theo key;
  mẫu ngừng dùng không tạo HĐ mới được nhưng nháp vẫn sửa được; mẫu không cọc ép cọc = 0 & lọc `hasDeposit`;
  gia đình (bố đứng tên, vợ, con) kích hoạt được & xuất Excel đúng quan hệ; quan hệ vô lý (con lớn tuổi hơn cha, "vợ" là nam, thiếu quan hệ) → 400 từng người;
  người < 18 tuổi cần đồng ý của người giám hộ; một người không ở 2 phòng, chuyển đi – vào ở cùng ngày hợp lệ; unit test `OccupantRelationshipRules`.
- E2E: F3, F5.

## 13. Phụ thuộc
- Dùng: M02 (room), M03 (renter), M04 (fee types), M06 (ghi chỉ số bàn giao/cuối), M08 (cọc), M07 (`IInvoiceLockReader`, tạo phiếu quyết toán).
- Phát event cho: M03 (cư trú), M07 (stale draft khi đổi giá/khoản thu), M02 (status dẫn xuất — query).
- Để tránh vòng phụ thuộc M05 ↔ M07: M05 chỉ phụ thuộc **interface** đặt trong `Application/Contracts/Abstractions`; M07 hiện thực.

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| CT-01 | Domain Contract + RentTerm/Fee/Occupant + events + unit test | 2.5d |
| CT-02 | EF + EXCLUDE constraints (btree_gist) + sequences + migration | 1d |
| CT-03 | Create/Update/Cancel draft + queries | 1.5d |
| CT-04 | Activate (tích hợp M06/M08/M03) | 1.5d |
| CT-05 | Occupants, rent terms, fees, extend, notice | 1.5d |
| CT-06 | Liquidation start/preview/cancel/complete | 2d |
| CT-07 | Transfer room | 0.5d |
| CT-08 | Tests (gồm concurrency) | 2d |
| CT-09 | Snapshot bên cho thuê / đơn giá / nội quy khi kích hoạt + kiểm tra tuổi người đại diện | 1d |
| CT-10 | Tài sản bàn giao + xe đăng ký + lý do chấm dứt | 1.5d |
| CT-11 ✅ | Mẫu hợp đồng (CRUD, mẫu gợi ý, trường tùy biến, chép vào HĐ) | 1.5d |
| CT-12 ✅ | Hợp đồng không cọc + lọc `hasDeposit` | 0.5d |
| CT-13 ✅ | Quan hệ người ở với người đứng tên, đồng ý người giám hộ, chặn ở 2 phòng | 1.5d |
| CT-14 ✅ | In HĐ (.docx) dùng `title` + `clauses` + `custom_field_values` + snapshot + khoản thu (CT-UC-15); PDF: P2 | 2d |
| CT-15 ✅ | Rà lần 2 (03/10/2026): vượt sức chứa khi kích hoạt, chuyển đi 2 lần, hủy thanh lý khi đã sang phòng khác, trần cọc, ngày trả phòng, xe khi dời ngày bắt đầu | 0.5d |
| CT-17 ✅ | Khoản thu của HĐ (`contract_fees`, EXCLUDE theo khoảng hiệu lực), in văn bản .docx (OpenXML), báo cáo dữ liệu cần xem lại | 2d |
| CT-16 ✅ | Hoàn thiện mục P2 / câu hỏi mở: ghi chú HĐ đang hiệu lực, cảnh báo mềm, HĐ quá hạn, chủ xe khi đổi người đứng tên, chủ hộ, khóa theo người thuê | 1.5d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Thu tiền phòng kỳ lẻ đầu tiên thế nào? | Theo `proration_mode` (Daily mặc định). Chủ trọ có thể sửa tiền tháng ở phiếu (M07) |
| Q2 | Tái ký khi hết hạn: HĐ mới hay phụ lục gia hạn? | Phụ lục gia hạn (`Extend`) — giữ chuỗi kỳ & cọc. HĐ mới chỉ khi đổi người đứng tên (ký lại — CT-UC-21) |
| Q3 | Đổi người đại diện giữa chừng? | Ký lại cho người còn ở (CT-UC-21) |
| Q4 | Thuê nguyên căn nhiều phòng 1 HĐ? | Ngoài phạm vi P1 — thuê nguyên căn: khai báo căn nhà là 1 khu có 1 phòng |
| Q5 ✅ | Người đứng tên **không ở cùng** (bố mẹ ký cho con) — ai là chủ hộ khi đăng ký tạm trú? | Đã làm: `household_head_renter_id` chọn trong số người ở, quan hệ khai so với chủ hộ (CT-BR-36) |
| Q6 | Có kiểm tra giới tính của **người đứng tên** khi khai vợ/chồng (Luật HN&GĐ không thừa nhận hôn nhân cùng giới)? | Chưa chặn — chỉ kiểm giới tính người được khai; cặp đôi cùng giới khai "Cùng ở thuê" / "Khác" |
| Q7 ✅ | Trường tùy biến điện / nước trùng với khoản thu M04? | Đã có M04: **khoản thu là nguồn duy nhất** để tính tiền và in văn bản (Điều "Tiền điện, nước và dịch vụ" sinh từ `contract_fees`). Trường điện nước trong mẫu gợi ý trở thành tùy chọn — nên bỏ khỏi mẫu của tổ chức để tránh nhập 2 nơi |
| Q8 ✅ | HĐ **quá hạn** — thêm người ở sau `end_date` | Đã làm: giữ quy tắc buộc gia hạn trước, trả mã riêng `CONTRACT_EXPIRED_EXTEND_FIRST` để UI hiện nút "Gia hạn" (CT-BR-37) |
| Q9 ✅ | Đổi người đứng tên ở bản nháp khi xe đã đăng ký cho người đứng tên cũ | Đã làm: chặn với `VEHICLE_OWNER_NOT_IN_CONTRACT` (CT-BR-38) |
| Q10 | Sửa hồ sơ người thuê (ngày sinh / giới tính) **sau khi** HĐ đã kích hoạt làm quan hệ không còn hợp lý | Không kiểm lại (HĐ đã ký giữ bản chụp); P2 báo cáo "dữ liệu cần xem lại" |
