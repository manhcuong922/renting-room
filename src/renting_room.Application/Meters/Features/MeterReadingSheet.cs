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

/// <param name="ClosingPeriodStart">Gửi lại khi lưu chỉ số (khóa chỉ số cuối kỳ — MT-BR-04).</param>
/// <param name="Previous">Chỉ số cũ (số cuối phiếu trước / nhận phòng / tháng trước).</param>
/// <param name="Current">Chỉ số cuối kỳ đã nhập (null = chưa nhập).</param>
/// <param name="Locked">Chỉ số đã dùng cho phiếu đã chốt — muốn sửa phải hủy phiếu (MT-BR-06).</param>
/// <param name="RecentAverage">MT-BR-08: trung bình 3 kỳ trước của HĐ (null = chưa đủ 3 kỳ) — UI cảnh báo ngay khi nhập ≥ 3 lần và tăng ≥ 50.</param>
/// <param name="UsageWarning">MT-BR-08: cảnh báo bất thường với số đã nhập (không chặn lưu).</param>
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
    string? UsageWarning = null);

public sealed record MeterReadingSheetDto(string BillingMonth, IReadOnlyList<MeterReadingSheetRow> Rows);

/// <summary>
/// MT-BR-09/10 (17/10/2026): công tơ đã có đoạn đo trên phiếu đã chốt ⇒ thay / tháo phải sau ngày cuối kỳ sử dụng của phiếu đó, nếu không
/// phần sản lượng từ chỉ số cuối kỳ tới số tháo không phiếu nào tính (kỳ sau bỏ qua công tơ đã tháo trước đầu kỳ).
/// </summary>
internal static class MeterPeriodLocks
{
    /// <summary>Lỗi nếu <paramref name="date"/> chưa qua kỳ đã chốt của công tơ.</summary>
    public static Error? Check(IReadOnlyDictionary<Guid, DateOnly> locks, Guid meterId, DateOnly date) =>
        locks.TryGetValue(meterId, out var until) && date <= until ? MeterErrors.PeriodLocked(until) : null;
}

/// <param name="FeeTypeId">Chỉ các công tơ của khoản thu này (VD đi ghi riêng điện).</param>
public sealed record GetMeterReadingSheetQuery(Guid PropertyId, string BillingMonth, string? Floor, Guid? FeeTypeId = null)
    : IRequest<Result<MeterReadingSheetDto>>;

public sealed class GetMeterReadingSheetQueryValidator : AbstractValidator<GetMeterReadingSheetQuery>
{
    public GetMeterReadingSheetQueryValidator(TimeProvider clock) => RuleFor(x => x.BillingMonth).BillingMonth(clock);
}

/// <summary>
/// MT-UC-04 / §3.4: mỗi dòng = (phòng, HĐ, công tơ) có kỳ sử dụng ứng với tháng thu — Postpaid: kỳ của tháng; Prepaid: kỳ liền trước.
/// Công tơ đã tháo trong kỳ không cần nhập (số tháo là số cuối).
/// </summary>
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

        var schedule = (await properties.GetBillingAsync(request.PropertyId, cancellationToken)).Schedule;
        var history = (await invoices.ListMeteredUsageAsync(contractIds, cancellationToken))
            .ToLookup(x => (x.ContractId, x.FeeTypeId));
        var rows = new List<MeterReadingSheetRow>();
        foreach (var contract in contracts)
        {
            var period = BillingMonths.PeriodIn(contract, month, schedule);
            var usage = period is null ? null : UsagePeriod.For(contract, period, schedule);
            // HĐ trả phòng trong kỳ sử dụng ⇒ chỉ số cuối nhập khi lập phiếu quyết toán, không qua lưới.
            if (usage is null || contract.ActualEndDate <= usage.End)
                continue;
            // Chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy kỳ trước (MT-BR-12) — như khi tính phiếu.
            var lastEnd = segments.Where(x => x.ContractId == contract.Id && x.PeriodStart < period!.Start)
                .GroupBy(x => x.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.PeriodStart)!.EndReadingId);
            var room = rooms[contract.RoomId];
            // CT-BR-47: chuyển tới phòng này giữa kỳ ⇒ chỉ số cũ = chỉ số nhận phòng của HĐ (công tơ phòng cũ đã có chỉ số cuối lúc chuyển).
            var stay = contract.RoomStays[^1];
            // Chuyển tới phòng này sau khi kỳ sử dụng kết thúc (thu trước) ⇒ công tơ phòng này chưa có gì để tính cho kỳ đó.
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
                    consumption is { } used ? UsageAnomaly.Check(fee.Name, fee.Unit, used, recent, contract.OccupantsOn(usage.End).Any()) : null));
            }
        }
        return new MeterReadingSheetDto(request.BillingMonth, rows.OrderBy(r => r.Floor).ThenBy(r => r.RoomCode).ThenBy(r => r.FeeTypeName).ToList());
    }

    private static SheetReadingDto? ToDto(MeterReading? r) => r is null ? null : new SheetReadingDto(r.Id, r.Kind, r.ReadingDate, r.Value);
}

public sealed record PeriodicReadingInput(Guid MeterId, Guid ContractId, DateOnly ClosingPeriodStart, DateOnly ReadingDate, decimal Value);

public sealed record ReadingRowError(int Index, string Code, string Message);

/// <summary>MT-BR-05: ngày ghi lệch xa ngày cuối kỳ sử dụng — không chặn lưu.</summary>
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

/// <summary>
/// MT-UC-04 / MT-BR-07: lưu chỉ số cuối kỳ hàng loạt — có dòng lỗi thì không lưu dòng nào (422 READINGS_INVALID kèm <c>rowErrors</c>).
/// Đã có chỉ số của kỳ đó ⇒ sửa giá trị (chưa khóa). Khóa công tơ theo id (C-07).
/// </summary>
public sealed class SaveMeterReadingsHandler(
    IMeterRepository meterRepository, IContractQueries contractQueries, IInvoiceQueries invoices, IPropertyQueries properties, IUnitOfWork unitOfWork)
    : IRequestHandler<SaveMeterReadingsCommand, Result<SaveMeterReadingsResult>>
{
    public async ValueTask<Result<SaveMeterReadingsResult>> Handle(SaveMeterReadingsCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
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

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SaveMeterReadingsResult(request.Readings.Count, warnings);
    }

    public const string DateFarFromPeriodEnd = "READING_DATE_FAR_FROM_PERIOD_END";
    private const int DaysBeforePeriodEnd = 7;
    private const int DaysAfterPeriodEnd = 15;

    /// <summary>MT-BR-05: chỉ số cuối kỳ nên ghi quanh ngày cuối kỳ (trước 7 ngày – sau 15 ngày); lệch xa thì nhắc kiểm lại kỳ.</summary>
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
