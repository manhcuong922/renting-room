# Lease / Contract Module

## Mission

Represent the agreement linking one tenant to one room for a period of time, at an
agreed rent. This is the aggregate that drives room occupancy status and payment schedules.

## Status

Not implemented. Depends on Room (done) and Tenant (planned) existing first.

## Domain

### `Lease` entity (aggregate root)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | |
| `RoomId` | `Guid` | FK to `Room` |
| `TenantId` | `Guid` | FK to `Tenant` |
| `StartDate` | `DateOnly` | |
| `EndDate` | `DateOnly?` | Null = open-ended lease |
| `MonthlyRent` | `decimal` | Snapshot of rent at lease signing — must NOT read live from `Room.MonthlyRent`, since historical leases should keep their agreed price even if room pricing changes later |
| `Status` | `LeaseStatus` | `Active`, `Ended`, `Terminated` |
| `CreatedAt` | `DateTimeOffset` | |

### `LeaseStatus` enum

`Active`, `Ended` (completed naturally), `Terminated` (ended early).

### Invariants

- A room can have at most one `Active` lease at a time.
- `EndDate`, if set, must be after `StartDate`.
- Creating a lease must transition the linked room to `Occupied` (cross-aggregate —
  see Open Questions for how this is coordinated without breaking aggregate boundaries).
- Ending/terminating a lease must transition the room back to `Available`.

## Use cases (Application layer)

| Use case | Type | Notes |
|----------|------|-------|
| `CreateLeaseCommand` | Command | Validates room is `Available` and tenant exists; marks room `Occupied` |
| `GetLeaseQuery` | Query | |
| `ListLeasesQuery` | Query | Filterable by room, tenant, or status |
| `EndLeaseCommand` | Command | Natural end of term; marks room `Available` |
| `TerminateLeaseCommand` | Command | Early termination; marks room `Available` |

## API (Api layer)

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/leases` | List, filterable via query params |
| `GET` | `/api/leases/{id}` | 404 if not found |
| `POST` | `/api/leases` | 201 + Location; 409 if room already has an active lease |
| `POST` | `/api/leases/{id}/end` | Explicit action endpoint, not PUT |
| `POST` | `/api/leases/{id}/terminate` | Explicit action endpoint, not PUT |

## Dependencies

- **Room** — a lease references an existing room and changes its status.
- **Tenant** — a lease references an existing tenant.

## Open questions

- Cross-aggregate consistency: should `CreateLeaseHandler` call into Room's use case
  (`MarkRoomOccupiedCommand`) via Mediator, or should this be a single transaction inside
  one handler using `IAppDbContext` directly? Recommendation: keep it in one handler/one
  `SaveChangesAsync` call for transactional safety, since EF Core's `DbContext` already
  gives us a unit of work — avoid orchestrating multiple Mediator commands for something
  that must be atomic.
- Do we need a `Deposit` field, or is that a separate Payment record? Recommendation:
  model deposit as a special `Payment` (see payment.md) rather than a Lease field, to
  keep payment history in one place.
