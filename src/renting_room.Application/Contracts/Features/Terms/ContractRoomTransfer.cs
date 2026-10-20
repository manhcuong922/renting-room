using System.Text.Json;
using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Billing;
using renting_room.Application.Common.Validation;
using renting_room.Application.Fees;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
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

    public static Warning ListedRentApplied(decimal rent) => new("ROOM_TRANSFER_LISTED_RENT_APPLIED",
        $"Giá thuê đổi sang giá niêm yết của phòng mới: {rent:N0}đ — sửa giá thuê trên hợp đồng nếu thỏa thuận khác.");

    public static Warning MeterMissingInNewRoom(string feeName) => new("METER_MISSING_IN_NEW_ROOM",
        $"Phòng mới không có công tơ \"{feeName}\" — nếu phòng mới tính khoản này theo đầu người thì thêm dịch vụ tương ứng cho hợp đồng.");

    public static Warning MeterOnlyInNewRoom(string feeName) => new("METER_ONLY_IN_NEW_ROOM",
        $"Phòng mới có công tơ \"{feeName}\" mà phòng cũ không có — nếu hợp đồng đang có dịch vụ tính theo đầu người cho khoản này thì bỏ để không tính 2 lần.");

    public static readonly Warning CancelledCheckRent = new("ROOM_TRANSFER_CANCELLED_CHECK_RENT",
        "Đã chuyển lại về phòng cũ — có giá thuê / dịch vụ đổi lúc chuyển chưa tự trả lại được, kiểm tra và sửa nếu cần.");
}

internal sealed record TransferUndo(
    DateOnly? RentFrom, decimal? RentAfter, decimal? RentBefore, bool RentTermAdded, Guid? RentChargeId, IReadOnlyList<TransferUndoFee> Fees)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static TransferUndo? Parse(string? json) => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<TransferUndo>(json);
}

internal sealed record TransferUndoFee(Guid FeeTypeId, decimal Quantity, decimal? UnitPriceOverride, DateOnly From);

internal sealed class TransferRentChange
{
    public DateOnly? From { get; private set; }
    public decimal? After { get; private set; }
    public decimal? Before { get; private set; }
    public bool TermAdded { get; private set; }
    public Guid? ChargeId { get; set; }

    public void Capture(Contract contract, DateOnly from, decimal rent)
    {
        (From, After, Before) = (from, rent, contract.CurrentRent(from));
        TermAdded = contract.RentTerms.All(t => t.EffectiveFrom != from);
    }
}

public enum TransferRentFrom
{
    CurrentPeriod,
    NextPeriod
}

public sealed record TransferRoomCommand(
    Guid ContractId, Guid ToRoomId, IReadOnlyList<MeterReadingInput>? OldRoomReadings,
    IReadOnlyList<MeterReadingInput>? NewRoomReadings, decimal? MonthlyRent, string? Note, TransferRentFrom? RentFrom = null,
    IReadOnlyList<Guid>? RemoveFeeTypeIds = null) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class TransferRoomCommandValidator : AbstractValidator<TransferRoomCommand>
{
    public TransferRoomCommandValidator()
    {
        RuleFor(x => x.ToRoomId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.MonthlyRent).OptionalMoney();
        RuleFor(x => x.RentFrom).IsInEnum();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-UC-12 / CT-BR-14: HĐ đi theo người thuê sang phòng trống cùng khu <b>từ hôm nay</b> (lúc chủ trọ bấm — không chọn ngày). 1 transaction,
/// khóa phòng (theo id) → HĐ (C-07): chỉ số cuối phòng cũ hôm nay → đổi phòng → chỉ số nhận phòng phòng mới → giá thuê mới theo quy tắc nửa kỳ
/// + bỏ dịch vụ không dùng ở phòng mới (CT-BR-48). Điện nước phòng cũ không thành khoản tiền: 2 mốc chỉ số là sản lượng đã lưu, phiếu của kỳ cộng
/// vào cùng dòng (CT-BR-47). Về đúng phòng vừa rời ngay trong ngày chuyển ⇒ xóa lần chuyển như chưa từng chuyển (thay lệnh hủy chuyển phòng).
/// Nháp đã lập ⇒ "Cần tính lại".
/// </summary>
public sealed class TransferRoomHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IContractQueries contractQueries, IRoomQueries rooms, IPropertyQueries properties,
    IFeeQueries fees, IMeterRepository meterRepository, IMeterQueries meterQueries, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries,
    IRoomChargeRepository roomChargeRepository, RoomChargeBilling roomCharges, ContractMeterReadings meterReadings, ISender sender,
    IInvoiceLockReader invoiceLocks, TimeProvider clock)
    : IRequestHandler<TransferRoomCommand, Result<IReadOnlyList<Warning>>>
{
    private const string UndoReason = "Chuyển lại về phòng cũ trong ngày";

    public async ValueTask<Result<IReadOnlyList<Warning>>> Handle(TransferRoomCommand request, CancellationToken cancellationToken)
    {
        var fromRoomId = await contractQueries.GetRoomIdAsync(request.ContractId, cancellationToken);
        if (fromRoomId is null)
            return ContractErrors.NotFound;
        var today = clock.GetUtcNow().ToBusinessDate();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { fromRoomId.Value, request.ToRoomId }.Distinct().Order())
            await unitOfWork.LockForUpdateAsync<Room>(id, cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = (await contracts.GetForUpdateAsync(request.ContractId, cancellationToken))!;
        if (contract.Status != ContractStatus.Active)
            return ContractErrors.NotActive;

        var undoing = contract.RoomMoves.MaxBy(m => m.FromDate) is { } last && last.RoomId == request.ToRoomId && contract.RoomSince == today;
        var result = undoing
            ? await UndoLastTransferAsync(contract, contract.RoomMoves.MaxBy(m => m.FromDate)!, cancellationToken)
            : await TransferAsync(contract, fromRoomId.Value, request, today, cancellationToken);
        if (result.IsFailure)
            return result;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Result<IReadOnlyList<Warning>>> TransferAsync(
        Contract contract, Guid fromRoomId, TransferRoomCommand request, DateOnly today, CancellationToken ct)
    {
        var check = await CheckTargetAsync(contract, request.ToRoomId, today, ct);
        if (check.IsFailure)
            return check.Error!;
        if (await BilledAfterAsync(contract, today, ct))
            return ContractErrors.TransferAlreadyBilled;

        var closing = await meterReadings.RecordAsync(contract, ReadingKind.Final, today, request.OldRoomReadings, ct);
        if (closing.IsFailure)
            return closing.Error!;
        var moved = contract.TransferRoom(request.ToRoomId, today, today);
        if (moved.IsFailure)
            return moved.Error!;
        var newMeters = request.NewRoomReadings
            ?? (await meterQueries.ListActiveIdsInRoomAsync(request.ToRoomId, today, ct)).Select(id => new MeterReadingInput(id, null)).ToList();
        var handover = await meterReadings.RecordAsync(contract, ReadingKind.Handover, today, newMeters, ct);
        if (handover.IsFailure)
            return handover.Error!;
        await unitOfWork.SaveChangesAsync(ct);

        var warnings = new List<Warning> { RoomTransferWarnings.ResidenceRoomChanged };
        warnings.AddRange(await MeterWarningsAsync(fromRoomId, request.ToRoomId, today, ct));
        var rentChange = new TransferRentChange();
        var rented = await ApplyRentAsync(contract, request, today, warnings, rentChange, ct);
        if (rented.IsFailure)
            return rented.Error!;
        var removedFees = await RemoveFeesAsync(contract, request.RemoveFeeTypeIds, ct);
        if (removedFees.IsFailure)
            return removedFees.Error!;
        var recorded = contract.RecordTransferUndo(new TransferUndo(
            rentChange.From, rentChange.After, rentChange.Before, rentChange.TermAdded, rentChange.ChargeId, removedFees.Value!).ToJson());
        return recorded.IsFailure ? recorded.Error! : warnings;
    }

    private async Task<Result<IReadOnlyList<TransferUndoFee>>> RemoveFeesAsync(Contract contract, IReadOnlyList<Guid>? feeTypeIds, CancellationToken ct)
    {
        var feesFrom = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, ct) ?? contract.StartDate;
        var removedFees = new List<TransferUndoFee>();
        foreach (var feeTypeId in (feeTypeIds ?? []).Distinct())
        {
            if (contract.Fees.FirstOrDefault(f => f.FeeTypeId == feeTypeId && f.Covers(feesFrom)) is { } current)
                removedFees.Add(new TransferUndoFee(feeTypeId, current.Quantity, current.UnitPriceOverride, feesFrom));
            var removed = await sender.Send(new RemoveContractFeeCommand(contract.Id, feeTypeId, null), ct);
            if (removed.IsFailure)
                return removed.Error!;
        }
        return removedFees;
    }

    private async Task<Result> ApplyRentAsync(
        Contract contract, TransferRoomCommand request, DateOnly date, List<Warning> warnings, TransferRentChange change, CancellationToken ct)
    {
        var room = (await rooms.FindAsync(request.ToRoomId, ct))!;
        if ((request.MonthlyRent ?? room.ListedRent) is not { } rent)
            return Result.Success();
        if (request.MonthlyRent is null && rent != contract.CurrentRent(date))
            warnings.Add(RoomTransferWarnings.ListedRentApplied(rent));

        var note = request.Note ?? $"Chuyển sang phòng {room.Code}";
        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        var period = contract.BillingPeriods(schedule, date).LastOrDefault(p => p.Start <= date && date <= p.End);
        if (period is null)
        {
            if (rent == contract.CurrentRent(date))
                return Result.Success();
            var from = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, ct) ?? contract.StartDate;
            change.Capture(contract, from, rent);
            return await sender.Send(new ChangeRentCommand(contract.Id, from, rent, null, note), ct);
        }

        var daysInOldRoom = date.DayNumber - period.Start.DayNumber;
        var periodDays = period.End.DayNumber - period.Start.DayNumber + 1;
        var fromThisPeriod = request.RentFrom is { } chosen ? chosen == TransferRentFrom.CurrentPeriod : daysInOldRoom * 2 <= periodDays;
        var target = fromThisPeriod ? period.Start : period.End.AddDays(1);
        var firstOpen = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, ct);
        var effectiveFrom = firstOpen is { } open && open > target ? open : target;
        var billedRent = contract.CurrentRent(period.Start);

        if (rent != contract.CurrentRent(effectiveFrom))
        {
            change.Capture(contract, effectiveFrom, rent);
            var changed = await sender.Send(new ChangeRentCommand(contract.Id, effectiveFrom, rent, null, note), ct);
            if (changed.IsFailure)
                return changed;
        }
        if (!fromThisPeriod || effectiveFrom == period.Start || billedRent is not { } billed || billed == rent)
            return Result.Success();

        var charge = RoomCharge.Create(contract.PropertyId, contract.RoomId, contract.Id,
            rent > billed ? RoomChargeKind.Surcharge : RoomChargeKind.Credit,
            $"Chênh lệch tiền phòng kỳ {period.Start:dd/MM}–{period.End:dd/MM} (chuyển phòng)", Math.Abs(rent - billed), date,
            "Giá phòng mới áp cho cả kỳ chuyển phòng");
        roomChargeRepository.Add(charge);
        change.ChargeId = charge.Id;
        if (await roomCharges.DraftForAsync(contract.Id, date, ct) is { } draft)
            RoomChargeBilling.Attach(draft, charge);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<IReadOnlyList<Warning>> MeterWarningsAsync(Guid oldRoomId, Guid newRoomId, DateOnly date, CancellationToken ct)
    {
        var meters = await meterQueries.ListActiveFeesAsync([oldRoomId, newRoomId], date, ct);
        var inOld = meters.Where(m => m.RoomId == oldRoomId).Select(m => m.FeeTypeId).ToHashSet();
        var inNew = meters.Where(m => m.RoomId == newRoomId).Select(m => m.FeeTypeId).ToHashSet();
        var differing = inOld.Union(inNew).Where(id => inOld.Contains(id) != inNew.Contains(id)).ToList();
        if (differing.Count == 0)
            return [];

        var names = await fees.ListNamesAsync(differing, ct);
        return names.Select(f => inOld.Contains(f.Id) ? RoomTransferWarnings.MeterMissingInNewRoom(f.Name) : RoomTransferWarnings.MeterOnlyInNewRoom(f.Name))
            .ToList();
    }

    private async Task<Result> CheckTargetAsync(Contract contract, Guid roomId, DateOnly date, CancellationToken ct)
    {
        if (roomId == contract.RoomId)
            return Result.Failure(ContractErrors.TransferSameRoom);
        var room = await rooms.FindAsync(roomId, ct);
        if (room is null)
            return Result.Failure(PropertyErrors.RoomNotFound);
        if (room.PropertyId != contract.PropertyId)
            return Result.Failure(ContractErrors.TransferOtherProperty);

        return room.IsArchived || room.IsUnderMaintenance || await RoomTakenAsync(contract.Id, roomId, date, ct)
            ? Result.Failure(ContractErrors.TransferRoomUnavailable)
            : Result.Success();
    }

    /// <summary>Phòng có HĐ khác chiếm từ ngày <paramref name="date"/> (hiệu lực / giữ chỗ / lịch sử chuyển phòng).</summary>
    private Task<bool> RoomTakenAsync(Guid contractId, Guid roomId, DateOnly date, CancellationToken ct) =>
        contractQueries.IsRoomTakenAsync(contractId, roomId, date, ct);

    /// <summary>Đoạn đo đã lập phiếu (chưa hủy) trên công tơ phòng cũ kết thúc sau D ⇒ không chèn được chỉ số cuối tại D.</summary>
    private async Task<bool> BilledAfterAsync(Contract contract, DateOnly date, CancellationToken ct)
    {
        var meterIds = await meterQueries.ListIdsInRoomAsync(contract.RoomId, ct);
        var endIds = await invoiceQueries.ListSegmentEndReadingIdsAsync(contract.Id, meterIds, ct);
        return endIds.Count > 0
            && await meterQueries.AnyReadingAfterAsync(endIds, date, ct);
    }

    private async Task<Result<IReadOnlyList<Warning>>> UndoLastTransferAsync(Contract contract, ContractRoomMove last, CancellationToken ct)
    {
        var (oldRoomId, newRoomId, date) = (last.RoomId, contract.RoomId, contract.RoomSince);
        if (await RoomTakenAsync(contract.Id, oldRoomId, date, ct))
            return ContractErrors.TransferRoomUnavailable;
        if (await invoiceQueries.HasInvoiceForRoomFromAsync(contract.Id, newRoomId, date, ct))
            return ContractErrors.TransferCancelBilled;

        var undo = TransferUndo.Parse(last.UndoSnapshot);
        var now = clock.GetUtcNow();
        var charges = await CancelTransferChargesAsync(contract.Id, oldRoomId, date, undo?.RentChargeId, now, ct);
        if (charges.IsFailure)
            return charges.Error!;
        var readings = await VoidReadingsAsync(contract.Id, oldRoomId, newRoomId, date, now, ct);
        if (readings.IsFailure)
            return readings.Error!;
        var reverted = undo is not null && Revert(contract, undo);
        var restored = contract.CancelLastTransfer();
        if (restored.IsFailure)
            return restored.Error!;
        return Result.Success<IReadOnlyList<Warning>>(reverted
            ? [RoomTransferWarnings.ResidenceRoomChanged]
            : [RoomTransferWarnings.ResidenceRoomChanged, RoomTransferWarnings.CancelledCheckRent]);
    }

    /// <summary>Trả lại giá thuê + dịch vụ lần chuyển đã đổi; false = có phần đã bị đổi tiếp sau lần chuyển nên giữ nguyên.</summary>
    private static bool Revert(Contract contract, TransferUndo undo)
    {
        var complete = true;
        if (undo is { RentFrom: { } from, RentAfter: { } after })
            complete &= contract.RevertTransferRent(from, after, undo.RentBefore, undo.RentTermAdded);
        foreach (var fee in undo.Fees)
            complete &= contract.RestoreTransferFee(new ContractFeeInput(fee.FeeTypeId, fee.Quantity, fee.UnitPriceOverride), fee.From);
        return complete;
    }

    private async Task<Result> CancelTransferChargesAsync(
        Guid contractId, Guid oldRoomId, DateOnly date, Guid? rentChargeId, DateTimeOffset now, CancellationToken ct)
    {
        var charges = await roomChargeRepository.ListTransferChargesAsync(contractId, oldRoomId, date, rentChargeId, ct);
        if (charges.Any(c => c.IsSettled))
            return Result.Failure(ContractErrors.TransferCancelSettled);
        foreach (var charge in charges)
        {
            if (charge.InvoiceId is { } invoiceId)
            {
                await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
                var invoice = await invoices.GetForUpdateAsync(invoiceId, ct);
                if (invoice is not { Status: InvoiceStatus.Draft })
                    return Result.Failure(ContractErrors.TransferCancelBilled);
                var removed = invoice.RemoveChargeLine(charge.Id);
                if (removed.IsFailure)
                    return removed;
            }
            var cancelled = charge.Cancel(UndoReason, now);
            if (cancelled.IsFailure)
                return cancelled;
        }
        return Result.Success();
    }

    /// <summary>Hủy chỉ số cuối phòng cũ tại ngày chuyển và mọi chỉ số của HĐ trên công tơ phòng mới từ ngày chuyển (chưa bị phiếu đã chốt khóa).</summary>
    private async Task<Result> VoidReadingsAsync(Guid contractId, Guid oldRoomId, Guid newRoomId, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        var meterRooms = await meterQueries.ListRoomMetersAsync([oldRoomId, newRoomId], ct);
        var newMeterIds = meterRooms.Where(m => m.RoomId == newRoomId).Select(m => m.Id).ToList();
        var locked = await invoiceQueries.GetLockedReadingIdsAsync(newMeterIds, ct);
        foreach (var meter in await meterRepository.LockAndLoadAsync(meterRooms.Select(m => m.Id), ct))
        {
            var isNewRoom = meter.RoomId == newRoomId;
            var targets = meter.Ordered
                .Where(r => r.ContractId == contractId && (isNewRoom ? r.ReadingDate >= date : r.Kind == ReadingKind.Final && r.ReadingDate == date))
                .ToList();
            if (targets.Any(r => locked.Contains(r.Id)))
                return Result.Failure(ContractErrors.TransferCancelBilled);
            foreach (var reading in targets)
                meter.VoidReading(reading.Id, UndoReason, now);
        }
        return Result.Success();
    }
}
