using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Application.Exports;
using renting_room.Application.Fees;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Meters;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Application.Identity;

namespace renting_room.Application.Contracts;

public sealed record GetContractDocumentQuery(Guid ContractId) : IRequest<Result<ExportFile>>;

public sealed class GetContractDocumentHandler(
    IContractRepository contracts,
    IRenterQueries renterQueries,
    IPropertyQueries properties,
    IRoomQueries rooms,
    IOrganizationQueries organizations,
    IFeeQueries fees,
    IMeterQueries meters,
    IUserQueries users,
    IPersonalDataProtector protector,
    IWordDocumentWriter writer,
    ICurrentUser currentUser,
    TimeProvider clock,
    IAuditTrail auditTrail)
    : IRequestHandler<GetContractDocumentQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetContractDocumentQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetForUpdateAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot) ?? await CurrentSnapshotAsync(contract, cancellationToken);
        var renterIds = contract.Occupants.Select(o => o.RenterId).Distinct().ToList();
        var renters = await renterQueries.ListAsync(renterIds, cancellationToken);
        var feeTypes = await ContractFeeRules.LoadTypesAsync(fees, meters, contract, cancellationToken);
        var agreedFees = UtilityPriceSnapshotJson.FromJson(contract.UtilityPriceSnapshot) ?? UtilityPriceSnapshotJson.Capture(contract, feeTypes);

        // Người ở in theo thời điểm: nháp = ngày bắt đầu; đang hiệu lực = hôm nay; đã / đang thanh lý = ngày trả phòng (không sau hôm nay).
        var today = clock.GetUtcNow().ToBusinessDate();
        var asOf = contract.Status switch
        {
            ContractStatus.Draft or ContractStatus.Cancelled => contract.StartDate,
            _ when contract.ActualEndDate is { } end && end < today => end,
            _ => contract.StartDate > today ? contract.StartDate : today
        };
        var full = await SensitiveDataAccess.CanViewAsync(users, currentUser, cancellationToken);
        var occupants = contract.Occupants
            .Where(o => renters.ContainsKey(o.RenterId) && (o.MoveOutDate is null || o.MoveOutDate >= asOf))
            .OrderByDescending(o => o.RenterId == contract.ReferenceRenterId)
            .ThenBy(o => o.RelationshipType ?? (OccupantRelationship)int.MaxValue)
            .Select(o => (renters[o.RenterId],
                renters[o.RenterId].IdNumberEncrypted is not { Length: > 0 } encrypted ? string.Empty
                : full ? protector.Decrypt(encrypted) : PersonalDataProtectorExtensions.Mask(renters[o.RenterId].IdNumberLast4), o))
            .ToList();

        var data = new ContractPrintData(contract, snapshot,
            IdNumber(snapshot?.Lessor.IdNumberEncrypted, snapshot?.Lessor.IdNumberLast4, full),
            IdNumber(snapshot?.Representative?.IdNumberEncrypted, snapshot?.Representative?.IdNumberLast4, full),
            occupants, agreedFees, feeTypes,
            (await properties.GetAsync(contract.PropertyId, cancellationToken))!.BillingSettings);

        if (full)
            auditTrail.RecordRead(AuditActions.PrintWithIdNumbers, nameof(Contract), contract.Id);
        var fileName = $"hop-dong_{contract.ContractNo.Replace('/', '-')}.docx";
        return new ExportFile(fileName, writer.Write(ContractPrintBuilder.Build(data)));
    }

    private string? IdNumber(string? base64, string? last4, bool full) =>
        base64 is null ? null
        : full ? protector.Decrypt(Convert.FromBase64String(base64))
        : PersonalDataProtectorExtensions.Mask(last4);

    private async Task<SigningSnapshot?> CurrentSnapshotAsync(Contract contract, CancellationToken ct)
    {
        var property = (await properties.GetAsync(contract.PropertyId, ct))!;
        if ((await LessorSource.EffectiveAsync(organizations, property, ct)).Lessor is not { } lessor)
            return null;
        var room = (await rooms.FindAsync(contract.RoomId, ct))!;
        var representative = (await renterQueries.ListAsync([contract.RepresentativeRenterId], ct))[contract.RepresentativeRenterId];
        return SigningSnapshot.From(property, lessor, room, representative, clock.GetUtcNow());
    }
}
