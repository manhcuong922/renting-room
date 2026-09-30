# Module Plans

Each file in this folder is the implementation plan for one bounded module of the
`renting_room` system, following the Clean Architecture layout in
`renting_room.Domain` / `renting_room.Application` / `renting_room.Infrastructure` / `renting_room`.

## Convention

Each plan follows the same structure:

- **Mission** — what the module is responsible for, in one or two sentences.
- **Domain** — entities, value objects, enums, and the invariants they enforce.
- **Use cases** — commands and queries the Application layer exposes (Mediator).
- **API** — endpoints exposed by the Api layer, with HTTP method, route, and status codes.
- **Dependencies** — other modules this one relies on (e.g., Lease depends on Room and Tenant).
- **Open questions** — decisions not yet made, to resolve before/during implementation.

## Status

| Module | File | Status |
|--------|------|--------|
| Room | [room.md](room.md) | Domain scaffolded (`Room` entity + CRUD use cases exist) |
| Tenant | [tenant.md](tenant.md) | Planned, not implemented |
| Lease / Contract | [lease.md](lease.md) | Planned, not implemented |
| Payment | [payment.md](payment.md) | Planned, not implemented |

## Build order

Suggested implementation order, based on dependencies between modules:

1. **Room** — foundation, already scaffolded.
2. **Tenant** — independent of Room, can be built in parallel.
3. **Lease** — depends on Room + Tenant (links a tenant to a room for a period).
4. **Payment** — depends on Lease (payments are made against a lease).
