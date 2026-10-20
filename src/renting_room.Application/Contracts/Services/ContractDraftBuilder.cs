using FluentValidation;
using renting_room.Application.Fees;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

public sealed class ContractDraftBuilder(IContractTemplateQueries templates, OccupantChecks occupantChecks, IFeeQueries fees)
{
    public static readonly PaymentMethod[] DefaultPaymentMethods = [PaymentMethod.Cash, PaymentMethod.BankTransfer];
    public const int DefaultCopies = 2;
    public const int MaxDepositMonths = 12;

    public async Task<Result<ContractDraftData>> BuildAsync(
        ContractInput input, Room room, Property property, Guid? currentTemplateId, CancellationToken ct)
    {
        var template = await LoadTemplateAsync(input.TemplateId, currentTemplateId, ct);
        if (template.IsFailure)
            return template.Error!;

        var noDeposit = template.Value?.NoDeposit == true;
        if (noDeposit && input.DepositAmount is > 0)
            return ContractTemplateErrors.DepositNotAllowed;
        var deposit = noDeposit ? 0 : input.DepositAmount ?? room.DefaultDeposit ?? 0;
        var document = BuildDocument(input, template.Value);

        var reference = input.HouseholdHeadRenterId ?? input.RepresentativeRenterId;
        if (input.HouseholdHeadRenterId is { } head && input.Occupants?.Any(o => o.RenterId == head) != true)
            OccupantChecks.ThrowIfInvalid(
                [new OccupantRuleViolation(-1, "householdHeadRenterId", ContractErrors.HouseholdHeadNotOccupant)],
                v => $"contract.{v.Field}");

        var occupants = (input.Occupants ?? [])
            .Select(o => new OccupantInput(o.RenterId, o.MoveInDate ?? input.StartDate, o.ExpectedEndDate, o.Relationship, o.Note,
                o.RenterId == reference ? null : o.RelationshipType, o.GuardianConsent ?? false,
                o.RenterId == input.RepresentativeRenterId ? OccupantRole.Resident : o.Role ?? OccupantRole.Resident))
            .ToList();
        var people = await occupantChecks.LoadPeopleAsync(occupants.Select(o => o.RenterId).Append(input.RepresentativeRenterId), ct);
        if (occupants.Any(o => !people.ContainsKey(o.RenterId)) || !people.ContainsKey(input.RepresentativeRenterId))
            return RenterErrors.RenterNotFound;
        if (!await occupantChecks.HasIdNumberAsync(input.RepresentativeRenterId, ct))
            return ContractErrors.RepresentativeIdRequired;
        if (!await occupantChecks.AllHaveIdNumberAsync(
                occupants.Where(o => o.Role == OccupantRole.CoTenant).Select(o => o.RenterId).ToList(), ct))
            return ContractErrors.CoTenantIdRequired;

        var rent = input.MonthlyRent ?? room.ListedRent;
        if (rent is null or <= 0)
            return Error.Validation("MONTHLY_RENT_REQUIRED", "Cần nhập giá thuê (phòng chưa có giá niêm yết).");
        if (deposit > rent.Value * MaxDepositMonths)
            return ContractErrors.DepositTooHigh;

        var resolved = await ContractFeeRules.ResolveAsync(fees, property.Id, input.Fees, ct);
        if (resolved.IsFailure)
            return resolved.Error!;

        var defaults = property.BillingSettings;
        return new ContractDraftData(
            input.RepresentativeRenterId,
            input.StartDate,
            input.EndDate,
            input.SignedDate,
            input.SignedPlace,
            input.EffectiveDate,
            rent.Value,
            deposit,
            input.DepositTerms,
            input.NoticeDays ?? defaults.NoticeDays,
            input.PaymentMethods ?? DefaultPaymentMethods,
            input.CopiesCount ?? DefaultCopies,
            input.TermsText,
            input.Note,
            occupants,
            document,
            input.HouseholdHeadRenterId,
            resolved.Value!,
            input.BillingStartDate);
    }

    private async Task<Result<ContractTemplate?>> LoadTemplateAsync(Guid? templateId, Guid? currentTemplateId, CancellationToken ct)
    {
        if (templateId is not { } id)
            return Result.Success<ContractTemplate?>(null);

        var template = await templates.GetAsync(id, ct);
        if (template is null)
            return ContractTemplateErrors.NotFound;
        if (template.IsArchived && templateId != currentTemplateId)
            return ContractTemplateErrors.Archived;
        return template;
    }

    private static ContractDocument BuildDocument(ContractInput input, ContractTemplate? template)
    {
        var type = template?.ContractType ?? input.ContractType ?? ContractType.RoomRental;
        var title = TextNormalizer.TrimToNull(input.Title) ?? template?.Title ?? ContractTypes.DefaultTitle(type);
        var clausesJson = input.Clauses is not null ? ContractDocumentJson.ToJson(input.Clauses) : template?.ClausesJson;
        var definitions = ContractDocumentJson.Fields(template?.FieldDefinitionsJson);
        var valuesJson = CustomFieldValues.Normalize(definitions, input.CustomFields, "contract.customFields");

        return new ContractDocument(
            template?.Id, type, title, clausesJson is "[]" ? null : clausesJson,
            definitions.Count == 0 ? null : template!.FieldDefinitionsJson, valuesJson);
    }
}
