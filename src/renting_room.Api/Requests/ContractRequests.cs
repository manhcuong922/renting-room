using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Domain.Contracts;

namespace renting_room.Requests;

public sealed record CreateContractRequest(Guid RoomId, string? ContractNo, ContractInput Contract);

public sealed record UpdateContractRequest(ContractInput Contract, uint Version);

public sealed record CancelContractRequest(string Reason);

public sealed record UpdateContractNoteRequest(string? Note);

public sealed record SignedDocumentRequest(bool HasSignedDocument, string? Note);

public sealed record ChangeContractFeeRequest(decimal? Quantity, decimal? UnitPriceOverride, DateOnly? EffectiveFrom);

public sealed record AddOccupantRequest(
    Guid RenterId, DateOnly MoveInDate, DateOnly? ExpectedEndDate, string? Relationship, string? Note,
    OccupantRelationship? RelationshipType = null, bool? GuardianConsent = null, OccupantRole? Role = null);

public sealed record ChangeOccupantRoleRequest(OccupantRole Role);

public sealed record ChangeRepresentativeRequest(Guid RenterId, DateOnly Date, Guid? HouseholdHeadRenterId = null, bool? OldRepresentativeMovesOut = null);

public sealed record MoveOccupantRequest(
    Guid ToContractId, DateOnly Date, OccupantRole? Role = null, OccupantRelationship? RelationshipType = null, string? Relationship = null,
    bool? GuardianConsent = null, string? Note = null);

public sealed record EndOccupancyRequest(DateOnly MoveOutDate);

/// <summary>Số người ở không bị giới hạn (PR-BR-06) — client cũ gửi <c>overrideCapacity</c> thì bị bỏ qua.</summary>
public sealed record ActivateContractRequest(IReadOnlyList<MeterReadingInput>? HandoverReadings);

public sealed record CompleteLiquidationRequest(DebtSettlement? Settlement, PaymentMethod? Method, DateOnly? PaidAt, string? Reason);

public sealed record FinalInvoiceRequest(IReadOnlyList<MeterReadingInput>? FinalReadings);

public sealed record ChangeRentRequest(DateOnly? EffectiveFrom, decimal MonthlyRent, string? AddendumNo, string? Note);

public sealed record ExtendContractRequest(DateOnly NewEndDate);

public sealed record HoldoverRequest(string? Note);

public sealed record RefundDepositRequest(DateOnly RefundedOn, decimal? Amount, string? Note);

/// <summary>Ngày chuyển luôn là hôm nay (CT-BR-14) — body không có ngày.</summary>
public sealed record TransferRoomRequest(
    Guid ToRoomId, IReadOnlyList<MeterReadingInput>? OldRoomReadings, IReadOnlyList<MeterReadingInput>? NewRoomReadings,
    decimal? MonthlyRent, string? Note, TransferRentFrom? RentFrom = null, IReadOnlyList<Guid>? RemoveFeeTypeIds = null);

public sealed record ResignContractRequest(DateOnly HandoverDate, Guid RepresentativeRenterId, DateOnly? EndDate, bool? TransferDeposit);

public sealed record GiveNoticeRequest(DateOnly NoticeDate, DateOnly PlannedMoveOutDate);

public sealed record StartLiquidationRequest(DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note);

public sealed record AssetReturnRequest(string? ConditionAtReturn);

public sealed record RegisterVehicleRequest(Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly? RegisteredFrom, string? Note);

public sealed record EndVehicleRequest(DateOnly EndDate);
