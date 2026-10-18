using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

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
    bool? HasDeposit = null,
    bool? MissingSignedDocument = null,
    bool? DepositNotRefunded = null) : IRequest<PagedResult<ContractSummaryDto>>;

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
        // Thiếu tài liệu: HĐ nháp / đang ở / đang thanh lý chưa có bản ký (giấy / ảnh / PDF).
        if (request.MissingSignedDocument is { } missing)
            query = query.Where(c => (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.HasSignedDocument != missing);
        if (request.HasDeposit is { } hasDeposit)
            query = hasDeposit ? query.Where(c => c.DepositAmount > 0) : query.Where(c => c.DepositAmount == 0);
        // M08 PM-BR-35: HĐ đã kết thúc có cọc mà chưa đánh dấu hoàn trả.
        if (request.DepositNotRefunded == true)
            query = query.Where(c => c.Status == ContractStatus.Ended && c.DepositAmount > 0 && c.DepositStatus == DepositStatus.Holding);
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
