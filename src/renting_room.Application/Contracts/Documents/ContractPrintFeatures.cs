using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Application.Exports;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// CT-UC-15: xuất văn bản hợp đồng (.docx) để in / ký / lưu. Người có quyền xem dữ liệu nhạy cảm (ID-BR-22) nhận số giấy tờ
/// đầy đủ (ghi log kiểm toán); người không có quyền nhận bản che số (********1234).
/// </summary>
public sealed record GetContractDocumentQuery(Guid ContractId) : IRequest<Result<ExportFile>>;

public sealed class GetContractDocumentHandler(
    IAppDbContext db,
    IPersonalDataProtector protector,
    IWordDocumentWriter writer,
    ICurrentUser currentUser,
    TimeProvider clock,
    IAuditTrail auditTrail)
    : IRequestHandler<GetContractDocumentQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetContractDocumentQuery request, CancellationToken cancellationToken)
    {
        var contract = await ContractMutation.LoadAsync(db, request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot) ?? await CurrentSnapshotAsync(contract, cancellationToken);
        var renterIds = contract.Occupants.Select(o => o.RenterId).Distinct().ToList();
        var renters = await db.Renters.AsNoTracking().Where(r => renterIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var feeTypes = await ContractFeeRules.LoadTypesAsync(db, contract, cancellationToken);
        var agreedFees = UtilityPriceSnapshotJson.FromJson(contract.UtilityPriceSnapshot) ?? UtilityPriceSnapshotJson.Capture(contract, feeTypes);

        // Người ở in theo thời điểm: nháp = ngày bắt đầu; đang hiệu lực = hôm nay; đã / đang thanh lý = ngày trả phòng (không sau hôm nay).
        var today = clock.GetUtcNow().ToBusinessDate();
        var asOf = contract.Status switch
        {
            ContractStatus.Draft or ContractStatus.Cancelled => contract.StartDate,
            _ when contract.ActualEndDate is { } end && end < today => end,
            _ => contract.StartDate > today ? contract.StartDate : today
        };
        var full = await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken);
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
            (await db.Properties.AsNoTracking().FirstAsync(p => p.Id == contract.PropertyId, cancellationToken)).BillingSettings);

        if (full)
            auditTrail.RecordRead(AuditActions.PrintWithIdNumbers, nameof(Contract), contract.Id);
        var fileName = $"hop-dong_{contract.ContractNo.Replace('/', '-')}.docx";
        return new ExportFile(fileName, writer.Write(ContractPrintBuilder.Build(data)));
    }

    private string? IdNumber(string? base64, string? last4, bool full) =>
        base64 is null ? null
        : full ? protector.Decrypt(Convert.FromBase64String(base64))
        : PersonalDataProtectorExtensions.Mask(last4);

    /// <summary>Bản nháp chưa có bản chụp ⇒ dựng tạm từ dữ liệu hiện tại (không lưu).</summary>
    private async Task<SigningSnapshot?> CurrentSnapshotAsync(Contract contract, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == contract.PropertyId, ct);
        if ((await LessorSource.EffectiveAsync(db, property, ct)).Lessor is not { } lessor)
            return null;
        var room = await db.Rooms.AsNoTracking().FirstAsync(r => r.Id == contract.RoomId, ct);
        var representative = await db.Renters.AsNoTracking().FirstAsync(r => r.Id == contract.RepresentativeRenterId, ct);
        return SigningSnapshot.From(property, lessor, room, representative, clock.GetUtcNow());
    }
}
