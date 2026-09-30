# Payment Module

## Mission

Track rent payments (and deposits) made against a lease, so the system can answer
"what's owed" and "what's been paid" per lease.

## Status

Not implemented. Depends on Lease existing first.

## Domain

### `Payment` entity

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | |
| `LeaseId` | `Guid` | FK to `Lease` |
| `Amount` | `decimal` | Must be > 0 |
| `Type` | `PaymentType` | `Rent`, `Deposit` |
| `PeriodStart` | `DateOnly?` | Which rent period this payment covers (null for `Deposit`) |
| `PaidAt` | `DateTimeOffset` | When the payment was actually made |
| `Method` | `PaymentMethod` | `Cash`, `BankTransfer` — extend as needed |
| `CreatedAt` | `DateTimeOffset` | |

### `PaymentType` / `PaymentMethod` enums

`PaymentType`: `Rent`, `Deposit`.
`PaymentMethod`: `Cash`, `BankTransfer` (extend only when a real payment provider is integrated).

### Invariants

- A payment must reference an existing, non-`Terminated` lease at the time of recording
  (recording a payment against a terminated lease should be rejected or require an
  explicit override — decide during implementation).
- `Amount` must be positive; refunds/corrections should be modeled as a separate
  negative-amount record type or an explicit `PaymentReversal`, not by mutating an
  existing payment (immutability — payments are a financial audit trail).

## Use cases (Application layer)

| Use case | Type | Notes |
|----------|------|-------|
| `RecordPaymentCommand` | Command | |
| `GetPaymentQuery` | Query | |
| `ListPaymentsByLeaseQuery` | Query | Primary way payments are browsed — always scoped to a lease |
| `GetLeaseBalanceQuery` | Query | Computed: sum of rent owed (by period) minus sum of rent paid |

## API (Api layer)

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/leases/{leaseId}/payments` | List payments for a lease |
| `POST` | `/api/leases/{leaseId}/payments` | 201 + Location; record a payment |
| `GET` | `/api/leases/{leaseId}/balance` | Computed balance, not a stored entity |

## Dependencies

- **Lease** — every payment is recorded against a lease.

## Open questions

- Is rent calculated on a fixed monthly schedule, or does the product need custom
  billing periods (weekly, prorated first month)? This significantly affects how
  `GetLeaseBalanceQuery` computes what's owed — do not build the balance calculation
  until this is confirmed with the user.
- Do we need to send payment reminders/notifications? That would be a separate
  Notification module, not part of Payment's core responsibility.
