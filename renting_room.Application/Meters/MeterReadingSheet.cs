using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record SheetReadingDto(Guid Id, ReadingKind Kind, DateOnly ReadingDate, decimal Value);

/// <param name="ClosingPeriodStart">Gửi lại khi lưu chỉ số (khóa chỉ số cuối kỳ — MT-BR-04).</param>
/// <param name="Previous">Chỉ số cũ (số cuối phiếu trước / nhận phòng / tháng trước).</param>
/// <param name="Current">Chỉ số cuối kỳ đã nhập (null = chưa nhập).</param>
/// <param name="Locked">Chỉ số đã dùng cho phiếu đã chốt — muốn sửa phải hủy phiếu (MT-BR-06).</param>
public sealed record MeterReadingSheetRow(
    Guid RoomId,
    string RoomCode,
    string? Floor,
    Guid ContractId,
    string ContractNo,
    string RepresentativeName,
    Guid MeterId,
    string? SerialNo,
    Guid FeeTypeId,
    string FeeTypeName,
    string Unit,
    DateOnly UsageStart,
    DateOnly UsageEnd,
    DateOnly ClosingPeriodStart,
    bool EndsWithFinal,
    SheetReadingDto? Previous,
    SheetReadingDto? Current,
    decimal? Consumption,
    bool Locked);

public sealed record MeterReadingSheetDto(string BillingMonth, IReadOnlyList<MeterReadingSheetRow> Rows);

internal static class ReadingLocks
{
    /// <summary>MT-BR-06: chỉ số là đầu / cuối của đoạn đo trên phiếu đã chốt (chưa hủy).</summary>
    public static async Task<HashSet<Guid>> LockedReadingIdsAsync(IAppDbContext db, IReadOnlyCollection<Guid> meterIds, CancellationToken ct)
    {
        var segments = await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Segments)
            .Where(s => meterIds.Contains(s.MeterId) && !s.Voided)
            .Select(s => new { s.StartReadingId, s.EndReadingId })
            .ToListAsync(ct);
        return segments.SelectMany(s => new[] { s.StartReadingId, s.EndReadingId }).ToHashSet();
    }
}

// ============================================================ Lưới ghi chỉ số

public sealed record GetMeterReadingSheetQuery(Guid PropertyId, string BillingMonth, string? Floor) : IRequest<Result<MeterReadingSheetDto>>;

public sealed class GetMeterReadingSheetQueryValidator : AbstractValidator<GetMeterReadingSheetQuery>
{
    public GetMeterReadingSheetQueryValidator(TimeProvider clock) => RuleFor(x => x.BillingMonth).BillingMonth(clock);
}

/// <summary>
/// MT-UC-04 / §3.4: mỗi dòng = (phòng, HĐ, công tơ) có kỳ sử dụng ứng với tháng thu — Postpaid: kỳ của tháng; Prepaid: kỳ liền trước.
/// Công tơ đã tháo trong kỳ không cần nhập (số tháo là số cuối).
/// </summary>
public sealed class GetMeterReadingSheetHandler(IAppDbContext db) : IRequestHandler<GetMeterReadingSheetQuery, Result<MeterReadingSheetDto>>
{
    public async ValueTask<Result<MeterReadingSheetDto>> Handle(GetMeterReadingSheetQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var month = BillingMonths.Parse(request.BillingMonth)!.Value;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.PropertyId == request.PropertyId
                && (string.IsNullOrWhiteSpace(request.Floor) || r.Floor == request.Floor.Trim()))
            .ToDictionaryAsync(r => r.Id, cancellationToken);
        var roomIds = rooms.Keys.ToList();
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.RentTerms).Include(c => c.Occupants).AsSplitQuery()
            .Where(c => roomIds.Contains(c.RoomId)
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.StartDate <= monthEnd)
            .ToListAsync(cancellationToken);
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => roomIds.Contains(m.RoomId)).ToListAsync(cancellationToken);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await db.FeeTypes.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var names = await RenterNamesAsync(contracts.Select(c => c.RepresentativeRenterId), cancellationToken);
        var locked = await ReadingLocks.LockedReadingIdsAsync(db, meters.Select(m => m.Id).ToList(), cancellationToken);
        var contractIds = contracts.Select(c => c.Id).ToList();
        var segments = await db.Invoices.AsNoTracking()
            .Where(i => contractIds.Contains(i.ContractId) && i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Segments.Select(s => new { i.ContractId, i.PeriodStart, s.MeterId, s.EndReadingId }))
            .ToListAsync(cancellationToken);

        var rows = new List<MeterReadingSheetRow>();
        foreach (var contract in contracts)
        {
            var period = BillingMonths.PeriodIn(contract, month);
            var usage = period is null ? null : UsagePeriod.For(contract, period);
            // HĐ trả phòng trong kỳ sử dụng ⇒ chỉ số cuối nhập khi lập phiếu quyết toán, không qua lưới.
            if (usage is null || contract.ActualEndDate <= usage.End)
                continue;
            // Chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy kỳ trước (MT-BR-12) — như khi tính phiếu.
            var lastEnd = segments.Where(x => x.ContractId == contract.Id && x.PeriodStart < period!.Start)
                .GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.PeriodStart)!.EndReadingId);
            var room = rooms[contract.RoomId];

            foreach (var meter in meters.Where(m => m.RoomId == contract.RoomId && m.Overlaps(usage.Start, usage.End)
                         && !(m.RemovedDate <= usage.End)))
            {
                var previous = usage.StartReading(meter, lastEnd.TryGetValue(meter.Id, out var id) ? id : null);
                var current = usage.EndReading(meter, contract.Id);
                var fee = fees[meter.FeeTypeId];
                rows.Add(new MeterReadingSheetRow(room.Id, room.Code, room.Floor, contract.Id, contract.ContractNo,
                    names.GetValueOrDefault(contract.RepresentativeRenterId, string.Empty), meter.Id, meter.SerialNo, fee.Id, fee.Name, fee.Unit,
                    usage.Start, usage.End, usage.ClosingPeriodStart, usage.EndsWithFinal, ToDto(previous), ToDto(current),
                    previous is not null && current is not null ? current.Value - previous.Value : null,
                    current is not null && locked.Contains(current.Id)));
            }
        }
        return new MeterReadingSheetDto(request.BillingMonth, rows.OrderBy(r => r.Floor).ThenBy(r => r.RoomCode).ThenBy(r => r.FeeTypeName).ToList());
    }

    private static SheetReadingDto? ToDto(MeterReading? r) => r is null ? null : new SheetReadingDto(r.Id, r.Kind, r.ReadingDate, r.Value);

    private Task<Dictionary<Guid, string>> RenterNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return db.Renters.AsNoTracking().Where(r => list.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.FullName, ct);
    }
}

// ============================================================ Lưu hàng loạt

public sealed record PeriodicReadingInput(Guid MeterId, Guid ContractId, DateOnly ClosingPeriodStart, DateOnly ReadingDate, decimal Value);

public sealed record ReadingRowError(int Index, string Code, string Message);

public sealed record SaveMeterReadingsResult(int Saved);

public sealed record SaveMeterReadingsCommand(Guid PropertyId, IReadOnlyList<PeriodicReadingInput> Readings) : IRequest<Result<SaveMeterReadingsResult>>;

public sealed class SaveMeterReadingsCommandValidator : AbstractValidator<SaveMeterReadingsCommand>
{
    public SaveMeterReadingsCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Readings).NotEmpty().Must(r => r.Count <= 1000).WithErrorCode("OUT_OF_RANGE")
            .Must(r => r.Select(x => (x.MeterId, x.ContractId, x.ClosingPeriodStart)).Distinct().Count() == r.Count)
            .WithErrorCode("DUPLICATE_READING").WithMessage("Mỗi (công tơ, hợp đồng, kỳ) chỉ 1 dòng.");
        RuleForEach(x => x.Readings).ChildRules(r =>
        {
            r.RuleFor(x => x.Value).ReadingValue();
            r.RuleFor(x => x.ReadingDate).ReadingDate(clock);
        });
    }
}

/// <summary>
/// MT-UC-04 / MT-BR-07: lưu chỉ số cuối kỳ hàng loạt — có dòng lỗi thì không lưu dòng nào (422 READINGS_INVALID kèm <c>rowErrors</c>).
/// Đã có chỉ số của kỳ đó ⇒ sửa giá trị (chưa khóa). Khóa công tơ theo id (C-07).
/// </summary>
public sealed class SaveMeterReadingsHandler(IAppDbContext db) : IRequestHandler<SaveMeterReadingsCommand, Result<SaveMeterReadingsResult>>
{
    public async ValueTask<Result<SaveMeterReadingsResult>> Handle(SaveMeterReadingsCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var meters = (await MeterMapping.LockAndLoadAsync(db, request.Readings.Select(r => r.MeterId), cancellationToken))
            .ToDictionary(m => m.Id);
        var contractIds = request.Readings.Select(r => r.ContractId).Distinct().ToList();
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.RentTerms).Where(c => contractIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var locked = await ReadingLocks.LockedReadingIdsAsync(db, meters.Keys.ToList(), cancellationToken);

        var errors = new List<ReadingRowError>();
        for (var index = 0; index < request.Readings.Count; index++)
        {
            var row = request.Readings[index];
            var error = Apply(row, meters.GetValueOrDefault(row.MeterId), contracts.GetValueOrDefault(row.ContractId), locked);
            if (error is not null)
                errors.Add(new ReadingRowError(index, error.Code, error.Message));
        }
        if (errors.Count > 0)
            return Error.BusinessRule("READINGS_INVALID", "Có dòng chỉ số không hợp lệ — chưa lưu dòng nào.").WithDetail("rowErrors", errors);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaveMeterReadingsResult(request.Readings.Count);
    }

    private static Error? Apply(PeriodicReadingInput row, Meter? meter, Contract? contract, HashSet<Guid> locked)
    {
        if (meter is null || contract is null || meter.PropertyId != contract.PropertyId || meter.RoomId != contract.RoomId)
            return MeterErrors.UnknownMeter;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating or ContractStatus.Ended))
            return ContractErrors.NotActive;
        if (!contract.BillingPeriods(row.ClosingPeriodStart).Any(p => p.Start == row.ClosingPeriodStart))
            return Error.Validation("NOT_PERIOD_START", "Kỳ không hợp lệ với hợp đồng — tải lại lưới.");

        var existing = meter.FindPeriodic(contract.Id, row.ClosingPeriodStart);
        if (existing is not null)
        {
            if (locked.Contains(existing.Id))
                return MeterErrors.ReadingLocked;
            var corrected = meter.Correct(existing.Id, row.Value, null);
            return corrected.IsFailure ? corrected.Error : null;
        }
        var recorded = meter.Record(ReadingKind.Periodic, row.ReadingDate, row.Value, contract.Id, null, row.ClosingPeriodStart);
        return recorded.IsFailure ? recorded.Error : null;
    }
}
