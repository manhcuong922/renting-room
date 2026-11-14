# 98 — Vấn đề còn mở (rà toàn bộ code + plan 21/10/2026, sắp xếp lại theo nhóm 27/10/2026, rà bổ sung bảo mật / ổn định / deploy 07/11/2026)

> **Cách dùng**: đây là danh sách *chưa xử lý*, gom theo **nhóm việc** (mỗi nhóm sửa chung gốc / chung file). Xử lý xong **cả nhóm** ⇒ **xóa nhóm đó** (không giữ ✅);
> xong từng issue lẻ ⇒ xóa issue đó. Chốt "không làm / giới hạn chấp nhận" ⇒ xóa ở đây, ghi 1 dòng vào [99-self-review.md](99-self-review.md) mục C.
> Lượt rà sau đọc file này thay vì rà lại từ đầu.
> Mức: `H` = HIGH (sai tiền / kẹt quy trình chính / lỗ hổng khai thác được), `M` = MEDIUM, `L` = LOW. ✔ = đã đọc lại code xác nhận; còn lại do agent rà, đã trích dòng code.
> Không có CRITICAL. Số dòng là của working tree 21/10/2026 — có thể lệch sau khi sửa.
> Trong mỗi nhóm, issue xếp từ mức cao xuống thấp. **Quyết định đã chốt** ghi ngay dưới tên nhóm.

## Thứ tự làm đề xuất
1. N4 (kẹt quy trình / sai tiền). Đã xong: N1 (thanh lý / ký lại / kết toán) 27/10/2026; N2 (chỉ số công tơ giữa các HĐ: H-02, M-09, M-10, L-10, Đ2) 28/10/2026; N3 (nháp liên tiếp và tính lại: H-03 31/10/2026, M-21, L-02, L-03 02/11/2026); lẻ L-04, L-07, L-08, L-11, L-21 03/11/2026; L-05, L-06, L-09, L-18 04/11/2026; nháp 2 tab (`version`), L-13, L-16, L-17 05/11/2026, L-14 chấp nhận (99 O-20), L-12, L-15 (host / port; chặn mật khẩu phổ biến: không làm — 99 O-21), L-20 (docs) 05/11/2026; H-06 (tài khoản ngân hàng: chỉ chủ trọ + mật khẩu đăng nhập), N5 / H-05 (import Excel: kẹp cột / dòng / sheet, giải nén ≤ 5 MB) 06/11/2026; **quản lý khu** thay phó quản lý (giao khu, toàn quyền trong khu — M01 ID-BR-24..29; M-12 hết hiệu lực) 08/11/2026; H-04 (sửa trực tiếp số đã thu + đảo kèm phiếu hoàn — M08 PM-BR-38/39) 09/11/2026; **N9** (M-24 docs, M-16 cache `/health/ready`, M-17 bắt buộc `KeysPath`, M-18 seeder chỉ Development), M-25 (chuẩn hóa ký tự điều khiển), M-26 (job ẩn danh bỏ qua hồ sơ lỗi) 11/11/2026; **N6** (M-13 hạn mức + audit dò số giấy tờ, M-15 audit mọi lần xuất người thuê, M-14 gửi mật khẩu tạm qua email cho tài khoản có email), **N7 / M-19** (validator chỉ số), **N8** (M-23 khóa + kiểm lại khi ẩn danh; M-20 chấp nhận — 99 O-23), M-18 (admin bootstrap bắt đổi mật khẩu; mật khẩu demo chấp nhận — 99 O-24), L-22 (key idempotency mới khi 4xx), L-23 (timeout 120s lệnh hàng loạt — còn đo, 99 O-26) 11/11/2026; **M-04** (sửa số lượng dòng giá bậc chia bậc lại; UI chỉ gửi trường đã đổi), **M-11** (mốc ngày bắt đầu đứng tên; chuyển người đứng tên bỏ qua HĐ nguồn) 11/11/2026; **M-08** (khóa giá chỉ khi đổi giá áp của dòng đã chốt), **M-01** (đảo phiếu thu bị chặn khi dòng hoàn trả đã chốt trỏ vào), **M-02** (ký lại ⇒ dịch vụ kỳ cuối HĐ cũ theo ngày = 0; còn khu thu trước — 99 O-28), **M-05** (chốt giữ nguyên số tiền, thêm ghi chú phần đã "Không trả lại" — 99 O-27) 13/11/2026
2. M-14 (phần còn lại), rồi nhóm lẻ

| Nhóm | Nội dung | Issue |
|---|---|---|
| N6 | Phân quyền và dữ liệu nhạy cảm | M-14 (còn tài khoản chưa có email) |
| Lẻ | Zalo | L-19 |

---

## N6 — Phân quyền tiền và dữ liệu nhạy cảm
M-12 hết hiệu lực 08/11/2026 — quản lý khu toàn quyền tiền trong khu được giao (99 O-22).

- **M-14** [Phân quyền, làm dở 11/11/2026] SystemAdmin cấp lại mật khẩu: tài khoản **có email** nay nhận mật khẩu tạm qua email chính chủ, admin không thấy (503 + hủy cấp lại nếu gửi lỗi) — ĐÃ XONG. Còn: tài khoản **chưa có email** (chỉ SĐT) tạm giữ cách cũ (admin nhận mật khẩu trong response) và `POST /admin/organizations` vẫn trả mật khẩu tạm của chủ trọ cho admin (99 O-25). Sửa: kênh Zalo (M11) hoặc bắt buộc email khi tạo tổ chức. **Chờ cấu hình SMTP** (`Email:Smtp` — docs/guides/postgresql.md; chưa cấu hình thì cấp lại cho tài khoản có email trả 503 `EMAIL_NOT_CONFIGURED`).

## Lẻ (chưa gom nhóm)
- **L-19** (M11, chưa code) Mã liên kết Zalo: bộ đếm dò mã trong RAM theo từng chat; hash SHA-256 không khóa. Sửa trước khi code: bộ đếm ở DB + giới hạn chung, HMAC có khóa, rút hạn mã.
