using renting_room.Application.Meters;
using renting_room.Domain.Billing;

namespace renting_room.Requests;

public sealed record InstallMeterRequest(Guid FeeTypeId, string? SerialNo, DateOnly InstalledDate, decimal InitialValue, string? Note);

public sealed record ReplaceMeterRequest(DateOnly Date, decimal OldFinalValue, string? NewSerialNo, decimal NewInitialValue, string? Note);

public sealed record RemoveMeterRequest(DateOnly Date, decimal FinalValue, string? Note);

public sealed record CorrectReadingRequest(decimal Value, string? Note);

public sealed record BulkReplaceMetersRequest(Guid FeeTypeId, DateOnly ReplacedOn, IReadOnlyList<BulkReplaceRow> Rows, string? Note);

public sealed record VoidReadingRequest(string Reason);

public sealed record AdjustReadingRequest(decimal Value, string Reason, AdjustmentHandling? Handling, AdjustmentSettlement? Settlement);

public sealed record BulkInstallMetersRequest(Guid FeeTypeId, DateOnly InstalledDate, IReadOnlyList<BulkInstallRow> Rows);
