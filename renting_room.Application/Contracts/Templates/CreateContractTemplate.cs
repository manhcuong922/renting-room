using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record CreateContractTemplateCommand(ContractTemplateInput Template) : IRequest<Result<Guid>>;

public sealed class CreateContractTemplateCommandValidator : AbstractValidator<CreateContractTemplateCommand>
{
    // Body gửi phẳng ⇒ key lỗi không có tiền tố "template.".
    public CreateContractTemplateCommandValidator()
    {
        RuleFor(x => x.Template).NotNull();
        RuleFor(x => x.Template).FlattenedValidator(new ContractTemplateInputValidator());
    }
}

public sealed class CreateContractTemplateHandler(IAppDbContext db) : IRequestHandler<CreateContractTemplateCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateContractTemplateCommand request, CancellationToken cancellationToken)
    {
        var input = request.Template;
        var name = input.Name.Trim();
        if (await db.ContractTemplates.AnyAsync(t => t.Name == name, cancellationToken))
            return ContractTemplateErrors.NameTaken;

        var template = ContractTemplate.Create(name, input.ContractType, input.Title,
            ContractDocumentJson.ToJson(input.Clauses) ?? "[]", ContractDocumentJson.ToJson(input.Fields) ?? "[]", input.NoDeposit);
        db.ContractTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return template.Id;
    }
}
