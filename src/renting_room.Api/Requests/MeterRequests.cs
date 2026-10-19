using renting_room.Application.Meters;

namespace renting_room.Requests;

public sealed record InstallMeterRequest(Guid FeeTypeId, string? SerialNo, DateOnly InstalledDate, decimal InitialValue, string? Note);

public sealed record ReplaceMeterRequest(DateOnly Date, decimal OldFinalValue, string? NewSerialNo, decimal NewInitialValue, string? Note);

public sealed record RemoveMeterRequest(DateOnly Date, decimal FinalValue, string? Note);

public sealed record CorrectReadingRequest(decimal Value, string? Note);

public sealed record BulkReplaceMetersRequest(Guid FeeTypeId, DateOnly ReplacedOn, IReadOnlyList<BulkReplaceRow> Rows, string? Note);

/// <summary>Công tơ của phòng & chỉ số (M06 đợt 1): lắp, thay (phiên bản), tháo, lịch sử, sửa chỉ số.</summary>
public sealed record VoidReadingRequest(string Reason);

/// <summary>MT-UC-09: điều chỉnh chỉ số đã chốt — `reason` bắt buộc.</summary>
public sealed record AdjustReadingRequest(decimal Value, string Reason);

public sealed record BulkInstallMetersRequest(Guid FeeTypeId, DateOnly InstalledDate, IReadOnlyList<BulkInstallRow> Rows);
