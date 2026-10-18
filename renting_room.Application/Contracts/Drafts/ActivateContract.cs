using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

// Kích hoạt (bàn giao phòng)
/// <param name="HandoverReadings">MT-BR-13: chỉ số nhận phòng cho mỗi công tơ của phòng; value null = "Dùng số mới nhất".</param>
public sealed record ActivateContractCommand(
    Guid Id, IReadOnlyList<MeterReadingInput>? HandoverReadings = null) : IRequest<Result>;

/// <summary>
/// CT-BR-01/02/18/19. Khóa theo thứ tự rooms → contracts (C-07). Hai hợp đồng cùng phòng kích hoạt song song:
/// khóa hàng phòng tuần tự hóa, EXCLUDE constraint trong DB là chốt chặn cuối (→ 409 ROOM_PERIOD_OVERLAP).
/// </summary>
public sealed class ActivateContractHandler(
    IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ActivateContractCommand, Result>
{
    public async ValueTask<Result> Handle(ActivateContractCommand request, CancellationToken cancellationToken)
    {
        var roomId = await db.Contracts.Where(c => c.Id == request.Id).Select(c => (Guid?)c.RoomId).FirstOrDefaultAsync(cancellationToken);
        if (roomId is null)
            return Result.Failure(ContractErrors.NotFound);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Room>(roomId.Value, cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.Id, cancellationToken);

        var contract = (await ContractMutation.LoadAsync(db, request.Id, cancellationToken))!;
        var room = await db.Rooms.FirstAsync(r => r.Id == contract.RoomId, cancellationToken);
        var property = await db.Properties.FirstAsync(p => p.Id == contract.PropertyId, cancellationToken);
        var representative = await db.Renters.FirstAsync(r => r.Id == contract.RepresentativeRenterId, cancellationToken);

        var occupantError = await CheckOccupantsAsync(contract, cancellationToken)
            ?? await OccupantChecks.FindRoleConflictAsync(db, contract, cancellationToken);
        if (occupantError is not null)
            return Result.Failure(occupantError);
        // CT-BR-14: HĐ khác đã chuyển khỏi phòng này vẫn tính là ở tới ngày chuyển (lịch sử không nằm trong EXCLUDE của contracts).
        if (await db.Contracts.AnyAsync(c => c.RoomMoves.Any(m => m.RoomId == contract.RoomId && m.ToDate >= contract.StartDate), cancellationToken))
            return Result.Failure(ContractErrors.RoomPeriodOverlap);

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        var (lessor, _) = await LessorSource.EffectiveAsync(db, property, cancellationToken);
        var feeTypes = await ContractFeeRules.LoadTypesAsync(db, contract, cancellationToken);
        var context = new ActivationContext(
            today,
            now,
            RoomAvailable: !room.IsArchived && !room.IsUnderMaintenance && !property.IsArchived,
            // Bên cho thuê: riêng của khu, không có thì của chủ trọ (PR-BR-17). Chưa khai cả hai ⇒ không chụp (chỉ cảnh báo LESSOR_INFO_INCOMPLETE).
            SigningSnapshotJson: lessor is null ? null : SigningSnapshot.From(property, lessor, room, representative, now).ToJson(),
            HouseRulesSnapshot: property.HouseRulesText,
            UtilityPriceSnapshotJson: UtilityPriceSnapshotJson.ToJson(UtilityPriceSnapshotJson.Capture(contract, feeTypes)));

        var activated = contract.Activate(context);
        if (activated.IsFailure)
            return activated;
        var handover = await ContractMeterReadings.RecordAsync(
            // K5: chỉ số nhận phòng tại "Tính tiền từ ngày" (= ngày bắt đầu, trừ HĐ nhập từ sổ cũ) — điện nước trước mốc đó không tính.
            db, contract, ReadingKind.Handover, contract.BillingStartDate, request.HandoverReadings, cancellationToken);
        if (handover.IsFailure)
            return handover;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>CT-BR-31 tại thời điểm bàn giao: không ai đang ở phòng khác (quan hệ người ở chỉ cảnh báo — ContractPaperWarnings).</summary>
    private async Task<Error?> CheckOccupantsAsync(Contract contract, CancellationToken ct)
    {
        var people = await OccupantChecks.LoadPeopleAsync(
            db, contract.Occupants.Select(o => o.RenterId).Append(contract.ReferenceRenterId), ct);
        var occupants = contract.Occupants.ToList();
        return await OccupantChecks.FindLivingElsewhereAsync(db, contract,
            occupants.Select(o => (o.RenterId, o.MoveInDate, o.MoveOutDate)).ToList(),
            people.ToDictionary(p => p.Key, p => p.Value.Name), ct);
    }
}
