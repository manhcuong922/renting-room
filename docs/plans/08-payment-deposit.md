# M08 — Payment & Deposit (Thu tiền, Phân bổ, Sổ cọc, Công nợ)

> Theo khuôn [_TEMPLATE.md](_TEMPLATE.md). Pháp lý: L1 (đặt cọc — Điều 328 BLDS), LEG-02.

## 1. Mục tiêu & phạm vi

**Mục tiêu**: Ghi nhận **tiền thực thu** và phân bổ vào phiếu báo tiền (M07); quản lý **tiền cọc** bằng sổ cọc riêng;
cung cấp **công nợ** chính xác theo HĐ / phòng / khu. Mọi số dư là **dẫn xuất** từ bút toán bất biến.

**Trong phạm vi**: phiếu thu (một/nhiều phiếu báo), phân bổ tự động/thủ công, đảo phiếu thu, số dư có (trả thừa), hoàn tiền thừa;
sổ cọc: nhận, nhận thêm, cấn trừ vào phiếu, hoàn, mất cọc, chuyển cọc sang HĐ mới; công nợ.

**Ngoài phạm vi**: cổng thanh toán, đối soát ngân hàng tự động (P2: VietQR có nội dung chuyển khoản = số phiếu; P3: webhook ngân hàng).

**Phase**: P1.

## 2. Thuật ngữ

| Thuật ngữ | Code | Định nghĩa |
|-----------|------|-----------|
| Phiếu thu | `Payment` | Một lần nhận tiền từ người thuê cho 1 HĐ |
| Phân bổ | `PaymentAllocation` | Phần tiền của phiếu thu gán vào 1 phiếu báo |
| Số dư có | `CreditBalance` | Tiền đã thu chưa phân bổ (trả thừa/trả trước) của HĐ |
| Đảo phiếu thu | `Reverse` | Hủy hiệu lực phiếu thu nhập sai (không xóa) |
| Sổ cọc | `DepositTransaction` | Bút toán cọc: `Receive`, `TopUp`, `DeductToInvoice`, `Refund`, `Forfeit`, `TransferOut`, `TransferIn` |
| Số dư cọc | `DepositBalance` | Σ vào − Σ ra của bút toán chưa đảo |

## 3. Nghiệp vụ

### 3.1 Use case

| ID | Mô tả |
|----|-------|
| PM-UC-01 | Ghi phiếu thu cho HĐ: số tiền, phương thức, ngày thu, người thu, mã giao dịch; phân bổ **tự động** (phiếu báo cũ nhất trước) hoặc **chỉ định** |
| PM-UC-02 | Phân bổ số dư có vào phiếu báo mới |
| PM-UC-03 | Hủy một phân bổ (để phân bổ lại) |
| PM-UC-04 | Đảo phiếu thu (lý do) |
| PM-UC-05 | Hoàn tiền thừa (từ số dư có) |
| PM-UC-06 | Nhận cọc / nhận thêm cọc |
| PM-UC-07 | Cấn trừ cọc vào phiếu báo (thường khi thanh lý) |
| PM-UC-08 | Hoàn cọc / ghi nhận mất cọc (lý do theo điều khoản HĐ) |
| PM-UC-09 | Chuyển cọc sang HĐ mới (chuyển phòng) |
| PM-UC-10 | Xem công nợ: theo HĐ, theo khu, danh sách quá hạn |
| PM-UC-11 | Xem sổ cọc của HĐ; tổng cọc đang giữ theo khu |

### 3.2 Quy tắc nghiệp vụ

| Mã | Quy tắc | Nơi kiểm tra |
|----|---------|-------------|
| PM-BR-01 | `payment.amount` > 0, số nguyên đồng. Không sửa số tiền phiếu thu; sai → đảo + ghi lại | Domain + CHECK |
| PM-BR-02 | Phân bổ chỉ vào phiếu báo **Finalized** của **cùng HĐ**; mỗi phân bổ > 0 | Domain + FK composite `(organization_id, contract_id, invoice_id)` |
| PM-BR-03 | Σ phân bổ hiệu lực của 1 phiếu thu ≤ số tiền phiếu thu | Domain (aggregate Payment) |
| PM-BR-04 | Σ phân bổ hiệu lực vào 1 phiếu báo ≤ `total_amount` (= `paid_amount` cache ≤ total, CHECK ở M07) | Domain + CHECK |
| PM-BR-05 | `invoices.paid_amount` được cập nhật **trong cùng transaction** với tạo/hủy phân bổ, sau khi khóa hàng invoice `FOR UPDATE` | Application |
| PM-BR-06 | Phân bổ tự động: phiếu báo chưa Paid của HĐ theo `due_date` tăng dần, rồi `period_start`; phần dư → số dư có | Domain service |
| PM-BR-07 | Đảo phiếu thu: đảo mọi phân bổ (giảm `paid_amount`), trạng thái `Reversed`, lý do bắt buộc. Không đảo phiếu thu loại `DepositDeduction` trực tiếp — phải đảo bút toán cọc tương ứng (đảo cả 2 cùng lúc) | Domain |
| PM-BR-08 | `CreditBalance(HĐ)` = Σ(amount − Σ phân bổ hiệu lực) của phiếu thu `Recorded` − Σ hoàn tiền thừa hiệu lực ≥ 0. Hoàn tiền thừa > số dư có → 422 | Application (khóa contract) |
| PM-BR-09 | `DepositBalance(HĐ)` = Σ(Receive, TopUp, TransferIn) − Σ(DeductToInvoice, Refund, Forfeit, TransferOut) trên bút toán chưa đảo, **luôn ≥ 0** — kiểm tra khi ghi bút toán ra và khi đảo bút toán vào | Application (khóa contract `FOR UPDATE`) |
| PM-BR-10 | `DeductToInvoice` tạo đồng thời 1 phiếu thu `method = DepositDeduction` và phân bổ vào phiếu báo chỉ định — **một transaction** | Application |
| PM-BR-11 | `Forfeit`/`Refund` lý do bắt buộc (Forfeit: tham chiếu điều khoản). Chỉ khi HĐ `Liquidating` hoặc `Ended`… ngoại lệ: HĐ `Draft` bị hủy mà đã nhận cọc (người thuê không đến — Điều 328: mất cọc) | Domain |
| PM-BR-12 | `TransferOut` (HĐ cũ) và `TransferIn` (HĐ mới) cùng số tiền, tạo cặp trong 1 transaction, liên kết `related_transaction_id`; 2 HĐ cùng tổ chức, HĐ mới có `previous_contract_id` = HĐ cũ | Application |
| PM-BR-13 | Ghi phiếu thu chỉ cho HĐ `Active`/`Liquidating`. HĐ `Draft` chỉ nhận bút toán cọc (cọc giữ chỗ); `Cancelled` chỉ Refund/Forfeit cọc; `Ended` → 422 (đã quyết toán xong theo CT-BR-12) | Domain |
| PM-BR-16 | **Xóa nợ** (người thuê bỏ đi, không đòi được): phiếu thu `method = WriteOff`, lý do bắt buộc, phân bổ vào phiếu báo như tiền thật để đóng công nợ và cho phép hoàn tất thanh lý; **không** tính vào doanh thu (M10). Chỉ khi HĐ `Liquidating` | Domain |
| PM-BR-17 | **Ghi có** (`method = CreditNote`) chỉ do hệ thống sinh (BL-BR-17), không qua API; tạo số dư có để hoàn cho người thuê | Application |
| PM-BR-18 | Phương thức "phi tiền mặt" (`DepositDeduction`, `WriteOff`, `CreditNote`) không nhập trực tiếp qua `POST /payments` (trừ `WriteOff` qua endpoint riêng) | Validator |
| PM-BR-14 | `paid_at` ≤ hôm nay; ≥ `start_date` HĐ − 60 ngày (cọc giữ chỗ) | Validator |
| PM-BR-15 | Số phiếu thu `PT{yyyy}-{seq:000000}` cấp khi tạo, unique trong tổ chức | DB + sequence |

### 3.3 Vòng đời
- Payment: `Recorded` → `Reversed`. Allocation: `Active` → `Cancelled`. DepositTransaction: `Recorded` → `Reversed`.
- Không có trạng thái nào cho phép sửa số tiền.

## 4. Dữ liệu

**`payments`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id | uuid | N | UNIQUE (organization_id, contract_id, id) |
| property_id, contract_id | uuid | N | FK composite |
| receipt_no | varchar(20) | N | UNIQUE (organization_id, receipt_no) |
| amount | numeric(18,0) | N | CHECK > 0 |
| method | varchar(20) | N | Tiền thật: `Cash`,`BankTransfer`,`EWallet`,`Other`; Phi tiền mặt (hệ thống/đặc biệt): `DepositDeduction`,`WriteOff`,`CreditNote` |
| write_off_reason | varchar(500) | Y | CHECK bắt buộc khi `WriteOff` |
| paid_at | date | N | |
| payer_name | varchar(200) | Y | |
| reference | varchar(100) | Y | mã giao dịch ngân hàng |
| note | varchar(500) | Y | |
| status | varchar(10) | N | `Recorded`,`Reversed` |
| reversed_at/by, reverse_reason | | Y | CHECK reason khi Reversed |
| created_at/by, xmin | | | |

**`payment_allocations`**: id, organization_id, contract_id, payment_id, invoice_id, amount numeric(18,0) CHECK > 0,
status (`Active`,`Cancelled`), created_at/by, cancelled_at/by.
- FK composite `(organization_id, contract_id, payment_id)` → payments; `(organization_id, contract_id, invoice_id)` → invoices (M07 cần UNIQUE `(organization_id, contract_id, id)`) ⇒ **DB chặn phân bổ chéo HĐ**.
- UNIQUE `(payment_id, invoice_id) WHERE status = 'Active'`.

**`credit_refunds`**: id, organization_id, contract_id, amount > 0, refunded_at date, method, note, status (`Recorded`,`Reversed`), audit.

**`deposit_transactions`**

| Cột | Kiểu | Null | Ghi chú |
|-----|------|------|--------|
| id, organization_id, contract_id | uuid | N | |
| type | varchar(16) | N | xem thuật ngữ |
| amount | numeric(18,0) | N | CHECK > 0 (chiều do `type` quyết định) |
| transaction_date | date | N | |
| method | varchar(20) | Y | với Receive/TopUp/Refund |
| invoice_id | uuid | Y | bắt buộc với DeductToInvoice |
| payment_id | uuid | Y | phiếu thu sinh ra (DeductToInvoice) |
| related_transaction_id | uuid | Y | cặp Transfer |
| reason | varchar(500) | Y | bắt buộc với Refund/Forfeit |
| status | varchar(10) | N | `Recorded`,`Reversed` |
| reversed_at/by, reverse_reason | | Y | |
| created_at/by, xmin | | | |

CHECK `type <> 'DeductToInvoice' OR (invoice_id IS NOT NULL AND payment_id IS NOT NULL)`; CHECK `type NOT IN ('Refund','Forfeit') OR reason IS NOT NULL`.

**`receipt_number_sequences`**: (organization_id, year) PK, last_value.

### Dẫn xuất (view / query)
- `v_contract_balances`: contract_id, outstanding (Σ total − paid của phiếu Finalized), credit_balance, deposit_balance.
- Không lưu các số dư này trong bảng `contracts` (tránh lệch). Nếu cần hiệu năng (P2) → materialized view refresh theo lịch, chỉ cho dashboard.

## 5. Domain model

```
Payment (aggregate root; Allocations)
  + Record(contract, amount, method, paidAt, ...)
  + Allocate(invoiceId, amount, invoiceRemaining)    // PM-BR-03/04
  + CancelAllocation(allocationId)
  + Reverse(reason, now)                             // trả danh sách (invoiceId, amount) để giảm paid_amount
  + Unallocated
AllocationPolicy (domain service): AutoAllocate(payment, openInvoices[]) — FIFO PM-BR-06
DepositLedger (domain service thuần): Balance(transactions), CanApply(newTx) : Result   // PM-BR-09
DepositTransaction (entity): Create(type, ...), Reverse(reason)
```

## 6. Application

| Use case | Loại | Lỗi nghiệp vụ |
|----------|------|---------------|
| `RecordPaymentCommand` (Idempotency-Key) | Cmd | input: contractId, amount, method, paidAt, allocations? (null = auto) → `{paymentId, receiptNo, allocations[], unallocated}`; lỗi `ALLOCATION_EXCEEDS_INVOICE`, `ALLOCATION_EXCEEDS_PAYMENT`, `INVOICE_NOT_FINALIZED`, `CONTRACT_NOT_BILLABLE` |
| `AllocateCreditCommand` | Cmd | `INSUFFICIENT_CREDIT` |
| `CancelAllocationCommand` | Cmd | |
| `ReversePaymentCommand` | Cmd | `PAYMENT_ALREADY_REVERSED`, `USE_DEPOSIT_REVERSAL` |
| `RefundCreditCommand` (Idempotency-Key) | Cmd | `INSUFFICIENT_CREDIT` |
| `RecordDepositCommand` (Receive/TopUp; Idempotency-Key) | Cmd | |
| `DeductDepositToInvoiceCommand` | Cmd | `INSUFFICIENT_DEPOSIT`, `ALLOCATION_EXCEEDS_INVOICE` |
| `RefundDepositCommand` / `ForfeitDepositCommand` | Cmd | `INSUFFICIENT_DEPOSIT`, `CONTRACT_STATE_INVALID` |
| `TransferDepositCommand` | Cmd | `INSUFFICIENT_DEPOSIT`, `CONTRACTS_NOT_LINKED` |
| `ReverseDepositTransactionCommand` | Cmd | `DEPOSIT_BALANCE_NEGATIVE` |
| `GetContractBalanceQuery` / `ListDebtsQuery` / `ListPaymentsQuery` / `GetDepositLedgerQuery` | Qry | |

## 7. API

| Method | Route | Mã lỗi |
|--------|-------|--------|
| POST | `/contracts/{contractId}/payments` | 422 |
| GET | `/payments?propertyIds=&from=&to=&method=&contractId=` | |
| GET | `/payments/{id}` | |
| POST | `/payments/{id}/reverse` | 409 `PAYMENT_ALREADY_REVERSED` |
| POST | `/payments/{id}/allocations`; POST `/payment-allocations/{id}/cancel` | 422 |
| POST | `/contracts/{contractId}/credit/allocate` · `/credit/refund` | 422 `INSUFFICIENT_CREDIT` |
| POST | `/invoices/{invoiceId}/write-off` `{ amount, reason }` | 422 `CONTRACT_STATE_INVALID` |
| GET | `/contracts/{contractId}/balance` | |
| GET | `/debts?propertyIds=&overdueOnly=true` | |
| GET / POST | `/contracts/{contractId}/deposit-transactions` (POST với `type` = Receive/TopUp/Refund/Forfeit) | 422 `INSUFFICIENT_DEPOSIT` |
| POST | `/contracts/{contractId}/deposit/deduct` `{ invoiceId, amount }` | |
| POST | `/contracts/{contractId}/deposit/transfer` `{ toContractId, amount }` | |
| POST | `/deposit-transactions/{id}/reverse` | 422 `DEPOSIT_BALANCE_NEGATIVE` |

```json
POST /api/v1/contracts/{cid}/payments
Idempotency-Key: 1d2e…
{ "amount": 4000000, "method": "BankTransfer", "paidAt": "2026-11-07", "reference": "FT2631100123", "allocations": null }
→ 201 {
  "paymentId": "…", "receiptNo": "PT2026-000045",
  "allocations": [ { "invoiceId": "…", "invoiceNo": "PB2026-000101", "amount": 3284400 } ],
  "unallocated": 715600
}
```

## 8. Validation

| Field | Quy tắc |
|-------|---------|
| amount | số nguyên 1 … 10.000.000.000 |
| method | enum; `DepositDeduction` **không** cho phép qua API phiếu thu (chỉ sinh nội bộ) |
| paidAt / transactionDate | PM-BR-14 |
| allocations[] | invoiceId không trùng; amount > 0; Σ ≤ amount |
| reference | ≤ 100 |
| reason | 1–500 khi bắt buộc |

## 9. Phân quyền
OrgOwner toàn quyền. OrgManager (P3): ghi thu; đảo phiếu thu / hoàn / mất cọc cần quyền riêng.

## 10. Toàn vẹn dữ liệu & concurrency

| Kịch bản | Rủi ro | Giải pháp |
|----------|--------|-----------|
| 2 phiếu thu song song cùng phân bổ vào 1 phiếu báo | Trả vượt tổng | Khóa `contracts` row (tuần tự hóa mọi bút toán tiền của 1 HĐ) + khóa `invoices` `FOR UPDATE` + CHECK `paid_amount ≤ total_amount` |
| Retry mạng tạo phiếu thu 2 lần | Thu trùng | Idempotency-Key bắt buộc |
| Hoàn cọc 2 lần song song | Số dư cọc âm | Khóa contract + kiểm số dư trong transaction (PM-BR-09) |
| `paid_amount` cache lệch với Σ phân bổ | Sai trạng thái thanh toán | Chỉ cập nhật trong cùng transaction; job kiểm tra đối soát hằng đêm (P2) cảnh báo nếu lệch; integration test bất biến `paid_amount = Σ allocations Active` |
| Phân bổ vào phiếu báo đã Void | Tiền vào phiếu hủy | Void yêu cầu paid = 0 (M07) và phân bổ yêu cầu Finalized ⇒ khóa invoice → tuần tự |
| Thanh lý hoàn tất trong khi đang ghi thu | Đóng HĐ còn tiền treo | M05 `CompleteLiquidation` khóa contract cùng thứ tự |
| Phân bổ chéo HĐ | Sai công nợ | FK composite có `contract_id` |

## 11. Audit & bảo mật
Audit mọi bút toán & đảo. Không có endpoint DELETE. Báo cáo thu theo người thu (P3).

## 12. Kế hoạch test
- Unit: AutoAllocate FIFO (đủ, thiếu, thừa); DepositLedger các tổ hợp; Reverse trả đúng delta.
- Integration: thu thừa → credit; phân bổ credit vào phiếu mới; đảo phiếu thu → paid_amount giảm, trạng thái về Unpaid;
  20 request phân bổ song song vào 1 phiếu báo → không vượt tổng; hoàn cọc vượt số dư → 422; cấn trừ cọc tạo đúng cặp payment + allocation;
  chuyển cọc giữa 2 HĐ; bất biến `paid_amount` sau mỗi test; **C-01** (phân bổ phiếu báo của org khác bị chặn ở FK).

## 13. Phụ thuộc
- Dùng: M05 (HĐ, trạng thái), M07 (phiếu báo, `paid_amount`).
- Bị dùng: M05 (điều kiện thanh lý: số dư cọc, credit, nợ), M10 (báo cáo thu, công nợ, doanh thu năm).

## 14. Task breakdown

| ID | Task | Ước lượng |
|----|------|-----------|
| PM-01 | Domain Payment/Allocation/DepositLedger + unit test | 1.5d |
| PM-02 | EF + FK composite + sequences + view balances | 1d |
| PM-03 | Record/allocate/cancel/reverse payment | 1.5d |
| PM-04 | Credit allocate/refund | 0.5d |
| PM-05 | Deposit commands (receive, deduct, refund, forfeit, transfer, reverse) | 1.5d |
| PM-06 | Debt & balance queries | 1d |
| PM-07 | Tests (concurrency, bất biến) | 1.5d |

## 15. Câu hỏi mở

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q1 | Cọc giữ chỗ trước khi ký HĐ? | Ghi Receive trên HĐ `Draft` (cho phép); nếu khách không đến → Cancel HĐ + Forfeit (PM-BR-11) |
| Q2 | Thu gộp nhiều phòng của 1 người trong 1 phiếu thu? | P1 không; tạo phiếu thu riêng từng HĐ |
| Q3 | Tiền cọc có cần sinh lãi/ghi nhận doanh thu? | Không; Forfeit được tính vào báo cáo "thu khác" |
