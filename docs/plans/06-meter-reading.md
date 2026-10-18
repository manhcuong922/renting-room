# M06 — Meter Reading (Công tơ & Ghi chỉ số)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Kỳ thu: C-05. Pháp lý: L9 (điện), L10 (nước).

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Quản lý **công tơ** của từng phòng cho các khoản thu nhóm `Metered` (M04) và **chỉ số** theo thời gian,
cung cấp **sản lượng tiêu thụ chính xác, không chồng lấn, không bỏ sót** cho M07. Có **màn hình riêng** nhập chỉ số cuối kỳ
cho cả khu (yêu cầu nghiệp vụ: "tiền điện tháng trước và tháng này").

**Công tơ đi theo phòng, không theo hợp đồng** (FE-BR-17, 04/10/2026): HĐ không đăng ký điện nước; HĐ đang hiệu lực của phòng
dùng công tơ của phòng kể từ chỉ số ngày nhận phòng (MT-BR-13).

**Trong phạm vi**: lắp/gỡ/thay công tơ; chỉ số ban đầu, bàn giao, định kỳ, cuối (thanh lý), tháo; màn hình ghi chỉ số hàng loạt;
cảnh báo bất thường; khóa chỉ số đã dùng cho phiếu đã chốt.

**Ngoài phạm vi**: công tơ tổng chia theo người/phòng (P3); đọc công tơ IoT; OCR ảnh.

**Hiện trạng (rà soát 10/10/2026 — đã code)**
- Lắp / thay (phiên bản) / gỡ công tơ từng phòng; **thay hàng loạt** giữa tháng theo khu (MT-UC-08); công tơ của phòng kèm chỉ số mới nhất; lịch sử; sửa chỉ số (đơn điệu).
- Kích hoạt HĐ: bắt buộc **chỉ số nhận phòng** mỗi công tơ (bỏ trống = số mới nhất); trả phòng: **chỉ số cuối** nhập khi lập phiếu quyết toán (MT-UC-05);
  chuyển phòng: chỉ số cuối phòng cũ + nhận phòng mới tại ngày chuyển (M05 CT-BR-14).
- Lưới ghi chỉ số hằng tháng (MT-UC-04) + lưu hàng loạt tất cả hoặc không (MT-BR-07); tính sản lượng tự cộng nhiều công tơ (thay công tơ — MT-BR-15,
  chuyển phòng thì chốt điện nước phòng cũ thành khoản phát sinh — M05 CT-BR-47); khóa chỉ số theo phiếu đã chốt (MT-BR-06); cảnh báo sản lượng bất thường (MT-BR-08).
- Ngừng dùng khoản điện nước còn công tơ hoạt động → 422 `FEE_HAS_ACTIVE_METERS`; HĐ ở phòng chưa có công tơ điện → cảnh báo `ROOM_WITHOUT_METER`.

**11–12/10/2026 — đã code thêm**: lắp công tơ hàng loạt, hủy chỉ số ghi nhầm, cảnh báo ngày ghi lệch xa cuối kỳ (MT-BR-05), lọc lưới theo khoản thu, báo cáo sản lượng phòng trống (MT-BR-14). **Chưa code**: ảnh chụp công tơ (chờ M09).
Chỉ số cuối / nhận phòng khi một HĐ chuyển đi rồi chuyển về **cùng một phòng**: ✅ làm 11/10/2026 (MT-BR-04, MT-BR-12).

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
| MT-UC-01 ✅ | **Bổ sung 13/10/2026 — lưới lắp hàng loạt**: `GET /properties/{id}/meters/install-sheet?feeTypeId=` trả các phòng đang dùng của khu **chưa có công tơ đang hoạt động** của khoản thu đó (mã phòng, tầng) — UI dựng lưới nhập serial / chỉ số đầu rồi gửi lệnh lắp hàng loạt, không phải hỏi từng phòng. Khoản thu không thuộc khu / không phải loại theo công tơ → lỗi như lệnh lắp. Lắp công tơ cho phòng (khoản thu Metered, số serial, ngày lắp, chỉ số ban đầu). ✅ 12/10/2026 **lắp hàng loạt** cho nhiều phòng của khu: `POST /properties/{id}/meters/bulk` `{ feeTypeId, installedDate, rows[{roomId, serialNo?, initialValue}] }` — tất cả hoặc không (lỗi 1 dòng ⇒ 422 `BULK_INSTALL_INVALID` kèm `rowErrors`), tối đa 500 dòng |
| MT-UC-02 ✅ | Thay công tơ (hỏng / quay vòng về 0): chỉ số tháo của công tơ cũ + chỉ số ban đầu công tơ mới trong **một** lệnh |
| MT-UC-03 ✅ | Gỡ công tơ (không thay) |
| MT-UC-04 ✅ | **Màn hình ghi chỉ số**: chọn khu + tháng thu (+ lọc tầng, lọc khoản thu `feeTypeId` ✅ 12/10/2026) → lưới mỗi dòng = (phòng, công tơ, HĐ): chỉ số cũ (ngày), ô nhập chỉ số mới, sản lượng tạm tính, cảnh báo; lưu hàng loạt |
| MT-UC-05 ✅ | Ghi **chỉ số nhận phòng** (`Handover`) khi kích hoạt HĐ — "dùng số mới nhất" hoặc nhập số khác (MT-BR-13) — và **chỉ số cuối** (`Final`) khi trả phòng. **Đổi 04/10/2026 ✅**: chỉ số cuối nhập ở bước **lập phiếu quyết toán** (M07 BL-UC-11) thay vì lúc hoàn tất — vì phiếu quyết toán cần chỉ số cuối để tính điện nước |
| MT-UC-09 ✅ | **Điều chỉnh chỉ số đã chốt** (18/10/2026 — gõ sai, phát hiện khi phiếu đã chốt / đã thu): `POST /meter-readings/{id}/adjust` `{ value, reason }` (Idempotency-Key) — sửa chỉ số về số đúng và **tự tạo khoản bù / phụ thu** phần tiền chênh vào phiếu kỳ sau (MT-BR-19); phiếu đã chốt giữ nguyên làm chứng từ. VD tháng 10 gõ 1.388 thay vì 1.338, phiếu đã thu đủ: điều chỉnh về 1.338 ⇒ khoản **bù 190.000đ** (50 kWh × 3.800đ) vào phiếu tháng 11, tháng 11 bắt đầu từ 1.338 nên nhập được 1.380 |
| MT-UC-06 ✅ | Sửa giá trị chỉ số chưa bị khóa (đã khóa ⇒ điều chỉnh — MT-UC-09). ✅ 11/10/2026 **hủy chỉ số ghi nhầm / ghi thừa**: `POST /meter-readings/{id}/void` `{ reason }` — chỉ chỉ số **cuối kỳ** (`Periodic`) và **kiểm tra** (`Adhoc`) chưa bị khóa (MT-BR-06); chỉ số lắp / tháo công tơ, nhận phòng, cuối HĐ gắn với vòng đời công tơ / HĐ nên không hủy tay → 422 `READING_NOT_VOIDABLE` (sửa giá trị, hoặc hủy thao tác gốc: hủy thanh lý, hủy chuyển phòng). Dòng bị hủy giữ trong DB kèm lý do; nháp liên quan "Cần tính lại" |
| MT-UC-07 ✅ | Xem lịch sử chỉ số của công tơ (biểu đồ sản lượng do UI vẽ từ lịch sử) |
| MT-UC-08 ✅ | **Thay công tơ hàng loạt giữa tháng** (M2 — chốt 09/10/2026; VD điện lực thay đồng loạt cả khu): chọn khu + loại công tơ (khoản thu) + **1 ngày thay chung** → lưới mỗi phòng có công tơ đang hoạt động: chỉ số cũ gần nhất, ô **số cuối công tơ cũ**, **serial mới**, **chỉ số đầu công tơ mới** (mặc định 0); bỏ trống số cuối = không thay phòng đó. Lưu **tất cả hoặc không** (MT-BR-07): dòng lỗi được đánh dấu (số cuối < số cũ, ngày thay trước chỉ số gần nhất / trong kỳ đã chốt, trùng serial). Kỳ có thay: tính tiền tự cộng 2 công tơ (MT-BR-15) |

### 3.2 Quy tắc nghiệp vụ



| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| MT-BR-01 ✅ | Mỗi `(room, fee_type)` có tối đa **1 công tơ đang hoạt động** | DB partial unique |
| MT-BR-02 ✅ | Công tơ chỉ gắn khoản thu `Metered`, cùng khu với phòng | Domain + FK composite `(org, property_id, fee_type_id)`, `(org, property_id, room_id)` |
| MT-BR-03 ✅ | **Đơn điệu**: theo thứ tự `(reading_date, sequence)`, giá trị chỉ số của **cùng một công tơ** không giảm. Chèn chỉ số lùi ngày phải ≥ chỉ số liền trước và ≤ chỉ số liền sau | Application, khóa `meters` row `FOR UPDATE` |
| MT-BR-04 ✅ | Chỉ số `Periodic` gắn `(contract_id, closing_period_start)`; tối đa 1 bản chưa hủy cho mỗi `(meter, contract, closing_period_start)`. `Final` và `Handover`: tối đa 1 / (meter, contract, **ngày ghi**) (đổi 11/10/2026 — trước là 1 / (meter, contract), làm HĐ chuyển về phòng cũ bị trùng khóa); tra chỉ số nhận phòng / chỉ số cuối luôn theo **lần ở** (khoảng ngày HĐ ở phòng) | DB partial unique |
| MT-BR-05 | `reading_date` ≤ hôm nay + 1 ✅ (400 `INVALID_READING_DATE`). ✅ 12/10/2026: `reading_date` của `Periodic` ngoài `[usage_end − 7 ngày, usage_end + 15 ngày]` → cảnh báo `READING_DATE_FAR_FROM_PERIOD_END` trong `warnings[]` của lưu hàng loạt (kèm `index` dòng) (không chặn) | Application |
| MT-BR-06 ✅ | Chỉ số đã được dùng trong dòng phiếu **Finalized** chưa Void, hoặc là chỉ số đầu / cuối của **khoản chốt điện nước khi chuyển phòng** chưa hủy (11/10/2026 — M07 BL-BR-38) → **khóa**: không sửa/hủy (422 `READING_LOCKED`) — gõ sai thì **điều chỉnh** (MT-UC-09, MT-BR-19). Nếu chỉ dùng trong phiếu Draft → cho sửa, phiếu Draft bị đánh dấu `is_stale` | Application (query M07) |
| MT-BR-19 ✅ | **Điều chỉnh chỉ số đã chốt** (MT-UC-09): chỉ cho chỉ số là **chỉ số cuối** của đoạn đo trên phiếu **đã chốt** chưa Void (chưa chốt → 422 `READING_NOT_LOCKED`, sửa trực tiếp; là chỉ số đầu / không thuộc đoạn đo nào → 422 `READING_NOT_ADJUSTABLE`); kỳ sau **đã chốt** với chỉ số này làm chỉ số đầu → 422 `READING_NEXT_PERIOD_FINALIZED` (tổng 2 kỳ đã tự bù nhau); số mới = số cũ → 400 `READING_UNCHANGED`; vẫn đơn điệu như MT-BR-03. Tiền chênh = thành tiền dòng điện nước của phiếu đó tính lại với sản lượng mới − với sản lượng cũ, **cùng bản giá của dòng** (giá ngày cuối kỳ sử dụng; theo bậc ⇒ bậc tính trên tổng của dòng); thu dư ⇒ khoản **bù** (`Credit`), thu thiếu ⇒ **phụ thu**; chênh 0đ ⇒ không tạo khoản. Khoản là khoản phát sinh thường của HĐ (M07 BL-BR-30..37, sửa / hủy được, **không** áp giới hạn bù 1 tháng tiền phòng), ngày phát sinh = hôm nay, phòng = phòng hiện tại của HĐ, mô tả "Điều chỉnh chỉ số Điện kỳ 05/10–04/11: 1388 → 1338 (−50 kWh)", lý do = `reason`; có nháp đang mở ⇒ gắn ngay, không thì chờ phiếu sau. HĐ đã kết thúc / đã chốt phiếu quyết toán → 422 `ROOM_CHARGE_NO_OPEN_INVOICE`. Dòng của phiếu đã chốt từng **sửa tay** ⇒ vẫn tạo khoản theo số hệ thống + cảnh báo `LINE_MANUALLY_EDITED` (chủ trọ tự chỉnh khoản). Nháp kỳ sau tự "Cần tính lại". Audit: sửa chỉ số + tạo khoản trong 1 transaction | Application |
| MT-BR-07 ✅ | Ghi chỉ số hàng loạt là **all-or-nothing**: lỗi bất kỳ dòng → không lưu dòng nào, trả lỗi theo từng dòng | Transaction |
| MT-BR-08 ✅ | Cảnh báo bất thường (không chặn — chốt làm 08/10, ngưỡng (chốt 09/10/2026); code 09/10/2026 — `UsageAnomaly`; lưới trả `recentAverage` (TB 3 kỳ) + `usageWarning`), hiện trên lưới chỉ số và phiếu nháp `UNUSUAL_USAGE`: (1) sản lượng **gấp ≥ 3 lần trung bình 3 kỳ trước VÀ tăng ≥ 50 đơn vị** (tránh báo nhầm phòng dùng ít: 10 → 35 kWh); (2) **= 0 khi phòng có người ở** (có thể hợp lệ — đi vắng — chỉ để kiểm lại số). HĐ chưa đủ 3 kỳ lịch sử ⇒ không cảnh báo (1). Sản lượng âm đã bị chặn (MT-BR-03) | Application |
| MT-BR-09 ✅ | **Công tơ có phiên bản**: 1 phòng có thể có nhiều công tơ cho cùng khoản thu theo thời gian, **chỉ 1 đang hoạt động** (MT-BR-01). Thay công tơ = ngừng công tơ cũ (**bắt buộc nhập số cuối** `Removal`) + thêm công tơ mới (số ban đầu `Initial`) cùng ngày, trong 1 lệnh. Không được thay nếu ngày thay < chỉ số cuối cùng đã ghi của công tơ cũ. **Ngày thay không được thuộc kỳ sử dụng đã chốt phiếu** (17/10/2026): ngày thay ≤ ngày cuối kỳ sử dụng của phiếu đã chốt gần nhất có đoạn đo trên công tơ này → 422 `METER_PERIOD_LOCKED` kèm `lockedUntil` — chọn ngày từ `lockedUntil + 1` (VD kỳ 05/10–04/11 đã chốt, thay thực tế 03/11 nhưng khai ngày 10/11 ⇒ chặn, khai 05/11: phần 1.338 → 1.345 của công tơ cũ vào phiếu tháng 11, không mất) | Domain + Application |
| MT-BR-10 ✅ | Gỡ công tơ (không thay) bắt buộc chỉ số tháo (`Removal`); ngày gỡ không thuộc kỳ đã chốt phiếu như MT-BR-09 → 422 `METER_PERIOD_LOCKED`; phòng đang có HĐ hiệu lực → **cảnh báo** `ROOM_HAS_OPEN_CONTRACT` (từ kỳ sau phiếu không còn dòng của khoản đó) | Application |
| MT-BR-11 ✅ | HĐ không đăng ký Metered. Kích hoạt HĐ không bị chặn vì công tơ; phòng chưa có công tơ của khoản Metered đang dùng ở khu → cảnh báo `ROOM_WITHOUT_METER` (phòng tính nước theo người thì bỏ qua). Phòng có công tơ → bắt buộc có chỉ số `Handover` tại `start_date` (MT-BR-13) | M05 Application |
| MT-BR-12 ✅ | **Chuỗi liên tục**: chỉ số đầu của đoạn đo kỳ N = chỉ số cuối của đoạn đo kỳ N−1 (cùng HĐ, cùng công tơ) **trên phiếu chưa Void**; kỳ đầu tiên dùng chỉ số `Handover` (hoặc `Initial` nếu công tơ lắp sau khi kích hoạt). Không có khoảng hở hoặc chồng lấn. **Thêm 11/10/2026**: không nối ngược qua mốc nhận phòng — số cuối của đoạn đo trước chỉ dùng khi ngày ghi nằm trong lần ở đang tính; ngoài ra dùng chỉ số nhận phòng của lần ở (HĐ rời phòng rồi quay lại không bị tính phần phòng trống / người khác dùng) | Domain (`MeterUsageCalculator`) + DB unique trên end reading |
| MT-BR-13 ✅ | **Chỉ số nhận phòng (mốc bắt đầu tính của HĐ)**: khi kích hoạt HĐ, **bắt buộc** nhập cho mỗi công tơ hoạt động của phòng 1 chỉ số `Handover` tại **ngày tính tiền** (`billing_start_date` — K5, mặc định = `start_date`; M05 CT-BR-04); chuyển phòng: tại ngày chuyển. Hai cách: **"Dùng số mới nhất"** = chỉ số gần nhất của công tơ ≤ `start_date` (số cuối của HĐ trước, hoặc số tháng trước đã dùng tính tiền) — hoặc **nhập số khác** ≥ số đó (đơn điệu MT-BR-03). Sửa lại được khi chưa bị khóa (MT-BR-06). VD: người cũ trả phòng ngày 15 ở số 100; thợ sửa chữa dùng 8 kWh; người mới vào ngày 18 ⇒ nhập **108**, người mới chỉ trả từ 108 | Domain + Application |
| MT-BR-14 | **Sản lượng khoảng trống** giữa `Final` của HĐ trước và `Handover` của HĐ sau (VD 100 → 108: sửa chữa, phòng trống) **không tính cho người thuê nào** ✅. ✅ 12/10/2026 báo cáo **sản lượng phòng trống**: `GET /properties/{id}/meters/vacant-usage?from=&to=` — mỗi lần nhận phòng có chỉ số cao hơn chỉ số liền trước trên công tơ là một khoảng không tính cho ai (phòng, khoản thu, từ số – đến số, sản lượng) + tổng theo khoản thu, để chủ trọ đối chiếu hóa đơn EVN | Domain + M10 |
| MT-BR-15 ✅ | **Kỳ có thay công tơ**: cuối kỳ chỉ nhập số của **công tơ mới**; hệ thống tự kiểm tra trong kỳ sử dụng có thay công tơ không và cộng: (số cuối công tơ cũ − số đầu kỳ của công tơ cũ) + (số cuối kỳ công tơ mới − số ban đầu công tơ mới), tính tiền trên **tổng sản lượng** theo bản giá mới nhất tới cuối kỳ (một giá hoặc theo bậc — BL-BR-05, FE-BR-10). Lưới ghi chỉ số hiển thị dòng phụ "Công tơ cũ (đã thay ngày dd/MM): 1.250 → 1.320 = 70" để chủ trọ kiểm tra. Cách thay thế: không ghi công tơ phiên bản, nhập tiền điện công tơ cũ thành **phụ thu** trên phiếu nháp (BL-BR-23) | Domain (`MeterUsageCalculator`) |
| MT-BR-18 ✅ | **Thay hàng loạt** (MT-UC-08): mỗi dòng = đúng lệnh thay công tơ đơn (MT-UC-02, cùng kiểm tra MT-BR-09); cả lô trong 1 transaction, khóa công tơ theo id; lỗi bất kỳ dòng ⇒ 422 `BULK_REPLACE_INVALID` kèm `rows[{meterId, code, message}]`, không lưu dòng nào. Tối đa 500 dòng. Nháp đã lập của phòng bị đánh dấu "Cần tính lại" (BL-BR-20) | Application |
| MT-BR-16 ✅ | **Chỉ số cũ của kỳ sau** = chỉ số cuối của đoạn đo trên phiếu **chưa Void** gần nhất của HĐ — nháp hoặc đã chốt (MT-BR-12; sửa câu chữ 17/10/2026 cho khớp code). Nháp chỉ được lập khi đủ chỉ số (M07 BL-BR-21) nên đoạn đo của nháp luôn đầy đủ; khi chốt phiếu, chỉ số cuối kỳ bị khóa | Domain |
| MT-BR-17 ✅ | Phòng **ngừng dùng / bảo trì không tháo công tơ** — công tơ vẫn chạy khi không có người thuê (sửa chữa, thử phòng); phần dùng khi phòng trống không tính cho ai (MT-BR-14) | Thiết kế |

### 3.3 Thuật toán sản lượng (dùng bởi M07)

```
Input: contract C, fee type F, usage period U = [Us, Ue], isFinal
0. Công tơ của các phòng C đã ở giao U (`Contract.RoomStays`), xét **từng lần ở** giao U (đổi 17/10/2026 — M05 CT-BR-47): bắt đầu từ `Handover` của
   lần ở nếu vào phòng trong U; lần ở **đã rời** (chuyển phòng) kết thúc bằng `Final` tại ngày chuyển nếu ngày chuyển ≤ Ue, ngược lại bằng
   `Periodic` của U (chưa ghi `Periodic` ⇒ dùng `Final` tại ngày chuyển — vài ngày sau Ue tính luôn vào U, kỳ sau không tính lại).
   Mọi đoạn đo của cùng khoản cộng thành 1 dòng. Đoạn đo đã có khoản chốt khi chuyển phòng (dữ liệu trước 17/10/2026) ⇒ bỏ qua
1. meters = công tơ của phòng trong lần ở đó với fee F có khoảng hoạt động giao U (tối đa n, sắp theo installed_date)
2. Với mỗi meter m (theo thứ tự):
   start = (a) end_reading của đoạn đo gần nhất của (C, m) trên phiếu chưa Void
           (b) nếu không có: reading Handover của (C, m)
           (c) nếu không có: reading Initial của m (công tơ lắp/thay sau khi HĐ kích hoạt)
   end   = nếu m bị tháo trong U: reading Removal của m
           ngược lại nếu lần ở đã rời: như bước 0
           ngược lại nếu isFinal: reading Final của (C, m)
           ngược lại: reading Periodic (C, m, closing_period_start = Us)
   start = end (cùng một chỉ số) ⇒ không có đoạn đo
   thiếu end → lỗi MISSING_READING(meter m); start.value > end.value → lỗi (không thể xảy ra nhờ BR-03)
   segment = (m, start, end, end.value − start.value)
3. quantity = Σ segment.consumption   (làm tròn 2 số lẻ)
Output: segments[], quantity
```

Bước 2(a) "đoạn đo gần nhất" chỉ đúng vì M07 bắt buộc **lập phiếu tuần tự** (BL-BR-21) và **hủy theo LIFO** (BL-BR-22):
không bao giờ tồn tại phiếu kỳ sau khi kỳ trước chưa có phiếu, nên đoạn gần nhất luôn là đoạn của kỳ liền trước.

Ví dụ thay công tơ giữa kỳ: cũ từ 1.250 → tháo 1.320 (70); mới 0 → cuối kỳ 45 (45) ⇒ 115 kWh.

### 3.4 Màn hình ghi chỉ số — cách xác định dòng

Với khu K và tháng thu M: mỗi HĐ `Active`/`Liquidating` của K × mỗi công tơ của phòng HĐ hoạt động trong kỳ sử dụng U (không cần HĐ đăng ký — FE-BR-17):
- `Postpaid`: kỳ sử dụng U = kỳ có `PeriodStart` thuộc tháng M.
- `Prepaid`: U = kỳ **liền trước** kỳ có `PeriodStart` thuộc tháng M (bỏ qua nếu kỳ đó trước `start_date` của HĐ).
- HĐ `Liquidating` mà `actual_end_date` ∈ U → **không** hiện trên lưới: chỉ số cuối nhập khi lập phiếu quyết toán (M07 BL-UC-11).
- HĐ **chuyển tới phòng hiện tại sau khi U kết thúc** (khu thu trước: U là kỳ trước, lúc đó HĐ còn ở phòng cũ) → **không** hiện trên lưới (11/10/2026):
  công tơ phòng mới chưa có gì để tính cho U; phần phòng cũ tính tới chỉ số cuối ghi lúc chuyển (M05 CT-BR-47, §3.3 bước 0).
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
- `(meter_id, contract_id, reading_date)` WHERE kind = 'Handover' ; riêng `(meter_id, contract_id, reading_date)` WHERE kind = 'Final' (11/10/2026)
- `(meter_id)` WHERE kind = 'Initial'; `(meter_id)` WHERE kind = 'Removal'

INDEX `(meter_id, reading_date, sequence) WHERE voided_at IS NULL`.

Đoạn đo trên phiếu lưu ở M07 (`invoice_meter_segments`) với `start_reading_id`, `end_reading_id`.

## 5. Domain model

> **Đã đổi khi code (đợt 1)**: chỉ số nằm **trong aggregate `Meter`** (không tách aggregate) — lịch sử 1 công tơ chỉ vài chục bản/năm,
> nạp cả để kiểm đơn điệu (MT-BR-03) ngay trong domain. Thứ tự cùng ngày: `Initial` < `Handover` < `Periodic`/`Adhoc` < `Final` < `Removal`, rồi `sequence`.

```
Meter (aggregate root, chứa readings)
  + Install(room, feeType, serial, date, initialValue)
  + Remove(date, finalValue)
  + static Replace(old, date, oldFinal, newSerial, newInitial) : Meter mới
  + Record(kind, date, value, contract?, closingPeriodStart?) / Correct(readingId, value)   // kiểm đơn điệu trong aggregate
  + FindHandover / FindPeriodic / FindFinal (contractId, …)
InvoiceCalculator (Domain/Billing): tính sản lượng theo §3.3 — thuần, unit test bảng
UsageAnomaly: MT-BR-08
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `InstallMeterCommand` (`BulkInstallMetersCommand`: chưa code) | Cmd | `METER_ALREADY_ACTIVE`, `FEE_NOT_METERED` |
| `ReplaceMeterCommand` | Cmd | `READING_NOT_MONOTONIC`, `INVALID_REPLACE_DATE` |
| `BulkReplaceMetersCommand` (MT-UC-08; `POST /properties/{id}/meters/bulk-replace` `{ feeTypeId, replacedOn, rows[{meterId, oldFinalValue, newSerial?, newInitialValue?}] }`; `GET /properties/{id}/meters/replace-sheet?feeTypeId=`) | Cmd / Qry | `BULK_REPLACE_INVALID` |
| `RemoveMeterCommand` | Cmd | bắt buộc chỉ số tháo; cảnh báo `ROOM_HAS_OPEN_CONTRACT` (MT-BR-10) |
| `GetMeterReadingSheetQuery` | Qry | propertyId, billingMonth (`yyyy-MM`), floor? → rows (§3.4) kèm trung bình 3 kỳ + cảnh báo bất thường |
| `SaveMeterReadingsCommand` (Idempotency-Key) | Cmd | upsert theo khóa MT-BR-04; lỗi ⇒ 422 `READINGS_INVALID` kèm `rowErrors[]`; nháp bị ảnh hưởng tự gắn "Cần tính lại" |
| `RecordHandoverReadingsCommand` / `RecordFinalReadingsCommand` | Cmd (nội bộ, gọi từ M05) | Handover: giá trị do người dùng nhập, mặc định = gợi ý MT-BR-13; sửa sau bằng `CorrectReadingCommand` khi chưa khóa |
| `CorrectReadingCommand` (`VoidReadingCommand`: chưa code) | Cmd | `READING_LOCKED`, `READING_NOT_MONOTONIC` |
| `ListMeterReadingsQuery` | Qry | theo meter, khoảng ngày |

## 7. API



| Method | Route | Ghi chú / lỗi |
|--------|-------|---------------|
| GET | `/rooms/{roomId}/meters?includeRemoved=false` | công tơ + `latestReading {value, date, kind}` (để điền "Dùng số mới nhất") |
| POST | `/rooms/{roomId}/meters` `{ feeTypeId, serialNo?, installedDate, initialValue, note? }` (Idempotency-Key) | 409 `METER_ALREADY_ACTIVE`, 422 `FEE_NOT_METERED` / `FEE_NOT_IN_PROPERTY` / `FEE_ARCHIVED` |
| POST | `/meters/{id}/replace` `{ date, oldFinalValue, newSerialNo?, newInitialValue, note? }` | → 201 id công tơ mới; 422 `METER_REMOVED`, `READING_NOT_MONOTONIC`, `INVALID_READING_DATE` |
| POST | `/meters/{id}/remove` `{ date, finalValue, note? }` | → 200 `{ warnings }` (`ROOM_HAS_OPEN_CONTRACT`) |
| GET | `/meters/{id}/readings` | lịch sử (mới nhất trước) |
| PUT | `/meter-readings/{id}` `{ value, note? }` | sửa giá trị; 422 `READING_NOT_MONOTONIC` (kèm `previousValue` / `nextValue`) |
| POST | `/contracts/{id}/activate` `{ handoverReadings: [{ meterId, value? }] }` | 422 `HANDOVER_READING_REQUIRED` (kèm `meterIds`) — `value` null = số mới nhất |
| POST | `/contracts/{id}/final-invoice` `{ finalReadings: [{ meterId, value }] }` (lập phiếu quyết toán) | 422 `FINAL_READING_REQUIRED` (kèm `meterIds`) |

| GET | `/properties/{propertyId}/meter-reading-sheet?billingMonth=2026-10&floor=` | lưới ghi chỉ số (MT-UC-04) |
| PUT | `/properties/{propertyId}/meter-readings` (lưu hàng loạt, Idempotency-Key) | 422 `READINGS_INVALID` + `rowErrors` |
| GET | `/properties/{propertyId}/meters/replace-sheet?feeTypeId=` | lưới thay công tơ hàng loạt (MT-UC-08) |
| POST | `/properties/{propertyId}/meters/bulk-replace` (Idempotency-Key) | 422 `BULK_REPLACE_INVALID` |
| POST | `/contracts/{id}/transfer-room` `{ oldRoomReadings, newRoomReadings }` | chỉ số cuối phòng cũ + nhận phòng mới (M05 CT-BR-14) |
| POST | `/meter-readings/{id}/adjust` `{ value, reason }` (Idempotency-Key) — điều chỉnh chỉ số đã chốt (MT-UC-09) | 200 `{ reading, charge, warnings }`; 422 `READING_NOT_LOCKED` / `READING_NOT_ADJUSTABLE` / `READING_NEXT_PERIOD_FINALIZED` / `ROOM_CHARGE_NO_OPEN_INVOICE`; 400 `READING_UNCHANGED` |

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
      "recentAverage": null, "usageWarning": null,
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
→ 200 { "saved": 1 }
→ 422 { "code": "READINGS_INVALID", "rowErrors": [ { "index": 0, "code": "READING_NOT_MONOTONIC", "previousValue": 1350 } ] }
```

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| value | 0 ≤ x ≤ 99.999.999,99; tối đa 2 số lẻ |
| readingDate | ≤ hôm nay + 1; ≥ installed_date của meter; < removed_date (nếu có) với kind khác Removal |
| kind | lưu hàng loạt chỉ ghi `Periodic` |
| closingPeriodStart | phải là `PeriodStart` hợp lệ của HĐ (C-05) |
| contractId | HĐ của đúng phòng của meter; trạng thái Active/Liquidating |
| bulk | ≤ 1.000 dòng; không trùng khóa trong request |
| billingMonth | `yyyy-MM` |

## 9. Phân quyền
P1: chủ trọ và phó quản lý toàn quyền nghiệp vụ (M01 §3.3). P3: phó quản lý chỉ ghi chỉ số khu được gán.

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Giải pháp |
|----------|-----------|
| 2 người ghi chỉ số cùng công tơ cùng lúc | Khóa `meters` row `FOR UPDATE` (sắp theo id khi bulk để tránh deadlock) + unique MT-BR-04 |
| Sửa chỉ số sau khi phiếu đã chốt | MT-BR-06 khóa; gõ sai ⇒ điều chỉnh (MT-BR-19): sửa chỉ số + khoản bù / phụ thu vào kỳ sau, không hủy phiếu |
| Chỉ số bị dùng 2 lần (2 phiếu) | `invoice_meter_segments` UNIQUE `(end_reading_id) WHERE voided = false` (M07) |
| Khoảng hở chỉ số giữa 2 kỳ | MT-BR-12: start luôn lấy từ end của đoạn trước — không lấy từ "chỉ số gần nhất theo ngày" |
| Void phiếu → đoạn đo được giải phóng | M07 đặt `voided = true` trên segment trong cùng transaction với Void phiếu |
| Lưu bulk bị retry | Idempotency-Key; upsert theo khóa nghiệp vụ nên retry cũng an toàn |
| Công tơ quay vòng (99999 → 00000) | Xử lý như **thay công tơ** (Removal 99999 + Initial 0) — tài liệu hướng dẫn người dùng |

## 11. Audit
Audit sửa/hủy chỉ số (giá trị cũ → mới, lý do), thay/gỡ công tơ.

## 12. Kế hoạch test
- Unit `InvoiceCalculator` (bảng): kỳ đầu dùng Handover; kỳ thường; thay công tơ giữa kỳ; công tơ lắp sau khi kích hoạt (dùng Initial); Final; thiếu end → lỗi; sau Void phiếu kỳ trước → start lùi về đoạn trước nữa; chuyển phòng giữa kỳ (phòng đã rời chỉ tính khi có chỉ số cuối kỳ), chuyển về phòng cũ, khu thu trước.
- Unit `Meter`: đơn điệu khi chèn lùi ngày giữa 2 chỉ số.
- Bổ sung 18/10/2026 (MT-BR-19): phiếu tháng 10 đã chốt + đã thu với chỉ số cuối gõ nhầm 1.388 (đúng 1.338) ⇒ `PUT` → 422 `READING_LOCKED`; `POST …/adjust { value: 1338 }` ⇒ chỉ số = 1.338, khoản **bù 190.000đ** chờ / gắn nháp tháng 11, tháng 11 nhập 1.380 được và tính 42 kWh; gõ thiếu (1.300 → đúng 1.338) ⇒ khoản **phụ thu**; giá bậc ⇒ chênh tính trên tổng của dòng; chỉ số chưa chốt → `READING_NOT_LOCKED`; kỳ sau đã chốt → `READING_NEXT_PERIOD_FINALIZED`; số không đổi → `READING_UNCHANGED`; HĐ đã kết thúc → `ROOM_CHARGE_NO_OPEN_INVOICE`.
- Bổ sung 17/10/2026: thay / gỡ công tơ (từng phòng, hàng loạt) với ngày thuộc kỳ sử dụng đã chốt phiếu → 422 `METER_PERIOD_LOCKED` kèm `lockedUntil`; ngày `lockedUntil + 1` ⇒ được, phiếu kỳ sau cộng phần còn lại của công tơ cũ; công tơ chưa có phiếu đã chốt ⇒ không chặn. Chuyển phòng giữa kỳ ⇒ 1 dòng điện = phòng cũ (tới chỉ số cuối lúc chuyển) + phòng mới; khu thu trước chuyển phòng khi chưa ghi chỉ số cuối kỳ trước ⇒ phòng cũ tính tới chỉ số lúc chuyển, kỳ sau không tính lại.
- Integration: sheet Prepaid vs Postpaid ra đúng U; bulk 1 dòng lỗi → không lưu dòng nào; sửa chỉ số đã khóa → 422; sửa chỉ số của draft → draft stale;
  2 request ghi song song cùng khóa → 1 thành công, 1 cập nhật (upsert) không trùng; **C-01**.

## 13. Phụ thuộc
- Dùng: M02 (room), M04 (fee Metered), M05 (HĐ, các lần ở, kỳ).
- Bị dùng: M05 (nhận phòng / cuối / chuyển phòng), M07 (`InvoiceInputs` nạp công tơ + chỉ số cho `InvoiceCalculator`), M10 (báo cáo sản lượng, đối chiếu EVN — P2).

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 ✅ | Nhiều phòng dùng chung 1 công tơ? | **Ngoài phạm vi** (chốt 08/10/2026): chỉ phòng trọ / chung cư mini mỗi phòng 1 công tơ riêng; căn nhiều buồng nhỏ thuê chung = 1 phòng 1 công tơ. Nhà nguyên căn / công tơ tổng: chủ trọ tự chia, nhập phụ thu nhiều phòng |
| Q2 | Có hệ số nhân (CT ratio)? | Không cần với nhà trọ |
| Q3 | Bắt buộc ảnh chụp công tơ? | Tùy chọn theo cài đặt khu (P2, chờ M09) |
