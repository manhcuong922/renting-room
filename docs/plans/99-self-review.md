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
| R-52 ✅ | .NET 8 hết hỗ trợ 10/11/2026 | Chạy production trên runtime không còn vá bảo mật | Nâng net10.0 ở P0 | README §3, P0-01 |
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

## C. Rủi ro còn lại / cần quyết định

| # | Vấn đề | Ảnh hưởng | Đề xuất |
|---|--------|-----------|---------|
| O-01 ⚠️ | Một số điều khoản pháp lý đánh dấu "Cần xác minh" ([00-legal-basis.md](00-legal-basis.md)) | Thông báo/cảnh báo trong app có thể sai | Rà soát với luật sư trước khi bán thương mại; nội dung cảnh báo pháp lý để ở cấu hình, không hard-code |
| O-02 ⚠️ | Công tơ tổng dùng chung nhiều phòng (phổ biến ở nhà trọ nhỏ) | Không tính được tiền điện cho các nhà này ở P1 | Thu thập nhu cầu; P3 `meter_allocations` |
| O-03 ⚠️ | Định nghĩa "doanh thu" cho báo cáo thuế (E6) | Số liệu khác cách hiểu của cơ quan thuế | Kế toán xác nhận; ghi rõ "chỉ tham khảo" |
| O-04 ⚠️ | Lưu trữ dữ liệu cá nhân trên cloud nước ngoài | Có thể vướng quy định chuyển dữ liệu xuyên biên giới (L11) | Chọn object storage + DB đặt tại VN |
| O-05 ⚠️ | Thời hạn lưu giữ dữ liệu người thuê sau khi rời đi | Lưu quá lâu = rủi ro; xóa sớm = mất chứng cứ tranh chấp | Mặc định gợi ý ẩn danh sau 24 tháng, không tự động |
| O-06 ⚠️ | `paid_amount` là cache | Bug code có thể làm lệch | Test bất biến sau mỗi integration test + job đối soát đêm (P2) |
| O-07 ⚠️ | Hiệu năng tạo phiếu cho khu lớn (load snapshot) | Chậm khi > 1.000 phòng | Loader theo khu (vài query batch), đo trong test hiệu năng P1 |
| O-08 ⚠️ | Chủ trọ cần thu theo **giường** / nhiều phòng 1 HĐ | Không hỗ trợ P1 | Workaround: mỗi giường 1 `Room` |
| O-09 ✅ | Người đứng tên không ở cùng ⇒ "chủ hộ" khi đăng ký tạm trú là người khác | Quan hệ khai so với người đứng tên không khớp tờ khai | Đã làm `household_head_renter_id` (CT-BR-36) |
| O-10 ✅ | CT-BR-31 (không ở 2 phòng) chỉ kiểm ở Application | 2 request song song thêm cùng người vào 2 phòng có thể lọt | Đã làm: advisory lock theo người thuê trong transaction (05-contract §10) |
| O-11 ⚠️ | Trường tùy biến điện / nước trùng khoản thu M04 | Nhập 2 nơi, lệch nhau | Khi có M04: tự điền trường từ `contract_fees` (05-contract Q7) |

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
