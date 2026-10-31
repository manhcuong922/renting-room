# 98 — Vấn đề còn mở (rà toàn bộ code + plan 21/10/2026, sắp xếp lại theo nhóm 27/10/2026)

> **Cách dùng**: đây là danh sách *chưa xử lý*, gom theo **nhóm việc** (mỗi nhóm sửa chung gốc / chung file). Xử lý xong **cả nhóm** ⇒ **xóa nhóm đó** (không giữ ✅);
> xong từng issue lẻ ⇒ xóa issue đó. Chốt "không làm / giới hạn chấp nhận" ⇒ xóa ở đây, ghi 1 dòng vào [99-self-review.md](99-self-review.md) mục C.
> Lượt rà sau đọc file này thay vì rà lại từ đầu.
> Mức: `H` = HIGH (sai tiền / kẹt quy trình chính / lỗ hổng khai thác được), `M` = MEDIUM, `L` = LOW. ✔ = đã đọc lại code xác nhận; còn lại do agent rà, đã trích dòng code.
> Không có CRITICAL. Số dòng là của working tree 21/10/2026 — có thể lệch sau khi sửa.
> Trong mỗi nhóm, issue xếp từ mức cao xuống thấp. **Quyết định đã chốt** ghi ngay dưới tên nhóm.

## Thứ tự làm đề xuất
1. N3 → N4 → N2 (kẹt quy trình / sai tiền). N1 (thanh lý / ký lại / kết toán) đã xong 27/10/2026; N2 còn M-10 + Đ2 (H-02, M-09, L-10 xong 28/10/2026)
2. N5 (DoS, sửa nhỏ) — có thể làm xen bất kỳ lúc nào
3. N6 (phân quyền; cần chốt thiết kế M-12, M-14)
4. N7, N8, N9, rồi nhóm lẻ

| Nhóm | Nội dung | Issue |
|---|---|---|
| N2 | Chỉ số công tơ giữa các HĐ | M-10, Đ2 |
| N3 | Nháp liên tiếp và tính lại | H-03, M-21, L-02, L-03 |
| N4 | Phiếu thu và hoàn tiền | H-04, M-01, L-21 |
| N5 | Import Excel | H-05, L-07 |
| N6 | Phân quyền tiền và dữ liệu nhạy cảm | H-06, M-12, M-13, M-14, M-15, L-13, L-14, L-16, L-17 |
| N7 | Tính tiền, điều chỉnh, validator | M-02, M-04, M-05, M-08, M-19 |
| N8 | Concurrency và toàn vẹn | M-20, M-23, L-04, L-05, L-06, L-18 |
| N9 | Hạ tầng và cấu hình | M-16, M-17, M-18, L-08, L-12, L-15 |
| Lẻ | Người ở, phòng, công tơ, Zalo, docs | M-11, L-09, L-11, L-19, L-20 |

---

## N2 — Chỉ số công tơ giữa các HĐ
- **M-10** [Công tơ] (còn lại sau 28/10/2026 — phần điểm đầu kỳ của người mới đã sửa; cùng họ Đ2, làm chung) Cùng ngày người cũ trả phòng cuối kỳ và người mới vào: người cũ gõ số chốt kỳ cao, hóa đơn đầu người mới đã chốt thì không điều chỉnh được số của người cũ (`MeterReadingAdjustment.cs:111` `READING_NEXT_PERIOD_FINALIZED` tính cả hóa đơn khác HĐ). Sửa: kiểm "kỳ sau đã chốt" chỉ trong cùng HĐ; phần chênh người cũ trả thừa/thiếu xử lý như Đ2.

- **Đ2** [Công tơ, sinh ra khi bàn H-02 28/10/2026] Số nhận phòng của người sau đã bị hóa đơn đầu **đã chốt** khóa thì không sửa (`READING_LOCKED`) cũng không điều chỉnh được (`READING_NOT_ADJUSTABLE` — MT-BR-19 chỉ nhận chỉ số cuối đoạn đo). VD B nhận phòng 16/10 gõ 1.050 (thật 1.080), hóa đơn tháng 10 chốt + thu 150 kWh = 570.000đ (thật 120 kWh): thu thừa 114.000đ, không có đường sửa; số cuối người cũ 1.080 cũng không ghi được vì lớn hơn 1.050. Sửa: mở MT-BR-19 cho chỉ số nhận phòng (tính lại hóa đơn đầu của HĐ, Xử lý ngay / Đẩy sang hóa đơn sau); luồng đối xứng CT-BR-02 khi số cuối người cũ > số nhận phòng người mới ("số nhận phòng người mới ghi sai?"). Làm cùng phần điều chỉnh của M-10. Sau H-02 (số cuối ghi ngay lúc thanh lý) chỉ còn xảy ra khi gõ sai cả hai bên.

## N3 — Nháp liên tiếp và tính lại
- **H-03** ✔ [Sai tiền + kẹt] Tính lại nháp kỳ k+1 khi nháp kỳ k đang `MISSING_READING` (vd hủy chỉ số cuối kỳ k): `DraftRecalculator.cs:15-23` không kiểm `PREVIOUS_PERIOD_INCOMPLETE` (chỉ `GenerateInvoices.cs:87` kiểm khi tạo mới) ⇒ k+1 nối chỉ số từ kỳ k−1, nuốt sản lượng 2 kỳ (giá bậc thu dư), chốt được. Nhập lại chỉ số k ⇒ 2 nháp cùng điểm đầu ⇒ 409 `ux_invoice_meter_segments_start`, không ghi được chỉ số.
  Sửa: tách hàm kiểm dùng chung; tính lại / chốt nháp có kỳ trước còn nháp `MISSING_READING` ⇒ gắn lỗi chặn `PREVIOUS_PERIOD_INCOMPLETE`, giữ số cũ, không tính.
- **M-21** Xóa nháp không khóa HĐ (`DeleteDraftInvoice.cs`) trong khi tạo phiếu khóa HĐ ⇒ xóa nháp tháng 10 song song tạo tháng 11 ⇒ tháng 11 tồn tại, tháng 10 mất, tạo lại báo `LATER_PERIOD_BILLED`. Sửa: khóa HĐ trước phiếu như Void / Finalize.
- **L-02** `Finalize` không xóa `IsStale` (`Invoice.Lifecycle.cs:7-22`) ⇒ phiếu đã chốt vẫn `isStale: true` trên DTO / lưới chỉ số.
- **L-03** `GetForUpdateAsync(...)!` rồi đọc `.Status` (`DraftRecalculator.cs:16`, `FinalizeInvoice.cs:139`, `VoidInvoice.cs:37`) ⇒ phiếu bị xóa giữa chừng ⇒ NullReference 500. Sửa: null ⇒ `NOT_FOUND`.

## N4 — Phiếu thu và hoàn tiền
- **H-04** ✔ [Kẹt + 500] Hóa đơn có phiếu hoàn PC (điều chỉnh chỉ số "Xử lý ngay") ⇒ đảo phiếu thu gốc: `ReversePayment.cs:56-63` trừ `paid` thành âm ⇒ `Invoice.Payments.cs:25-28` throw ⇒ 500. Đảo PC trước ⇒ 422 `PAYMENT_EXCEEDS_DEBT` (`ReversePayment.cs:60`); hủy hóa đơn cần `paid = 0` ⇒ phiếu thu sai kẹt vĩnh viễn.
  VD: PT-1 = 1.000.000, tổng sau điều chỉnh 800.000, PC-1 = 200.000 ⇒ đã thu 800.000; đảo PT-1 ⇒ −200.000 (500), đảo PC-1 ⇒ 1.000.000 > 800.000 (422).
  Sửa: đảo phiếu thu thì đảo kèm các phiếu hoàn chưa đảo của cùng hóa đơn trong 1 transaction, chỉ kiểm khoảng hợp lệ [0, tổng] ở **kết quả cuối** (800.000 − 1.000.000 + 200.000 = 0) + test.
- **M-01** [Lệch sổ] Đảo phiếu thu của *phiếu nguồn* sau khi dòng Hoàn trả trỏ vào nó đã chốt / đã hoàn: `ReversePayment.cs:46-65` không kiểm BL-BR-27 ⇒ "đã hoàn 2tr" tiền chưa từng thu. Sửa: 422 khi Σ hoàn trả đã chốt trỏ vào phiếu > thực thu còn lại.
- **L-21** docs/api: `conventions.md` `PaymentKind` thiếu `Refund` (`DepositStatus` đã đủ — 27/10/2026) (FE cộng PC thành tiền thu); `auth.md:163` nói chưa có `PUT /me`; 11 §7 dùng `/api/...` thay vì `/api/v1`.

## N5 — Import Excel
- **H-05** ✔ [DoS] Import Excel làm sập tiến trình cho mọi tổ chức: `ClosedXmlSpreadsheetReader.cs:33-49` mỗi dòng cấp mảng `lastColumn` của cả sheet (tới 16.384), đọc mọi sheet; `ImportSheet.cs:78` kiểm 500 dòng *sau khi* đọc hết. File < 2 MB, 1 ô cột XFD + vài chục nghìn dòng ⇒ GB RAM.
  Sửa: kẹp số cột = số cột mẫu, dừng khi vượt `MaxRows + header`, chỉ đọc sheet khớp mẫu.
- **L-07** Lưu import người đang ở trùng `rowNumber` ⇒ `ToDictionary` throw ⇒ 500 (`TenancyImportChecker.cs:32,90`). Sửa: validator RowNumber duy nhất.

## N6 — Phân quyền tiền và dữ liệu nhạy cảm
**Chờ chốt:** M-12 (mô hình quyền "điều chỉnh tiền"), M-14 (cách SystemAdmin reset mật khẩu chủ trọ).

- **H-06** ✔ [Phân quyền] Phó quản lý sửa được tài khoản ngân hàng nhận tiền của khu (`PropertiesController.cs:91`, chỉ `OrgMember`) — số TK in lên mọi phiếu / tin M11 ⇒ chuyển hướng tiền người thuê. Plan mâu thuẫn: 02 PR-UC-10 (`OrgOwner`) vs 02 §9, api/properties.md.
  Sửa: chỉ chủ trọ; đổi TK ⇒ hỏi mật khẩu + audit nổi bật.
- **M-12** [Phân quyền, thiết kế] Quyền bỏ nợ riêng (ID-BR-23) lách được: phó quản lý không có quyền vẫn hủy phiếu → giảm trừ 100% → chốt 0đ; kỳ cuối "Không thu"; khoản bù "đã trả ngay" (không bị trần 1 tháng, tính là tiền chi); "Xử lý ngay" sinh PC; "Đã thu toàn bộ" khi hoàn tất. Sửa: 1 quyền "điều chỉnh tiền" cho thao tác giảm / chi tiền. (M-06 "Không hoàn" tạm giới hạn `OrgOwner`, sau này gộp vào quyền này.)
- **M-13** [Dữ liệu nhạy cảm] `POST renters/search` theo số giấy tờ (`SearchRenters.cs:29-52`) mở cho mọi phó quản lý, không kiểm `SensitiveDataAccess`; DTO lộ 4 số cuối + ngày sinh + giới tính ⇒ dò ~6.300 tổ hợp dựng lại CCCD, không audit. Sửa: cần quyền nhạy cảm + rate limit `sensitive`.
- **M-14** [Phân quyền, cần quyết] SystemAdmin reset mật khẩu chủ trọ, nhận mật khẩu tạm trong response (`AdminUserFeatures.cs:20-30`) ⇒ đăng nhập làm chủ trọ — trái README §2 / ID-BR-11. Sửa: gửi qua kênh của chủ trọ, hoặc audit hiện trong `/audit-logs` của tổ chức + báo chủ trọ.
- **M-15** [Audit] Export người thuê không `includeSensitive` (≤ 20.000 dòng: ngày sinh, SĐT, địa chỉ, liên hệ khẩn cấp) không audit (`ExportRenters.cs:105-107`, RP-BR-03). Sửa: luôn audit (người, bộ lọc, số dòng).
- **L-13** Hạn mức đăng nhập theo tài khoản (5/phút) = ngưỡng khóa (5 lần, 15 phút) ⇒ biết SĐT chủ trọ là khóa được tài khoản lặp lại. Sửa: khóa theo (IP, tài khoản) / lũy tiến.
- **L-14** Thêm phó quản lý / `PUT /me` báo `PhoneTaken/EmailTaken` toàn hệ thống ⇒ dò tài khoản tổ chức khác (có thể chấp nhận).
- **L-16** Audit đọc dữ liệu nhạy cảm qua hàng đợi RAM — mất ~2s khi crash, đầy thì chỉ log (trái LEG-06). Cân nhắc ghi đồng bộ cho reveal / export.
- **L-17** Ẩn danh bỏ sót ô ghi chú tự do (`termination_note`, `contracts.note`, `deposit_note`, `holdover_note`, lý do khoản phát sinh, ghi chú dòng phiếu, `payments.note`) và bản sao trong `audit_logs`.

## N7 — Tính tiền, điều chỉnh, validator
- **M-02** [Thu thừa] Ký lại ở khu tính theo ngày: HĐ cũ thu dịch vụ theo ngày kỳ cuối + HĐ mới thu trọn dịch vụ kỳ đầu (`InvoiceCalculator.ServiceFactor`) ⇒ 1 tháng internet thu ~1,5 lần (05 CT-BR-11 vs 07 BL-BR-04). Sửa: ký lại ⇒ dịch vụ kỳ cuối HĐ cũ = không thu.
- **M-04** [Sai tiền] Sửa tay chỉ số lượng dòng điện nước giá bậc: `Invoice.Lines.cs:213` = số lượng × giá bình quân của sản lượng cũ, không chia bậc lại (80→120 kWh: 238.500 thay vì 265.000). Sửa: dòng có `Price.Tiers` mà không gửi `amount` ⇒ `Price.Amount(q)`.
- **M-05** [Thu lại phần đã bỏ] Điều chỉnh chỉ số lần sau sau khi đã "Không trả lại": khoản cũ bị hủy nên không thấy (`RoomChargeRepository.cs:27-30`), chênh vẫn tính từ số lúc chốt (`AfterEndAdjustment.cs:38-42`, `PreviousFinalCorrection.cs:56`) ⇒ gộp lại phần chủ trọ đã quyết bỏ (50 kWh + 5 ⇒ 55). Sửa: trừ phần các khoản đã "Không trả lại", hoặc chặn điều chỉnh tiếp.
- **M-08** [Khóa giá] FE-BR-07 khóa theo max ngày dòng đã chốt trên **toàn tổ chức** (`FeePriceLockReader`) ⇒ nháp thiếu giá ở kỳ cũ không bao giờ thêm được giá ⇒ không chốt ⇒ không hoàn tất thanh lý, khu không đổi được cài đặt kỳ thu. Sửa: chỉ chặn khi bản giá mới làm đổi giá áp của một dòng đã chốt. (Độ chắc: trung bình)
- **M-19** Chỉ số gửi kèm kích hoạt / chuyển phòng / hóa đơn trả phòng không có validator (`ContractRequests.cs:33,39,52`): giá trị âm / quá lớn ⇒ `Meter.GuardValue` throw; trùng `meterId` ⇒ `ContractMeterReadings.cs:18` `ToDictionary` throw ⇒ 500; 3 số lẻ bị làm tròn sau khi kiểm đơn điệu. Sửa: validator `MeterReadingInput` dùng chung cho 3 lệnh.

## N8 — Concurrency và toàn vẹn
Cách sửa chung: khóa theo thứ tự HĐ → phiếu → công tơ → người thuê; map 40P01 → 409.

- **M-20** Chốt hóa đơn không khóa `fee_types` (`FinalizeInvoice.cs:33-39`) trái 04 §10 ⇒ thêm giá đúng lúc chốt: cả hai thành công, phiếu chốt giá cũ dù có bản giá `effective_from ≤ service_to` (trái FE-BR-07). Sửa: chốt (đơn + hàng loạt) khóa `fee_types` theo id, hoặc sửa plan.
- **M-23** Ẩn danh (tay / job) kiểm "còn HĐ mở / còn nợ" ngoài transaction, `RenterAnonymizer.cs:25-43` không kiểm lại; luồng HĐ dùng advisory lock, không khóa hàng `renters` ⇒ tạo HĐ cho người quay lại đúng lúc job chạy ⇒ hồ sơ bị xóa giấy tờ, không sửa lại được. Sửa: trong anonymize lấy `LockRentersAsync` + kiểm lại.
- **L-04** `CancelLiquidation.cs:32-34` khóa công tơ (`VoidFinalAsync`) trước người thuê (`CheckOccupantsAsync`) — ngược thứ tự kích hoạt ⇒ deadlock 40P01 khi song song kích hoạt HĐ ký lại; 40P01 chưa map ⇒ 500. Sửa: đổi thứ tự + map 40P01 → 409.
- **L-05** Đổi cài đặt kỳ thu (`PropertyBillingFeatures.cs:96-110`, khóa property) song song tạo phiếu (`GenerateInvoices.cs:67-70`, không khóa property) ⇒ nháp theo lịch cũ lọt `BILLING_SETTINGS_DRAFT_INVOICES`. Sửa: tạo phiếu `FOR SHARE` property.
- **L-06** Archive khu (`ArchiveProperty.cs:19-33`) không khóa phòng trước khi kiểm HĐ ⇒ nháp tạo song song nằm trên phòng đã archive (hủy được).
- **L-18** `AppDbContext.LockForUpdateAsync` (`:95-108`) khóa theo id không lọc `organization_id` ⇒ khóa tạm hàng tổ chức khác nếu biết UUID (không lộ dữ liệu).

## N9 — Hạ tầng và cấu hình
- **M-16** [DoS] `/health/ready` ẩn danh, miễn rate limit (`ApiSetup.cs:147-149`, `RateLimitingSetup.cs:181`), mỗi lần mở kết nối DB ⇒ cạn pool. Sửa: cache 5–10s hoặc chỉ mạng nội bộ.
- **M-17** [Cấu hình] Data Protection không bắt buộc `KeysPath` ngoài Development, khóa không mã hóa (`ApiSetup.cs:65-67`) ⇒ redeploy container mất khóa = mọi `id_number_encrypted` thành rác. Sửa: bắt buộc khóa bền vững + `ProtectKeysWith*`, dừng app khi thiếu. (Làm sớm trong nhóm — rủi ro mất dữ liệu.)
- **M-18** [Cấu hình] Seeder demo chỉ kiểm `Enabled`, không kiểm môi trường (`DatabaseInitializer.cs:55-56`); mật khẩu demo trong `appsettings.Development.json` đã commit ⇒ server test chạy Development ra Internet = vào được chủ trọ demo + stack trace. Sửa: seeder chỉ Development, mật khẩu từ user-secrets / env.
- **L-08** `Page` không có trần (`CommonRules.cs:58`) ⇒ `(Page-1)*PageSize` tràn int ⇒ 500.
- **L-12** Log ghi nguyên `PostgresException` (Detail "Failing row contains …" có PII) — `GlobalExceptionHandler.cs:49,102,105`. Sửa: log SqlState / constraint / table.
- **L-15** `AllowedHosts: "*"`; `docker-compose.yml:16` mở Postgres mọi interface (`postgres/postgres` khi thiếu `.env`) — bind `127.0.0.1`; mật khẩu không chặn danh sách phổ biến.

## Lẻ (chưa gom nhóm)
- **M-11** [Người ở] Đổi người đứng tên bị chặn nhầm `REPRESENTATIVE_IS_RESIDENT_ELSEWHERE`: `OccupantChecks.cs:60-61` so với `contract.StartDate` thay vì ngày bắt đầu đứng tên (gọi từ `ChangeRepresentative.cs:38`) ⇒ người vừa chuyển từ HĐ khác sang không làm đứng tên được. Sửa: dùng `RepresentativeSince ?? StartDate`. Cùng gốc: chuyển chính người đứng tên sang phòng khác với vai trò mặc định `Resident` ⇒ 409 (`MoveOccupant.cs:52` → `OccupantChecks.cs:66-75`), trái CT-BR-52 — bỏ qua HĐ nguồn khi kiểm.
- **L-09** `RoomQueries.HasOpenContractAsync` (`:57-61`) bỏ qua `RoomMoves` ⇒ ngừng dùng / bảo trì được phòng cũ ngay ngày chuyển phòng dù hiện "Đang thuê"; `ChangeRoomState.cs:98` bảo trì không xét HĐ nháp (PR-BR-05 chặn).
- **L-11** Thay công tơ hàng loạt không kiểm trùng serial (`MeterBulkReplace.cs:80-98`, MT-UC-08 có).
- **L-19** (M11, chưa code) Mã liên kết Zalo: bộ đếm dò mã trong RAM theo từng chat; hash SHA-256 không khóa. Sửa trước khi code: bộ đếm ở DB + giới hạn chung, HMAC có khóa, rút hạn mã.
- **L-20** Plan / docs mâu thuẫn, lỗi thời:
  - 07 BL-BR-30 (`ROOM_CHARGE_NO_OPEN_INVOICE` khi hóa đơn trả phòng đã chốt) vs BL-BR-32; api/room-charges.md dòng 90 vs 28–30.
  - 07 §4 CHECK `subtotal + discount_total >= 0` thiếu ngoại lệ hóa đơn trả phòng (BL-BR-10).
  - 06 MT-BR-11/13 chỉ số nhận phòng "tại `start_date`" vs 05 CT-BR-02/04 "tại ngày tính tiền".
  - 02 PR-BR-16 câu đầu vs danh sách nhãn (`ExpiredAwaitingDecision` / `Holdover`).
  - 11 NT-UC-10 (chỉ `OrgOwner`) vs 11 §9; 11 §4.1 "phiếu đã chốt bất biến" vs 07 BL-BR-14.
  - 07 §7 ghi `PUT /lines`, `POST /finalize` nhận `version` và `endReading` — code không có (2 tab sửa cùng nháp ghi đè nhau).
  - Còn nhắc "hủy chuyển phòng" / "khoản chốt điện nước": 05 §1, 06 §1, MT-UC-06, 07 BL-BR-38; 99 R-121, T-11 (`ROOM_TRANSFER_RENT_UNCHANGED`), T-12 tự mâu thuẫn; 07 §6 lý do bỏ qua khác code; 01 §6/§7/§14 còn "chưa code" `PUT /me`; 03 RT-BR-02 trỏ CT-BR-10 đã bỏ.
