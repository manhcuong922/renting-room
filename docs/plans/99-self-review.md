# 99 — Self-review: lỗi tìm thấy & cách xử lý

> Review chéo toàn bộ plan (README, M01–M10) với trọng tâm: **bất đồng dữ liệu**, **mất toàn vẹn**, **race condition**,
> **rò rỉ dữ liệu giữa tổ chức**, **sai pháp lý**. Mỗi mục ghi: vấn đề → kịch bản lỗi → cách xử lý → nơi đã sửa.
> Trạng thái: ✅ đã sửa trong plan · ⚠️ rủi ro còn lại / cần quyết định.
> Mục A–B là **nhật ký các lỗi đã xử lý** (để tra lý do thiết kế). Việc còn lại: mục **C**.

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
| R-31 ✅ | Prepaid: thanh lý giữa kỳ đã thu trọn tháng | Thu thừa tiền phòng những ngày không ở | Phiếu quyết toán cảnh báo `RENT_OVERPAID`; chủ trọ thêm dòng **Hoàn trả** (phiếu nguồn = phiếu đã thu) ⇒ phiếu âm "Chờ hoàn" → xác nhận đã hoàn | BL-BR-17, BL-BR-27 |
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
| R-55 ✅ | Không lưu **bên cho thuê** ở đâu | Hợp đồng in ra thiếu nội dung bắt buộc (Điều 163 khoản 1), không dùng làm giấy tờ tạm trú được | Thông tin bên cho thuê theo khu + snapshot khi kích hoạt; thiếu ⇒ cảnh báo (09/10/2026) | PR-BR-12, CT-BR-19 |
| R-56 ✅ | Thiếu **thời điểm có hiệu lực** | Tranh chấp ngày bắt đầu nghĩa vụ (nhất là cọc trước ngày bàn giao) | `effective_date` mặc định = ngày ký (Điều 164) | CT-BR-20 |
| R-57 ✅ | Người dưới 18 tuổi đứng tên ký | Hợp đồng có thể vô hiệu (BLDS Điều 117) | Cảnh báo (09/10/2026 — trước chặn): bản giấy cần người giám hộ ký | CT-BR-18 |
| R-58 ✅ | Đơn giá điện nước chỉ nằm ở danh mục khu | Chủ trọ đổi giá → không chứng minh được giá đã thỏa thuận lúc ký | `utility_price_snapshot` | CT-BR-19 |
| R-59 ✅ | Kết thúc hợp đồng không ghi lý do | Chủ trọ đơn phương chấm dứt ngoài các trường hợp luật cho phép mà không được cảnh báo | `termination_reason` + `termination_ground` theo Điều 172 (ground tùy chọn từ 09/10/2026) | CT-BR-21 |
| R-60 ✅ | Không có biên bản tài sản bàn giao | Tranh chấp khi trả phòng | `contract_assets` ghi tình trạng lúc giao / lúc trả; bồi thường = phụ thu trên phiếu quyết toán | CT-BR-23 |
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
| R-77 ✅ | HĐ không cọc vẫn nhận cọc mặc định của phòng | HĐ hiện có cọc dù thỏa thuận không cọc | Mẫu `no_deposit` ⇒ cọc = 0, gửi > 0 bị chặn; lọc `hasDeposit` | CT-BR-27 |
| R-78 ✅ | Người ở không có quan hệ với người đứng tên; dữ liệu vô lý (vợ là nam, con lớn tuổi hơn cha, 2 vợ, vợ chồng chưa đủ tuổi) | Không lập được tờ khai tạm trú chung hộ; danh sách gửi công an sai | Danh mục quan hệ theo TT 55/2021 Điều 6 (sửa bởi TT 66/2023) + kiểm tra hợp lý — **cảnh báo** kèm tên người (09/10/2026) | CT-BR-28, 29 |
| R-79 ✅ | Người chưa thành niên ở trọ cùng người không phải cha mẹ / giám hộ | Tờ khai tạm trú bị từ chối (Luật Cư trú Điều 28) | Cảnh báo thiếu `guardian_consent` (09/10/2026) | CT-BR-30 |
| R-80 ✅ | Một người được thêm vào 2 phòng cùng lúc | Danh sách người ở / tạm trú mâu thuẫn; đếm sai số người tính định mức điện (L9) | Chặn ở Application; chuyển đi – vào ở cùng ngày hợp lệ | CT-BR-31 |
| R-81 ✅ | (phát hiện khi code) HĐ đang thanh lý: `move_out_date` của người ở chỉ ghi khi hoàn tất | Kiểm tra ở 2 phòng chặn nhầm HĐ mới; xuất Excel theo ngày vẫn liệt kê người đã trả phòng | Ngày ra hiệu lực = `COALESCE(move_out_date, actual_end_date)` | M05 §4, E1 |
| R-82 ✅ | Xuất Excel: tên người nhập dạng `=HYPERLINK(...)`, SĐT / CCCD mất số 0 đầu | Chèn công thức độc hại; sai dữ liệu | Quote-prefix chuỗi bắt đầu `= + - @`; cột chữ định dạng `@` | RP-BR-02 |

## B5. Rà nghiệp vụ hợp đồng lần 2 — 03/10/2026 (mỗi lỗi có test tái hiện trước khi sửa)

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-83 ✅ | Nháp cho phép "vẫn thêm" vượt sức chứa nhưng kích hoạt luôn chặn, không có cách vượt | Gia đình vợ chồng + con nhỏ ở phòng 2 người không kích hoạt được HĐ | Kích hoạt nhận `overrideCapacity` (ghi audit) — **thay bằng R-155 (09/10/2026)**: bỏ hẳn giới hạn số người | CT-BR-09 |
| R-84 ✅ | Ghi "chuyển đi" lần 2 ghi đè ngày ra cũ | Kéo dài ngày ra chồng lên nơi ở mới ⇒ một người ở 2 phòng (lọt CT-BR-31) | Đã chuyển đi thì không ghi lại → 409 `OCCUPANT_ALREADY_MOVED_OUT` | CT-BR-08 |
| R-85 ✅ | Hủy thanh lý khi người ở đã sang phòng khác | Người ở trở lại "đang ở" vô thời hạn ở phòng cũ ⇒ ở 2 phòng | Kiểm CT-BR-31 khi hủy thanh lý | CT-BR-34 |
| R-86 ✅ | Kiểm "ở nơi khác" cũng bắt HĐ cùng phòng | Lỗi `OCCUPANT_LIVES_ELSEWHERE` che lỗi gốc `ROOM_PERIOD_OVERLAP` | Chỉ xét phòng khác | `OccupantChecks` |
| R-87 ✅ | Không giới hạn tiền cọc | Nhập thừa số 0 (100 triệu thay vì 1 triệu) | ≤ 12 tháng tiền thuê | CT-BR-32 |
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
| R-125 ✅ | Prepaid: phiếu kỳ cuối lập trước khi báo trả phòng | Điện nước kỳ cuối không ai thu | Phiếu quyết toán thu phần còn thiếu; tiền phòng đã thu thừa ⇒ cảnh báo `RENT_OVERPAID`, chủ trọ thêm dòng Hoàn trả (BL-BR-27) | BL-UC-11, BL-BR-02/17 |
| R-126 ✅ | Hoàn tất thanh lý không xét tiền | HĐ kết thúc còn nợ / sót tiền | Bắt phiếu quyết toán đã chốt; còn nợ ⇒ cảnh báo + "Đã thu toàn bộ" / "Bỏ nợ" | CT-BR-12, PM-UC-14, PM-BR-16 |
| R-127 ✅ | PM-BR-13 (HĐ kết thúc không thu) mâu thuẫn code (cho thu) | Khó hiểu khi nào được thu | Hoàn tất buộc hết nợ ⇒ HĐ kết thúc không cần thu nữa — giữ PM-BR-13, sửa code | PM-BR-13 |
| R-128 ✅ | Số phiếu thu lấy năm ngày thu | Ghi bù đầu năm ⇒ số PT lộn năm | Năm của ngày lập | PM-BR-15 |
| R-130 ✅ | Chỉ một giá điện nước | Khu tính theo bậc (như EVN) không dùng được | Bản giá một giá hoặc theo bậc | FE-BR-15, BL-BR-05 |
| R-131 | Cư trú thiết kế nặng (bản ghi, tạm vắng, sự kiện domain) chưa code | Khối việc lớn không cần ở P1 | P1 = người ở trong phòng (M05); phần thủ tục ⏸ P2 | M03 |
| R-132 ✅ | Phòng ngừng dùng có cần tháo công tơ | Bắt tháo thì mất số đo khi sửa chữa | Không tháo; HĐ mới chọn số khi kích hoạt | PR-BR-05, MT-BR-17 |
| R-133 ✅ | Phạm vi đợt 1 quá rộng (cọc, hoàn tiền, bồi thường, cư trú) | Kéo dài đợt 1, nghiệp vụ chưa ổn | Đợt 1 chỉ thu; sau đó đã làm hoàn trả (R-138), cọc tối giản (R-174), bồi thường = phụ thu (R-160); cư trú vẫn P2 | M03, M05, M07, M08 |

## B10. Rà plan sau đợt 1 — quyết định 07/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-134 ✅ | Chu kỳ đóng tiền phòng nhiều tháng cắt tại `end_date` | Chu kỳ 3 tháng từ 01/08, hết hạn 15/09 rồi gia hạn / ở tiếp ⇒ 16/09–31/10 không phiếu nào thu tiền phòng; trả phòng sớm ⇒ thu thừa nhiều tháng | **Bỏ chu kỳ nhiều tháng**, chỉ thu hằng tháng (chưa có phương án xử lý trọn) | BL-BR-03/26, C-05, migration `RemoveRentCycleAddInvoiceRefund` |
| R-135 ✅ | M04 lấy giá tại đầu kỳ, M07 / code lấy tại cuối kỳ (điện nước) và đầu kỳ (dịch vụ) | Hai tài liệu nói khác nhau; dịch vụ tăng giá vẫn tính giá cũ cả kỳ | **Giá theo phiên bản**: mọi khoản lấy bản mới nhất tới ngày cuối khoảng tính, áp cả dòng, không chia nửa kỳ | FE-BR-10/11, BL-BR-04/05 |
| R-136 ✅ | Trả phòng sớm khi đã đóng trọn kỳ: phiếu quyết toán bỏ qua, không dấu vết | Chủ trọ quên nghĩa vụ trả lại tiền phòng chưa ở | Cảnh báo `RENT_OVERPAID` + nhóm dòng **Hoàn trả** (nhập tay, được làm tổng âm, xác nhận đã hoàn; chặn hoàn tất thanh lý khi còn Chờ hoàn) | BL-BR-17/27, CT-BR-12 |
| R-137 ✅ | Bỏ nợ: plan `method = WriteOff` + `write_off_reason`, code `kind` + `note` | Báo cáo doanh thu lọc theo `method` sẽ sai; thêm WriteOff vào phương thức thanh toán của HĐ | Giữ code (`kind`), sửa plan | PM-BR-16/18, M10 E6 |
| R-138 ✅ | Hoàn tiền cho người thuê đi qua phiếu thu hay phiếu tiền phòng | Hai module mâu thuẫn | Hoàn trả đi qua dòng phiếu tiền phòng (BL-BR-27); M08 không có ghi có / số dư có (để sau cùng thanh toán online) | BL-BR-27 |
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

## B12. Phần 5 (ngoài tính tiền) — 08/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-147 ✅ | Audit log ghi được nhưng chủ trọ không xem được | ID-BR-19 "biết phó quản lý nào đã làm gì" chỉ đọc được bằng SQL | `GET /audit-logs` chỉ chủ trọ, lọc tường minh theo tổ chức (bảng không có global filter), phó quản lý 403 | C-10, ID-BR-19 |
| R-148 ✅ | Plan ghi UUIDv7, code dùng `Guid.NewGuid()` | Id ngẫu nhiên làm index phân mảnh | Id entity dùng `Guid.CreateVersion7()`; security stamp / JTI / họ token giữ ngẫu nhiên | C-02 |
| R-150 ✅ | Chưa có ẩn danh; nhật ký giữ tên / SĐT vô thời hạn | Dữ liệu cá nhân người thuê cũ còn mãi trong hồ sơ, phiếu, nhật ký | Job hằng ngày ẩn danh sau 36 tháng, xóa cả trong phiếu, phiếu thu, bản chụp lúc ký, audit | RT-BR-06 |
| R-155 ✅ | Phần mềm chặn số người theo sức chứa phòng | Chủ trọ cho gia đình có con nhỏ ở phòng "2 người" phải bấm vượt + bị ghi nhật ký; import phải hỏi xác nhận | Số người = mô tả loại phòng, không chặn / không cảnh báo | PR-BR-06 |
| R-156 ✅ | Bắt số giấy tờ cho mọi người ở | Trẻ < 14 tuổi chưa có CCCD không khai được ⇒ danh sách người ở thiếu | Giấy tờ bắt buộc từ 14 tuổi / người đứng tên | RT-BR-01 |
| R-157 ✅ | Import giữ bản xem trước trong RAM, lưu tất cả hoặc không gì, chỉ số công tơ khai ở 2 form | Tốn RAM, mất khi khởi động lại, 1 dòng lỗi chặn cả file, chỉ số 2 nơi mâu thuẫn | Xem trước không giữ ở server, sửa trên màn, lưu phần hợp lệ theo đơn vị (dòng phòng / phòng), chỉ số chỉ ở form phòng; import chỉ tạo mới | PR-UC-11, RT-UC-10 |
| R-153 ✅ | Kỳ chuyển tiếp chọn "gần 1 tháng nhất" (1 → 20: 01/11–19/11) | Kỳ sau 20/11–19/12 cũng là "tháng 11" ⇒ 2 phiếu cùng tháng, lập phiếu theo tháng bị lệch | Kỳ chuyển tiếp luôn tới trước ngày chốt mới của tháng sau (lệch tới ±27 ngày, chủ trọ chọn số ngày tính) | PR-BR-09, BL-BR-28 |
| R-154 ✅ | Lệnh nghiệp vụ tự mở transaction riêng | Import nhiều dòng không lưu được "tất cả hoặc không gì" | `IAppDbContext.BeginTransactionAsync`: đang có transaction ⇒ tham gia | PR-UC-11, RT-UC-10 |
| R-152 ✅ | Bên cho thuê khai theo từng khu dù hầu hết là chính chủ trọ | Nhập lặp lại mỗi khu; khu mới quên khai ⇒ HĐ cảnh báo, in thiếu | Khai thông tin chủ trọ 1 lần ở tổ chức, khu thừa hưởng, chỉ khai riêng khi khác | PR-BR-17 |
| R-151 ✅ | HĐ bắt đủ giấy tờ pháp lý (bên cho thuê, SĐT, tuổi, quan hệ, căn cứ Điều 172) mới kích hoạt | Chủ trọ nhập HĐ cũ / import không kích hoạt được ⇒ không thu được tiền, bỏ dùng phần mềm | Chỉ bắt buộc phần thu tiền; giấy tờ thành cảnh báo + cờ "Thiếu tài liệu" | CT-BR-46, CT-UC-23 |
| R-149 ✅ | Plan nhắc Serilog nhưng code không dùng | Hiểu sai cách che dữ liệu | Bỏ Serilog: log chỉ ghi id, FE che hiển thị | C-11 |

## B13. Nhóm 2 (tiền cọc, công nợ, phiếu) — 09/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-160 ✅ | Bồi thường tài sản không có chỗ ghi tiền | Chủ trọ tính tay, dễ quên thu | Bồi thường = **phụ thu** trên phiếu quyết toán (không cơ chế riêng) | M05 CT-BR-23 |
| R-162 ✅ | Bỏ nợ chỉ có khi hoàn tất thanh lý | Người thuê trả 2tr / 3tr, chủ trọ cho 1tr mà phiếu vẫn "còn nợ" mãi, phải sửa phiếu đã chốt | "Thanh toán + bỏ phần còn lại" 1 thao tác, bỏ nợ bất kỳ lúc nào, chỉ chủ trọ, lý do bắt buộc | M08 PM-BR-16, 29 |
| R-163 ✅ | Phiếu tháng mới không cho thấy nợ cũ | Gửi phiếu tháng 12 mà người thuê không biết còn nợ tháng 11 ⇒ chuyển thiếu | Phiếu hiện "Nợ các kỳ trước" + "Tổng cần thanh toán" (chỉ hiển thị); thu vẫn FIFO | M08 PM-UC-18 |
| R-164 ✅ | Hoàn trả nhập tự do | Hoàn tiền người thuê chưa từng trả; hoàn 2 lần cho cùng một sai sót | Hoàn trả bắt buộc phiếu nguồn, ≤ số đã thu thật − các lần hoàn trước | M07 BL-BR-27 |
| R-165 ✅ | Nháp lập sớm, sau đó nhập chỉ số / đổi giá | Chủ trọ không biết nháp nào đã cũ, chốt mới bị `DRAFT_STALE` từng phiếu | Cờ "Cần tính lại" tự đặt trong cùng lần lưu dữ liệu nguồn + tính lại hàng loạt các nháp có cờ | M07 BL-BR-20 |
| R-166 ✅ | Tổng phiếu số lẻ (523.560) | Người thuê chuyển 523.000 ⇒ còn nợ 560 mãi | Làm tròn xuống nghìn, dòng "Làm tròn" riêng, bật theo khu (mặc định bật) | M07 BL-BR-29 |
| R-167 ✅ | Điện lực thay công tơ cả khu giữa tháng | Phải thay từng phòng một (hàng chục lệnh), dễ sót phòng ⇒ phiếu thiếu sản lượng | Lưới thay hàng loạt 1 ngày chung, lưu tất cả hoặc không | M06 MT-UC-08, MT-BR-18 |
| R-168 ✅ | Nhật ký giữ vô thời hạn | Bảng `audit_logs` phình mãi | Job xóa nhật ký cũ hơn 5 năm | README C-10 |

## B14. Chốt 10/10/2026

| # | Vấn đề | Kịch bản lỗi | Xử lý | Nơi sửa |
|---|--------|--------------|-------|---------|
| R-170 ✅ | Bỏ nợ chỉ chủ trọ | Chủ trọ ở xa, phó quản lý tin cậy không tự đóng được HĐ người bỏ trốn | Quyền bỏ nợ bật **riêng từng phó quản lý** | M01 ID-BR-23 |
| R-171 ✅ | Chuyển phòng phải thanh lý + HĐ mới | Mất lịch sử HĐ, 2 phiếu trong tháng, phải chuyển cọc tay | HĐ giữ nguyên, chỉ đổi phòng từ ngày D (cùng khu); điện nước kỳ đó cộng 2 phòng | M05 CT-UC-12, CT-BR-14, 47 |
| R-172 ✅ | "Cần tính lại" báo thừa | Đổi xe / cài đặt khu cũng gắn cờ nháp | Chỉ giá, chỉ số, giá thuê / người ở / dịch vụ, chuyển phòng | M07 BL-BR-20 |
| R-173 ✅ | Chưa định nghĩa doanh thu | Báo cáo cộng cả bỏ nợ / tiền cọc ⇒ sai số thực thu | Doanh thu = các khoản đã thu (gồm phụ thu; **không gồm** tiền cọc, không gồm bỏ nợ) − giảm trừ / hoàn | M10 E6 |
| R-174 ✅ | Sổ cọc (nhận / trừ vào phiếu / quyết toán / chuyển / mất cọc) quá phức tạp so với nhu cầu | Chủ trọ chỉ cần biết có cọc, bao nhiêu, đã hoàn chưa | **Bỏ sổ cọc** (10/10/2026): cọc chỉ là thông tin trên HĐ — đang giữ / đã hoàn (ngày, số tiền, ghi chú) / đã chuyển (ký lại); không chặn hủy / thanh lý, chỉ nhắc "Chưa hoàn cọc" | M08 PM-BR-32..35 |
| R-175 ✅ | Phụ thu / bù phát sinh giữa tháng phải nhớ tới cuối tháng mới gõ vào nháp | Người xử lý tại chỗ (quản lý khu) quên báo ⇒ sót thu, hoặc người thuê đã trả ngay lại bị thu lần 2 | **Khoản phát sinh theo phòng**: tạo ngay lúc xảy ra, có trạng thái đã thanh toán; cuối tháng tự vào phiếu, khoản đã thanh toán hiện trên phiếu nhưng không tính tổng | M07 BL-BR-30..37 |

## C. Việc còn lại — cập nhật 10/10/2026

Nguồn duy nhất cho "còn gì chưa làm". Từng plan chỉ ghi "chưa code" tại chỗ và trỏ về đây.

### C1. Đã chốt làm, chưa code (làm tiếp từ đây — sửa plan trước, viết kịch bản test, rồi code)

| # | Việc | Hiện trạng | Plan cần sửa |
|---|------|-----------|--------------|
| T-01 ✅ | **Chuyển phòng: chốt điện nước phòng cũ thành khoản phát sinh** (code 11/10/2026 — migration `TransferMeterClosing`; đổi 11/10/2026 — thay cách "phiếu cộng công tơ 2 phòng", CT-BR-47) | Hiện trạng: phiếu kỳ chuyển cộng công tơ 2 phòng; mỗi HĐ chỉ được 1 chỉ số nhận phòng và 1 chỉ số cuối trên một công tơ (MT-BR-04) ⇒ chuyển về phòng cũ (A → B → A) bị trùng khóa `ux_meter_readings_handover` (409 `DUPLICATE_VALUE`). **Chốt**: khi chuyển, chỉ số cuối phòng cũ bắt buộc ⇒ hệ thống tạo **khoản phát sinh hệ thống** của HĐ, mỗi công tơ 1 khoản: sản lượng = chỉ số cuối − chỉ số đầu (số cuối đoạn đo đã lập phiếu gần nhất nếu ngày ghi ≥ ngày vào phòng, không thì chỉ số nhận phòng của lần ở); đơn giá = bản giá hiệu lực **tại ngày chuyển** (theo bậc ⇒ tính bậc riêng trên sản lượng của khoản); giá đổi sau ngày chuyển trong kỳ **không tính lại**; khoản lưu công tơ, 2 chỉ số, sản lượng, đơn giá và **khóa 2 chỉ số** tới khi khoản bị hủy; chọn được "đã thu ngay". Phiếu chỉ tính công tơ của **phòng HĐ đang ở tại cuối kỳ sử dụng**, bắt đầu từ chỉ số nhận phòng của lần ở đó — không nối chỉ số đầu ngược qua mốc nhận phòng; đoạn đã nằm trong khoản chốt thì phiếu không tính lại. Khóa `Handover` / `Final` đổi thành (công tơ, HĐ, ngày ghi) ⇒ chuyển về phòng cũ được | M05 CT-UC-12, CT-BR-14, 47; M06 MT-BR-04, 12, §3.3; M07 BL-BR-05, 30 |
| T-02 ✅ | **Hủy chuyển phòng** (nhập nhầm phòng / ngày — code 11/10/2026, `POST /contracts/{id}/transfer-room/cancel`) | Trước đây chưa có. Chỉ hủy lần chuyển gần nhất; điều kiện: khoản chốt điện nước của lần chuyển chưa nằm trên phiếu đã chốt, chỉ số nhận phòng mới chưa bị phiếu đã chốt khóa, phòng cũ từ ngày chuyển chưa có HĐ khác (kể cả nháp). Làm: trả lại phòng + ngày vào phòng, hủy khoản chốt, hủy (void) 2 nhóm chỉ số; **không** tự đảo giá thuê / dịch vụ đã đổi lúc chuyển — chỉ cảnh báo kiểm lại; nháp tự "Cần tính lại" | M05 CT-UC-12, CT-BR-14 |
| T-03 ✅ | **Nợ của HĐ cũ sau khi ký lại** (thu hẹp + code 11/10/2026 — M08 PM-BR-31) | Trường hợp thường gặp (đổi người đứng tên, vẫn ở tiếp) chuyển sang **T-08** — không ký lại nên nợ không đứt. Ký lại chỉ còn dùng khi có tranh chấp phải thanh lý HĐ cũ: phiếu HĐ mới hiện **danh sách riêng "Nợ HĐ trước"** (theo `previous_contract_id`), **không cộng** vào "Tổng cần thanh toán"; thu vẫn ghi trên HĐ cũ | M08 PM-BR-31; M07 `GetInvoiceQuery` |
| T-04 ✅ | Số phòng đang thuê của khu (code 11/10/2026 — đếm bằng `OccupiedRoomIds`, cùng điều kiện với trạng thái phòng) | Trước đây: Đếm theo phòng hiện tại của HĐ `Active` / `Liquidating` ⇒ lệch với PR-BR-02 trong **ngày chuyển phòng** (phòng cũ) và **ngày trả phòng** của HĐ đã `Ended` | M02 PR-BR-02 |
| T-05 ✅ | Xem trước nhập người đang ở (code 11/10/2026 — báo `ROOM_PERIOD_OVERLAP` kèm ngày phòng còn HĐ khác ở) | Trước đây: kiểm "phòng đang có người" chỉ xét HĐ nháp / hiệu lực / thanh lý đang gắn phòng; bước lưu lại kiểm từ **ngày vào ở sớm nhất** (= ngày bắt đầu HĐ) ⇒ phòng có HĐ đã kết thúc / đã chuyển đi sau ngày đó qua xem trước rồi lỗi lúc lưu. Dùng chung truy vấn "phòng bị chiếm trong khoảng ngày" với T-04 | M03 RT-UC-10 |
| T-06 ✅ | **Nhãn "Còn nợ" / "Quá hạn" của phòng đi theo HĐ** (chốt + code 11/10/2026) | Trước đây cộng theo phòng ghi trên phiếu ⇒ chuyển phòng xong nợ nằm lại phòng cũ. Nay = phiếu đã chốt chưa thu đủ của các HĐ **đang gắn với phòng** (phòng hiện tại của HĐ) | M02 PR-BR-16 |
| T-07 ✅ | **HĐ quá hạn mà vẫn ở = coi như không thời hạn** (chốt + code 11/10/2026) | Trước đây sau `end_date` không thêm được người ở, không đổi được giá / dịch vụ cho tới khi gia hạn. Nay HĐ `Active` không còn bị `end_date` chặn các thao tác đó; cờ quá hạn / ở tiếp chỉ còn là lời nhắc ký phụ lục | M05 CT-BR-37, 45 |
| T-08 ✅ | **Đổi người đứng tên trên cùng HĐ** (chốt + code 11/10/2026 — M05 CT-BR-51) | Hiện chỉ có ký lại (thanh lý + HĐ mới). Làm lệnh đổi người đứng tên từ ngày X: người mới phải đang ở (hoặc là đồng thuê) và có giấy tờ; lưu lịch sử người đứng tên; **nợ, cọc, khoản phát sinh, chuỗi chỉ số giữ nguyên — người mới mặc định nhận nợ cũ; cọc giữ nguyên trên HĐ, hệ thống không hỏi việc hoàn cho người cũ** (thỏa thuận bên ngoài giữa những người thuê); bật cờ "Thiếu tài liệu" để bổ sung bản ký sau; quan hệ người ở khai lại nếu chủ hộ rời đi. Có tranh chấp ⇒ dùng ký lại (CT-UC-21) | M05 CT-UC-21, CT-BR-44, Q3 |
| T-09 ✅ | **Đồng thuê trên một HĐ** (chốt + code 11/10/2026 — M05 CT-BR-49; không làm 2 HĐ một phòng) | Giữ "một phòng một HĐ". Người ở có **vai trò**: đứng tên / đồng thuê / người ở; đổi vai trò người ở ↔ đồng thuê được khi HĐ đang hiệu lực. Đồng thuê **chỉ mang tính giấy tờ**: là người có tên trên HĐ, cùng chịu trách nhiệm pháp lý khi tranh chấp ⇒ phải có giấy tờ như người đứng tên và được in vào bên thuê; **không** chia tiền — vẫn 1 phiếu / phòng | M05 CT-BR-28 |
| T-10 ✅ | **Chuyển người** (HĐ ở lại, người đi — code 11/10/2026, M05 CT-BR-52) | Hiện là 2 thao tác rời (ghi chuyển đi + thêm vào HĐ khác). Làm 1 lệnh: ghi chuyển đi ở HĐ cũ và vào HĐ của phòng đích cùng ngày, chọn vai trò (đồng thuê / người ở), chuyển kèm xe của người đó. **Phòng đích phải đang có HĐ hiệu lực** — phòng trống ⇒ chặn, tạo HĐ mới như bình thường (chốt 11/10/2026). Khoản thu chi **không** đi theo người. Người đứng tên chuyển đi ⇒ kèm T-08 | M05 CT-UC-04, CT-BR-31 |
| T-12 ✅ | **Người đứng tên không được đang là người ở của phòng khác** (chốt + code 11/10/2026 — M05 CT-BR-50; kiểm khi kích hoạt và các thao tác trên HĐ đang hiệu lực, HĐ nháp chưa kiểm) | Một người thuê được nhiều phòng bằng nhiều HĐ mà mình **đứng tên**, nhưng không vừa là người ở (không đứng tên, không đồng thuê) dưới HĐ của người khác vừa đứng tên HĐ khác. Kiểm khi tạo / sửa nháp, kích hoạt, đổi người đứng tên → 409 `REPRESENTATIVE_IS_RESIDENT_ELSEWHERE`. Hiện chưa chặn | M05 CT-BR-09, 31 |
| T-11 ✅ | **Chuyển phòng: giá thuê và dịch vụ** (code 11/10/2026 — M05 CT-BR-48; API cho bỏ trống giá = giữ giá cũ nhưng trả cảnh báo `ROOM_TRANSFER_RENT_UNCHANGED` khi giá niêm yết phòng mới khác, việc bắt buộc chọn nằm ở UI) | Hiện không gửi giá ⇒ âm thầm giữ giá cũ; dịch vụ theo phòng (VD phí điều hòa) đi theo HĐ sang phòng mới. Làm: bắt buộc chọn giá (giữ giá cũ / giá niêm yết phòng mới — điền sẵn / giá khác). **Giá kỳ chuyển phòng (chốt 11/10/2026) — không chia ngày theo từng phòng**: số ngày đã ở phòng cũ trong kỳ (đầu kỳ → trước ngày chuyển) **quá nửa kỳ** ⇒ kỳ đó vẫn tính **giá cũ**, giá mới áp từ kỳ sau (VD chuyển 17/3 ⇒ tháng 3 giá cũ, tháng 4 giá mới); **không quá nửa kỳ** ⇒ cả kỳ đó tính **giá mới** (VD chuyển 5/3 ⇒ tháng 3 giá mới). Hệ thống điền sẵn theo quy tắc này, chủ trọ đổi được. Kỳ chuyển đã có phiếu chốt tiền phòng (thu trước) mà phải tính giá mới ⇒ tạo khoản phát sinh **chênh lệch cả kỳ** (giá mới − giá cũ: phụ thu hoặc bù; sửa / hủy được như khoản thường), không sửa phiếu đã chốt. Màn chuyển phòng liệt kê dịch vụ đang áp để **giữ hoặc bỏ** (áp từ kỳ chưa chốt đầu tiên); cảnh báo khi cách tính nước đổi (phòng mới không có công tơ nước mà HĐ không có "nước theo người", hoặc ngược lại) | M05 CT-UC-12; M04 FE-BR-16 |
| T-13 ✅ | Chuyển phòng không gửi giá ⇒ lấy **giá niêm yết phòng mới** (11/10/2026) | Trước đây âm thầm giữ giá cũ | M05 CT-BR-48 |
| T-14 ✅ | HĐ nháp cảnh báo vai trò trùng; ẩn danh người từng đứng tên thay cả tên trên phiếu thời họ đứng tên (11/10/2026) | — | M05 CT-BR-50, 51 |
| T-15 ✅ | Nhập Excel: nhiều dòng "Đứng tên" cùng phòng ⇒ dòng đầu đứng tên, còn lại là đồng thuê (11/10/2026) | Trước đây lỗi `REPRESENTATIVE_COUNT` | M03 RT-UC-10 |
| T-16 ✅ | Ẩn danh hai mức: hết hạn lưu giữ họ tên, theo yêu cầu xóa cả tên (11/10/2026) — **cần xác nhận pháp lý việc giữ tên** | Trước đây luôn xóa tên | M03 RT-BR-06 |
| T-17 ✅ | Lịch sử thuê theo phòng + tìm theo tên (11/10/2026) | Trước đây dự kiến lịch sử theo người, chưa code | M03 RT-UC-04 |
| T-18 ✅ | Hủy chỉ số ghi nhầm (cuối kỳ / kiểm tra) (11/10/2026) | Trước đây chỉ sửa được giá trị | M06 MT-UC-06 |

### C2. Bất cập / thiếu sót nghiệp vụ — cần chủ trọ quyết

| # | Vấn đề | Kịch bản | Đề xuất |
|---|--------|----------|---------|
| G-01 ✅ (11/10/2026: giới hạn khoản bù chưa trả ≤ 1 tháng tiền phòng — M07 BL-BR-30) | **Khoản bù lớn hơn phiếu quyết toán** | Bù chưa trả lớn hơn phần thu ⇒ "chờ phiếu sau" (BL-BR-33); phiếu quyết toán là phiếu cuối ⇒ khoản treo mãi, người thuê không nhận được | Phiếu quyết toán cho phép khoản bù làm tổng âm ⇒ trạng thái "Chờ hoàn", xác nhận đã hoàn như dòng Hoàn trả (BL-BR-27) |
| G-02 ✗ không làm (chốt 11/10/2026: khoản bù ít xảy ra, xử lý bên ngoài) | **Tạo khoản phát sinh cho nhiều phòng** | Mất nước cả khu, bù 50k mỗi phòng giữa tháng: phải tạo từng phòng. Thêm tay nhiều phòng chỉ dùng được khi phòng **đã có nháp** (BL-UC-05) | `POST /room-charges/bulk` theo khu / tầng / danh sách phòng, kết quả từng phòng |
| G-03 ✗ không làm (hủy rồi tạo lại) | **Sửa khoản phát sinh** | Gõ nhầm số tiền / nội dung khoản đang chờ: phải hủy rồi tạo lại | Giữ nguyên (đơn giản, có lý do hủy để truy vết) — hoặc cho sửa khi chưa vào phiếu đã chốt |
| G-04 | **Phụ thu đã thu ngay không có phiếu thu** | Khoản `is_settled` không sinh số `PT…` ⇒ không có chứng từ đưa người thuê; tiền chỉ vào báo cáo qua M10 (chưa làm) | Giữ nguyên tới khi làm M10 E6; nếu cần chứng từ thì cân nhắc sinh phiếu thu |
| G-05 ✗ không làm (chốt 11/10/2026: không ai thu thêm cọc khi đổi phòng / tăng giá) | **Đổi tiền cọc khi HĐ đang hiệu lực** | Tăng giá thuê kèm thu thêm cọc: `deposit_amount` chỉ sửa được ở nháp | Lệnh đổi cọc thỏa thuận (ghi chú bắt buộc, audit) — CT-UC-05 |
| G-06 ✅ (11/10/2026 — xử lý bằng T-08 đổi người đứng tên trên cùng HĐ + T-03 hiện nợ HĐ trước; không tự chuyển nợ) | **Ký lại khi còn nợ** | Hoàn tất thanh lý HĐ cũ buộc "Đã thu toàn bộ" hoặc "Bỏ nợ" (CT-BR-12) ⇒ không chuyển nợ sang HĐ mới được; người thuê vẫn ở và trả dần thì HĐ cũ phải để `Liquidating` | Làm T-03 (thấy nợ HĐ cũ trên phiếu HĐ mới) và cho HĐ cũ ở `Liquidating` tới khi thu xong; không tự chuyển nợ |
| G-07 ✗ không làm (chốt 11/10/2026: kết thúc HĐ buộc xử lý hết nợ — thu đủ hoặc bỏ nợ; trả sau thì ghi ở ghi chú HĐ) | **Thu được nợ sau khi HĐ đã kết thúc** | Người bỏ trốn đã "Bỏ nợ", sau quay lại trả: HĐ `Ended` không ghi thu, không đảo bỏ nợ được (PM-BR-07, 13) | Cho ghi thu / đảo bỏ nợ trên HĐ `Ended` riêng với phiếu `WrittenOff` — hoặc chấp nhận ghi ngoài sổ |
| G-08 | **Người thuê chuyển khoản thừa** | Chuyển tròn số lớn hơn nợ ⇒ 422 `PAYMENT_EXCEEDS_DEBT`, phần thừa không ghi được | Đã chốt để sau cùng thanh toán online (số dư có — F3). Tạm thời: ghi đúng số nợ, phần thừa trả lại / ghi chú |
| G-09 ⏸ để sau (không ưu tiên) | **Gửi phiếu cho người thuê** | Chưa in / xuất phiếu, chưa có mã QR ⇒ chủ trọ chụp màn hình gửi Zalo | BL-UC-12 (xuất ảnh / PDF) nên làm trước BL-UC-13 |
| G-10 ✅ (11/10/2026: `PUT /me`, `GET/PUT /org` — M01 ID-UC-03, 12) | **Chủ trọ không tự sửa được hồ sơ của mình / tên tổ chức** | Không có `PUT /me`, `PUT /admin/organizations/{id}` | Làm ID-UC-03, ID-UC-12 |
| G-11 ✅ | Dữ liệu demo | ✅ 11/10/2026: thêm phó quản lý thứ hai **được cấp quyền bỏ nợ** (`Seed:DemoData:WriteOffManagerPhone`, cùng mật khẩu phó quản lý); kịch bản chuyển về phòng cũ (207 ⇄ 208) | — |

### C3. Có trong plan, chưa code

| Module | Chưa code | Mức |
|--------|-----------|-----|
| README | Bảng ĐVHC tỉnh / xã (C-12) — địa chỉ đang nhập chữ, mã không kiểm. Hướng đã chọn 12/10/2026: **file JSON tĩnh** kèm bản phát hành (không bảng DB); **chờ nguồn dữ liệu** chính thức 34 tỉnh / ~3.300 xã rồi mới code. Partition `audit_logs` | P1 / P3 |
| M01 | ✅ 11–12/10/2026: sửa thông tin tổ chức, `PUT /me`, số khu / phòng trong danh sách tổ chức, `login_events`, job dọn refresh token, đăng xuất vô hiệu ngay access token. **Còn**: quên mật khẩu (để sau — cân nhắc Zalo); giới hạn gói `max_properties` / `max_rooms` | P2 / P3 |
| M02 | Giới hạn gói (PR-BR-11); ảnh phòng (chờ M09) | P3 |
| M03 | ✅ 11–12/10/2026: lịch sử thuê theo phòng (RT-UC-04), ghi nhận đồng ý (RT-UC-05), ẩn hồ sơ (RT-BR-05), lọc theo khu / đang ở. **Còn**: kiểm mã tỉnh trong số CCCD (chờ bảng ĐVHC); ảnh giấy tờ (chờ M09); **toàn bộ thủ tục cư trú** — bản ghi tạm trú, tạm vắng, nhắc việc (RT-UC-06..08, 11; LEG-03, LEG-04) | P1 / P2 |
| M05 | ✅ 12/10/2026: xem trước thanh lý (`liquidation/preview`). **Còn**: file scan HĐ (CT-UC-11 — chờ M09); đổi cọc khi HĐ đang hiệu lực (G-05 — đã chốt không làm); in PDF / phụ lục / biên bản bàn giao (để sau cùng phần in ấn); domain event | P1 / P2 |
| M06 | ✅ 11–12/10/2026: lắp công tơ hàng loạt, hủy chỉ số, cảnh báo ngày ghi lệch xa cuối kỳ (MT-BR-05), lọc lưới theo khoản thu, báo cáo sản lượng phòng trống (MT-BR-14). **Còn**: ảnh công tơ (chờ M09) | P1 |
| M07 | In / xuất phiếu (BL-UC-12); gửi Zalo + VietQR (BL-UC-13) | P2 |
| M07 ✅ 12/10/2026 | Tạo phiếu nháp cho nhiều khu trong 1 lệnh (`POST /invoices/generate-bulk`) / theo 1 HĐ (`contractId`) — BL-UC-01; số tổng hợp phải thu / đã thu / còn nợ / quá hạn (`GET /invoices/totals`) — BL-UC-02 | xong |
| M08 | Số dư có / trả thừa (F3), thanh toán online, đối soát ngân hàng | P3 |
| M09 | **Toàn bộ** (upload / tải file, hạn mức, kiểm loại file, xóa file khi ẩn danh) | P1 |
| M10 | E2 tiền phòng tháng, E3 thông tin phòng, E4 công nợ, E5 danh sách cọc; E6 doanh thu, E7 đối chiếu hóa đơn EVN (LEG-05), dashboard, nhắc việc, export bất đồng bộ | P1 / P2 |

Không làm (đã chốt): sổ cọc / trừ cọc vào phiếu, lãi / phạt chậm trả, công tơ tổng chia nhiều phòng, chia định mức điện theo số người, người thuê đăng nhập.

### C4. Rủi ro còn lại (không phải việc code)

| # | Vấn đề | Ảnh hưởng | Đề xuất |
|---|--------|-----------|---------|
| O-01 ⚠️ | Một số điều khoản pháp lý đánh dấu "Cần xác minh" ([00-legal-basis.md](00-legal-basis.md)) | Thông báo/cảnh báo trong app có thể sai | Rà soát với luật sư trước khi bán thương mại; nội dung cảnh báo pháp lý để ở cấu hình, không hard-code |
| O-03 ⚠️ | Định nghĩa "doanh thu" cho báo cáo thuế (E6) | Số liệu khác cách hiểu của cơ quan thuế | Kế toán xác nhận; ghi rõ "chỉ tham khảo" |
| O-04 ⚠️ | Lưu trữ dữ liệu cá nhân trên cloud nước ngoài | Có thể vướng quy định chuyển dữ liệu xuyên biên giới (L11) | Chọn object storage + DB đặt tại VN |
| O-06 ⚠️ | `paid_amount` là cache | Bug code có thể làm lệch | Test bất biến sau mỗi integration test + job đối soát đêm (P2) |
| O-07 ⚠️ | Hiệu năng tạo phiếu cho khu lớn (load snapshot) | Chậm khi > 1.000 phòng | Loader theo khu (vài query batch); chưa đo hiệu năng |
| O-08 ⚠️ | Chủ trọ cần thu theo **giường** / nhiều phòng 1 HĐ | Không hỗ trợ P1 | Workaround: mỗi giường 1 `Room` |
| O-12 ⚠️ | Giá điện tham chiếu theo TT 60/2025 | Ngưỡng cảnh báo cố định trong cấu hình có thể lỗi thời khi EVN đổi giá | Cập nhật `Fees:ElectricityPriceWarningThreshold`; P2 bảng cấu hình do admin sửa |
| O-13 ⚠️ | Điện một giá / theo bậc do chủ trọ đặt (FE-BR-15) | TT 60/2025: tổng tiền điện thu không vượt hóa đơn EVN; một giá cao có thể vượt khi phòng dùng ít | Giữ cảnh báo ngưỡng (FE-BR-13) + làm đối chiếu hóa đơn (LEG-05, M10 E7) |
| O-14 ⚠️ | Đồ của người bỏ đi không báo (CT-BR-41) | Tự ý xử lý đồ có thể bị khiếu nại | Hệ thống chỉ nhắc lập biên bản có người làm chứng; cách xử lý đồ / cọc cần ghi trong điều khoản HĐ — hỏi luật sư |
| O-15 ⚠️ | "Ở tiếp chưa ký lại" (CT-BR-45) | BLDS 2015 không tự gia hạn; tranh chấp khó chứng minh điều khoản đang áp | Nhắc ký phụ lục (nhắc việc tự động: P2); bản in ghi rõ HĐ đã hết hạn |
| O-17 ⚠️ | Chưa ghi nhận đồng ý xử lý dữ liệu cá nhân (RT-UC-05) | L11 yêu cầu có căn cứ xử lý dữ liệu | Tạm thời: điều khoản đồng ý nằm trong mẫu HĐ giấy; làm RT-UC-05 trước khi bán thương mại |

## D. Ma trận kiểm tra chéo (đã đối chiếu)

| Ràng buộc | Định nghĩa ở | Được dùng/tôn trọng ở |
|-----------|--------------|------------------------|
| Kỳ thu C-05 | README | M05 (rent terms, fees), M06 (sheet, usage), M07 (lập phiếu, prorate) |
| Thứ tự khóa C-07 | README | M02 §10, M05 §10, M06 §10, M07 §10, M08 §10 |
| Khóa kỳ đã chốt (`firstOpenPeriodStart`, `lockedUntil`) | M07 interfaces | M04 FE-BR-07, M05 CT-BR-05/06, M06 MT-BR-06 |
| Đánh dấu "Cần tính lại" | BL-BR-20 (`InvoiceStaleMarker`) | FE-BR-09, MT-BR-06, MT-BR-18, M05 CT-BR-47 |
| Bất biến tài chính C-06 | README | M07 BL-BR-14, M08 PM-BR-01/07, ngoại lệ ẩn danh RT-BR-06 |
| FK composite C-01 | README | M02 (group members), M04, M05, M06, M07, M08 |
| Che dữ liệu cá nhân LEG-06 | 00-legal | M03 §11, M09 FS-BR-07, M10 RP-BR-03 |
