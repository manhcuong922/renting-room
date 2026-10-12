# Dữ liệu mẫu (demo seeder)

Dữ liệu dựng sẵn để test tay trên máy dev và server test. Tạo qua **chính các lệnh nghiệp vụ** nên đúng mọi quy tắc;
ngày tính theo **hôm nay** nên lúc nào seed cũng có phiếu quá hạn, HĐ hết hạn, phiếu chờ hoàn…

## Bật / tắt

| Môi trường | Cấu hình |
|------------|----------|
| Dev | `appsettings.Development.json` → `Seed:DemoData:Enabled = true` (sẵn) |
| Server test | biến môi trường `Seed__DemoData__Enabled=true`, `Seed__DemoData__OwnerPassword`, `Seed__DemoData__ManagerPassword` (+ tùy chọn `OrganizationCode`, `OwnerPhone`, `ManagerPhone`, `WriteOffManagerPhone`) |
| Môi trường thật | **không bật** (mặc định `false` trong `appsettings.json`) |

Chạy khi app khởi động, sau migration và tạo SystemAdmin. Đã có tổ chức cùng mã (`DEMO`) ⇒ bỏ qua, không nhân đôi.
Muốn seed lại từ đầu trên dev: `docker compose down -v` → `docker compose up -d` → chạy API.
Seed lỗi giữa chừng ⇒ app dừng với thông báo `Demo seed failed at <Lệnh>: <MÃ_LỖI>` — sửa seeder rồi làm lại như trên.

## Tài khoản (dev)

| Vai trò | Đăng nhập | Mật khẩu |
|---------|-----------|----------|
| Chủ trọ | `0900000009` | `ChuTroDemo2026` |
| Phó quản lý | `0900000008` | `PhoQuanLy2026` — **chưa có quyền bỏ nợ** (chủ trọ bật / tắt riêng tại màn thành viên) |
| Phó quản lý được bỏ nợ | `0900000007` (`Seed:DemoData:WriteOffManagerPhone`) | cùng mật khẩu phó quản lý — **đã được cấp quyền bỏ nợ** (PM-BR-16) |
| SystemAdmin | theo `Bootstrap:Admin` (user-secrets) | |

## Tình huống

Ký hiệu: **M** = tháng hiện tại; khu B chốt ngày 5 nên "kỳ này" = kỳ bắt đầu ngày 5 gần nhất.

Chủ trọ khai bên cho thuê **một lần** ở tổ chức (`PUT /org/lessor` — cá nhân Nguyễn Văn Chủ).

**Khu A — thu sau, chốt ngày 1, dùng thông tin chủ trọ làm bên cho thuê.** Điện 3.500đ một giá; nước 15.000đ → **18.000đ từ tháng M** (giá theo phiên bản);
wifi 100.000đ → **120.000đ từ ngày 10 tháng M−1** (cả tháng M−1 tính giá mới); rác; nước theo người; giữ xe máy.

| Phòng | Tình huống |
|-------|-----------|
| 101 | Vợ chồng + con 9 tuổi (có đồng ý người giám hộ), 2 xe máy, 3 tháng đã thu đủ; HĐ **duy nhất đã có bản ký** (các HĐ khác mang nhãn "Thiếu tài liệu"); hôm nay hỏng khóa ⇒ **khoản phát sinh "Thay khóa cửa" chờ vào phiếu** tháng này |
| 102 | Phiếu M−2 **quá hạn** chưa thu (nhãn đỏ **Quá hạn**, lọc `GET /rooms?overdue=true`); phiếu M−1 **thu một phần**; phiếu M−1 hiện **"Nợ các kỳ trước"** (M−2) + **"Tổng cần thanh toán"**; người ở ghép **chưa khai quan hệ** ⇒ cảnh báo `RELATIONSHIP_REQUIRED`; **khoản bù "Mất nước 2 ngày" chờ vào phiếu** tháng này |
| 103 | Phiếu nháp M−1: **sửa tay** tiền phòng, **phụ thu** thay khóa, **giảm trừ** mất nước; điện **= 0** khi có người ở ⇒ cảnh báo `UNUSUAL_USAGE`; khoản **"Làm thêm chìa khóa" đã thu ngay** ⇒ hiện trên nháp, không tính vào tổng; phụ thu / giảm trừ trên nháp cũng nằm trong danh sách khoản phát sinh của phòng |
| 104 | Người nước ngoài (hộ chiếu) **vào giữa tháng**, ở ghép; phòng tính **nước theo người** (không công tơ nước); giá niêm yết tăng 3tr → 3,1tr rồi **áp cho người đang thuê** ⇒ HĐ 2,8tr → 3,1tr từ tháng M (tháng M−1 đã lập phiếu giữ giá cũ) |
| 105 | **Đang thanh lý** — báo trả phòng sau 15 ngày; phiếu M−1 **"Thanh toán + bỏ phần còn lại"** (trả thiếu 200k, chủ trọ bỏ do mất nước) |
| 106 | Bỏ đi không báo giữa tháng M−1, làm vỡ gương ⇒ tài sản ghi "Vỡ", phiếu quyết toán có **phụ thu "Bồi thường gương nhà tắm" 300k**; hoàn tất thanh lý bằng **bỏ nợ**; cọc **chưa đánh dấu hoàn** ⇒ nhãn **"Chưa hoàn cọc"** (`GET /contracts?depositNotRefunded=true`) |
| 201 | HĐ cũ đã kết thúc ~10 tháng trước (lịch sử) — **"Đã hoàn trả cọc"** đủ 2,5tr |
| 202 | HĐ **hết hạn chờ quyết định**; người đứng tên **không có SĐT** ⇒ cảnh báo `REPRESENTATIVE_PHONE_MISSING` |
| 203 | HĐ hết hạn, **ở tiếp chưa ký lại** — vẫn **thêm được người ở** sau ngày hết hạn (quá hạn = không thời hạn); phiếu M−2 **chưa thu**; **chuyển sang phòng 206 hôm nay** (giá mới 3,2tr) ⇒ HĐ và cọc giữ nguyên, 203 trống từ mai, **điện + nước của 203 chốt thành 2 khoản phát sinh hệ thống** (không sửa / hủy tay), nhãn **"Còn nợ" sang 206** |
| 204 | **HĐ nháp** sắp vào ở + 1 HĐ nháp **đã hủy**: khách đổi ý ⇒ ghi **"Đã hoàn trả cọc" 0đ** kèm lý do (mất cọc giữ chỗ) |
| 205 | Phòng **bảo trì** |
| 206 | Phòng của HĐ **chuyển từ 203** (chuyển phòng hôm nay) — mang theo nợ; thử **hủy chuyển phòng** tại đây |
| 207 | HĐ **chuyển sang 208 rồi chuyển về lại 207** (phòng đã từng ở) — mỗi lần chuyển sinh 2 khoản chốt điện nước; hôm nay nhận thêm **người đồng thuê chuyển từ 208 sang** (chuyển người). Xem **lịch sử thuê của phòng** (`GET /rooms/{id}/tenancy-history`): 2 lần ở của cùng một HĐ |
| 208 | Sau khi 207 chuyển về: HĐ mới 3 người (đứng tên, em gái, bạn **đồng thuê**); hôm nay bạn đồng thuê **chuyển sang 207**, người đứng tên rời đi, **em gái đứng tên thay trên cùng HĐ** ⇒ HĐ về "Thiếu tài liệu", có lịch sử người đứng tên, quan hệ phải khai lại |
| 210, 211 | Hai phòng mới **lắp công tơ điện hàng loạt** bằng 1 lệnh (`POST /properties/{id}/meters/bulk`), chưa có công tơ nước. 210: thợ sơn dùng **14 kWh lúc phòng trống** ⇒ HĐ nhận phòng ở số 14, hiện ở **sản lượng phòng trống** (`GET /properties/{id}/meters/vacant-usage`); 211 trống |
| 209 | **HĐ nháp** có người đứng tên đang là **người ở thường của phòng 102** ⇒ chi tiết HĐ cảnh báo `REPRESENTATIVE_IS_RESIDENT_ELSEWHERE`; kích hoạt bị chặn cho tới khi đổi người đó thành đồng thuê ở 102 hoặc ghi chuyển đi. Thêm 1 hồ sơ người thuê chưa từng thuê |

Khu A có khoản **"Phí điều hòa"** thêm hàng loạt cho 101, 102 qua màn "Phòng đang dùng dịch vụ" (`POST /fee-types/{id}/usage`).

**Khu B — thu trước, chốt ngày 5, bên cho thuê **riêng của khu** là doanh nghiệp (khác chủ trọ), điện giá bậc** (≤50: 1.984 · ≤100: 2.050 · ≤200: 2.380 · >200: 2.998).

| Phòng | Tình huống |
|-------|-----------|
| B01 | Trả phòng sớm kỳ này ⇒ phiếu quyết toán có cảnh báo **thu thừa**, dòng **hoàn trả** (phiếu nguồn = phiếu kỳ này đã thu trọn tiền phòng), tổng âm ⇒ **Chờ hoàn**; HĐ đang thanh lý, cọc **đang giữ** |
| B02 | Trả phòng sớm kỳ trước ⇒ hoàn trả **đã xác nhận hoàn**, HĐ kết thúc, **"Đã hoàn trả cọc"** đủ; người thuê **ẩn danh bằng tay** — biển số xe máy đã bị xóa |
| B03 | **Thay công tơ điện** giữa kỳ trước ⇒ phiếu nháp kỳ này có 2 đoạn đo |
| B04 | 3 người, **450 kWh** ⇒ phiếu nháp tính giá bậc (tổng lẻ ⇒ **"Làm tròn"**); người ở thứ 4 vào **sau khi lập nháp** ⇒ nháp **"Cần tính lại"** (`GET /invoices?stale=true`); khoản **bù "hỏng quạt" đã trả tiền mặt** ⇒ hiện trên nháp, không trừ vào tổng |
| B05 | Phòng trống |
| B07 → B08 | **Chuyển phòng hôm nay ở khu thu trước, điện giá bậc**: điện 30 kWh (bậc 1) + nước 2 m³ của B07 chốt thành 2 khoản phát sinh hệ thống; giá thuê mới áp cho kỳ này nhưng **kỳ này đã chốt tiền phòng** ⇒ khoản phát sinh **"Chênh lệch tiền phòng" 300k** ở B08, mốc giá mới từ kỳ sau |
| B06 | Người đứng tên chuyển đi, vợ **ký lại** HĐ mới từ mai ⇒ cọc HĐ cũ **"Đã chuyển sang HĐ mới"**, HĐ cũ đang thanh lý |

**Khu C — nhập từ Excel** (thu sau, chốt ngày 1): import phòng (xem trước → lưu dòng hợp lệ; C05 gõ sai số người bị bỏ qua; chỉ số đầu
kỳ ⇒ công tơ lắp từ ngày 1 tháng M), rồi import người thuê đang ở (1 sheet, mỗi dòng 1 người; phòng C09 không tồn tại bị bỏ qua).

| Phòng | Tình huống |
|-------|-----------|
| C01 | Vợ chồng + **con 7 tuổi không giấy tờ**; HĐ bắt đầu 2 năm trước, **tính tiền từ đầu tháng M**; công tơ điện + nước từ import phòng |
| C02 | Người đứng tên **đã có hồ sơ** ⇒ import dùng lại, không sửa |
| C03 | **Hà Văn Toàn đứng tên (không ở)** — cùng người đứng tên C01 ⇒ 1 người thuê 2 phòng; sinh viên ở cùng |
| C04 | Phòng trống |
| C06 | File có **2 dòng "Đứng tên"** cùng phòng ⇒ dòng đầu (Bùi Văn Đạt) đứng tên, dòng sau (Bùi Văn Lực) nhập là **đồng thuê** |

Khu C: **điện lực thay công tơ điện hàng loạt** hôm nay (`EVN-C01`, `EVN-C02`) ⇒ phiếu tháng này cộng 2 công tơ.

**Khu D — đổi ngày chốt** (thu sau): chốt ngày 1, đã lập + thu đủ phiếu M−2, M−1 của D01, rồi đổi sang **ngày 5** ⇒ kỳ chuyển tiếp
đầu tháng M → ngày 4 tháng M+1 (dư 4 ngày, gợi ý tính thêm 4 ngày); phiếu nháp tháng M có dòng "Tiền phòng (1 tháng + 4 ngày — đổi ngày chốt)"
(còn thiếu chỉ số cuối kỳ). Khu D **tắt "Làm tròn tổng phiếu"** (các khu khác bật mặc định) ⇒ tổng nháp giữ số lẻ.

Không seed được HĐ "kết thúc hơn 36 tháng" (phần tính tiền không được trước hôm nay quá 1 năm) — ẩn danh tự động
được kiểm bằng integration test với ngày giả lập.

**Bổ sung 12/10/2026** (khu A): 101 — chồng có **ghi nhận đồng ý** theo điều khoản HĐ, vợ đồng ý miệng việc nhận thông báo rồi **đã rút lại** (`GET /renters/{id}/consents`); hồ sơ **Vũ Văn Cũ đã ẩn** (chỉ hiện khi `GET /renters?includeArchived=true`); 105 đang thanh lý ⇒ thử **xem trước thanh lý** (`GET /contracts/{id}/liquidation/preview`: `blockers` có `LIQUIDATION_BEFORE_END_DATE`, `FINAL_INVOICE_REQUIRED`). Thử trên dữ liệu sẵn có: `GET /invoices/totals` (số tổng hợp), `POST /invoices/generate-bulk` (tạo phiếu mọi khu), `GET /renters?propertyId=…` / `?staying=false`, `GET /org/login-events` (sau vài lần đăng nhập đúng / sai), đăng xuất rồi gọi lại bằng access token cũ ⇒ 401; ghi chỉ số cuối kỳ với ngày ghi đầu tháng ⇒ `warnings` có `READING_DATE_FAR_FROM_PERIOD_END`.

**Không có trong dữ liệu mẫu** (cần dữ liệu nhiều năm — có integration test riêng): xóa nhật ký cũ hơn 5 năm (`AuditRetentionTests`),
ẩn danh người thuê sau 36 tháng (`RenterAnonymizationTests`).

**Thử thêm trên dữ liệu mẫu** (không seed sẵn vì là thao tác bị từ chối / không để lại dấu trên màn hình): tạo khoản **bù** lớn hơn một tháng tiền phòng ở phòng bất kỳ ⇒ 422 `ROOM_CHARGE_CREDIT_TOO_HIGH`; **hủy chỉ số** cuối kỳ của phòng 103 (phiếu còn nháp) ⇒ được, của phòng 101 (phiếu đã chốt) ⇒ `READING_LOCKED`; **tìm theo tên** `GET /tenancy-history?search=tô` ⇒ các phòng 207, 208; chủ trọ **sửa hồ sơ / tên tổ chức** (`PUT /me`, `PUT /org`).

## Quy tắc

Mỗi tính năng / case nghiệp vụ mới ⇒ **thêm kịch bản** vào `renting_room.Infrastructure/Seeding/DemoDataSeeder.*.cs`, cập nhật bảng trên
và `DemoDataSeederTests` (kiểm seed chạy được, không nhân đôi, đủ trạng thái HĐ / phiếu).
