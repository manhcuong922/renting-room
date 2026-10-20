namespace renting_room.Domain.Contracts;

public sealed record ContractDocument(
    Guid? TemplateId,
    ContractType ContractType,
    string Title,
    string? ClausesJson,
    string? FieldDefinitionsJson,
    string? FieldValuesJson)
{
    public static ContractDocument Default(ContractType type = ContractType.RoomRental) =>
        new(null, type, ContractTypes.DefaultTitle(type), null, null, null);
}
