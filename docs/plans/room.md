# Room Module

## Mission

Manage the physical inventory of rentable rooms: their identity, pricing, and
availability status. This is the foundation module other modules (Lease) reference.

## Status

Domain and basic CRUD scaffolded already:
- `renting_room.Domain/Entities/Room.cs`
- `renting_room.Domain/Enums/RoomStatus.cs`
- `renting_room.Application/Rooms/**`
- `renting_room.Infrastructure/Persistence/Configurations/RoomConfiguration.cs`
- `renting_room/Endpoints/RoomEndpoints.cs`

Remaining work is listed under **Not yet implemented** below.

## Domain

### `Room` entity

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | |
| `Name` | `string` | Required, max 200 chars |
| `MonthlyRent` | `decimal` | Must be > 0 |
| `Status` | `RoomStatus` | `Available`, `Occupied`, `UnderMaintenance` |
| `CreatedAt` | `DateTimeOffset` | |

### Invariants

- A room can only be marked `Occupied` when it is currently `Available` (enforced by `MarkOccupied()`).
- A room cannot be deleted while it has an active lease (once Lease module exists — see Open Questions).

### Not yet implemented

- `UnderMaintenance` transition methods (`MarkUnderMaintenance()`, `MarkAvailableFromMaintenance()`).
- Room attributes beyond name/rent: size (m²), floor, amenities list — only add if the product actually needs them (YAGNI).

## Use cases (Application layer)

| Use case | Type | Status |
|----------|------|--------|
| `CreateRoomCommand` | Command | Implemented |
| `GetRoomQuery` | Query | Implemented |
| `ListRoomsQuery` | Query | Implemented |
| `UpdateRoomCommand` | Command | Not implemented |
| `MarkRoomOccupiedCommand` | Command | Not implemented — needed once Lease creates/ends |
| `MarkRoomAvailableCommand` | Command | Not implemented |
| `DeleteRoomCommand` | Command | Not implemented — must check no active lease first |

## API (Api layer)

| Method | Route | Status | Notes |
|--------|-------|--------|-------|
| `GET` | `/api/rooms` | Implemented | List all rooms |
| `GET` | `/api/rooms/{id}` | Implemented | 404 if not found |
| `POST` | `/api/rooms` | Implemented | 201 + Location header |
| `PUT` | `/api/rooms/{id}` | Not implemented | Update name/rent |
| `DELETE` | `/api/rooms/{id}` | Not implemented | 409 if room has active lease |
| `POST` | `/api/rooms/{id}/status` | Not implemented | Explicit status transition, not a generic PATCH |

## Dependencies

None — this is a foundation module.

## Open questions

- Should room status be derived from active leases (computed) instead of a stored field,
  to avoid the two ever going out of sync? Revisit once Lease module exists.
- Do we need room "types" (single/double/studio) or is a flat list sufficient for now?
