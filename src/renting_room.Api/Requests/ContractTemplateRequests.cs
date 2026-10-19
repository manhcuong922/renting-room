using renting_room.Domain.Contracts;

namespace renting_room.Requests;

public sealed record UpdateContractTemplateRequest(
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause>? Clauses,
    IReadOnlyList<CustomFieldDefinition>? Fields,
    uint Version,
    bool NoDeposit = false);
