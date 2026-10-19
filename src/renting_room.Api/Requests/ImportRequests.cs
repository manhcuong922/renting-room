using renting_room.Application.Imports;

namespace renting_room.Requests;

public sealed record SaveRoomImportRequest(IReadOnlyList<RoomImportRow> Rows);

public sealed record SaveTenancyImportRequest(IReadOnlyList<TenancyImportRow> Rows);
