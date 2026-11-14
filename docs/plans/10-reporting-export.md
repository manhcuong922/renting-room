# M10 — Reporting & Export (Xuất Excel, Dashboard, Nhắc việc)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L6, L9, L11, L12, LEG-05, LEG-06.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Xuất **Excel** cho 1 hoặc **nhiều khu**: danh sách người thuê, tiền phòng tháng, thông tin phòng;
dashboard tổng quan; nhắc việc (HĐ sắp hết hạn, cư trú chưa đăng ký, công nợ quá hạn).

**Trạng thái (10/10/2026)**: ✅ chỉ mới code **E1 Danh sách người thuê** (+ khung xuất Excel dùng chung, RP-BR-01..03, 06..09 cho E1).
**Code 09/11/2026**: E2 Thu tiền theo kỳ. **Chưa code**: E3 Thông tin phòng, E4 Công nợ, E5 Danh sách cọc (P1 — nghiệp vụ phiếu + thu tiền đã ổn định, làm được);
E6 doanh thu, E7 đối chiếu hóa đơn điện EVN (LEG-05), dashboard, nhắc việc, export bất đồng bộ (P2). Doanh thu **không tính** tiền bỏ nợ (`WriteOff`) — PM-BR-16.
**Ngoài phạm vi**: BI tùy biến, PDF phức tạp.

## 2. Danh mục export

| Mã | Tên | Tham số | Nội dung (cột) | Ghi chú |
|----|-----|---------|----------------|---------|
| E1 ✅ | Danh sách người thuê | **13/10/2026**: cột vai trò có 3 giá trị **Đại diện / Đồng thuê / Người ở** (trước chỉ 2). propertyIds[] (trống = tất cả), `floors[]`, `roomGroupIds[]`, `roomIds[]`, `fromDate`/`toDate` (mặc định hôm nay; người ở giao khoảng ngày — gồm cả đã chuyển đi), `layout` = `SheetPerProperty` \| `SheetPerFloor` \| `SingleSheet`, `includeSensitive` | STT, Khu, Tầng, Phòng, Số HĐ, Vai trò (Đại diện/Người ở), Họ tên, Ngày sinh, Giới tính, SĐT, Loại & **số giấy tờ (che)**, Ngày cấp, Nơi cấp, Quốc tịch, Địa chỉ thường trú, Nghề nghiệp, Nơi làm việc, **Quan hệ với người đứng tên (chủ hộ)**, **Người chưa thành niên** (đã/chưa có đồng ý của cha mẹ, giám hộ), Ngày vào, Ngày ra (HĐ đang thanh lý = ngày trả phòng), Trạng thái HĐ, Liên hệ khẩn cấp. P2: Tình trạng tạm trú, Hạn tạm trú (khi có bản ghi cư trú M03). Thứ tự trong phòng: người đứng tên → vợ/chồng → cha mẹ → con → … | Phục vụ đối chiếu với công an khu vực; `includeSensitive=true` mới hiện đầy đủ số giấy tờ (audit) |
| E2 | **Thu tiền theo kỳ** (đổi 09/11/2026 — thay "Tiền phòng tháng"; chủ trọ xem lại tiền từng tháng / khu, kể cả bỏ nợ và sửa giảm đã thu) | `propertyIds[]` (trống = mọi khu được xem — quản lý khu: khu được giao), `fromMonth`, `toMonth` (`yyyy-MM`, ≤ 24 tháng), `includeDrafts` (mặc định false — RP-BR-04) | **Sheet "Chi tiết"** — 1 dòng / hóa đơn (tháng + trả phòng, bỏ hóa đơn đã hủy), xếp theo kỳ → khu → phòng: STT, Kỳ, Khu, Phòng, Số hóa đơn, Loại (Tháng / Trả phòng), Người đứng tên (bản chụp trên hóa đơn), Trạng thái, Tiền phòng, Điện nước, Dịch vụ, Phụ thu − giảm trừ − hoàn trả, Kết toán (nợ chuyển − trừ cọc, chỉ hóa đơn trả phòng), Tổng, **Đã thu** (thực thu — PM-BR-36), Ngày thu gần nhất, **Bỏ nợ**, Lý do bỏ nợ, Người bỏ nợ, **Sửa giảm** (Σ phần giảm đã thu — M08 PM-BR-38), Lý do sửa giảm, Người sửa, Còn nợ, Đơn giá điện, Đơn giá nước (bộ giá trên dòng — M07 BL-BR-39: "3.800", theo bậc "bậc 1.984–3.460"; 2 kỳ khác giá ⇒ "a / b"). Nhiều lần bỏ nợ / sửa ⇒ lý do + người nối bằng "; ". **Sheet "Tổng theo kỳ"** — 1 dòng / (kỳ, khu): số hóa đơn, tổng, đã thu, bỏ nợ, sửa giảm, còn nợ + dòng tổng cộng. Rỗng ⇒ vẫn trả file, sheet chi tiết không có dòng | Code 09/11/2026 |
| E3 (chưa code) | Thông tin phòng | propertyIds[], asOfDate | Khu, Phòng, Tầng, Diện tích, Sức chứa, Giá niêm yết, Trạng thái (dẫn xuất), Nhóm phòng, Số HĐ hiện hành, Người đại diện, Giá thuê HĐ, Ngày bắt đầu/hết hạn, Số người ở, Tiền cọc thỏa thuận + trạng thái cọc, Khoản dịch vụ đăng ký (giữ xe ×2…) | |
| E4 (chưa code) | Công nợ | propertyIds[], asOfDate | Khu, Phòng, Người đại diện, SĐT, Số phiếu nợ, Tổng nợ, Nợ quá hạn, Ngày quá hạn lâu nhất | |
| E5 (chưa code) | Danh sách cọc | propertyIds[] | Theo HĐ: phòng, người đứng tên, cọc thỏa thuận, trạng thái (đang giữ / đã hoàn ngày … số tiền / đã chuyển) — M08 PM-BR-32 | |
| E6 (P2 — chưa code) | Doanh thu năm / tháng | year, month, khu | **Định nghĩa (chốt 10/10/2026)** — 2 con số: (1) **Doanh thu** = tiền phòng + điện nước + dịch vụ + **phụ thu** − giảm trừ − **hoàn trả**, chỉ tính phần **đã thu** (phân bổ từ phiếu thu tiền thật `kind = Receipt`), **không** tính phần bỏ nợ (`WriteOff`). (2) **Tiền thực nhận trong kỳ** = phiếu thu `Receipt` theo ngày thu + **phụ thu đã thu ngay** (khoản phát sinh `is_settled`, theo `settled_on`) − tiền hoàn trả đã xác nhận (theo `refunded_on`) − **bù đã trả ngay** (M07 BL-BR-37). Doanh thu (1) cũng cộng phụ thu đã thu ngay và trừ bù đã trả ngay. Tiền cọc **không** vào doanh thu (chỉ theo dõi — M08 PM-BR-32); ghi chú ngưỡng 500 triệu (L12) | Không tính thuế; định nghĩa cần kế toán xác nhận |
| E7 (P2 — chưa code) | Đối chiếu điện | propertyId, billingMonth, evnBillAmount, evnKwh | Σ kWh & Σ tiền điện thu các phòng vs hóa đơn EVN → cảnh báo nếu thu > hóa đơn (L9) | |

## 3. Quy tắc nghiệp vụ

| Mã | Quy tắc |
|----|---------|
| RP-BR-01 | Mọi export lọc theo tổ chức (C-01); `propertyIds` không thuộc tổ chức → 404 cả request (không bỏ qua im lặng) |
| RP-BR-02 | **Chống CSV/Excel formula injection**: giá trị chuỗi bắt đầu bằng `=`, `+`, `-`, `@`, Tab, CR được ghi dạng text với tiền tố `'`; luôn ghi kiểu dữ liệu tường minh (số là số, ngày là ngày) — không ghi chuỗi người dùng nhập vào ô công thức |
| RP-BR-03 | Dữ liệu cá nhân che mặc định (LEG-06); `includeSensitive=true` yêu cầu quyền + ghi audit (ai, khi nào, khu nào, số dòng). **Mọi lần xuất danh sách người thuê đều ghi audit** (11/11/2026, M-15 — trước đây chỉ khi có số giấy tờ; bản che vẫn có ngày sinh, SĐT, địa chỉ, liên hệ khẩn cấp, ≤ 20.000 dòng): kèm số giấy tờ ⇒ `ExportWithIdNumbers`, không kèm ⇒ `ExportRenters`; chi tiết `{ rowCount, propertyIds, from, to }`, ghi trước khi trả file |
| RP-BR-04 | Số liệu tiền lấy **từ phiếu đã chốt** (snapshot), không tính lại; phiếu Draft chỉ xuất khi chọn rõ và sheet ghi chú "NHÁP" |
| RP-BR-05 | Giới hạn đồng bộ: ≤ 20.000 dòng/file; vượt → 422 `EXPORT_TOO_LARGE` (P2: chuyển sang job nền + tải sau) |
| RP-BR-06 | Tên sheet: tên khu cắt ≤ 31 ký tự, bỏ ký tự `[]:*?/\`, trùng thì thêm hậu tố `(2)` |
| RP-BR-07 | Tên file: `{loai}_{yyyyMMdd-HHmm}.xlsx` (giờ VN), header `Content-Disposition` dùng `filename*=UTF-8''…` |
| RP-BR-08 | Thống nhất thuật ngữ "Phiếu báo tiền phòng" (LEG-08) |
| RP-BR-09 ✅ | Export chạy trong transaction `REPEATABLE READ` read-only để số liệu nhất quán giữa các sheet |

## 4. Dữ liệu
Không có bảng nghiệp vụ mới (P1). P2: `export_jobs(id, organization_id, type, params jsonb, status, attachment_id, requested_by, created_at, finished_at, error)`;
`reminders` sinh động bằng query (không lưu) để tránh lệch.

## 5–6. Thiết kế

```
Application/Reporting/
  Queries/ (read model: projection DTO bằng LINQ Select / Dapper cho truy vấn nặng, AsNoTracking)
  Exports/  IExcelWriter (Infrastructure: ClosedXML) — builder chung: header style, freeze pane, auto-filter, column width, number/date format, sanitize (RP-BR-02)
```
| Use case | Loại |
|----------|------|
| `ExportRentersQuery` (E1), `ExportMonthlyInvoicesQuery` (E2), `ExportRoomsQuery` (E3), `ExportDebtsQuery` (E4), `ExportDepositsQuery` (E5) | Qry → Stream |
| `GetDashboardQuery` (P2): tỷ lệ lấp đầy, phải thu/đã thu tháng, nợ quá hạn, HĐ sắp hết hạn, cư trú cần xử lý | Qry |
| `ListRemindersQuery` (P2) | Qry |

## 7. API

| Method | Route | Response |
|--------|-------|----------|
| POST | `/exports/renters` ✅ | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| POST | `/exports/monthly-invoices` ✅ (09/11/2026) | xlsx — E2 Thu tiền theo kỳ |
| POST | `/exports/rooms` (chưa code) | xlsx |
| POST | `/exports/debts` (chưa code) | xlsx |
| POST | `/exports/deposits` (chưa code) | xlsx |
| GET | `/dashboard?propertyIds=` (P2) | JSON |
| GET | `/reminders?propertyIds=&types=` (P2) | JSON |

(POST vì tham số là danh sách dài và không nên bị cache/prefetch.)

```json
POST /api/v1/exports/monthly-invoices
{ "propertyIds": ["…", "…"], "fromMonth": "2026-09", "toMonth": "2026-11", "includeDrafts": false }
```

## 8. Validation
propertyIds ≤ 100 (E1, E2), id không thuộc tổ chức / khu được giao ⇒ 404 `PROPERTY_NOT_FOUND`; tháng `yyyy-MM`; `fromMonth ≤ toMonth`, khoảng ≤ 24 tháng (E2: 400 `INVALID_MONTH_RANGE`); layout enum.

## 9. Phân quyền
Chủ trọ (mọi khu) và quản lý khu (khu được giao — dữ liệu ngoài khu không vào file, M01 ID-BR-24); `includeSensitive = true` (E1): chủ trọ và quản lý khu (khu được giao — M01 ID-BR-22), tài khoản không còn hoạt động ⇒ 403 `SENSITIVE_DATA_FORBIDDEN`.

## 10. Toàn vẹn
Số liệu nhất quán (RP-BR-09); dùng snapshot phiếu (RP-BR-04); cột khoản thu động gộp theo **tên** — 2 khu có "Wifi" khác giá vẫn chung cột (đơn giá ở từng dòng), khoản cùng tên khác nhóm → tách cột theo `tên (đơn vị)`.

## 12. Test
- Golden file: mở file sinh ra bằng ClosedXML, kiểm tra header, số dòng, tổng = Σ DB.
- Formula injection: tên người thuê `=HYPERLINK(...)` → ô là text.
- Nhiều khu, khoản thu khác nhau → cột động đúng; khu org khác → 404; che số giấy tờ mặc định; audit khi includeSensitive.
- Hiệu năng: 5.000 phòng / 20.000 dòng < 10s, bộ nhớ < 300MB.
- **E2 Thu tiền theo kỳ (09/11/2026)**: X1 khu A 2 phòng, 2 tháng: P101 tháng trước thu đủ 3tr; P102 tháng trước thu 2tr + bỏ nợ 1tr lý do "Khó khăn"; P101 tháng này thu 3tr rồi sửa xuống 2,8tr lý do "Nhập nhầm" ⇒ sheet Chi tiết 4 dòng (đủ cột, đã thu / bỏ nợ / sửa giảm / còn nợ đúng, lý do + người bỏ nợ / sửa, đơn giá điện "3.800"); sheet Tổng theo kỳ 2 dòng + tổng cộng khớp Σ.
  X2 nháp chỉ có khi `includeDrafts = true`; hóa đơn đã hủy không có. X3 `fromMonth > toMonth` / > 24 tháng ⇒ 400; khu không tồn tại ⇒ 404. X4 quản lý khu chỉ thấy dòng khu được giao; khu ngoài ⇒ 404.

## 15. Câu hỏi mở
| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Mẫu E1 có cần đúng mẫu của công an phường? | Đã có cột quan hệ với chủ hộ + người chưa thành niên theo tờ khai CT01; thu thập mẫu thực tế từ người dùng; P2 thêm template tùy biến |
| Q2 | Xuất PDF phiếu báo để gửi Zalo? | P2 (QuestPDF — kiểm tra license cộng đồng/thương mại) |
