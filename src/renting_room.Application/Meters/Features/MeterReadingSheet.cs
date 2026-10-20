using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record SheetReadingDto(Guid Id, ReadingKind Kind, DateOnly ReadingDate, decimal Value);

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
    bool Locked,
    decimal? RecentAverage = null,
    string? UsageWarning = null,
    SheetInvoiceDto? Invoice = null);

/// <summary>Hóa đơn của HĐ cho kỳ đang ghi (M06 §3.4) — null = chưa lập.</summary>
public sealed record SheetInvoiceDto(Guid Id, InvoiceStatus Status, bool IsStale);

public sealed record MeterReadingSheetDto(string BillingMonth, IReadOnlyList<MeterReadingSheetRow> Rows);

internal static class MeterPeriodLocks
{
    /// <summary>Lỗi nếu <paramref name="date"/> chưa qua kỳ đã chốt của công tơ.</summary>
    public static Error? Check(IReadOnlyDictionary<Guid, DateOnly> locks, Guid meterId, DateOnly date) =>
        locks.TryGetValue(meterId, out var until) && date <= until ? MeterErrors.PeriodLocked(until) : null;
}

public sealed record GetMeterReadingSheetQuery(Guid PropertyId, string BillingMonth, string? Floor, Guid? FeeTypeId = null)
    : IRequest<Result<MeterReadingSheetDto>>;

public sealed class GetMeterReadingSheetQueryValidator : AbstractValidator<GetMeterReadingSheetQuery>
{
    public GetMeterReadingSheetQueryValidator(TimeProvider clock) => RuleFor(x => x.BillingMonth).BillingMonth(clock);
}

public sealed class GetMeterReadingSheetHandler(
    IPropertyQueries properties, IRoomQueries roomQueries, IContractQueries contractQueries, IMeterQueries meterQueries, IFeeQueries feeQueries,
    IRenterQueries renters, IInvoiceQueries invoices)
    : IRequestHandler<GetMeterReadingSheetQuery, Result<MeterReadingSheetDto>>
{
    public async ValueTask<Result<MeterReadingSheetDto>> Handle(GetMeterReadingSheetQuery request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var month = BillingMonths.Parse(request.BillingMonth)!.Value;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var rooms = (await roomQueries.ListByPropertyAsync(request.PropertyId, request.Floor, cancellationToken)).ToDictionary(r => r.Id);
        var roomIds = rooms.Keys.ToList();
        var contracts = await contractQueries.ListForMeterSheetAsync(roomIds, monthEnd, cancellationToken);
        var meters = await meterQueries.ListByRoomsAsync(roomIds, cancellationToken);
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var fees = await feeQueries.GetTypesAsync(feeIds, cancellationToken);
        var names = await renters.GetNamesAsync(contracts.Select(c => c.RepresentativeRenterId).Distinct().ToList(), cancellationToken);
        var locked = await invoices.GetLockedReadingIdsAsync(meters.Select(m => m.Id).ToList(), cancellationToken);
        var contractIds = contracts.Select(c => c.Id).ToList();
        var segments = await invoices.ListSegmentEndsAsync(contractIds, cancellationToken);
        var billed = (await invoices.ListSheetInvoicesAsync(contractIds, cancellationToken))
            .ToDictionary(x => (x.ContractId, x.PeriodStart), x => new SheetInvoiceDto(x.Id, x.Status, x.IsStale));

        var schedule = (await properties.GetBillingAsync(request.PropertyId, cancellationToken)).Schedule;
        var history = (await invoices.ListMeteredUsageAsync(contractIds, cancellationToken))
            .ToLookup(x => (x.ContractId, x.FeeTypeId));
        var rows = new List<MeterReadingSheetRow>();
        foreach (var contract in contracts)
        {
            var period = BillingMonths.PeriodIn(contract, month, schedule);
            var usage = period is null ? null : UsagePeriod.For(contract, period, schedule);
            if (usage is null || contract.ActualEndDate <= usage.End)
                continue;
            var lastEnd = segments.Where(x => x.ContractId == contract.Id && x.PeriodStart < period!.Start)
                .GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.PeriodStart)!.EndReadingId);
            var room = rooms[contract.RoomId];
            var stay = contract.RoomStays[^1];
            if (stay.From > usage.End)
                continue;

            foreach (var meter in meters.Where(m => m.RoomId == contract.RoomId && m.Overlaps(usage.Start, usage.End)
                         && !(m.RemovedDate <= usage.End) && (request.FeeTypeId is null || m.FeeTypeId == request.FeeTypeId)))
            {
                var previous = usage.StartReading(meter, lastEnd.TryGetValue(meter.Id, out var id) ? id : null, contract.Id, stay);
                var current = usage.EndReading(meter, contract.Id);
                var fee = fees[meter.FeeTypeId];
                var recent = history[(contract.Id, fee.Id)].Where(x => x.PeriodStart < period!.Start)
                    .OrderByDescending(x => x.PeriodStart).Select(x => x.Quantity).ToList();
                decimal? consumption = previous is not null && current is not null ? current.Value - previous.Value : null;
                rows.Add(new MeterReadingSheetRow(room.Id, room.Code, room.Floor, contract.Id, contract.ContractNo,
                    names.GetValueOrDefault(contract.RepresentativeRenterId, string.Empty), meter.Id, meter.SerialNo, fee.Id, fee.Name, fee.Unit,
                    usage.Start, usage.End, usage.ClosingPeriodStart, usage.EndsWithFinal, ToDto(previous), ToDto(current),
                    consumption,
                    current is not null && locked.Contains(current.Id),
                    UsageAnomaly.RecentAverage(recent),
                    consumption is { } used ? UsageAnomaly.Check(fee.Name, fee.Unit, used, recent, contract.OccupantsOn(usage.End).Any()) : null,
                    billed.GetValueOrDefault((contract.Id, period!.Start))));
            }
        }
        return new MeterReadingSheetDto(request.BillingMonth, rows.OrderBy(r => r.Floor).ThenBy(r => r.RoomCode).ThenBy(r => r.FeeTypeName).ToList());
    }

    private static SheetReadingDto? ToDto(MeterReading? r) => r is null ? null : new SheetReadingDto(r.Id, r.Kind, r.ReadingDate, r.Value);
}

public sealed record PeriodicReadingInput(Guid MeterId, Guid ContractId, DateOnly ClosingPeriodStart, DateOnly ReadingDate, decimal Value);

public sealed record ReadingRowError(int Index, string Code, string Message);

public sealed record ReadingRowWarning(int Index, string Code, string Message);

public sealed record SaveMeterReadingsResult(int Saved, IReadOnlyList<ReadingRowWarning>? Warnings = null);

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

public sealed class SaveMeterReadingsHandler(
    IMeterRepository meterRepository, IContractQueries contractQueries, IInvoiceQueries invoices, IPropertyQueries properties, IUnitOfWork unitOfWork,
    MeterDraftRefresher drafts)
    : IRequestHandler<SaveMeterReadingsCommand, Result<SaveMeterReadingsResult>>
{
    public async ValueTask<Result<SaveMeterReadingsResult>> Handle(SaveMeterReadingsCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var affected = await drafts.LockContractsAsync(request.Readings.Select(r => r.MeterId).Distinct().ToList(), cancellationToken);
        var meters = (await meterRepository.LockAndLoadAsync(request.Readings.Select(r => r.MeterId), cancellationToken))
            .ToDictionary(m => m.Id);
        var contractIds = request.Readings.Select(r => r.ContractId).Distinct().ToList();
        var contracts = await contractQueries.ListWithRentTermsAsync(contractIds, cancellationToken);
        var locked = await invoices.GetLockedReadingIdsAsync(meters.Keys.ToList(), cancellationToken);
        var billing = await properties.GetBillingsAsync(contracts.Values.Select(c => c.PropertyId), cancellationToken);

        var errors = new List<ReadingRowError>();
        var warnings = new List<ReadingRowWarning>();
        for (var index = 0; index < request.Readings.Count; index++)
        {
            var row = request.Readings[index];
            var contract = contracts.GetValueOrDefault(row.ContractId);
            var error = Apply(row, meters.GetValueOrDefault(row.MeterId), contract,
                contract is null ? null : billing[contract.PropertyId].Schedule, locked);
            if (error is not null)
                errors.Add(new ReadingRowError(index, error.Code, error.Message));
            else if (DateWarning(row, contract!, billing[contract!.PropertyId].Schedule) is { } warning)
                warnings.Add(new ReadingRowWarning(index, DateFarFromPeriodEnd, warning));
        }
        if (errors.Count > 0)
            return Error.BusinessRule("READINGS_INVALID", "Có dòng chỉ số không hợp lệ — chưa lưu dòng nào.").WithDetail("rowErrors", errors);

        await drafts.RecalculateAsync(affected, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaveMeterReadingsResult(request.Readings.Count, warnings);
    }

    public const string DateFarFromPeriodEnd = "READING_DATE_FAR_FROM_PERIOD_END";
    private const int DaysBeforePeriodEnd = 7;
    private const int DaysAfterPeriodEnd = 15;

    private static string? DateWarning(PeriodicReadingInput row, Contract contract, BillingSchedule schedule)
    {
        var period = contract.BillingPeriods(schedule, row.ClosingPeriodStart).FirstOrDefault(p => p.Start == row.ClosingPeriodStart);
        if (period is null || (row.ReadingDate >= period.End.AddDays(-DaysBeforePeriodEnd) && row.ReadingDate <= period.End.AddDays(DaysAfterPeriodEnd)))
            return null;
        return $"Ngày ghi {row.ReadingDate:dd/MM/yyyy} lệch xa ngày cuối kỳ {period.End:dd/MM/yyyy} — kiểm tra lại có ghi nhầm kỳ không.";
    }

    private static Error? Apply(PeriodicReadingInput row, Meter? meter, Contract? contract, BillingSchedule? schedule, HashSet<Guid> locked)
    {
        if (meter is null || contract is null || meter.PropertyId != contract.PropertyId || meter.RoomId != contract.RoomId)
            return MeterErrors.UnknownMeter;
        if (contract.Status is not (ContractStatus.Active or ContractStatus.Liquidating or ContractStatus.Ended))
            return ContractErrors.NotActive;
        if (!contract.IsPeriodStart(schedule!, row.ClosingPeriodStart))
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
