# Dữ liệu mẫu (demo seeder)

Dữ liệu dựng sẵn để test tay trên máy dev và server test. Tạo qua **chính các lệnh nghiệp vụ** nên đúng mọi quy tắc;
ngày tính theo **hôm nay** nên lúc nào seed cũng có phiếu quá hạn, HĐ hết hạn, phiếu chờ hoàn…

## Bật / tắt

| Môi trường | Cấu hình |
|------------|----------|
| Dev | `appsettings.Development.json` → `Seed:DemoData:Enabled = true` (sẵn) |
| Server test | biến môi trường `Seed__DemoData__Enabled=true`, `Seed__DemoData__OwnerPassword`, `Seed__DemoData__ManagerPassword` (+ tùy chọn `OrganizationCode`, `OwnerPhone`, `ManagerPhone`) |
| Môi trường thật | **không bật** (mặc định `false` trong `appsettings.json`) |

Chạy khi app khởi động, sau migration và tạo SystemAdmin. Đã có tổ chức cùng mã (`DEMO`) ⇒ bỏ qua, không nhân đôi.
Muốn seed lại từ đầu trên dev: `docker compose down -v` → `docker compose up -d` → chạy API.
Seed lỗi giữa chừng ⇒ app dừng với thông báo `Demo seed failed at <Lệnh>: <MÃ_LỖI>` — sửa seeder rồi làm lại như trên.

## Tài khoản (dev)

| Vai trò | Đăng nhập | Mật khẩu |
|---------|-----------|----------|
| Chủ trọ | `0900000009` | `ChuTroDemo2026` |
| Phó quản lý | `0900000008` | `PhoQuanLy2026` |
| SystemAdmin | theo `Bootstrap:Admin` (user-secrets) | |

## Tình huống

Ký hiệu: **M** = tháng hiện tại; khu B chốt ngày 5 nên "kỳ này" = kỳ bắt đầu ngày 5 gần nhất.

Chủ trọ khai bên cho thuê **một lần** ở tổ chức (`PUT /org/lessor` — cá nhân Nguyễn Văn Chủ).

**Khu A — thu sau, chốt ngày 1, dùng thông tin chủ trọ làm bên cho thuê.** Điện 3.500đ một giá; nước 15.000đ → **18.000đ từ tháng M** (giá theo phiên bản);
wifi 100.000đ → **120.000đ từ ngày 10 tháng M−1** (cả tháng M−1 tính giá mới); rác; nước theo người; giữ xe máy.

| Phòng | Tình huống |
|-------|-----------|
| 101 | Vợ chồng + con 9 tuổi (có đồng ý người giám hộ), 2 xe máy, 3 tháng đã thu đủ; HĐ **duy nhất đã có bản ký** (các HĐ khác mang nhãn "Thiếu tài liệu") |
| 102 | Phiếu M−2 **quá hạn** chưa thu (nhãn đỏ **Quá hạn**, lọc `GET /rooms?overdue=true`); phiếu M−1 **thu một phần**; người ở ghép **chưa khai quan hệ** ⇒ cảnh báo `RELATIONSHIP_REQUIRED` |
| 103 | Phiếu nháp M−1: **sửa tay** tiền phòng, **phụ thu** thay khóa, **giảm trừ** mất nước; điện **= 0** khi có người ở ⇒ cảnh báo `UNUSUAL_USAGE` |
| 104 | Người nước ngoài (hộ chiếu) **vào giữa tháng**, ở ghép; phòng tính **nước theo người** (không công tơ nước); giá niêm yết tăng 3tr → 3,1tr rồi **áp cho người đang thuê** ⇒ HĐ 2,8tr → 3,1tr từ tháng M (tháng M−1 đã lập phiếu giữ giá cũ) |
| 105 | **Đang thanh lý** — báo trả phòng sau 15 ngày |
| 106 | Bỏ đi không báo giữa tháng M−1 ⇒ phiếu quyết toán, **bỏ nợ**, HĐ đã kết thúc |
| 201 | HĐ cũ đã kết thúc ~10 tháng trước (lịch sử) |
| 202 | HĐ **hết hạn chờ quyết định**; người đứng tên **không có SĐT** ⇒ cảnh báo `REPRESENTATIVE_PHONE_MISSING` |
| 203 | HĐ hết hạn, **ở tiếp chưa ký lại** |
| 204 | **HĐ nháp** sắp vào ở + 1 HĐ nháp **đã hủy** |
| 205 | Phòng **bảo trì** |
| 206 | Phòng trống; thêm 1 hồ sơ người thuê chưa từng thuê |

Khu A có khoản **"Phí điều hòa"** thêm hàng loạt cho 101, 102 qua màn "Phòng đang dùng dịch vụ" (`POST /fee-types/{id}/usage`).

**Khu B — thu trước, chốt ngày 5, bên cho thuê **riêng của khu** là doanh nghiệp (khác chủ trọ), điện giá bậc** (≤50: 1.984 · ≤100: 2.050 · ≤200: 2.380 · >200: 2.998).

| Phòng | Tình huống |
|-------|-----------|
| B01 | Trả phòng sớm kỳ này ⇒ phiếu quyết toán có cảnh báo **thu thừa**, dòng **hoàn trả**, tổng âm ⇒ **Chờ hoàn**; HĐ đang thanh lý |
| B02 | Trả phòng sớm kỳ trước ⇒ hoàn trả **đã xác nhận hoàn**, HĐ kết thúc, người thuê **ẩn danh bằng tay** (yêu cầu xóa dữ liệu) — biển số xe máy đã bị xóa |
| B03 | **Thay công tơ điện** giữa kỳ trước ⇒ phiếu nháp kỳ này có 2 đoạn đo |
| B04 | 3 người, **450 kWh** ⇒ phiếu nháp tính giá bậc |
| B05 | Phòng trống |

**Khu C — nhập từ Excel** (thu sau, chốt ngày 1): import phòng (xem trước → lưu dòng hợp lệ; C05 gõ sai số người bị bỏ qua; chỉ số đầu
kỳ ⇒ công tơ lắp từ ngày 1 tháng M), rồi import người thuê đang ở (1 sheet, mỗi dòng 1 người; phòng C09 không tồn tại bị bỏ qua).

| Phòng | Tình huống |
|-------|-----------|
| C01 | Vợ chồng + **con 7 tuổi không giấy tờ**; HĐ bắt đầu 2 năm trước, **tính tiền từ đầu tháng M**; công tơ điện + nước từ import phòng |
| C02 | Người đứng tên **đã có hồ sơ** ⇒ import dùng lại, không sửa |
| C03 | **Hà Văn Toàn đứng tên (không ở)** — cùng người đứng tên C01 ⇒ 1 người thuê 2 phòng; sinh viên ở cùng |
| C04 | Phòng trống |

**Khu D — đổi ngày chốt** (thu sau): chốt ngày 1, đã lập + thu đủ phiếu M−2, M−1 của D01, rồi đổi sang **ngày 5** ⇒ kỳ chuyển tiếp
đầu tháng M → ngày 4 tháng M+1 (dư 4 ngày, gợi ý tính thêm 4 ngày); phiếu nháp tháng M có dòng "Tiền phòng (1 tháng + 4 ngày — đổi ngày chốt)"
(còn thiếu chỉ số cuối kỳ).

Không seed được HĐ "kết thúc hơn 36 tháng" (phần tính tiền không được trước hôm nay quá 1 năm) — ẩn danh tự động
được kiểm bằng integration test với ngày giả lập.

## Quy tắc

Mỗi tính năng / case nghiệp vụ mới ⇒ **thêm kịch bản** vào `renting_room.Infrastructure/Seeding/DemoDataSeeder.*.cs`, cập nhật bảng trên
và `DemoDataSeederTests` (kiểm seed chạy được, không nhân đôi, đủ trạng thái HĐ / phiếu).
