using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

// Truy vấn
public sealed record GetContractTemplateQuery(Guid Id) : IRequest<Result<ContractTemplateDto>>;

public sealed class GetContractTemplateHandler(IAppDbContext db) : IRequestHandler<GetContractTemplateQuery, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(GetContractTemplateQuery request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        return template is null ? ContractTemplateErrors.NotFound : ContractTemplateDto.From(template);
    }
}
