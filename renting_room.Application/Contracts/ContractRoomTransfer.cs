using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

public static class RoomTransferWarnings
{
    public static readonly Warning ResidenceRoomChanged = new("RESIDENCE_ROOM_CHANGED",
        "Đã chuyển phòng — cập nhật số phòng trên đăng ký tạm trú của người ở nếu cần.");
}

/// <param name="OldRoomReadings">Chỉ số cuối các công tơ phòng cũ tại ngày chuyển — bắt buộc.</param>
/// <param name="NewRoomReadings">Chỉ số nhận phòng các công tơ phòng mới; null / value null = số mới nhất.</param>
/// <param name="MonthlyRent">Giá thuê phòng mới (tùy chọn) — áp từ kỳ chưa chốt đầu tiên như sửa giá (CT-UC-05).</param>
public sealed record TransferRoomCommand(
    Guid ContractId, Guid ToRoomId, DateOnly Date, IReadOnlyList<MeterReadingInput>? OldRoomReadings,
    IReadOnlyList<MeterReadingInput>? NewRoomReadings, decimal? MonthlyRent, string? Note) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class TransferRoomCommandValidator : AbstractValidator<TransferRoomCommand>
{
    public TransferRoomCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.ToRoomId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.MonthlyRent).OptionalMoney();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-UC-12 / CT-BR-14: HĐ đi theo người thuê sang phòng trống cùng khu từ ngày D. 1 transaction, khóa phòng (theo id) → HĐ (C-07):
/// chỉ số cuối phòng cũ tại D → đổi phòng → chỉ số nhận phòng phòng mới tại D → (tùy chọn) giá mới từ kỳ chưa chốt đầu tiên.
/// Điện nước kỳ chứa D cộng công tơ 2 phòng (CT-BR-47); nháp đã lập ⇒ "Cần tính lại".
/// </summary>
public sealed class TransferRoomHandler(IAppDbContext db, ISender sender, TimeProvider clock)
    : IRequestHandler<TransferRoomCommand, Result<IReadOnlyList<Warning>>>
{
    public async ValueTask<Result<IReadOnlyList<Warning>>> Handle(TransferRoomCommand request, CancellationToken cancellationToken)
    {
        var fromRoomId = await db.Contracts.Where(c => c.Id == request.ContractId).Select(c => (Guid?)c.RoomId).FirstOrDefaultAsync(cancellationToken);
        if (fromRoomId is null)
            return ContractErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { fromRoomId.Value, request.ToRoomId }.Distinct().Order())
            await db.LockForUpdateAsync<Room>(id, cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = (await ContractMutation.LoadAsync(db, request.ContractId, cancellationToken))!;
        if (contract.Status != ContractStatus.Active)
            return ContractErrors.NotActive;

        var check = await CheckTargetAsync(contract, request.ToRoomId, request.Date, cancellationToken);
        if (check.IsFailure)
            return check.Error!;
        if (await BilledAfterAsync(contract, request.Date, cancellationToken))
            return ContractErrors.TransferAlreadyBilled;

        var closing = await ContractMeterReadings.RecordAsync(db, contract, ReadingKind.Final, request.Date, request.OldRoomReadings, cancellationToken);
        if (closing.IsFailure)
            return closing.Error!;
        var moved = contract.TransferRoom(request.ToRoomId, request.Date, clock.GetUtcNow().ToBusinessDate());
        if (moved.IsFailure)
            return moved.Error!;
        var newMeters = request.NewRoomReadings ?? await db.Meters
            .Where(m => m.RoomId == request.ToRoomId && m.InstalledDate <= request.Date && (m.RemovedDate == null || m.RemovedDate > request.Date))
            .Select(m => new MeterReadingInput(m.Id, null)).ToListAsync(cancellationToken);
        var handover = await ContractMeterReadings.RecordAsync(db, contract, ReadingKind.Handover, request.Date, newMeters, cancellationToken);
        if (handover.IsFailure)
            return handover.Error!;
        await db.SaveChangesAsync(cancellationToken);

        if (request.MonthlyRent is { } rent && rent != contract.CurrentRent(request.Date))
        {
            var code = await db.Rooms.Where(r => r.Id == request.ToRoomId).Select(r => r.Code).FirstAsync(cancellationToken);
            var changed = await sender.Send(new ChangeRentCommand(contract.Id, null, rent, null, request.Note ?? $"Chuyển sang phòng {code}"),
                cancellationToken);
            if (changed.IsFailure)
                return changed.Error!;
        }
        await transaction.CommitAsync(cancellationToken);
        return Result.Success<IReadOnlyList<Warning>>([RoomTransferWarnings.ResidenceRoomChanged]);
    }

    /// <summary>Phòng mới: cùng khu, đang dùng, không HĐ nào chiếm từ ngày D (HĐ hiệu lực / giữ chỗ / lịch sử chuyển phòng).</summary>
    private async Task<Result> CheckTargetAsync(Contract contract, Guid roomId, DateOnly date, CancellationToken ct)
    {
        if (roomId == contract.RoomId)
            return Result.Failure(ContractErrors.TransferSameRoom);
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null)
            return Result.Failure(PropertyErrors.RoomNotFound);
        if (room.PropertyId != contract.PropertyId)
            return Result.Failure(ContractErrors.TransferOtherProperty);

        var occupied = await db.Contracts.AnyAsync(c => c.RoomId == roomId && c.Id != contract.Id
            && (c.Status == ContractStatus.Draft
                || ((c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                    && (c.ActualEndDate == null || c.ActualEndDate >= date))), ct)
            || await db.Contracts.AnyAsync(c => c.RoomMoves.Any(m => m.RoomId == roomId && m.ToDate >= date), ct);
        return room.IsArchived || room.IsUnderMaintenance || occupied
            ? Result.Failure(ContractErrors.TransferRoomUnavailable)
            : Result.Success();
    }

    /// <summary>Đoạn đo đã lập phiếu (chưa hủy) trên công tơ phòng cũ kết thúc sau D ⇒ không chèn được chỉ số cuối tại D.</summary>
    private async Task<bool> BilledAfterAsync(Contract contract, DateOnly date, CancellationToken ct)
    {
        var meterIds = await db.Meters.Where(m => m.RoomId == contract.RoomId).Select(m => m.Id).ToListAsync(ct);
        var endIds = await db.Invoices.Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Segments).Where(s => !s.Voided && meterIds.Contains(s.MeterId)).Select(s => s.EndReadingId).ToListAsync(ct);
        return endIds.Count > 0
            && await db.Meters.SelectMany(m => m.Readings).AnyAsync(r => endIds.Contains(r.Id) && r.ReadingDate > date, ct);
    }
}
