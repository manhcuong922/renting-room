# Tenant Module

## Mission

Manage tenant identity and contact information for people renting rooms. Independent
of Room — a tenant can exist before being linked to any room via a Lease.

## Status

Not implemented. No `Tenant` entity, use cases, or endpoints exist yet.

## Domain

### `Tenant` entity

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | |
| `FullName` | `string` | Required, max 200 chars |
| `Email` | `string` | Required, must be a valid email format |
| `PhoneNumber` | `string` | Required |
| `IdentityNumber` | `string` | National ID / passport number — required for lease contracts |
| `CreatedAt` | `DateTimeOffset` | |

### Invariants

- `Email` must be unique across tenants (enforced at persistence via a unique index,
  not just application-level validation — see `dotnet-ef-core` skill for unique constraint patterns).
- A tenant cannot be deleted while they have an active lease (once Lease module exists).

## Use cases (Application layer)

| Use case | Type | Notes |
|----------|------|-------|
| `CreateTenantCommand` | Command | |
| `GetTenantQuery` | Query | |
| `ListTenantsQuery` | Query | Consider pagination once tenant count grows — see `optimizing-ef-core-queries` skill |
| `UpdateTenantCommand` | Command | Update contact info only, not identity number (treat as immutable once set) |
| `DeleteTenantCommand` | Command | Must check no active lease first |

## API (Api layer)

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/tenants` | List all tenants |
| `GET` | `/api/tenants/{id}` | 404 if not found |
| `POST` | `/api/tenants` | 201 + Location header; 409 if email already exists |
| `PUT` | `/api/tenants/{id}` | Update contact info |
| `DELETE` | `/api/tenants/{id}` | 409 if tenant has active lease |

## Dependencies

None — independent of Room, can be built in parallel.

## Open questions

- Do we need tenant document uploads (ID scan, contract signature) in scope, or is that
  a separate File/Document module? Defer until product requirement is confirmed.
- Should email uniqueness be case-insensitive? (Recommended: yes, normalize to lowercase
  before storing/comparing.)
tesst
