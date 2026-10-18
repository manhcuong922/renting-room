using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ContractTemplateInput(
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause>? Clauses,
    IReadOnlyList<CustomFieldDefinition>? Fields,
    bool NoDeposit = false);

public sealed record ContractTemplateDto(
    Guid Id,
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause> Clauses,
    IReadOnlyList<CustomFieldDefinition> Fields,
    bool NoDeposit,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string Version)
{
    public static ContractTemplateDto From(ContractTemplate t) => new(
        t.Id, t.Name, t.ContractType, t.Title,
        ContractDocumentJson.Clauses(t.ClausesJson), ContractDocumentJson.Fields(t.FieldDefinitionsJson),
        t.NoDeposit, t.IsArchived, t.CreatedAt, t.Version.ToString());
}

internal sealed class ContractTemplateInputValidator : AbstractValidator<ContractTemplateInput>
{
    public ContractTemplateInputValidator()
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên mẫu");
        RuleFor(x => x.ContractType).IsInEnum();
        RuleFor(x => x.Title).RequiredText(200, "Tiêu đề hợp đồng");
        this.ClausesRules(x => x.Clauses, "clauses");
        this.FieldsRules(x => x.Fields, "fields");
    }
}
