using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ListContractTemplatesQuery(ContractType? Type, bool IncludeArchived) : IRequest<IReadOnlyList<ContractTemplateDto>>;

public sealed class ListContractTemplatesHandler(IAppDbContext db) : IRequestHandler<ListContractTemplatesQuery, IReadOnlyList<ContractTemplateDto>>
{
    public async ValueTask<IReadOnlyList<ContractTemplateDto>> Handle(ListContractTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.ContractTemplates.AsNoTracking();
        if (request.Type is { } type)
            query = query.Where(t => t.ContractType == type);
        if (!request.IncludeArchived)
            query = query.Where(t => t.ArchivedAt == null);

        var templates = await query.OrderBy(t => t.ContractType).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return templates.Select(ContractTemplateDto.From).ToList();
    }
}
