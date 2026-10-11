using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Billing;
using renting_room.Application.Common.Validation;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
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

    public static Warning RentUnchanged(decimal current, decimal listed) => new("ROOM_TRANSFER_RENT_UNCHANGED",
        $"Giá thuê vẫn giữ {current:N0}đ — giá niêm yết phòng mới là {listed:N0}đ. Đổi giá thuê trên hợp đồng nếu đã thỏa thuận giá mới.");

    public static Warning MeterMissingInNewRoom(string feeName) => new("METER_MISSING_IN_NEW_ROOM",
        $"Phòng mới không có công tơ \"{feeName}\" — nếu phòng mới tính khoản này theo đầu người thì thêm dịch vụ tương ứng cho hợp đồng.");

    public static Warning MeterOnlyInNewRoom(string feeName) => new("METER_ONLY_IN_NEW_ROOM",
        $"Phòng mới có công tơ \"{feeName}\" mà phòng cũ không có — nếu hợp đồng đang có dịch vụ tính theo đầu người cho khoản này thì bỏ để không tính 2 lần.");

    public static readonly Warning CancelledCheckRent = new("ROOM_TRANSFER_CANCELLED_CHECK_RENT",
        "Đã hủy chuyển phòng — giá thuê / dịch vụ đã đổi lúc chuyển không tự đổi lại, kiểm tra và sửa nếu cần.");
}

/// <summary>
/// CT-BR-47 / BL-BR-38: chốt điện nước phòng cũ tại ngày chuyển thành khoản phát sinh hệ thống của HĐ — mỗi công tơ 1 khoản, từ chỉ số
/// cuối kỳ gần nhất HĐ đã ghi trong lần ở này (không có thì chỉ số nhận phòng) tới chỉ số cuối tại ngày chuyển, theo giá hiệu lực ngày chuyển.
/// </summary>
internal static class RoomTransferClosing
{
    public const string Reason = "Chốt chỉ số khi chuyển phòng";

    /// <summary>Gọi sau khi đã ghi chỉ số cuối và <b>trước</b> khi đổi phòng trên HĐ (phòng / ngày vào phòng còn là của phòng cũ).</summary>
    public static async Task<Result> CreateChargesAsync(IAppDbContext db, Contract contract, DateOnly date, CancellationToken ct)
    {
        var meterIds = await db.Meters
            .Where(m => m.RoomId == contract.RoomId && m.InstalledDate <= date && (m.RemovedDate == null || m.RemovedDate > date))
            .Select(m => m.Id).ToListAsync(ct);
        if (meterIds.Count == 0)
            return Result.Success();

        var meters = await MeterMapping.LockAndLoadAsync(db, meterIds, ct);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await db.FeeTypes.AsNoTracking().Include(f => f.Prices).Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var roomCode = await db.Rooms.Where(r => r.Id == contract.RoomId).Select(r => r.Code).FirstAsync(ct);
        var draft = await RoomChargeBilling.DraftForAsync(db, contract.Id, date, ct);

        foreach (var meter in meters)
        {
            var own = meter.Ordered.Where(r => r.ContractId == contract.Id && r.ReadingDate >= contract.RoomSince).ToList();
            if (own.Any(r => r.ReadingDate > date))
                return Result.Failure(ContractErrors.TransferAlreadyBilled);
            var end = own.LastOrDefault(r => r.Kind == ReadingKind.Final && r.ReadingDate == date);
            var start = own.LastOrDefault(r => r.Kind is ReadingKind.Periodic or ReadingKind.Handover)
                ?? (meter.InstalledDate > contract.RoomSince
                    ? meter.Ordered.FirstOrDefault(r => r.Kind == ReadingKind.Initial)
                    : meter.LatestOnOrBefore(contract.RoomSince));
            if (end is null || start is null || end.Value <= start.Value)
                continue;

            var fee = fees[meter.FeeTypeId];
            var price = fee.ResolvePrice(date);
            if (price is null)
                return Result.Failure(ContractErrors.TransferPriceMissing.WithDetail("feeTypeId", fee.Id));
            var quantity = end.Value - start.Value;
            var amount = Invoice.Money(price.Amount(quantity));
            if (amount <= 0)
                continue;
            if (amount > RoomCharge.MaxAmount)
                return Result.Failure(ContractErrors.TransferUsageTooLarge.WithDetail("meterId", meter.Id));

            // Theo bậc: đơn giá hiển thị = giá bình quân (FE-BR-15), như dòng điện nước trên phiếu.
            var unitPrice = price.IsTiered ? decimal.Round(amount / quantity, 2) : price.UnitPrice;
            var charge = RoomCharge.CreateMeterClosing(contract.PropertyId, contract.RoomId, contract.Id,
                $"{fee.Name} phòng {roomCode} tới {date:dd/MM}: {start.Value:0.##} → {end.Value:0.##}", amount, date, Reason,
                new MeterClosing(meter.Id, start.Id, end.Id, fee.Id, quantity, unitPrice));
            db.RoomCharges.Add(charge);
            if (draft is not null)
                RoomChargeBilling.Attach(draft, charge);
        }
        return Result.Success();
    }
}

/// <summary>CT-BR-48: kỳ áp giá thuê mới khi chuyển phòng — bỏ trống ⇒ theo số ngày đã ở phòng cũ trong kỳ (quá nửa kỳ ⇒ kỳ sau).</summary>
public enum TransferRentFrom
{
    CurrentPeriod,
    NextPeriod
}

/// <param name="OldRoomReadings">Chỉ số cuối các công tơ phòng cũ tại ngày chuyển — bắt buộc.</param>
/// <param name="NewRoomReadings">Chỉ số nhận phòng các công tơ phòng mới; null / value null = số mới nhất.</param>
/// <param name="MonthlyRent">Giá thuê sau khi chuyển; null = giữ giá đang áp (CT-BR-48).</param>
/// <param name="RentFrom">Kỳ áp giá mới — cả kỳ, không chia ngày theo từng phòng; null = tự chọn theo quy tắc nửa kỳ.</param>
/// <param name="RemoveFeeTypeIds">Dịch vụ không dùng ở phòng mới ⇒ thôi tính từ kỳ chưa chốt đầu tiên.</param>
public sealed record TransferRoomCommand(
    Guid ContractId, Guid ToRoomId, DateOnly Date, IReadOnlyList<MeterReadingInput>? OldRoomReadings,
    IReadOnlyList<MeterReadingInput>? NewRoomReadings, decimal? MonthlyRent, string? Note, TransferRentFrom? RentFrom = null,
    IReadOnlyList<Guid>? RemoveFeeTypeIds = null) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class TransferRoomCommandValidator : AbstractValidator<TransferRoomCommand>
{
    public TransferRoomCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.ToRoomId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.Date).ReadingDate(clock);
        RuleFor(x => x.MonthlyRent).OptionalMoney();
        RuleFor(x => x.RentFrom).IsInEnum();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-UC-12 / CT-BR-14: HĐ đi theo người thuê sang phòng trống cùng khu từ ngày D. 1 transaction, khóa phòng (theo id) → HĐ (C-07):
/// chỉ số cuối phòng cũ tại D → khoản chốt điện nước phòng cũ (CT-BR-47) → đổi phòng → chỉ số nhận phòng phòng mới tại D →
/// giá thuê mới theo quy tắc nửa kỳ + bỏ dịch vụ không dùng ở phòng mới (CT-BR-48). Nháp đã lập ⇒ "Cần tính lại".
/// </summary>
public sealed class TransferRoomHandler(IAppDbContext db, ISender sender, IInvoiceLockReader invoiceLocks, TimeProvider clock)
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
        var charged = await RoomTransferClosing.CreateChargesAsync(db, contract, request.Date, cancellationToken);
        if (charged.IsFailure)
            return charged.Error!;
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

        var warnings = new List<Warning> { RoomTransferWarnings.ResidenceRoomChanged };
        warnings.AddRange(await MeterWarningsAsync(fromRoomId.Value, request.ToRoomId, request.Date, cancellationToken));
        var rented = await ApplyRentAsync(contract, request, warnings, cancellationToken);
        if (rented.IsFailure)
            return rented.Error!;
        foreach (var feeTypeId in (request.RemoveFeeTypeIds ?? []).Distinct())
        {
            var removed = await sender.Send(new RemoveContractFeeCommand(contract.Id, feeTypeId, null), cancellationToken);
            if (removed.IsFailure)
                return removed.Error!;
        }
        await transaction.CommitAsync(cancellationToken);
        return warnings;
    }

    /// <summary>
    /// CT-BR-48: giá mới áp cho <b>cả kỳ</b> — kỳ chứa ngày chuyển nếu ở phòng cũ không quá nửa kỳ, ngược lại từ kỳ sau (chủ trọ chọn khác được).
    /// Kỳ được chọn đã chốt tiền phòng ⇒ mốc giá ghi từ kỳ chưa chốt đầu tiên + khoản phát sinh chênh lệch cả kỳ.
    /// </summary>
    private async Task<Result> ApplyRentAsync(Contract contract, TransferRoomCommand request, List<Warning> warnings, CancellationToken ct)
    {
        var room = await db.Rooms.Where(r => r.Id == request.ToRoomId).Select(r => new { r.Code, r.ListedRent }).FirstAsync(ct);
        if (request.MonthlyRent is not { } rent)
        {
            if (room.ListedRent is { } listed && contract.CurrentRent(request.Date) is { } current && listed != current)
                warnings.Add(RoomTransferWarnings.RentUnchanged(current, listed));
            return Result.Success();
        }

        var note = request.Note ?? $"Chuyển sang phòng {room.Code}";
        var schedule = (await PropertyBilling.LoadAsync(db, contract.PropertyId, ct)).Schedule;
        var period = contract.BillingPeriods(schedule, request.Date).LastOrDefault(p => p.Start <= request.Date && request.Date <= p.End);
        if (period is null)
            return rent == contract.CurrentRent(request.Date) ? Result.Success() : await sender.Send(new ChangeRentCommand(contract.Id, null, rent, null, note), ct);

        var daysInOldRoom = request.Date.DayNumber - period.Start.DayNumber;
        var periodDays = period.End.DayNumber - period.Start.DayNumber + 1;
        var fromThisPeriod = request.RentFrom is { } chosen ? chosen == TransferRentFrom.CurrentPeriod : daysInOldRoom * 2 <= periodDays;
        var target = fromThisPeriod ? period.Start : period.End.AddDays(1);
        var firstOpen = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, ct);
        var effectiveFrom = firstOpen is { } open && open > target ? open : target;
        var billedRent = contract.CurrentRent(period.Start);

        if (rent != contract.CurrentRent(effectiveFrom))
        {
            var changed = await sender.Send(new ChangeRentCommand(contract.Id, effectiveFrom, rent, null, note), ct);
            if (changed.IsFailure)
                return changed;
        }
        if (!fromThisPeriod || effectiveFrom == period.Start || billedRent is not { } billed || billed == rent)
            return Result.Success();

        // Kỳ chuyển đã chốt tiền phòng theo giá cũ ⇒ thu / bù phần chênh cả kỳ bằng khoản phát sinh thường (sửa / hủy được).
        var charge = RoomCharge.Create(contract.PropertyId, contract.RoomId, contract.Id,
            rent > billed ? RoomChargeKind.Surcharge : RoomChargeKind.Credit,
            $"Chênh lệch tiền phòng kỳ {period.Start:dd/MM}–{period.End:dd/MM} (chuyển phòng)", Math.Abs(rent - billed), request.Date,
            "Giá phòng mới áp cho cả kỳ chuyển phòng");
        db.RoomCharges.Add(charge);
        if (await RoomChargeBilling.DraftForAsync(db, contract.Id, request.Date, ct) is { } draft)
            RoomChargeBilling.Attach(draft, charge);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>CT-BR-48: phòng cũ có công tơ của một khoản mà phòng mới không có (hoặc ngược lại) ⇒ nhắc đổi dịch vụ tính theo đầu người.</summary>
    private async Task<IReadOnlyList<Warning>> MeterWarningsAsync(Guid oldRoomId, Guid newRoomId, DateOnly date, CancellationToken ct)
    {
        var meters = await db.Meters
            .Where(m => (m.RoomId == oldRoomId || m.RoomId == newRoomId) && m.InstalledDate <= date && (m.RemovedDate == null || m.RemovedDate > date))
            .Select(m => new { m.RoomId, m.FeeTypeId }).ToListAsync(ct);
        var inOld = meters.Where(m => m.RoomId == oldRoomId).Select(m => m.FeeTypeId).ToHashSet();
        var inNew = meters.Where(m => m.RoomId == newRoomId).Select(m => m.FeeTypeId).ToHashSet();
        var differing = inOld.Union(inNew).Where(id => inOld.Contains(id) != inNew.Contains(id)).ToList();
        if (differing.Count == 0)
            return [];

        var names = await db.FeeTypes.Where(f => differing.Contains(f.Id)).OrderBy(f => f.SortOrder).Select(f => new { f.Id, f.Name }).ToListAsync(ct);
        return names.Select(f => inOld.Contains(f.Id) ? RoomTransferWarnings.MeterMissingInNewRoom(f.Name) : RoomTransferWarnings.MeterOnlyInNewRoom(f.Name))
            .ToList();
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

public sealed record CancelRoomTransferCommand(Guid ContractId) : IRequest<Result<IReadOnlyList<Warning>>>;

/// <summary>
/// CT-BR-14: hủy lần chuyển phòng gần nhất (nhập nhầm phòng / ngày / chỉ số). 1 transaction, khóa phòng (theo id) → HĐ → phiếu nháp (C-07):
/// hủy khoản chốt điện nước, hủy chỉ số cuối phòng cũ và các chỉ số của HĐ trên công tơ phòng mới, trả HĐ về phòng cũ.
/// Giá thuê / dịch vụ đã đổi lúc chuyển không tự đổi lại — trả cảnh báo.
/// </summary>
public sealed class CancelRoomTransferHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<CancelRoomTransferCommand, Result<IReadOnlyList<Warning>>>
{
    private const string VoidReason = "Hủy chuyển phòng";

    public async ValueTask<Result<IReadOnlyList<Warning>>> Handle(CancelRoomTransferCommand request, CancellationToken cancellationToken)
    {
        var rooms = await db.Contracts.Where(c => c.Id == request.ContractId)
            .Select(c => new { c.RoomId, Previous = c.RoomMoves.OrderByDescending(m => m.FromDate).Select(m => (Guid?)m.RoomId).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        if (rooms is null)
            return ContractErrors.NotFound;
        if (rooms.Previous is not { } previousRoomId)
            return ContractErrors.TransferNothingToCancel;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { rooms.RoomId, previousRoomId }.Distinct().Order())
            await db.LockForUpdateAsync<Room>(id, cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = (await ContractMutation.LoadAsync(db, request.ContractId, cancellationToken))!;
        if (contract.Status != ContractStatus.Active)
            return ContractErrors.NotActive;
        if (contract.RoomMoves.MaxBy(m => m.FromDate) is not { } last)
            return ContractErrors.TransferNothingToCancel;

        var (oldRoomId, newRoomId, date) = (last.RoomId, contract.RoomId, contract.RoomSince);
        if (await OldRoomTakenAsync(contract.Id, oldRoomId, date, cancellationToken))
            return ContractErrors.TransferRoomUnavailable;
        // Đã lập phiếu (nháp / đã chốt) cho phòng mới từ ngày chuyển ⇒ xóa nháp / hủy phiếu đó trước.
        if (await db.Invoices.AnyAsync(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void && i.RoomId == newRoomId
                && i.PeriodEnd >= date, cancellationToken))
            return ContractErrors.TransferCancelBilled;

        var now = clock.GetUtcNow();
        var charges = await CancelClosingChargesAsync(contract.Id, oldRoomId, date, now, cancellationToken);
        if (charges.IsFailure)
            return charges.Error!;
        var readings = await VoidReadingsAsync(contract.Id, oldRoomId, newRoomId, date, now, cancellationToken);
        if (readings.IsFailure)
            return readings.Error!;
        var restored = contract.CancelLastTransfer();
        if (restored.IsFailure)
            return restored.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success<IReadOnlyList<Warning>>([RoomTransferWarnings.ResidenceRoomChanged, RoomTransferWarnings.CancelledCheckRent]);
    }

    /// <summary>Phòng cũ từ ngày chuyển đã có HĐ khác (hiệu lực / giữ chỗ / lịch sử chuyển phòng) ⇒ không trả HĐ về được.</summary>
    private async Task<bool> OldRoomTakenAsync(Guid contractId, Guid roomId, DateOnly date, CancellationToken ct) =>
        await db.Contracts.AnyAsync(c => c.Id != contractId && c.RoomId == roomId
            && (c.Status == ContractStatus.Draft
                || ((c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                    && (c.ActualEndDate == null || c.ActualEndDate >= date))), ct)
        || await db.Contracts.AnyAsync(c => c.Id != contractId && c.RoomMoves.Any(m => m.RoomId == roomId && m.ToDate >= date), ct);

    /// <summary>Khoản chốt điện nước của lần chuyển: đã vào phiếu đã chốt ⇒ chặn; đang trên nháp ⇒ xóa dòng; rồi hủy khoản.</summary>
    private async Task<Result> CancelClosingChargesAsync(Guid contractId, Guid oldRoomId, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        var charges = await db.RoomCharges
            .Where(c => c.ContractId == contractId && c.MeterId != null && c.RoomId == oldRoomId && c.IncurredOn == date && c.CancelledAt == null)
            .ToListAsync(ct);
        foreach (var charge in charges)
        {
            if (charge.InvoiceId is { } invoiceId)
            {
                await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
                var invoice = await InvoiceAccess.LoadAsync(db, invoiceId, ct);
                if (invoice is not { Status: InvoiceStatus.Draft })
                    return Result.Failure(ContractErrors.TransferCancelBilled);
                var removed = invoice.RemoveChargeLine(charge.Id);
                if (removed.IsFailure)
                    return removed;
            }
            var cancelled = charge.Cancel(VoidReason, now);
            if (cancelled.IsFailure)
                return cancelled;
        }
        return Result.Success();
    }

    /// <summary>Hủy chỉ số cuối phòng cũ tại ngày chuyển và mọi chỉ số của HĐ trên công tơ phòng mới từ ngày chuyển (chưa bị phiếu đã chốt khóa).</summary>
    private async Task<Result> VoidReadingsAsync(Guid contractId, Guid oldRoomId, Guid newRoomId, DateOnly date, DateTimeOffset now, CancellationToken ct)
    {
        var meterRooms = await db.Meters.Where(m => m.RoomId == oldRoomId || m.RoomId == newRoomId).Select(m => new { m.Id, m.RoomId }).ToListAsync(ct);
        var newMeterIds = meterRooms.Where(m => m.RoomId == newRoomId).Select(m => m.Id).ToList();
        var locked = await ReadingLocks.LockedReadingIdsAsync(db, newMeterIds, ct);
        foreach (var meter in await MeterMapping.LockAndLoadAsync(db, meterRooms.Select(m => m.Id), ct))
        {
            var isNewRoom = meter.RoomId == newRoomId;
            var targets = meter.Ordered
                .Where(r => r.ContractId == contractId && (isNewRoom ? r.ReadingDate >= date : r.Kind == ReadingKind.Final && r.ReadingDate == date))
                .ToList();
            if (targets.Any(r => locked.Contains(r.Id)))
                return Result.Failure(ContractErrors.TransferCancelBilled);
            foreach (var reading in targets)
                meter.VoidReading(reading.Id, VoidReason, now);
        }
        return Result.Success();
    }
}
