# M08 — Payment & Deposit (Thu tiền, Bỏ nợ, Công nợ, Theo dõi cọc)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L1 (đặt cọc — Điều 328 BLDS), LEG-02.
> **Rà soát 10/10/2026**: file viết lại theo đúng phạm vi đã chốt — bỏ hẳn sổ cọc / trừ cọc / quyết toán cọc (thiết kế G1 cũ đã hủy).

## 1. Mục tiêu & phạm vi

**Mục tiêu**: ghi nhận **tiền thực thu** của người thuê và phân bổ vào phiếu tiền phòng đã chốt (M07); đóng phần nợ không thu được (**bỏ nợ**);
cho biết **công nợ** theo HĐ / phòng; **theo dõi tiền cọc** ở mức tối giản: HĐ có cọc không, cọc bao nhiêu, đã hoàn trả chưa.

**Trong phạm vi**: thu tiền bằng tay (1 phiếu hoặc tự động phiếu cũ nhất trước), đảo phiếu thu, "Đã thu toàn bộ" khi hoàn tất thanh lý, bỏ nợ
(riêng / kèm thanh toán / khi hoàn tất thanh lý), nợ các kỳ trước trên phiếu, trạng thái cọc của HĐ.

**Ngoài phạm vi**: sổ cọc / trừ cọc vào phiếu / quyết toán cọc / chuyển cọc / mất cọc (**không làm** — chốt 10/10/2026), tính lãi chậm trả
(không làm), cổng thanh toán.

**Để sau (backlog)**: VietQR trên phiếu gửi Zalo (P2); thanh toán online / đối soát ngân hàng (P3). Khi có thanh toán online, tiền chuyển thừa
không chặn được ⇒ lúc đó mới làm **số dư có** (trả thừa, tự trừ vào phiếu sau) và bỏ chặn `PAYMENT_EXCEEDS_DEBT` cho kênh online.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Phiếu thu | `Payment` | Một lần ghi nhận tiền của 1 HĐ, số `PT{yyyy}-{000000}` |
| Phân bổ | `PaymentAllocation` | Phần tiền của phiếu thu gán vào 1 phiếu tiền phòng |
| Loại phiếu thu | `PaymentKind` | `Receipt` = tiền thật; `WriteOff` = **bỏ nợ** (không phải tiền, không tính doanh thu) |
| Đảo phiếu thu | `Reverse` | Hủy hiệu lực phiếu thu nhập sai (không xóa) |
| Cọc thỏa thuận | `contracts.deposit_amount` | Số tiền cọc ghi trên HĐ; 0 = HĐ không cọc |
| Trạng thái cọc | `contracts.deposit_status` | `Holding` (đang giữ), `Refunded` (đã hoàn trả), `Transferred` (chuyển sang HĐ ký lại) — chỉ có nghĩa khi `deposit_amount > 0` |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| PM-UC-01 ✅ | **Đã thu** bằng tay (tiền mặt / chuyển khoản tự kiểm): từ phiếu bấm "Đã thu" (mặc định đủ số còn nợ, sửa được nếu thu một phần — **trả dần**, phần còn lại vẫn là nợ), hoặc thu theo HĐ (tự trừ phiếu cũ nhất trước); phương thức, ngày thu, người nộp, mã giao dịch |
| PM-UC-04 ✅ | Đảo phiếu thu nhập sai (lý do) — phiếu tiền phòng trở lại còn nợ |
| PM-UC-12 ✅ | **Công nợ khi xem phòng**: nhãn "Còn nợ" + số tiền, nhãn đỏ "Quá hạn" (M02 PR-BR-16) |
| PM-UC-14 ✅ | **"Đã thu toàn bộ"** khi hoàn tất thanh lý: 1 phiếu thu đúng tổng còn nợ (mặc định tiền mặt, hôm nay) |
| PM-UC-16 ✅ | **"Thanh toán + bỏ phần còn lại"** trên 1 phiếu (VD nợ 3tr, trả 2tr, chủ trọ cho 1tr) và **bỏ nợ riêng** (một phần / toàn bộ) — bất kỳ lúc nào HĐ còn hiệu lực / đang thanh lý; lý do bắt buộc |
| PM-UC-18 ✅ | **Nợ các kỳ trước trên phiếu**: chi tiết phiếu / nháp hiện danh sách phiếu cũ còn nợ + "Tổng cần thanh toán" — chỉ hiển thị, không cộng vào phiếu |
| PM-UC-20 ✅ | **Theo dõi cọc**: chi tiết HĐ / phòng hiện có cọc không, cọc bao nhiêu, đang giữ / đã hoàn trả (ngày, số tiền thực trả, ghi chú) / đã chuyển sang HĐ ký lại; bấm **"Đã hoàn trả cọc"**, bỏ đánh dấu nếu nhập nhầm; lọc HĐ đã kết thúc mà **chưa hoàn cọc** |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| PM-BR-01 ✅ | `payment.amount` > 0, số nguyên đồng, ≤ 10 tỷ. Không sửa số tiền phiếu thu — sai thì đảo rồi ghi lại | Domain + CHECK |
| PM-BR-02 ✅ | Phân bổ chỉ vào phiếu **đã chốt** của **cùng HĐ**; mỗi phân bổ > 0; Σ phân bổ = số tiền phiếu thu | Domain + FK composite `(organization_id, contract_id, invoice_id)` |
| PM-BR-05 ✅ | `invoices.paid_amount` cập nhật **cùng transaction** với tạo / hủy phân bổ, sau khi khóa HĐ → các phiếu `FOR UPDATE` | Application |
| PM-BR-06 ✅ | Thu theo HĐ (không chỉ định phiếu): phân bổ phiếu còn nợ theo `due_date` tăng dần rồi `period_start` (cũ nhất trước). Thu vượt số còn nợ (của phiếu / của HĐ) → 422 `PAYMENT_EXCEEDS_DEBT`. Phiếu tổng âm (Chờ hoàn — M07 BL-BR-27) không nhận phân bổ, không bù trừ nợ | Application |
| PM-BR-07 ✅ | Đảo phiếu thu: hủy mọi phân bổ (giảm `paid_amount`), trạng thái `Reversed`, lý do bắt buộc; HĐ đã kết thúc → 422 `CONTRACT_NOT_BILLABLE` | Domain + Application |
| PM-BR-13 ✅ | Chỉ ghi thu / bỏ nợ cho HĐ `Active` / `Liquidating`. HĐ `Ended`: không ghi thu (hoàn tất thanh lý đã buộc hết nợ — thu đủ hoặc bỏ nợ, M05 CT-BR-12) | Domain |
| PM-BR-14 ✅ | `paid_at` ≤ hôm nay và ≥ ngày bắt đầu HĐ − 60 ngày | Validator |
| PM-BR-15 ✅ | Số phiếu thu `PT{yyyy}-{seq:000000}` unique trong tổ chức; `yyyy` = năm của **ngày lập** (ghi bù đầu năm không làm lộn số) | DB + sequence |
| PM-BR-16 ✅ | **Bỏ nợ**: phiếu thu `kind = WriteOff`, lý do bắt buộc (`note`), phân bổ như tiền thật để đóng công nợ; phiếu tiền phòng hiện `WrittenOff`, `written_off_amount` ghi phần bỏ; **không** tính doanh thu / tiền thu (M10 lọc `kind = Receipt`); vẫn cấp số `PT`, đảo được. Làm được: bỏ nợ riêng, "Thanh toán + bỏ phần còn lại" (PM-BR-29), "Bỏ nợ" khi hoàn tất thanh lý. **Quyền**: chủ trọ, hoặc phó quản lý được chủ trọ cấp quyền **riêng từng người** (M01 ID-BR-23) → không có quyền 403 `WRITE_OFF_NOT_ALLOWED` | Domain + Application |
| PM-BR-18 ✅ | `POST /contracts/{id}/payments` chỉ ghi tiền thật (`kind = Receipt`, `method` ∈ `Cash` / `BankTransfer` / `EWallet`) | Validator |
| PM-BR-29 ✅ | **Thanh toán + bỏ phần còn lại**: 1 lệnh, 1 transaction trên 1 phiếu — phiếu thu `Receipt` số thực trả (> 0, < số còn nợ; bằng số còn nợ → 422 `NOTHING_TO_WRITE_OFF`) + phiếu thu `WriteOff` phần còn lại. Bỏ nợ riêng: ≤ số còn nợ của phiếu (mặc định toàn bộ) | Application |
| PM-BR-31 ✅ | **Nợ các kỳ trước** (PM-UC-18): phiếu đã chốt của cùng HĐ, kỳ trước phiếu đang xem, còn nợ; "Tổng cần thanh toán" = còn phải trả của phiếu này (nháp: tổng phiếu) + nợ các kỳ trước | Query |
| PM-BR-32 ✅ | **Cọc tối giản** (chốt 10/10/2026): cọc chỉ là thông tin theo dõi trên HĐ — **không** ghi nhận tiền cọc vào / ra, **không** trừ cọc vào phiếu, **không** chặn hủy / thanh lý vì cọc, **không** tính vào doanh thu. `deposit_amount = 0` ⇒ "Không cọc". `deposit_amount > 0` ⇒ trạng thái mặc định `Holding` ("Đang giữ cọc") | Domain |
| PM-BR-33 ✅ | **"Đã hoàn trả cọc"** — `POST /contracts/{id}/deposit/refund { refundedOn, amount, note }`: HĐ có cọc, đang `Holding`; `refundedOn` ≤ hôm nay; `amount` 0…`deposit_amount` (mặc định = cọc); trả **ít hơn** cọc (chủ trọ giữ lại một phần / mất cọc) ⇒ `note` bắt buộc → 400. Được ở mọi trạng thái HĐ (kể cả đã kết thúc, đã hủy — khách không đến). `DELETE /contracts/{id}/deposit/refund` bỏ đánh dấu (nhập nhầm) ⇒ về `Holding` | Domain + Application |
| PM-BR-34 ✅ | **Ký lại HĐ** (M05 CT-UC-21): HĐ mới chép cọc thỏa thuận; mặc định HĐ cũ ⇒ `Transferred` ("Đã chuyển sang HĐ …"); chọn không chuyển ⇒ HĐ cũ vẫn `Holding` để hoàn sau. **Chuyển phòng** (M05 CT-UC-12): HĐ giữ nguyên ⇒ cọc không đổi | Application |
| PM-BR-35 ✅ | **Nhắc hoàn cọc**: hoàn tất thanh lý **không chặn** vì cọc; chi tiết HĐ / danh sách HĐ có cờ "Chưa hoàn cọc" cho HĐ `Ended` có cọc đang `Holding`; lọc `GET /contracts?depositNotRefunded=true` | Query |

### 3.3 Vòng đời
- Payment: `Recorded` → `Reversed`. Không sửa số tiền.
- Cọc của HĐ: `Holding` ⇄ `Refunded` (bỏ đánh dấu khi nhập nhầm); `Holding` → `Transferred` (ký lại).

## 4. Dữ liệu

**`payments`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | UNIQUE (organization_id, contract_id, id) |
| property_id, contract_id | uuid | N | FK composite → contracts |
| receipt_no | varchar(20) | N | UNIQUE (organization_id, receipt_no) |
| amount | numeric(18,0) | N | CHECK > 0 |
| method | varchar(20) | N | `Cash`, `BankTransfer`, `EWallet` |
| kind | varchar(10) | N | `Receipt`, `WriteOff` |
| paid_at | date | N | |
| payer_name | varchar(200) | Y | xóa khi ẩn danh người đứng tên (RT-BR-06) |
| reference | varchar(100) | Y | mã giao dịch ngân hàng |
| note | varchar(500) | Y | lý do bỏ nợ |
| status | varchar(10) | N | `Recorded`, `Reversed` |
| reversed_at, reverse_reason | | Y | CHECK reason khi Reversed |
| created_at/by, xmin | | | |

**`payment_allocations`**: id, organization_id, contract_id, payment_id, invoice_id, amount > 0, cancelled_at.
FK composite `(organization_id, contract_id, payment_id)` → payments và `(organization_id, contract_id, invoice_id)` → invoices ⇒ DB chặn phân bổ chéo HĐ.

**Cọc trên `contracts`** (M05): `deposit_amount` numeric(18,0) ≥ 0, `deposit_terms`, `deposit_status` varchar(12) (`Holding` / `Refunded` /
`Transferred`), `deposit_refunded_on` date, `deposit_refunded_amount` numeric(18,0), `deposit_note` varchar(500).
CHECK: `deposit_status <> 'Refunded' OR (deposit_refunded_on IS NOT NULL AND deposit_refunded_amount BETWEEN 0 AND deposit_amount)`.

**`document_number_sequences`**: (organization_id, prefix, year) — dùng chung `PB` (phiếu tiền phòng) và `PT` (phiếu thu).

## 5. Domain model

```
Payment (aggregate; Allocations)
  + Record(propertyId, contractId, receiptNo, amount, method, paidAt, payerName, reference, note, allocations, kind)
  + Reverse(reason, now) : (invoiceId, amount)[]       // để giảm paid_amount
  + ClearPayerName()                                     // RT-BR-06
Contract (M05)
  + RefundDeposit(refundedOn, amount, note, today)       // PM-BR-33
  + CancelDepositRefund()
  + MarkDepositTransferred(note)                         // PM-BR-34
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `RecordPaymentCommand` (Idempotency-Key) | Cmd | `PAYMENT_EXCEEDS_DEBT`, `NO_OUTSTANDING_INVOICE`, `INVOICE_NOT_FINALIZED`, `CONTRACT_NOT_BILLABLE`, `INVALID_PAID_AT` |
| `ReversePaymentCommand` | Cmd | `PAYMENT_ALREADY_REVERSED`, `CONTRACT_NOT_BILLABLE` |
| `WriteOffInvoiceCommand` / `PayAndWriteOffCommand` (Idempotency-Key) | Cmd | `WRITE_OFF_NOT_ALLOWED`, `PAYMENT_EXCEEDS_DEBT`, `NOTHING_TO_WRITE_OFF`, `INVOICE_NOT_FINALIZED` |
| `ListPaymentsQuery` | Qry | |
| `RefundDepositCommand` / `CancelDepositRefundCommand` | Cmd | `NO_DEPOSIT`, `DEPOSIT_ALREADY_REFUNDED`, `DEPOSIT_NOT_REFUNDED`, `INVALID_REFUND_AMOUNT` |
| Nợ các kỳ trước | Qry | trong `GetInvoiceQuery` (M07) |

## 7. API

| Method | Route | Ghi chú |
|--------|-------|---------|
| POST | `/contracts/{contractId}/payments` | Idempotency-Key; 201 phiếu thu |
| GET | `/payments?contractId=&roomId=&invoiceId=` | |
| POST | `/payments/{id}/reverse` `{ reason }` | 409 `PAYMENT_ALREADY_REVERSED` |
| POST | `/invoices/{invoiceId}/write-off` `{ amount?, reason }` | Idempotency-Key; 403 `WRITE_OFF_NOT_ALLOWED` |
| POST | `/invoices/{invoiceId}/pay-and-write-off` `{ amount, method, paidAt, payerName?, reference?, reason }` | Idempotency-Key; 403, 422 |
| POST | `/contracts/{id}/deposit/refund` `{ refundedOn, amount?, note? }` | "Đã hoàn trả cọc" |
| DELETE | `/contracts/{id}/deposit/refund` | Bỏ đánh dấu (nhập nhầm) |
| GET | `/contracts?depositNotRefunded=true` | HĐ đã kết thúc có cọc chưa hoàn |

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| amount (phiếu thu) | số nguyên 1 … 10.000.000.000 |
| method | `Cash` / `BankTransfer` / `EWallet` |
| paidAt | PM-BR-14 |
| reference / payerName | ≤ 100 / ≤ 200 |
| reason (đảo, bỏ nợ) | 1–500, bắt buộc |
| deposit refund amount | 0 … `deposit_amount`; < cọc ⇒ `note` bắt buộc (≤ 500) |

## 9. Phân quyền
Chủ trọ và phó quản lý làm mọi việc thu tiền; **bỏ nợ** chỉ chủ trọ hoặc phó quản lý được cấp quyền riêng (M01 ID-BR-23).

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Rủi ro | Giải pháp |
|----------|--------|-----------|
| 2 phiếu thu song song vào 1 phiếu | Thu vượt tổng | Khóa `contracts` → `invoices` `FOR UPDATE` + CHECK `paid_amount ≤ total_amount` |
| Retry mạng | Thu trùng | Idempotency-Key bắt buộc |
| `paid_amount` lệch Σ phân bổ | Sai trạng thái | Chỉ cập nhật trong cùng transaction với phân bổ |
| Hủy phiếu đã thu | Tiền mồ côi | Hủy phiếu yêu cầu `paid_amount = 0` (M07 BL-BR-15) — đảo phiếu thu trước |
| Hoàn tất thanh lý khi đang ghi thu | Đóng HĐ còn nợ | `CompleteLiquidation` khóa HĐ cùng thứ tự |

## 11. Audit & bảo mật
Audit mọi phiếu thu, đảo, bỏ nợ, đánh dấu hoàn cọc. Không có endpoint DELETE phiếu thu.

## 12. Kế hoạch test
- Thu 1 phiếu / thu theo HĐ (cũ nhất trước) / thu vượt → `PAYMENT_EXCEEDS_DEBT`; đảo phiếu thu → phiếu về còn nợ; đảo 2 lần → 409.
- Bỏ nợ: trả 2tr + bỏ 1tr trên phiếu 3tr → phiếu `WrittenOff`, tiền thu thật 2tr; bằng số còn nợ → `NOTHING_TO_WRITE_OFF`; phó quản lý chưa có quyền → 403,
  được cấp quyền → làm được; thiếu lý do → 400.
- Nợ các kỳ trước: phiếu tháng 12 hiện nợ tháng 11 + tổng cần thanh toán; thu bằng tổng → trả tháng 11 trước.
- Cọc: HĐ có cọc mặc định "Đang giữ"; "Đã hoàn trả cọc" đủ / một phần (thiếu ghi chú → 400) / trả hơn cọc → 400; bỏ đánh dấu; HĐ không cọc → `NO_DEPOSIT`;
  ký lại → HĐ cũ "Đã chuyển"; hoàn tất thanh lý chưa hoàn cọc vẫn được, HĐ hiện trong lọc `depositNotRefunded`.

## 13. Phụ thuộc
- Dùng: M05 (HĐ, trạng thái, cọc thỏa thuận), M07 (phiếu, `paid_amount`).
- Bị dùng: M05 (hoàn tất thanh lý: thu đủ / bỏ nợ), M10 (báo cáo thu, công nợ, doanh thu).

## 14. Task

| ID | Task | Trạng thái |
|----|------|-----------|
| PM-01 | Phiếu thu, phân bổ tự động / chỉ định, đảo, "Đã thu toàn bộ" | ✅ |
| PM-02 | Bỏ nợ riêng + thanh toán kèm bỏ nợ, quyền theo từng phó quản lý | ✅ |
| PM-03 | Nợ các kỳ trước trên phiếu | ✅ |
| PM-04 | Cọc tối giản: trạng thái cọc trên HĐ, "Đã hoàn trả cọc" / bỏ đánh dấu, ký lại ⇒ đã chuyển, lọc chưa hoàn cọc + test + seeder (thay sổ cọc G1 cũ) | ✅ |
