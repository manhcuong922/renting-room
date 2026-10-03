# M10 — Reporting & Export (Xuất Excel, Dashboard, Nhắc việc)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L6, L9, L11, L12, LEG-05, LEG-06.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Xuất **Excel** cho 1 hoặc **nhiều khu**: danh sách người thuê, tiền phòng tháng, thông tin phòng;
dashboard tổng quan; nhắc việc (HĐ sắp hết hạn, cư trú chưa đăng ký, công nợ quá hạn).

**Trong phạm vi (P1)**: E1 Danh sách người thuê; E2 Tiền phòng tháng; E3 Thông tin phòng; E4 Công nợ; E5 Sổ cọc.
**P2**: dashboard, nhắc việc, đối chiếu hóa đơn điện EVN (LEG-05), báo cáo doanh thu năm (L12), export bất đồng bộ.
**Ngoài phạm vi**: BI tùy biến, PDF phức tạp.

## 2. Danh mục export

| Mã | Tên | Tham số | Nội dung (cột) | Ghi chú |
|----|-----|---------|----------------|---------|
| E1 ✅ | Danh sách người thuê | propertyIds[] (trống = tất cả), `floors[]`, `roomGroupIds[]`, `roomIds[]`, `fromDate`/`toDate` (mặc định hôm nay; người ở giao khoảng ngày — gồm cả đã chuyển đi), `layout` = `SheetPerProperty` \| `SheetPerFloor` \| `SingleSheet`, `includeSensitive` | STT, Khu, Tầng, Phòng, Số HĐ, Vai trò (Đại diện/Người ở), Họ tên, Ngày sinh, Giới tính, SĐT, Loại & **số giấy tờ (che)**, Ngày cấp, Nơi cấp, Quốc tịch, Địa chỉ thường trú, Nghề nghiệp, Nơi làm việc, **Quan hệ với người đứng tên (chủ hộ)**, **Người chưa thành niên** (đã/chưa có đồng ý của cha mẹ, giám hộ), Ngày vào, Ngày ra (HĐ đang thanh lý = ngày trả phòng), Trạng thái HĐ, Liên hệ khẩn cấp. P2: Tình trạng tạm trú, Hạn tạm trú (khi có bản ghi cư trú M03). Thứ tự trong phòng: người đứng tên → vợ/chồng → cha mẹ → con → … | Phục vụ đối chiếu với công an khu vực; `includeSensitive=true` mới hiện đầy đủ số giấy tờ (audit) |
| E2 | Tiền phòng tháng | propertyIds[], billingMonth, `statuses` (mặc định Finalized), layout | Khu, Phòng, Số phiếu, Người đại diện, Kỳ, Tiền phòng (gốc / thực thu tháng), **một cột cho mỗi khoản thu** (động theo danh mục của các khu được chọn, gộp theo tên), Chỉ số điện cũ/mới/sản lượng, Chỉ số nước cũ/mới/sản lượng, Điều chỉnh, Tổng, Đã thu, Còn nợ, Hạn, Trạng thái | Hàng tổng cuối mỗi sheet bằng **công thức SUM**; số tiền định dạng `#,##0` |
| E3 | Thông tin phòng | propertyIds[], asOfDate | Khu, Phòng, Tầng, Diện tích, Sức chứa, Giá niêm yết, Trạng thái (dẫn xuất), Nhóm phòng, Số HĐ hiện hành, Người đại diện, Giá thuê HĐ, Ngày bắt đầu/hết hạn, Số người ở, Tiền cọc đang giữ, Khoản thu đăng ký (giữ xe ×2…) | |
| E4 | Công nợ | propertyIds[], asOfDate | Khu, Phòng, Người đại diện, SĐT, Số phiếu nợ, Tổng nợ, Nợ quá hạn, Ngày quá hạn lâu nhất | |
| E5 | Sổ cọc | propertyIds[], from, to | Bút toán cọc + số dư cuối kỳ theo HĐ | |
| E6 (P2) | Doanh thu năm | year | Tổng thu theo tháng: tiền phòng, dịch vụ, mất cọc; ghi chú ngưỡng 500 triệu (L12). **Định nghĩa**: Σ phân bổ từ phiếu thu tiền thật (`Cash/BankTransfer/EWallet/Other`) + `DepositDeduction` (cọc chuyển thành tiền thuê tại thời điểm cấn trừ) + bút toán `Forfeit`; **loại trừ** `WriteOff`, `CreditNote`, bút toán nhận/hoàn cọc | Không tính thuế; định nghĩa doanh thu cần kế toán/luật sư xác nhận |
| E7 (P2) | Đối chiếu điện | propertyId, billingMonth, evnBillAmount, evnKwh | Σ kWh & Σ tiền điện thu các phòng vs hóa đơn EVN → cảnh báo nếu thu > hóa đơn (L9) | |

## 3. Quy tắc nghiệp vụ

| Mã | Quy tắc |
|----|---------|
| RP-BR-01 | Mọi export lọc theo tổ chức (C-01); `propertyIds` không thuộc tổ chức → 404 cả request (không bỏ qua im lặng) |
| RP-BR-02 | **Chống CSV/Excel formula injection**: giá trị chuỗi bắt đầu bằng `=`, `+`, `-`, `@`, Tab, CR được ghi dạng text với tiền tố `'`; luôn ghi kiểu dữ liệu tường minh (số là số, ngày là ngày) — không ghi chuỗi người dùng nhập vào ô công thức |
| RP-BR-03 | Dữ liệu cá nhân che mặc định (LEG-06); `includeSensitive=true` yêu cầu quyền + ghi audit (ai, khi nào, khu nào, số dòng) |
| RP-BR-04 | Số liệu tiền lấy **từ phiếu đã chốt** (snapshot), không tính lại; phiếu Draft chỉ xuất khi chọn rõ và sheet ghi chú "NHÁP" |
| RP-BR-05 | Giới hạn đồng bộ: ≤ 20.000 dòng/file; vượt → 422 `EXPORT_TOO_LARGE` (P2: chuyển sang job nền + tải sau) |
| RP-BR-06 | Tên sheet: tên khu cắt ≤ 31 ký tự, bỏ ký tự `[]:*?/\`, trùng thì thêm hậu tố `(2)` |
| RP-BR-07 | Tên file: `{loai}_{yyyyMMdd-HHmm}.xlsx` (giờ VN), header `Content-Disposition` dùng `filename*=UTF-8''…` |
| RP-BR-08 | Thống nhất thuật ngữ "Phiếu báo tiền phòng" (LEG-08) |
| RP-BR-09 | Export chạy trong transaction `REPEATABLE READ` read-only để số liệu nhất quán giữa các sheet |

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
| `ExportRentersQuery` (E1), `ExportMonthlyInvoicesQuery` (E2), `ExportRoomsQuery` (E3), `ExportDebtsQuery` (E4), `ExportDepositLedgerQuery` (E5) | Qry → Stream |
| `GetDashboardQuery` (P2): tỷ lệ lấp đầy, phải thu/đã thu tháng, nợ quá hạn, HĐ sắp hết hạn, cư trú cần xử lý | Qry |
| `ListRemindersQuery` (P2) | Qry |

## 7. API

| Method | Route | Response |
|--------|-------|----------|
| POST | `/exports/renters` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| POST | `/exports/monthly-invoices` | xlsx |
| POST | `/exports/rooms` | xlsx |
| POST | `/exports/debts` | xlsx |
| POST | `/exports/deposit-ledger` | xlsx |
| GET | `/dashboard?propertyIds=` (P2) | JSON |
| GET | `/reminders?propertyIds=&types=` (P2) | JSON |

(POST vì tham số là danh sách dài và không nên bị cache/prefetch.)

```json
POST /api/v1/exports/monthly-invoices
{ "propertyIds": ["…", "…"], "billingMonth": "2026-11", "statuses": ["Finalized"], "layout": "SheetPerProperty" }
```

## 8. Validation
propertyIds 1–50, không trùng; billingMonth `yyyy-MM`; from ≤ to, khoảng ≤ 24 tháng; layout enum.

## 9. Phân quyền
OrgOwner; OrgManager (P3) chỉ khu được gán, `includeSensitive` cần quyền `ExportSensitive`.

## 10. Toàn vẹn
Số liệu nhất quán (RP-BR-09); dùng snapshot phiếu (RP-BR-04); cột khoản thu động gộp theo **tên** — 2 khu có "Wifi" khác giá vẫn chung cột (đơn giá ở từng dòng), khoản cùng tên khác nhóm → tách cột theo `tên (đơn vị)`.

## 12. Test
- Golden file: mở file sinh ra bằng ClosedXML, kiểm tra header, số dòng, tổng = Σ DB.
- Formula injection: tên người thuê `=HYPERLINK(...)` → ô là text.
- Nhiều khu, khoản thu khác nhau → cột động đúng; khu org khác → 404; che số giấy tờ mặc định; audit khi includeSensitive.
- Hiệu năng: 5.000 phòng / 20.000 dòng < 10s, bộ nhớ < 300MB.

## 14. Task

| ID | Task | Ước lượng |
|----|------|-----------|
| RP-01 ✅ | `ISpreadsheetWriter` (ClosedXML 0.105) + chống công thức + tên sheet + style chung | 1d |
| RP-02 | E1 ✅ (lọc khu / tầng / nhóm phòng / phòng / khoảng ngày; chia sheet theo khu / tầng), E3 | 1.5d |
| RP-03 | E2 (cột động) | 1.5d |
| RP-04 | E4, E5 | 1d |
| RP-05 | Tests | 1d |
| RP-06 (P2) | Dashboard, reminders, E6, E7, export job | 4d |

## 15. Câu hỏi mở
| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Mẫu E1 có cần đúng mẫu của công an phường? | Đã có cột quan hệ với chủ hộ + người chưa thành niên theo tờ khai CT01; thu thập mẫu thực tế từ người dùng; P2 thêm template tùy biến |
| Q2 | Xuất PDF phiếu báo để gửi Zalo? | P2 (QuestPDF — kiểm tra license cộng đồng/thương mại) |
