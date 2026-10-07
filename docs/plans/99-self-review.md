# 99 — Self-review: lỗi tìm thấy & cách xử lý

> Review chéo toàn bộ plan (README, M01–M10) với trọng tâm: **bất đồng dữ liệu**, **mất toàn vẹn**, **race condition**,
> **rò rỉ dữ liệu giữa tổ chức**, **sai pháp lý**. Mỗi mục ghi: vấn đề → kịch bản lỗi → cách xử lý → nơi đã sửa.
> Trạng thái: ✅ đã sửa trong plan · ⚠️ rủi ro còn lại / cần quyết định.

## A. Lỗi của bộ plan cũ (room/tenant/lease/payment) — lý do thay thế

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-01 ✅ | `Room.Status` lưu cứng + đổi qua `MarkOccupied/MarkAvailable` do Lease gọi | Kích hoạt HĐ lỗi giữa chừng → phòng `Occupied` mà không có HĐ; 2 HĐ cùng lúc đều thấy `Available` | Trạng thái **dẫn xuất** từ HĐ theo ngày; chống trùng bằng **EXCLUDE constraint** trên khoảng thời gian | PR-BR-02, CT-BR-01 |
| R-02 ✅ | Tenant bắt buộc email, email unique | Người thuê không có email; trùng người giữa các chủ trọ | Định danh bằng **số giấy tờ** (hash) unique **trong tổ chức**; email/SĐT tùy chọn | RT-BR-01/02 |
| R-03 ✅ | Lease 1-1 tenant, không có người ở cùng | Không quản lý được tạm trú từng người, phí theo đầu người sai | Tách người đại diện & `contract_occupants` có ngày vào/ra | M05 |
| R-04 ✅ | Payment ghi thẳng vào lease, balance "tính sau" | Không biết tiền trả cho kỳ nào, không có chỉ số điện nước | Phiếu báo theo kỳ (M07) + phân bổ (M08) | M07, M08 |
| R-05 ✅ | Không có `organization_id` | Không làm được B2B | C-01 multi-tenant | README §6 |

## B. Lỗi tìm thấy khi review bản nháp mới — đã sửa

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-10 ✅ | Global query filter là lớp cô lập tổ chức **duy nhất** | Raw SQL / `IgnoreQueryFilters` / bug gán sai id → HĐ org A trỏ tới phòng org B | **FK composite `(organization_id, id)`** ở mọi quan hệ ⇒ DB từ chối liên kết chéo; test bắt buộc mỗi module | C-01 |
| R-11 ✅ | Kỳ thu với anchor 29–31 | Tháng 2 không có ngày 30 → crash / kỳ nhảy cóc | Kẹp về ngày cuối tháng (`AnchorDate`) | C-05 |
| R-12 ✅ | HĐ bắt đầu **trước** anchor trong cùng tháng (03/10, anchor 5) | 2 kỳ cùng bắt đầu trong tháng 10 → "tháng thu" không định danh được kỳ, sinh 2 phiếu / bỏ sót | **Gộp** kỳ lẻ vào kỳ kế tiếp; prorate = Σ overlap/len kỳ chuẩn, làm tròn 1 lần | C-05 |
| R-13 ✅ | Đổi giá thuê / khoản thu **giữa kỳ** hoặc cho kỳ đã chốt | Phiếu đã chốt mâu thuẫn với giá trong HĐ; không biết áp giá nào | Chỉ đổi từ **đầu kỳ** và ≥ kỳ đầu tiên chưa chốt | CT-BR-05/06 |
| R-14 ✅ | Đổi `billing_anchor_day` sau khi đã có phiếu | Chuỗi kỳ vỡ, chồng/hở ngày | Khóa cài đặt kỳ thu sau phiếu đầu | CT-BR-04 |
| R-15 ✅ | Thêm bảng giá **hồi tố** | Giá trong danh mục ≠ giá trên phiếu đã chốt cho cùng kỳ | `effective_from` > ngày cuối dòng phiếu đã chốt | FE-BR-07 |
| R-16 ✅ | Đổi nhóm khoản thu (Metered → Fixed) | Đăng ký, công tơ, phiếu cũ mất nghĩa | `group` bất biến | FE-BR-02 |
| R-17 ✅ | Lấy "chỉ số cũ" = chỉ số gần nhất theo ngày | Chỉ số ad-hoc / sửa lùi ngày → tính trùng hoặc bỏ sót sản lượng | Chuỗi **liên kết theo đoạn đo đã lập phiếu**; UNIQUE start/end reading trên segment chưa void | MT-BR-12, M07 §4 |
| R-18 ✅ | Sửa chỉ số sau khi phiếu đã chốt | Phiếu và chỉ số lệch | Khóa chỉ số đã dùng ở phiếu Finalized | MT-BR-06 |
| R-19 ✅ | Lập phiếu bỏ qua 1 kỳ, hoặc hủy phiếu kỳ giữa | Kỳ sau "nuốt" sản lượng kỳ bị bỏ; lập lại kỳ giữa lấy sai chỉ số đầu | **Lập phiếu tuần tự** + **hủy LIFO** | BL-BR-21/22, M06 §3.3 |
| R-20 ✅ | Thay công tơ / công tơ quay vòng về 0 | Sản lượng âm hoặc khổng lồ | Đoạn đo nhiều segment; quay vòng = thay công tơ | MT-BR-09, M06 §3.3 |
| R-21 ✅ | Giảm giá cộng dồn (% + cố định + thủ công) | Tổng phiếu âm | Cap giảm giá quy tắc, chặn giảm thủ công làm âm, CHECK `total ≥ 0` | BL-BR-10 |
| R-22 ✅ | % giảm tính trên giá gốc hay giá đã sửa tháng? | 2 người dùng hiểu khác nhau → khiếu nại | Thứ tự tính cố định: override → % trên tiền phòng sau override, không lũy kế | BL-BR-08 |
| R-23 ✅ | "Sửa tiền phòng tháng" bị hiểu là sửa giá phòng | Giá HĐ bị đổi ngầm cho các tháng sau | Override chỉ trên dòng Rent của phiếu nháp, giữ `original_amount` | BL-BR-07 |
| R-24 ✅ | Chốt phiếu nháp đã cũ (giá/chỉ số đổi sau khi tạo) | Phiếu chốt sai số liệu | Chốt = **tính lại & so sánh** trong transaction; lệch → 409 | BL-BR-12 |
| R-25 ✅ | Bấm tạo phiếu 2 lần / 2 người cùng tạo | Phiếu trùng kỳ | Partial unique `(contract, type, period_start)` + Idempotency-Key | BL-BR-01, C-08 |
| R-26 ✅ | Cấp số phiếu song song | Trùng số | Bảng sequence + `UPDATE … RETURNING` + UNIQUE | BL-BR-13 |
| R-27 ✅ | Phân bổ thanh toán song song | `paid_amount` > tổng; cache lệch Σ phân bổ | Khóa contract → invoice; cập nhật cache cùng transaction; CHECK | PM-BR-05, M08 §10 |
| R-28 ✅ | Phân bổ tiền của HĐ A vào phiếu của HĐ B | Công nợ sai | FK composite `(organization_id, contract_id, invoice_id)` | M08 §4, M07 §4 |
| R-29 ✅ | Void phiếu đã có tiền | Tiền "mồ côi" | Void yêu cầu `paid_amount = 0` | BL-BR-15 |
| R-30 ✅ | Số dư cọc âm do hoàn 2 lần song song | Hoàn vượt cọc | Sổ cọc bất biến + kiểm số dư dưới khóa contract | PM-BR-09 |
| R-31 ✅ | Prepaid: thanh lý giữa kỳ đã thu trọn tháng | Hoàn tiền làm phiếu Final âm | Cắt về 0, phần dư thành **CreditNote** → số dư có → hoàn cùng cọc | BL-BR-17, PM-BR-17 |
| R-32 ✅ | Người thuê bỏ trốn còn nợ | Không bao giờ hoàn tất thanh lý được | Xóa nợ `WriteOff` có lý do, không tính doanh thu | PM-BR-16 |
| R-33 ✅ | Thanh lý khi đã lập phiếu kỳ sau ngày trả phòng | Thu tiền kỳ người thuê không ở | Chặn bắt đầu thanh lý nếu còn phiếu/chỉ số sau `actual_end_date` | CT-BR-11 |
| R-34 ✅ | Hủy thanh lý sau khi phòng đã có HĐ mới | 2 HĐ chồng lấn | EXCLUDE kiểm lại khi `actual_end_date = NULL` → 409 | M05 §3.3 |
| R-35 ✅ | HĐ hết hạn tự chuyển "Expired" | Ngừng lập phiếu trong khi người thuê vẫn ở → thất thu | Hết hạn vẫn `Active` (cờ dẫn xuất `IsOverdue`) | CT-BR-03 |
| R-36 ✅ | HĐ đăng ký điện nhưng phòng chưa có công tơ | Phiếu không bao giờ chốt được, phát hiện muộn | Chặn ngay khi kích hoạt | CT-BR-02, MT-BR-11 |
| R-37 ✅ | Kích hoạt HĐ với ngày bắt đầu tương lai xa | Phòng hiển thị "đang ở" sai; chỉ số bàn giao vô nghĩa | Kích hoạt = bàn giao thật: `start_date ≤ hôm nay + 1`; trạng thái phòng tính theo ngày | CT-BR-02, PR-BR-02 |
| R-38 ✅ | Phí theo đầu người khi người ở vào/ra giữa kỳ | Mỗi lần tính lại ra số khác | Snapshot số người tại `period_start` | FE-BR-14, BL-BR-04 |
| R-39 ✅ | Đổi tên khoản thu/mã phòng sau khi chốt | Phiếu cũ hiển thị tên mới | Snapshot trên dòng phiếu | C-06 |
| R-40 ✅ | Lệch múi giờ (server UTC) | Phiếu cấp lúc 0h–7h sáng VN mang ngày hôm trước; "hôm nay" sai khi kiểm hạn | `TimeProvider` theo Asia/Ho_Chi_Minh | C-04 |
| R-41 ✅ | Sai số làm tròn | Tổng phiếu ≠ Σ dòng; lệch vài đồng khi đối chiếu | `decimal`, làm tròn ở dòng, tổng = Σ dòng | C-03 |
| R-42 ✅ | Deadlock giữa các lệnh khóa nhiều bảng | Timeout ngẫu nhiên khi chốt hàng loạt | Thứ tự khóa toàn cục | C-07 |
| R-43 ✅ | Trùng người thuê do khác định dạng số CCCD (khoảng trắng) | 2 hồ sơ cho 1 người | Chuẩn hóa trước khi hash, 1 hàm duy nhất | M03 §10 |
| R-44 ✅ | Ẩn danh hóa nhưng tên vẫn nằm trong snapshot phiếu & audit log | Vi phạm yêu cầu xóa dữ liệu (L11) | Ẩn danh thay snapshot + xóa giá trị trong audit | RT-BR-06, BL-BR-14 |
| R-45 ✅ | `owner_id` đa hình của file không có FK | File trỏ sang đối tượng org khác | Kiểm tra owner cùng org khi upload; owner không bị xóa vật lý | FS-BR-04 |
| R-46 ✅ | Excel formula injection (tên `=HYPERLINK(...)`) | Mở file chạy công thức độc | Escape tiền tố `'`, ghi kiểu tường minh | RP-BR-02 |
| R-47 ✅ | Export lộ số CCCD | Rò rỉ dữ liệu cá nhân | Che mặc định + quyền + audit | RP-BR-03, LEG-06 |
| R-48 ✅ | Admin nền tảng xem được dữ liệu người thuê | Vi phạm vai trò bên xử lý dữ liệu (L11) | SystemAdmin bị chặn khỏi API nghiệp vụ | ID-BR-11 |
| R-49 ✅ | Tạm ngưng tổ chức nhưng access token còn hạn | Vẫn thao tác 15 phút | Middleware kiểm trạng thái (cache ≤ 60s) + SecurityStamp | ID-BR-08 |
| R-50 ✅ | Địa chỉ 3 cấp (có quận/huyện) | Sai từ 01/07/2025 (bỏ cấp huyện) | Mô hình 2 cấp + `address_text` cho địa chỉ cũ | C-12, LEG-07 |
| R-51 ✅ | Gọi "hóa đơn" | Nhầm với hóa đơn GTGT/HĐĐT | "Phiếu báo tiền phòng" | LEG-08 |
| R-52 ✅ | .NET 8 hết hỗ trợ 10/11/2026 | Chạy production trên runtime không còn vá bảo mật | Đã nâng net10.0 (EF Core 10, Npgsql 10, Swashbuckle 10); 305 test xanh, migration không đổi | README §3, P0-01 |
| R-53 ✅ | Mật khẩu DB trong `appsettings.json` được commit | Lộ secret khi dùng mật khẩu thật | User Secrets / env | C-11, P0-02 |
| R-54 ✅ | Test bằng InMemory/SQLite | Không kiểm được EXCLUDE, partial index, xmin → test xanh nhưng production lỗi | Testcontainers PostgreSQL | C-13 |

## B2. Rà lại hợp đồng theo luật (bổ sung)

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-55 ✅ | Không lưu **bên cho thuê** ở đâu | Hợp đồng in ra thiếu nội dung bắt buộc (Điều 163 khoản 1), không dùng làm giấy tờ tạm trú được | Thông tin bên cho thuê theo khu + snapshot khi kích hoạt | PR-BR-12, CT-BR-19 |
| R-56 ✅ | Thiếu **thời điểm có hiệu lực** | Tranh chấp ngày bắt đầu nghĩa vụ (nhất là cọc trước ngày bàn giao) | `effective_date` mặc định = ngày ký (Điều 164) | CT-BR-20 |
| R-57 ✅ | Người dưới 18 tuổi đứng tên ký | Hợp đồng có thể vô hiệu (BLDS Điều 117) | Chặn khi kích hoạt | CT-BR-18 |
| R-58 ✅ | Đơn giá điện nước chỉ nằm ở danh mục khu | Chủ trọ đổi giá → không chứng minh được giá đã thỏa thuận lúc ký | `utility_price_snapshot` | CT-BR-19 |
| R-59 ✅ | Kết thúc hợp đồng không ghi lý do | Chủ trọ đơn phương chấm dứt ngoài các trường hợp luật cho phép mà không được cảnh báo | `termination_reason` + `termination_ground` theo Điều 172 | CT-BR-21 |
| R-60 ✅ | Không có biên bản tài sản bàn giao | Tranh chấp trừ cọc khi trả phòng | `contract_assets` + gợi ý trừ cọc có xác nhận | CT-BR-23 |
| R-61 ✅ | Mật khẩu tạm không hết hạn | Mật khẩu tạm bị lộ dùng được mãi | Hạn 72h | ID-BR-20 |

## B3. Rà nghiệp vụ sau khi code M01 (phó quản lý), M02, M03 (cơ bản), M05 — 02/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-62 ✅ | Bản chụp lúc ký chỉ có bên cho thuê | Sửa hồ sơ người thuê / mã phòng / diện tích sau khi ký ⇒ hợp đồng in lại sai so với bản đã ký (Điều 163: tên, địa chỉ các bên, mô tả nhà) | `signing_snapshot` gồm bên cho thuê + bên thuê + phòng + ngân hàng; migration chuyển dữ liệu cũ | CT-BR-19 |
| R-63 ✅ | Hủy hợp đồng nháp không nhả biển số xe | Biển số bị khóa vĩnh viễn trong tổ chức, không đăng ký được ở phòng khác | Hủy nháp ⇒ kết thúc đăng ký xe | `Contract.Cancel` |
| R-64 ✅ | Hợp đồng đã kích hoạt nhưng ngày bàn giao là ngày mai ⇒ phòng hiện "Trống" | Nhân viên khác tưởng phòng trống, tạo hợp đồng mới (DB vẫn chặn khi kích hoạt nhưng gây nhầm lẫn) | Hiển thị "Giữ chỗ" | PR-BR-02 |
| R-65 ✅ | Nhập HĐ cũ có ngày hiệu lực thỏa thuận sớm hơn ngày bắt đầu, không ghi ngày ký | Ngày ký mặc định > ngày hiệu lực ⇒ vi phạm CHECK ⇒ 422 khó hiểu | Ngày ký mặc định không muộn hơn ngày hiệu lực | `Contract.Activate` |
| R-66 ✅ | Phụ lục đổi giá cho phép trên hợp đồng nháp | Sửa nháp sau đó xóa mất phụ lục; phụ lục cho HĐ chưa ký vô nghĩa | Chỉ hợp đồng Active | CT-BR-05 |
| R-67 ✅ | Đổi loại giấy tờ (CCCD → hộ chiếu) mà không nhập lại số | Số CCCD cũ gắn nhãn hộ chiếu; hash sai loại ⇒ chống trùng / tìm kiếm hỏng | Bắt buộc nhập lại số khi đổi loại (người thuê + bên cho thuê) | `ID_NUMBER_REQUIRED` |
| R-68 ✅ | Tìm theo số giấy tờ mặc định loại CCCD | Không tìm được người nước ngoài theo hộ chiếu nếu không chọn loại | Không chọn loại ⇒ thử mọi loại | `SearchRenters` |
| R-69 ✅ | Tạo nháp không khóa phòng | Tạo nháp song song với ngừng dùng phòng ⇒ nháp trên phòng đã ngừng dùng | Khóa hàng phòng khi tạo nháp | `CreateContractHandler` |
| R-70 ✅ | Số HĐ tự sinh trùng số người dùng tự nhập trước đó | Tạo hợp đồng báo 409 khó hiểu | Lấy số kế tiếp (tối đa 5 lần) | `CreateContractHandler` |
| R-71 ✅ | Lịch kỳ thu cắt tại ngày hết hạn thỏa thuận | HĐ quá hạn vẫn đang ở nhưng lịch kỳ thu dừng ⇒ M07 sẽ bỏ sót kỳ | Chỉ cắt tại ngày trả phòng thực tế (CT-BR-03) | `GetBillingPeriods` |
| R-72 ✅ | (phát hiện khi code) Entity con có Id do domain sinh bị EF lưu bằng UPDATE | Thêm người ở / xe / tài sản / thành viên nhóm lỗi 409 | `ValueGeneratedNever` cho mọi Id | `AppDbContext` |
| R-73 ✅ | (phát hiện khi code) Hoàn tất thanh lý trước ngày trả phòng | Phòng hiện trống khi người thuê còn ở | Chỉ hoàn tất từ ngày trả phòng; HĐ Ended vẫn chiếm phòng tới hết ngày trả | CT-BR-12 |

## B4. Rà theo hợp đồng giấy thực tế & case gia đình — 02–03/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-74 ✅ | Key lỗi validation không khớp body (`POST /renters`, tài sản bàn giao, phòng) | UI không gắn được lỗi vào ô nhập (`renter.fullName` thay vì `fullName`, `maxOccupants` thay vì `spec.maxOccupants`) | Helper `FlattenedValidator`; tiền tố `spec.`; test hồi quy | Application |
| R-75 ✅ | HĐ thực tế có tiêu đề, điều khoản theo mục, thỏa thuận điện (giá nhà nước, trả cuối tháng), nước (đ/người, trả đầu tháng), wifi — DB không có chỗ chứa | Mất nội dung khi nhập HĐ giấy; mỗi chủ trọ một kiểu HĐ | Mẫu hợp đồng + trường tùy biến chép vào HĐ | CT-BR-25, 26 |
| R-76 ✅ | HĐ giấy thiếu điều khoản giải quyết tranh chấp, thời điểm & hiện trạng bàn giao (Luật Nhà ở 2023 Điều 163) | HĐ in ra thiếu nội dung bắt buộc | Mẫu gợi ý có sẵn các mục này | Mẫu gợi ý |
| R-77 ✅ | HĐ không cọc vẫn nhận cọc mặc định của phòng | Sổ cọc sai, phiếu quyết toán hoàn cọc không có thật | Mẫu `no_deposit` ⇒ cọc = 0, gửi > 0 bị chặn; lọc `hasDeposit` | CT-BR-27 |
| R-78 ✅ | Người ở không có quan hệ với người đứng tên; dữ liệu vô lý (vợ là nam, con lớn tuổi hơn cha, 2 vợ, vợ chồng chưa đủ tuổi) | Không lập được tờ khai tạm trú chung hộ; danh sách gửi công an sai | Danh mục quan hệ theo TT 55/2021 Điều 6 (sửa bởi TT 66/2023) + kiểm tra hợp lý; kiểm lại khi kích hoạt | CT-BR-28, 29 |
| R-79 ✅ | Người chưa thành niên ở trọ cùng người không phải cha mẹ / giám hộ | Tờ khai tạm trú bị từ chối (Luật Cư trú Điều 28) | Bắt buộc `guardian_consent` | CT-BR-30 |
| R-80 ✅ | Một người được thêm vào 2 phòng cùng lúc | Danh sách người ở / tạm trú mâu thuẫn; đếm sai số người tính định mức điện (L9) | Chặn ở Application; chuyển đi – vào ở cùng ngày hợp lệ | CT-BR-31 |
| R-81 ✅ | (phát hiện khi code) HĐ đang thanh lý: `move_out_date` của người ở chỉ ghi khi hoàn tất | Kiểm tra ở 2 phòng chặn nhầm HĐ mới; xuất Excel theo ngày vẫn liệt kê người đã trả phòng | Ngày ra hiệu lực = `COALESCE(move_out_date, actual_end_date)` | M05 §4, E1 |
| R-82 ✅ | Xuất Excel: tên người nhập dạng `=HYPERLINK(...)`, SĐT / CCCD mất số 0 đầu | Chèn công thức độc hại; sai dữ liệu | Quote-prefix chuỗi bắt đầu `= + - @`; cột chữ định dạng `@` | RP-BR-02 |

## B5. Rà nghiệp vụ hợp đồng lần 2 — 03/10/2026 (mỗi lỗi có test tái hiện trước khi sửa)

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-83 ✅ | Nháp cho phép "vẫn thêm" vượt sức chứa nhưng kích hoạt luôn chặn, không có cách vượt | Gia đình vợ chồng + con nhỏ ở phòng 2 người không kích hoạt được HĐ | Kích hoạt nhận `overrideCapacity` (ghi audit); audit cả khi thêm người ở | CT-BR-09 |
| R-84 ✅ | Ghi "chuyển đi" lần 2 ghi đè ngày ra cũ | Kéo dài ngày ra chồng lên nơi ở mới ⇒ một người ở 2 phòng (lọt CT-BR-31) | Đã chuyển đi thì không ghi lại → 409 `OCCUPANT_ALREADY_MOVED_OUT` | CT-BR-08 |
| R-85 ✅ | Hủy thanh lý khi người ở đã sang phòng khác | Người ở trở lại "đang ở" vô thời hạn ở phòng cũ ⇒ ở 2 phòng | Kiểm CT-BR-31 khi hủy thanh lý | CT-BR-34 |
| R-86 ✅ | Kiểm "ở nơi khác" cũng bắt HĐ cùng phòng | Lỗi `OCCUPANT_LIVES_ELSEWHERE` che lỗi gốc `ROOM_PERIOD_OVERLAP` | Chỉ xét phòng khác | `OccupantChecks` |
| R-87 ✅ | Không giới hạn tiền cọc (plan §8 có, code chưa làm) | Nhập thừa số 0 (100 triệu thay vì 1 triệu) ⇒ sổ cọc / hoàn cọc sai | ≤ 12 tháng tiền thuê | CT-BR-32 |
| R-88 ✅ | Ngừng dùng phòng / khu, bảo trì được ngay **trong ngày trả phòng** | Phòng "Đang thuê" (người thuê còn dọn đồ) nhưng đã bị ngừng dùng | Tính HĐ `Ended` có ngày trả phòng ≥ hôm nay là còn chiếm phòng | CT-BR-33 |
| R-89 ✅ | Dời ngày bắt đầu nháp về sau, xe đã đăng ký giữ nguyên ngày cũ | Xe "giữ" trước ngày bắt đầu HĐ ⇒ M07 tính phí giữ xe trước khi thuê | Dời `registered_from` theo | CT-BR-35 |
| R-90 ✅ | Plan lệch code: PUT HĐ Active sửa ghi chú (plan có, code không), phân quyền phó quản lý "P3 theo khu" (code: toàn quyền) | Người làm UI / tester hiểu sai | Sửa plan theo thực tế, ghi phần chưa làm là P2/P3 | 05-contract §7, §9 |

## B6. Rà code M04 + bản in + báo cáo, đối chiếu plan — 04/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-91 ✅ | Bản in lấy khoản thu **hiện tại** thay vì thỏa thuận lúc ký | Khu tăng giá nước sau khi ký ⇒ in lại HĐ cũ ra giá mới — sai văn bản đã ký | In theo `utility_price_snapshot` + các thay đổi sau ký thành dòng "Từ ngày …" | CT-UC-15 |
| R-92 ✅ | Plan lệch code: `utility_price_snapshot` (CT-BR-19) có cột nhưng chưa bao giờ ghi | Không chứng minh được đơn giá đã thỏa thuận (R-58) | Chụp khi kích hoạt; API `utilityPrices` | CT-BR-19 |
| R-93 ✅ | Định dạng tiền làm tròn đơn giá lẻ | 15.500,5đ/m³ in thành 15.501 đ | `#,##0.##` | `VietnameseMoney` |
| R-94 ✅ | Bản in liệt kê cả người đã chuyển đi | Văn bản sai danh sách người ở | Chỉ người còn ở tại thời điểm in (HĐ kết thúc: tại ngày trả phòng) | CT-UC-15 |
| R-95 ✅ | Plan lệch code: chưa có cảnh báo số xe ≠ phí giữ xe (CT-BR-22) | Thu thiếu / thừa phí giữ xe | `fee_types.vehicle_type` + cảnh báo | CT-BR-22 |
| R-96 ✅ | Plan lệch code: chưa cảnh báo bên cho thuê đơn phương chấm dứt báo trước < 30 ngày (CT-BR-21) | Chủ trọ vi phạm Điều 172 mà không biết | `liquidation_started_on` + cảnh báo | CT-BR-21 |
| R-97 ✅ | Plan lệch code (Q7): mẫu gợi ý vẫn bắt buộc nhập điện nước bằng trường tùy biến | Nhập 2 nơi (khoản thu + trường), dễ lệch | Mẫu gợi ý thay bằng quy định sinh hoạt (khách qua đêm, giờ đóng cổng, thú cưng) | `ContractTemplatePresets` |
| R-98 ✅ | **Plan tự mâu thuẫn**: CT-BR-10 (ở 2 nơi → cảnh báo) vs CT-BR-31 (→ chặn) | Người làm UI / tester hiểu sai | Giữ CT-BR-31; CT-BR-10 ghi là đã thay thế; đứng tên nhiều phòng vẫn được | 05-contract |

## B7. Xử lý 16 bất cập theo quyết định của chủ trọ — 04/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-99 ✅ | Phó quản lý xem / xuất / in được số giấy tờ đầy đủ; plan các module ghi quyền khác nhau | Lộ dữ liệu cá nhân qua tài khoản phụ | Quyền dữ liệu nhạy cảm do chủ trọ cấp, đọc DB mỗi request; reveal / export đầy đủ → 403; in → che số. Thống nhất dòng phân quyền M02–M10 = "P1 như chủ trọ, trừ dữ liệu nhạy cảm" | ID-BR-21/22, LEG-06 |
| R-100 ✅ | M04 tự gắn Điện/Nước vào mọi HĐ + MT-BR-11 bắt phòng có công tơ | Phòng tính nước theo người bị chặn kích hoạt; HĐ và công tơ lệch nhau | Điện nước theo công tơ **đi theo phòng**, không gắn HĐ (400 `FEE_METERED_FOLLOWS_ROOM`); MT-BR-11 thành cảnh báo | FE-BR-17, CT-BR-02, MT-BR-11, BL-BR-05 |
| R-101 ✅ | Chỉ số đầu HĐ mới lấy cứng chỉ số cuối HĐ trước | Người mới trả tiền điện dùng khi sửa chữa / phòng trống | Chỉ số nhận phòng chỉnh được tại ngày vào ở; sản lượng khoảng trống không tính cho ai, hiện ở báo cáo | MT-BR-13/14 |
| R-102 ✅ → đảo lại ở R-130 | Bậc thang điện (M04) chưa được M07 dùng; chủ trọ không dùng bậc thang | Tính sai / phức tạp không cần thiết | Một giá; bỏ `tiers` khỏi code + DB | FE-BR-15, BL-BR-05 |
| R-103 ✅ | Thay công tơ giữa kỳ khi công tơ cũ hỏng không có chỉ số tháo | Không lập được phiếu hoặc mất sản lượng | Dòng thủ công gắn khoản thu trong phiếu nháp | BL-BR-23 |
| R-104 ✅ | M08 chưa tính HĐ không cọc / mức cọc | Ghi cọc mâu thuẫn với HĐ | Cọc trên HĐ chỉ lưu trữ; sổ cọc độc lập | PM-BR-19 |
| R-105 ✅ | Plan lệch code: 409 trùng giấy tờ không kèm id hồ sơ cũ | UI phải tự tìm lại hồ sơ | `existingRenterId` trong ProblemDetails | RT-BR-02 |
| R-106 ✅ | Nhánh người nước ngoài (RT-BR-04, `ForeignerResidence`, L7 chưa xác minh) | Làm tính năng không ai dùng, căn cứ chưa chắc | Bỏ khỏi phạm vi; giữ `nationality` / `Passport` | L7, RT-BR-04/07 |
| R-107 ✅ | Chưa có lý do "bỏ đi không báo" | Chủ trọ chọn sai lý do; không có hướng dẫn xử lý đồ để lại / cọc / tạm trú | `Abandoned` + ghi chú bắt buộc + cảnh báo hướng dẫn | CT-BR-41 |
| R-108 ✅ | HĐ không thời hạn: luật cho chấm dứt sau 90 ngày nhưng thanh lý chỉ hẹn trước tối đa 60 ngày | Không bao giờ báo trước đủ 90 ngày trong hệ thống ⇒ luôn bị cảnh báo sai | Căn cứ `IndefiniteTermNotice` được hẹn tới 90 ngày, cảnh báo < 90 | CT-BR-42, L4b |
| R-109 ✅ | Lý do `Expired` chọn được cho HĐ không thời hạn / trả sớm | Báo cáo lý do kết thúc sai; che giấu chấm dứt trước hạn | 422 `EXPIRED_REASON_INVALID` | CT-BR-21 |
| R-110 ✅ | Khoản gắn sau khi ký nhưng hiệu lực từ ngày bắt đầu không có trên bản in | Văn bản thiếu khoản đang thu | In mọi khoản không có trong bản chụp lúc ký | CT-BR-43 |
| R-111 ✅ | M02 chưa tính ngày trả phòng (đã có ở M05 + code) | Đếm phòng trống sai | PR-BR-02/05 | 02-property-room |
| R-112 ✅ | Plan lệch code: export không cùng transaction | Đếm và dữ liệu lệch nhau khi có người sửa giữa chừng | `REPEATABLE READ` | RP-BR-09 |

## B8. Quyết định nghiệp vụ phiếu tiền phòng, công tơ, hết hạn — 04/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-113 ✅ | 3 nhóm khoản thu khó hiểu; phòng tính nước theo người vẫn "có" nước theo công tơ | In thừa dòng nước công tơ; chủ trọ nhầm nhóm | 2 nhóm: Điện nước (công tơ, theo phòng) / Dịch vụ (theo phòng · đầu người · số gói); phụ thu nhập trên phiếu | M04 FE-06 |
| R-114 ✅ | Người đứng tên rời đi, người khác vẫn ở | HĐ mất người chịu trách nhiệm mà không ai biết | Cờ `REPRESENTATIVE_MOVED_OUT` + ký lại cho người còn ở | CT-BR-44, CT-UC-21 |
| R-115 ✅ | HĐ hết hạn mà người thuê vẫn ở | Báo phòng trống sai; không có quyết định của chủ trọ | Phòng vẫn Đang thuê; chủ trọ chọn gia hạn / ở tiếp chưa ký / thu lại phòng | CT-BR-45, CT-UC-22, PR-BR-02 |
| R-116 | Phiếu nháp chỉ cho sửa tiền phòng | Không xử lý được case lệch thực tế | Sửa tay mọi ô, đánh dấu + giữ giá trị hệ thống; tính lại theo phòng / tầng / khu | BL-BR-07, BL-UC-06 |
| R-117 | Thay công tơ giữa kỳ | Thiếu / thu trùng tiền điện | 2 cách: phụ thu tay hoặc công tơ phiên bản tự cộng | BL-BR-23, MT-BR-09/15 — ✅ đợt 1 M06: ghi thay công tơ phiên bản; phần tính tiền chờ M07 |
| R-118 | Giá điện đổi giữa kỳ | Tranh cãi giá nào | Cả kỳ tính giá mới (giá tại ngày cuối kỳ sử dụng) | BL-BR-05 |
| R-119 ✅ | Bản in / bản chụp giá lấy mọi khoản điện nước của khu | Phòng tính nước theo người vẫn in dòng nước theo công tơ | Chỉ lấy khoản có công tơ thực ở phòng; công tơ lắp sau khi ký in theo giá ngày bắt đầu | FE-BR-17, M06 đợt 1 |
| R-120 ✅ | Kích hoạt / thanh lý không ghi mốc công tơ | Không có chỉ số đầu / cuối để tính tiền điện của từng người thuê | Bắt buộc chỉ số nhận phòng (hoặc dùng số mới nhất) và chỉ số cuối | MT-BR-13, CT-BR-12 |
| R-121 ✅ | Phiếu đầu tiên của HĐ nhập từ trước bị chặn vì "chưa lập phiếu kỳ trước" (BL-BR-21) | Không lập được phiếu nào cho HĐ cũ đưa vào phần mềm | Phiếu đầu tiên lập ở kỳ bất kỳ; chỉ số đầu = chỉ số gần nhất ≤ đầu kỳ; từ phiếu thứ 2 mới bắt tuần tự | BL-BR-21 |
| R-122 ✅ | Prepaid: điện nước kỳ cuối không có phiếu nào thu (chưa có phiếu Final) | Mất tiền điện tháng cuối | Phiếu kỳ cuối gộp điện nước tới chỉ số cuối HĐ | M07 đợt 1 |
| R-123 ✅ | Thu vượt nợ khi chưa có số dư có | Tiền thừa không có chỗ ghi ⇒ lệch sổ | Chặn `PAYMENT_EXCEEDS_DEBT` tới đợt 2 | M08 đợt 1 |
| R-124 ✅ | EF gộp các index unique trùng cột (chỉ số lắp / nhận phòng) | Migration thiếu ràng buộc ⇒ trùng chỉ số | Đặt tên index + `HasDatabaseName`, kiểm tra trong DB | M06 |

## B9. Hoàn thiện đợt 1 — quyết định 04/10/2026 (✅ đã code 05/10, trừ cư trú để sau)

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-125 ✅ | Prepaid: phiếu kỳ cuối lập trước khi báo trả phòng | Điện nước kỳ cuối không ai thu | Phiếu quyết toán thu phần còn thiếu (đợt 1); hoàn tiền phòng chưa ở để sau | BL-UC-11, BL-BR-02/17 |
| R-126 ✅ | Hoàn tất thanh lý không xét tiền | HĐ kết thúc còn nợ / sót tiền | Bắt phiếu quyết toán đã chốt; còn nợ ⇒ cảnh báo + "Đã thu toàn bộ" / "Bỏ nợ" | CT-BR-12, PM-UC-14, PM-BR-16 |
| R-127 ✅ | PM-BR-13 (HĐ kết thúc không thu) mâu thuẫn code (cho thu) | Khó hiểu khi nào được thu | Hoàn tất buộc hết nợ ⇒ HĐ kết thúc không cần thu nữa — giữ PM-BR-13, sửa code | PM-BR-13 |
| R-128 ✅ | Số phiếu thu lấy năm ngày thu | Ghi bù đầu năm ⇒ số PT lộn năm | Năm của ngày lập | PM-BR-15 |
| R-129 ❌ | Thu theo chu kỳ nhiều tháng chưa có | Phòng đóng 3 tháng / lần phải sửa tay từng phiếu | ~~`rent_cycle_months`~~ — **đã bỏ 07/10/2026** (R-134) | — |
| R-130 ✅ | Chỉ một giá điện nước | Khu tính theo bậc (như EVN) không dùng được | Bản giá một giá hoặc theo bậc | FE-BR-15, BL-BR-05 |
| R-131 | Cư trú thiết kế nặng (bản ghi, tạm vắng, sự kiện domain) chưa code | Khối việc lớn không cần ở P1 | P1 = người ở trong phòng (M05); phần thủ tục ⏸ P2 | M03 |
| R-132 ✅ | Phòng ngừng dùng có cần tháo công tơ | Bắt tháo thì mất số đo khi sửa chữa | Không tháo; HĐ mới chọn số khi kích hoạt | PR-BR-05, MT-BR-17 |
| R-133 | Phạm vi đợt 1 quá rộng (cọc, hoàn tiền, bồi thường, cư trú) | Kéo dài đợt 1, nghiệp vụ chưa ổn | Để sau toàn bộ phần cọc / chi tiền ra / cư trú; đợt 1 chỉ thu | M03, M05, M07, M08 |

## B10. Rà plan sau đợt 1 — quyết định 07/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-134 ✅ | Chu kỳ đóng tiền phòng nhiều tháng cắt tại `end_date` | Chu kỳ 3 tháng từ 01/08, hết hạn 15/09 rồi gia hạn / ở tiếp ⇒ 16/09–31/10 không phiếu nào thu tiền phòng; trả phòng sớm ⇒ thu thừa nhiều tháng | **Bỏ chu kỳ nhiều tháng**, chỉ thu hằng tháng (chưa có phương án xử lý trọn) | BL-BR-03/26, C-05, migration `RemoveRentCycleAddInvoiceRefund` |
| R-135 ✅ | M04 lấy giá tại đầu kỳ, M07 / code lấy tại cuối kỳ (điện nước) và đầu kỳ (dịch vụ) | Hai tài liệu nói khác nhau; dịch vụ tăng giá vẫn tính giá cũ cả kỳ | **Giá theo phiên bản**: mọi khoản lấy bản mới nhất tới ngày cuối khoảng tính, áp cả dòng, không chia nửa kỳ | FE-BR-10/11, BL-BR-04/05 |
| R-136 ✅ | Trả phòng sớm khi đã đóng trọn kỳ: phiếu quyết toán bỏ qua, không dấu vết | Chủ trọ quên nghĩa vụ trả lại tiền phòng chưa ở | Cảnh báo `RENT_OVERPAID` + nhóm dòng **Hoàn trả** (nhập tay, được làm tổng âm, xác nhận đã hoàn; chặn hoàn tất thanh lý khi còn Chờ hoàn) | BL-BR-17/27, CT-BR-12 |
| R-137 ✅ | Bỏ nợ: plan `method = WriteOff` + `write_off_reason`, code `kind` + `note` | Báo cáo doanh thu lọc theo `method` sẽ sai; thêm WriteOff vào phương thức thanh toán của HĐ | Giữ code (`kind`), sửa plan | PM-BR-16/18, M10 E6 |
| R-138 ✅ | PM-BR-17 nói đợt 1 có `refund_due`, M07 BL-BR-27 hoãn | Hai module mâu thuẫn | Hoàn trả đi qua dòng phiếu (BL-BR-27); PM-BR-17 chỉ còn ghi có đợt 2 | PM-BR-17 |
| R-139 ✅ | Phụ thu chỉ thêm từng phiếu | Phụ thu chung cả khu / tầng phải bấm từng phòng | Thêm dòng tay cho nhiều phòng một lúc (`POST /invoices/manual-lines`) | BL-UC-05 |
| R-140 ✅ | Còn nợ cộng `total − paid` của mọi phiếu | Phiếu tổng âm sẽ trừ vào nợ của phiếu khác | Nợ chỉ cộng phiếu `paid < total`; số phải hoàn tách riêng (`refundDue`) | PM-BR-06, PM-UC-12 |

## B11. Chốt nghiệp vụ tính tiền — 08/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-141 ✅ | "Tháng thu" chưa có định nghĩa chung cho cả khu (ngày chốt theo từng HĐ, cho chọn 1–31) | Mỗi phòng một kỳ; chu kỳ 30 ngày thì ngày thu trôi, 13 kỳ / năm, tháng 12 có 2 kỳ | **Ngày chốt cố định hằng tháng của khu** (1–28), mọi phòng dùng chung; ngày tạo phiếu không ảnh hưởng kỳ | C-05, M02 PR-BR-09, M05 CT-BR-04 |
| R-142 ✅ | Thu trước / thu sau, tính theo ngày, hạn thanh toán chọn theo từng HĐ | Cùng khu, ngày 05/11 phòng thu trước cần phiếu tháng 11, phòng thu sau cần phiếu tháng 10 — "tạo phiếu tháng này" lệch | Cài đặt kỳ thu **theo khu**; tạo phiếu cả khu, lọc bớt phòng có vấn đề; trả phòng giữa tháng ⇒ phiếu quyết toán riêng | M02 PR-BR-09, M07 |
| R-143 ✅ | Giá áp theo ngày bấm tạo phiếu | Phiếu tháng 10 tạo 01/11 dính giá tháng 11; cùng tháng hai phòng hai giá; lập bù phiếu cũ dùng giá hôm nay | Giá = bản mới nhất có hiệu lực tới **ngày cuối kỳ được tính** (thu trước: điện nước theo kỳ dùng điện); kỳ đã có phiếu chốt thì khóa giá kỳ đó ⇒ giá mới chỉ từ kỳ sau | FE-BR-07/10, BL-BR-04/05 |
| R-144 ✅ | Dịch vụ tính theo ngày ở kỳ lẻ | Vào ở 3 ngày vẫn bị chia lẻ wifi / giữ xe, khó thỏa thuận | Dịch vụ **thu trọn tháng**, chủ trọ sửa số lượng trên nháp; điện nước không đổi | BL-BR-04 |
| R-145 ✅ | Quy tắc giảm / tăng tự động (theo %, nhiều tháng) | Phức tạp, chủ trọ không dùng giảm % | **Bỏ**; dùng phụ thu / giảm trừ / hoàn trả cho 1 hoặc nhiều phòng | M07 BL-UC-10, BL-BR-08/09/19 |
| R-146 ✅ | Phí tối thiểu, định mức theo số người, công tơ tổng | Thêm phức tạp không cần cho phòng trọ thường | Không làm: dùng bao nhiêu thu bấy nhiêu; mỗi phòng 1 hộ; chỉ công tơ riêng từng phòng | M04 Q2/Q3, M06 Q1 |

## C. Rủi ro còn lại / cần quyết định

| # | Vấn đề | Ảnh hưởng | Đề xuất |
|---|--------|-----------|---------|
| O-01 ⚠️ | Một số điều khoản pháp lý đánh dấu "Cần xác minh" ([00-legal-basis.md](00-legal-basis.md)) | Thông báo/cảnh báo trong app có thể sai | Rà soát với luật sư trước khi bán thương mại; nội dung cảnh báo pháp lý để ở cấu hình, không hard-code |
| O-02 ✅ | Công tơ tổng dùng chung nhiều phòng | Không tính tự động được | **Ngoài phạm vi** (chốt 08/10/2026): chỉ phòng / chung cư mini có công tơ riêng; công tơ tổng thì chủ trọ tự chia, nhập phụ thu nhiều phòng (M06 Q1) |
| O-03 ⚠️ | Định nghĩa "doanh thu" cho báo cáo thuế (E6) | Số liệu khác cách hiểu của cơ quan thuế | Kế toán xác nhận; ghi rõ "chỉ tham khảo" |
| O-04 ⚠️ | Lưu trữ dữ liệu cá nhân trên cloud nước ngoài | Có thể vướng quy định chuyển dữ liệu xuyên biên giới (L11) | Chọn object storage + DB đặt tại VN |
| O-05 ⚠️ | Thời hạn lưu giữ dữ liệu người thuê sau khi rời đi | Lưu quá lâu = rủi ro; xóa sớm = mất chứng cứ tranh chấp | Mặc định gợi ý ẩn danh sau 24 tháng, không tự động |
| O-06 ⚠️ | `paid_amount` là cache | Bug code có thể làm lệch | Test bất biến sau mỗi integration test + job đối soát đêm (P2) |
| O-07 ⚠️ | Hiệu năng tạo phiếu cho khu lớn (load snapshot) | Chậm khi > 1.000 phòng | Loader theo khu (vài query batch), đo trong test hiệu năng P1 |
| O-08 ⚠️ | Chủ trọ cần thu theo **giường** / nhiều phòng 1 HĐ | Không hỗ trợ P1 | Workaround: mỗi giường 1 `Room` |
| O-09 ✅ | Người đứng tên không ở cùng ⇒ "chủ hộ" khi đăng ký tạm trú là người khác | Quan hệ khai so với người đứng tên không khớp tờ khai | Đã làm `household_head_renter_id` (CT-BR-36) |
| O-10 ✅ | CT-BR-31 (không ở 2 phòng) chỉ kiểm ở Application | 2 request song song thêm cùng người vào 2 phòng có thể lọt | Đã làm: advisory lock theo người thuê trong transaction (05-contract §10) |
| O-11 ✅ | Trường tùy biến điện / nước trùng khoản thu M04 | Nhập 2 nơi, lệch nhau | M04 đã có: văn bản HĐ in điện nước từ `contract_fees`; khuyến nghị bỏ trường điện nước khỏi mẫu (05-contract Q7) |
| O-12 ⚠️ | Giá điện tham chiếu & định mức theo TT 60/2025 | Ngưỡng cảnh báo cố định trong cấu hình có thể lỗi thời khi EVN đổi giá | Cập nhật `Fees:ElectricityPriceWarningThreshold`; P2 bảng cấu hình do admin sửa. **Không** chia định mức theo số người — mỗi phòng 1 hộ (chốt 08/10/2026) |
| O-13 ⚠️ | Điện một giá / theo bậc do chủ trọ đặt (FE-BR-15) | TT 60/2025: tổng tiền điện thu không vượt hóa đơn EVN; một giá cao có thể vượt khi phòng dùng ít | Giữ cảnh báo ngưỡng (FE-BR-13) + làm đối chiếu hóa đơn (LEG-05, M10 E7) |
| O-14 ⚠️ | Đồ của người bỏ đi không báo (CT-BR-41) | Tự ý xử lý đồ có thể bị khiếu nại | Hệ thống chỉ nhắc lập biên bản có người làm chứng; cách xử lý đồ / cọc cần ghi trong điều khoản HĐ — hỏi luật sư |
| O-15 ⚠️ | "Ở tiếp chưa ký lại" (CT-BR-45) | BLDS 2015 không tự gia hạn; tranh chấp khó chứng minh điều khoản đang áp | Nhắc ký phụ lục mỗi 30 ngày; bản in ghi rõ HĐ đã hết hạn |
| O-16 ⚠️ | Sửa tay phiếu (BL-BR-07), phụ thu / hoàn trả nhiều phòng, bỏ nợ | Sửa tay quá nhiều làm số liệu báo cáo lệch nguồn; không biết ai sửa | Báo cáo M10 có cột "đã sửa tay"; audit log (C-10) **chưa làm** — hiện chỉ có `created_by/updated_by` |

## C2. Câu hỏi mở sau khi rà chéo — 04/10/2026

| # | Câu hỏi | Đề xuất mặc định |
|---|---------|------------------|
| Q-A ❌ | Chu kỳ đóng tiền phòng nhiều tháng mà HĐ có `end_date` rơi giữa chu kỳ | ~~Chu kỳ cuối tính tới `end_date`; gia hạn thì chu kỳ chạy tiếp~~ — làm sót tiền (R-134); bỏ chu kỳ nhiều tháng |
| Q-B ✅ | Kỳ đầu lẻ / gộp có tính là tháng thứ 1 của chu kỳ? | **Chốt**: có; tiền phòng kỳ lẻ vẫn prorate theo ngày |
| Q-C ✅ | Giá theo bậc khi kỳ sử dụng ngắn / dài | **Chốt**: bậc chỉ theo lượng tiêu thụ của kỳ, không quy đổi theo số ngày |
| Q-D ✅ | Người bỏ đi không báo: chỉ số cuối đọc ngày phát hiện | **Chốt**: ghi với ngày = ngày trả phòng; phần dùng tới ngày phát hiện tính cho người thuê |
| Q-E ⏸ | Trừ cọc khi chưa có sổ cọc | **Để sau** cùng sổ cọc, hoàn tiền, bồi thường (M08 đợt 2) |
| Q-F ✅ | "Đã thu toàn bộ" mặc định phương thức nào | **Chốt**: tiền mặt, hôm nay, đổi được |

Tài liệu UI (`docs/api/`) mô tả **code đang chạy** (một giá, chưa có phiếu quyết toán / chu kỳ) — cập nhật cùng lúc code các mục B9.

## C3. Việc còn dở — cập nhật 08/10/2026 (làm tiếp từ đây)

**Câu hỏi chờ chốt (nhóm 1 — tháng thu)**

| # | Câu hỏi | Đề xuất đang chờ xác nhận |
|---|---------|---------------------------|
| K4 | Đổi ngày chốt / cài đặt kỳ thu của khu khi khu đã có phiếu | Chỉ cho đổi khi khu chưa có phiếu nào |
| K5 | Kỳ đầu khi vào ở giữa kỳ (VD khu chốt ngày 5, vào 03/11 hoặc 20/10) | Trường "Tính tiền từ ngày" trên HĐ (mặc định = ngày bắt đầu; HĐ nhập từ sổ cũ đặt = đầu kỳ đầu tiên dùng phần mềm); phiếu đầu tiên tự gồm mọi ngày lẻ tới hết tháng thu đang tạo; tháng thu của phiếu = tháng của kỳ mà phiếu kết thúc. Tránh phiếu lẻ 2 ngày và tránh sót 15 ngày khi quên lập phiếu kỳ đầu (thu trước) |
| K6 | Sửa giá thuê / dịch vụ trên HĐ | Sửa trực tiếp trên HĐ, hệ thống tự áp từ kỳ chưa chốt đầu tiên và giữ lịch sử (tháng cũ tính lại vẫn ra giá cũ) |
| K7 | Giá có hiệu lực **01/11** với khu chốt ngày 1 thuộc tháng mấy | Tháng 11 (kỳ tháng 10 = 01/10–31/10) — tháng 10 vẫn giá cũ |
| L1 | Phiếu quyết toán: dịch vụ thu trọn tháng như BL-BR-04 | Có, chủ trọ sửa số lượng |
| L2 | Nước theo đầu người | Tự đếm số người đầu kỳ làm số gói, sửa được trên nháp |
| L3 | Ngưỡng cảnh báo điện nước (MT-BR-08) | Gấp 3× trung bình 3 kỳ **và** tăng ≥ 50 đơn vị, hoặc = 0 khi có người; chưa đủ 3 kỳ thì bỏ qua |
| L4 | Tự gắn dịch vụ theo nhóm phòng (VD phòng có điều hòa) | Để sau |
| L5 | Import Excel (tải template → điền → kiểm lỗi từng dòng → nhập tất cả hoặc không) | Mức 1 phòng trước; mức 2 người thuê + HĐ đang ở + chỉ số hiện tại ngay sau |
| — | Mặc định khu mới thu trước hay thu sau | Thu sau (chủ trọ ưu tiên luồng thu sau) |

**Nhóm để sau (E–J, đã liệt kê ví dụ ngày 07/10)**: giới hạn số tiền hoàn trả (E1), bù trừ hoàn trả với nợ phiếu khác (E2), tổng âm trên phiếu thường (E3), nút tạo dòng hoàn trả từ `RENT_OVERPAID` (E4), "Nợ cũ" trên phiếu (F1), bỏ nợ khi HĐ còn hiệu lực (F2), số dư có (F3), phạt chậm trả (F4), trả cọc khi chưa có sổ cọc (G1), làm tròn tổng phiếu về nghìn (H1), cờ nháp lỗi thời (I1), đối chiếu hóa đơn EVN (J1). Ngoài tính tiền: audit log C-10, UUIDv7 C-02, Serilog C-11, mật khẩu DB trong `appsettings.Development.json`, thu gọn phần pháp lý M05 nếu HĐ chỉ để lưu thông tin.

**Code chưa theo plan (làm sau khi chốt các câu trên)**

| Việc | Plan |
|------|------|
| Chuyển cài đặt kỳ thu từ HĐ sang khu: bỏ `billing` trên HĐ, ngày chốt 1–28, migration dữ liệu (khu 29–31 → 28) | C-05, PR-BR-09, CT-BR-04 |
| Kỳ đầu theo K5 (sau khi chốt) | C-05, BL-BR-02/21 |
| Dịch vụ thu trọn tháng, không prorate (phiếu thường; phiếu quyết toán theo L1) | BL-BR-04, BL-BR-17 |
| Nhãn đỏ "Quá hạn" trên thẻ phòng | PR-BR-16 |
| Cảnh báo điện nước bất thường | MT-BR-08 |
| Dọn `GetFirstOpenRentPeriodStartAsync` (trùng `GetFirstOpenPeriodStartAsync` sau khi bỏ chu kỳ) | CT-BR-05 |
| Import Excel phòng (mức 1), rồi người thuê + HĐ (mức 2) | L5 |

**Đã code nhưng chưa kiểm / chưa commit (từ 07/10)**: bỏ chu kỳ nhiều tháng, giá dịch vụ theo cuối kỳ, nhóm Hoàn trả + xác nhận hoàn, thêm dòng tay nhiều phòng — build + 171 unit test xanh; **chưa chạy integration test** (cần Docker), **chưa áp migration** `RemoveRentCycleAddInvoiceRefund` (cần Postgres), `openapi.json` sửa bằng script — xuất lại khi chạy được app.

## D. Ma trận kiểm tra chéo (đã đối chiếu)

| Ràng buộc | Định nghĩa ở | Được dùng/tôn trọng ở |
|-----------|--------------|------------------------|
| Kỳ thu C-05 | README | M05 (rent terms, fees), M06 (sheet, usage), M07 (lập phiếu, prorate) |
| Thứ tự khóa C-07 | README | M02 §10, M05 §10, M06 §10, M07 §10, M08 §10 |
| Khóa kỳ đã chốt (`firstOpenPeriodStart`, `lockedUntil`) | M07 interfaces | M04 FE-BR-07, M05 CT-BR-05/06, M06 MT-BR-06 |
| Đánh dấu stale | BL-BR-20 | FE-BR-09, MT-BR-06, M05 events, BL-BR-10 rules |
| Bất biến tài chính C-06 | README | M07 BL-BR-14, M08 PM-BR-01/07, ngoại lệ ẩn danh RT-BR-06 |
| FK composite C-01 | README | M02 (group members), M04, M05, M06, M07, M08 |
| Che dữ liệu cá nhân LEG-06 | 00-legal | M03 §11, M09 FS-BR-07, M10 RP-BR-03 |
