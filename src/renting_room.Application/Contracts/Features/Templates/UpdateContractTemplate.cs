using FluentValidation;
using Mediator;
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
public sealed class UpdateContractTemplateHandler(IContractTemplateRepository templates, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateContractTemplateCommand, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(UpdateContractTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await templates.GetForUpdateAsync(request.Id, cancellationToken);
        if (template is null)
            return ContractTemplateErrors.NotFound;

        var input = request.Template;
        var name = input.Name.Trim();
        if (await templates.IsNameTakenAsync(name, template.Id, cancellationToken))
            return ContractTemplateErrors.NameTaken;

        unitOfWork.SetExpectedVersion(template, request.Version);
        template.Update(name, input.ContractType, input.Title,
            ContractDocumentJson.ToJson(input.Clauses) ?? "[]", ContractDocumentJson.ToJson(input.Fields) ?? "[]", input.NoDeposit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ContractTemplateDto.From(template);
    }
}
