using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Contracts;

public sealed record ListContractsQuery(
    Guid? PropertyId,
    Guid? RoomId,
    Guid? RenterId,
    ContractStatus? Status,
    int? ExpiringWithinDays,
    bool? Overdue,
    string? Search,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    bool? HasDeposit = null) : IRequest<PagedResult<ContractSummaryDto>>;

public sealed class ListContractsQueryValidator : AbstractValidator<ListContractsQuery>
{
    public ListContractsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.ExpiringWithinDays).InclusiveBetween(0, 365).When(x => x.ExpiringWithinDays is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.Search).OptionalText(30);
    }
}

public sealed class ListContractsHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ListContractsQuery, PagedResult<ContractSummaryDto>>
{
    public async ValueTask<PagedResult<ContractSummaryDto>> Handle(ListContractsQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var query = db.Contracts.AsNoTracking();

        if (request.PropertyId is { } propertyId)
            query = query.Where(c => c.PropertyId == propertyId);
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId);
        if (request.RenterId is { } renterId)
            query = query.Where(c => c.RepresentativeRenterId == renterId || c.Occupants.Any(o => o.RenterId == renterId));
        if (request.Status is { } status)
            query = query.Where(c => c.Status == status);
        if (request.ExpiringWithinDays is { } days)
        {
            var until = today.AddDays(days);
            query = query.Where(c => c.Status == ContractStatus.Active && c.EndDate >= today && c.EndDate <= until);
        }
        if (request.Overdue == true)
            query = query.Where(c => c.Status == ContractStatus.Active && c.EndDate < today);
        // CT-BR-27: nhóm hợp đồng không cọc / có cọc.
        if (request.HasDeposit is { } hasDeposit)
            query = hasDeposit ? query.Where(c => c.DepositAmount > 0) : query.Where(c => c.DepositAmount == 0);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = TextNormalizer.NormalizeCode(request.Search);
            query = query.Where(c => c.ContractNo.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.StartDate).ThenBy(c => c.ContractNo)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ContractSummaryDto(
                c.Id,
                c.ContractNo,
                c.Status,
                c.PropertyId,
                db.Properties.Where(p => p.Id == c.PropertyId).Select(p => p.Code).First(),
                c.RoomId,
                db.Rooms.Where(r => r.Id == c.RoomId).Select(r => r.Code).First(),
                c.RepresentativeRenterId,
                db.Renters.Where(r => r.Id == c.RepresentativeRenterId).Select(r => r.FullName).First(),
                c.StartDate,
                c.EndDate,
                c.ActualEndDate,
                c.RentTerms.Where(t => t.EffectiveFrom <= today).OrderByDescending(t => t.EffectiveFrom).Select(t => (decimal?)t.MonthlyRent).FirstOrDefault(),
                c.Occupants.Count(o => o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today)),
                c.Status == ContractStatus.Active && c.EndDate < today,
                c.ContractType,
                c.DepositAmount,
                null))
            .ToListAsync(cancellationToken);

        var flags = await ContractFlagReader.LoadAsync(db, items.Select(i => i.Id), today, cancellationToken);
        items = items.Select(i => i with { Flags = flags.GetValueOrDefault(i.Id, []) }).ToList();
        return new PagedResult<ContractSummaryDto>(items, request.Page, request.PageSize, total);
    }
}

public sealed record GetContractQuery(Guid Id) : IRequest<Result<ContractDetailDto>>;

internal static class ContractMeterWarnings
{
    /// <summary>MT-BR-11: HĐ đang hiệu lực mà phòng chưa có công tơ điện (khu có khoản Điện) ⇒ không tính được tiền điện.</summary>
    public static async Task<IReadOnlyList<Warning>> ForAsync(IAppDbContext db, Contract contract, DateOnly today, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Active)
            return [];
        var electricity = await db.FeeTypes.Where(f => f.PropertyId == contract.PropertyId && f.SystemCode == FeeSystemCodes.Electricity
                && f.ArchivedAt == null)
            .Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
        if (electricity is null || await db.Meters.AnyAsync(m => m.RoomId == contract.RoomId && m.FeeTypeId == electricity
                && (m.RemovedDate == null || m.RemovedDate > today), ct))
            return [];
        return [new Warning("ROOM_WITHOUT_METER", "Phòng chưa có công tơ điện — lắp công tơ (kèm chỉ số) để tính được tiền điện.")];
    }
}

public sealed class GetContractHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetContractQuery, Result<ContractDetailDto>>
{
    private Task<IReadOnlyList<Warning>> MeterWarningsAsync(Contract contract, DateOnly today, CancellationToken ct) =>
        ContractMeterWarnings.ForAsync(db, contract, today, ct);

    public async ValueTask<Result<ContractDetailDto>> Handle(GetContractQuery request, CancellationToken cancellationToken)
    {
        var contract = await db.Contracts.AsNoTracking()
            .Include(c => c.RentTerms).Include(c => c.Occupants).Include(c => c.Assets).Include(c => c.Vehicles).Include(c => c.Fees)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var renterIds = contract.Occupants.Select(o => o.RenterId).Append(contract.RepresentativeRenterId).Distinct().ToList();
        var names = await db.Renters.AsNoTracking().Where(r => renterIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, cancellationToken);
        var propertyCode = await db.Properties.Where(p => p.Id == contract.PropertyId).Select(p => p.Code).FirstAsync(cancellationToken);
        var roomCode = await db.Rooms.Where(r => r.Id == contract.RoomId).Select(r => r.Code).FirstAsync(cancellationToken);
        var today = clock.GetUtcNow().ToBusinessDate();
        var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot);

        return new ContractDetailDto(
            contract.Id, contract.ContractNo, contract.Status, contract.PropertyId, propertyCode, contract.RoomId, roomCode,
            contract.RepresentativeRenterId, names.GetValueOrDefault(contract.RepresentativeRenterId, string.Empty),
            contract.SignedDate, contract.SignedPlace, contract.EffectiveDate, contract.StartDate, contract.EndDate, contract.ActualEndDate,
            contract.NoticeGivenDate, contract.PlannedMoveOutDate, contract.NoticeDays, contract.DepositAmount, contract.DepositTerms,
            new BillingSettingsInput(contract.BillingAnchorDay, contract.ChargeMode, contract.ProrationMode, contract.PaymentDueDays),
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
                    o.GuardianConsent, o.RenterId == contract.ReferenceRenterId)).ToList(),
            contract.Assets.OrderBy(a => a.Name)
                .Select(a => new AssetDto(a.Id, a.Name, a.Quantity, a.ConditionAtHandover, a.ConditionAtReturn, a.ValueEstimate,
                    a.CompensationValue, a.Note)).ToList(),
            contract.Vehicles.OrderBy(v => v.RegisteredFrom)
                .Select(v => new VehicleDto(v.Id, v.RenterId, v.VehicleType, v.PlateNumber, v.BrandColor, v.RegisteredFrom, v.RegisteredTo,
                    v.Note)).ToList(),
            ContractDocumentDto.From(contract),
            ContractWarnings.For(contract, await ContractFeeRules.LoadParkingFeesAsync(db, contract.PropertyId, cancellationToken), today)
                .Concat(await MeterWarningsAsync(contract, today, cancellationToken)).ToList(),
            contract.HouseholdHeadRenterId,
            await ContractFeeRules.ToDtosAsync(db, contract, today, cancellationToken),
            UtilityPriceSnapshotJson.FromJson(contract.UtilityPriceSnapshot)
                ?? UtilityPriceSnapshotJson.Capture(contract, await ContractFeeRules.LoadTypesAsync(db, contract, cancellationToken)),
            contract.Flags(today),
            contract.HoldoverSince,
            contract.HoldoverNote,
            contract.PreviousContractId,
            contract.Version.ToString());
    }
}

/// <summary>Danh sách kỳ thu (C-05) — để UI chọn ngày áp dụng giá mới, xem lịch thu.</summary>
public sealed record GetBillingPeriodsQuery(Guid ContractId, DateOnly? Until) : IRequest<Result<IReadOnlyList<BillingPeriod>>>;

public sealed class GetBillingPeriodsHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<GetBillingPeriodsQuery, Result<IReadOnlyList<BillingPeriod>>>
{
    private const int DefaultMonthsAhead = 12;

    public async ValueTask<Result<IReadOnlyList<BillingPeriod>>> Handle(GetBillingPeriodsQuery request, CancellationToken cancellationToken)
    {
        var contract = await db.Contracts.AsNoTracking()
            .Where(c => c.Id == request.ContractId)
            .Select(c => new { c.StartDate, c.EndDate, c.ActualEndDate, c.BillingAnchorDay })
            .FirstOrDefaultAsync(cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var until = request.Until
            ?? contract.ActualEndDate
            ?? (contract.EndDate is { } end && end >= today ? end : today.AddMonths(DefaultMonthsAhead));
        var maxUntil = contract.StartDate.AddYears(10);
        if (until > maxUntil)
            until = maxUntil;

        // Chỉ ngày trả phòng thực tế mới cắt kỳ: hết hạn mà chưa thanh lý thì vẫn tiếp tục thu (CT-BR-03).
        var periods = BillingPeriodCalculator.Periods(contract.StartDate, contract.ActualEndDate, contract.BillingAnchorDay, until);
        return Result.Success(periods);
    }
}
