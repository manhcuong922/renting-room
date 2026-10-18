using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record UpdateContractTemplateCommand(Guid Id, ContractTemplateInput Template, uint Version) : IRequest<Result<ContractTemplateDto>>;

public sealed class UpdateContractTemplateCommandValidator : AbstractValidator<UpdateContractTemplateCommand>
{
    public UpdateContractTemplateCommandValidator()
    {
        RuleFor(x => x.Template).NotNull();
        RuleFor(x => x.Template).FlattenedValidator(new ContractTemplateInputValidator());
    }
}

/// <summary>CT-BR-25: sửa mẫu không đổi hợp đồng đã kích hoạt; bản nháp nhận nội dung mới ở lần sửa nháp kế tiếp.</summary>
public sealed class UpdateContractTemplateHandler(IAppDbContext db) : IRequestHandler<UpdateContractTemplateCommand, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(UpdateContractTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (template is null)
            return ContractTemplateErrors.NotFound;

        var input = request.Template;
        var name = input.Name.Trim();
        if (await db.ContractTemplates.AnyAsync(t => t.Name == name && t.Id != template.Id, cancellationToken))
            return ContractTemplateErrors.NameTaken;

        db.SetExpectedVersion(template, request.Version);
        template.Update(name, input.ContractType, input.Title,
            ContractDocumentJson.ToJson(input.Clauses) ?? "[]", ContractDocumentJson.ToJson(input.Fields) ?? "[]", input.NoDeposit);
        await db.SaveChangesAsync(cancellationToken);
        return ContractTemplateDto.From(template);
    }
}
