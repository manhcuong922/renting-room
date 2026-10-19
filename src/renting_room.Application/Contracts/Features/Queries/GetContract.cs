using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record GetContractQuery(Guid Id) : IRequest<Result<ContractDetailDto>>;

internal static class ContractMeterWarnings
{
    /// <summary>MT-BR-11: HĐ đang hiệu lực mà phòng chưa có công tơ điện (khu có khoản Điện) ⇒ không tính được tiền điện.</summary>
    public static async Task<IReadOnlyList<Warning>> ForAsync(
        IFeeQueries fees, IMeterQueries meters, Contract contract, DateOnly today, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Active)
            return [];
        var electricity = await fees.FindElectricityIdAsync(contract.PropertyId, ct);
        if (electricity is null || await meters.HasMeterOnAsync(contract.RoomId, electricity.Value, today, ct))
            return [];
        return [new Warning("ROOM_WITHOUT_METER", "Phòng chưa có công tơ điện — lắp công tơ (kèm chỉ số) để tính được tiền điện.")];
    }
}

public sealed class GetContractHandler(
    IContractQueries contracts, IRenterQueries renters, IPropertyQueries properties, IRoomQueries rooms, IFeeQueries fees, IMeterQueries meters,
    ContractPaperWarnings paperWarnings, TimeProvider clock)
    : IRequestHandler<GetContractQuery, Result<ContractDetailDto>>
{
    private Task<IReadOnlyList<Warning>> MeterWarningsAsync(Contract contract, DateOnly today, CancellationToken ct) =>
        ContractMeterWarnings.ForAsync(fees, meters, contract, today, ct);

    public async ValueTask<Result<ContractDetailDto>> Handle(GetContractQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetDetailsAsync(request.Id, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var renterIds = contract.Occupants.Select(o => o.RenterId).Append(contract.RepresentativeRenterId)
            .Concat(contract.RepresentativeHistory.Select(h => h.RenterId)).Distinct().ToList();
        var names = await renters.GetNamesAsync(renterIds, cancellationToken);
        var property = (await properties.GetAsync(contract.PropertyId, cancellationToken))!;
        var propertyCode = property.Code;
        var roomCode = (await rooms.GetCodeAsync(contract.RoomId, cancellationToken))!;
        var today = clock.GetUtcNow().ToBusinessDate();
        var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot);
        var movedFrom = contract.RoomMoves.Select(m => m.RoomId).ToList();
        var movedCodes = await rooms.GetCodesAsync(movedFrom, cancellationToken);

        return new ContractDetailDto(
            contract.Id, contract.ContractNo, contract.Status, contract.PropertyId, propertyCode, contract.RoomId, roomCode,
            contract.RepresentativeRenterId, names.GetValueOrDefault(contract.RepresentativeRenterId, string.Empty),
            contract.SignedDate, contract.SignedPlace, contract.EffectiveDate, contract.StartDate, contract.EndDate, contract.ActualEndDate,
            contract.NoticeGivenDate, contract.PlannedMoveOutDate, contract.NoticeDays, contract.DepositAmount, contract.DepositTerms,
            new BillingSettingsInput(property.BillingAnchorDay, property.ChargeMode, property.ProrationMode, property.PaymentDueDays),
            contract.PaymentMethods, contract.CopiesCount, contract.TermsText, contract.Note,
            contract.CurrentRent(today), contract.IsOverdue(today),
            snapshot?.ToLessorDto(), snapshot?.ToRoomDto(), snapshot?.ToRepresentativeDto(), contract.HouseRulesSnapshot,
            contract.TerminationReason, contract.TerminationGround, contract.TerminationNote,
            contract.ActivatedAt, contract.EndedAt, contract.CancelledAt, contract.CancelReason,
            contract.RentTerms.OrderBy(t => t.EffectiveFrom)
                .Select(t => new RentTermDto(t.Id, t.EffectiveFrom, t.MonthlyRent, t.AddendumNo, t.Note)).ToList(),
            contract.Occupants.OrderBy(o => o.MoveInDate)
                .Select(o => new OccupantDto(o.Id, o.RenterId, names.GetValueOrDefault(o.RenterId, string.Empty), o.MoveInDate, o.MoveOutDate,
                    o.ExpectedEndDate, o.Relationship, o.Note, o.RenterId == contract.RepresentativeRenterId, o.RelationshipType,
                    o.GuardianConsent, o.RenterId == contract.ReferenceRenterId, o.Role)).ToList(),
            contract.Assets.OrderBy(a => a.Name)
                .Select(a => new AssetDto(a.Id, a.Name, a.Quantity, a.ConditionAtHandover, a.ConditionAtReturn, a.ValueEstimate, a.Note))
                .ToList(),
            contract.Vehicles.OrderBy(v => v.RegisteredFrom)
                .Select(v => new VehicleDto(v.Id, v.RenterId, v.VehicleType, v.PlateNumber, v.BrandColor, v.RegisteredFrom, v.RegisteredTo,
                    v.Note)).ToList(),
            ContractDocumentDto.From(contract),
            ContractWarnings.For(contract, await fees.ListParkingFeesAsync(contract.PropertyId, cancellationToken), today)
                .Concat(await MeterWarningsAsync(contract, today, cancellationToken))
                .Concat(await paperWarnings.ForAsync(contract, today, cancellationToken)).ToList(),
            contract.HouseholdHeadRenterId,
            await ContractFeeRules.ToDtosAsync(fees, meters, contract, today, cancellationToken),
            UtilityPriceSnapshotJson.FromJson(contract.UtilityPriceSnapshot)
                ?? UtilityPriceSnapshotJson.Capture(contract, await ContractFeeRules.LoadTypesAsync(fees, meters, contract, cancellationToken)),
            contract.Flags(today),
            contract.HoldoverSince,
            contract.HoldoverNote,
            contract.PreviousEndDate,
            contract.PreviousContractId,
            contract.Version.ToString(),
            contract.HasSignedDocument,
            contract.SignedDocumentNote,
            contract.BillingStartDate,
            new ContractDepositDto(contract.DepositAmount, contract.HasDeposit ? contract.DepositStatus : null, contract.DepositRefundedOn,
                contract.DepositRefundedAmount, contract.DepositNote,
                contract.Status == ContractStatus.Ended && contract.HasDeposit && contract.DepositStatus == DepositStatus.Holding),
            contract.RoomSince,
            contract.RoomMoves.OrderBy(m => m.FromDate)
                .Select(m => new RoomMoveDto(m.RoomId, movedCodes.GetValueOrDefault(m.RoomId, ""), m.FromDate, m.ToDate)).ToList(),
            contract.RepresentativeSince ?? contract.StartDate,
            contract.RepresentativeHistory.OrderBy(h => h.FromDate)
                .Select(h => new RepresentativeHistoryDto(h.RenterId, names.GetValueOrDefault(h.RenterId, string.Empty), h.FromDate, h.ToDate)).ToList());
    }
}
