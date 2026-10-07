using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Meters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

internal sealed class ContractInputValidator : AbstractValidator<ContractInput>
{
    public const int MaxOccupantsInRequest = 20;

    public ContractInputValidator(TimeProvider clock)
    {
        RuleFor(x => x.RepresentativeRenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end is null || (end > x.StartDate && end <= x.StartDate.AddYears(10)))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Ngày kết thúc phải sau ngày bắt đầu và không quá 10 năm.");
        RuleFor(x => x.StartDate)
            .Must(d => d >= clock.GetUtcNow().ToBusinessDate().AddYears(-1))
            .WithErrorCode("INVALID_START_DATE").WithMessage("Ngày bắt đầu không được trước hôm nay quá 1 năm.");
        RuleFor(x => x.SignedDate)
            .Must(d => d is null || d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_SIGNED_DATE").WithMessage("Ngày ký không được ở tương lai.");
        // CT-BR-20 (Luật Nhà ở 2023 Điều 164): hiệu lực theo thỏa thuận, không trước ngày ký.
        RuleFor(x => x.EffectiveDate)
            .Must((x, d) => d is null || ((x.SignedDate is null || d >= x.SignedDate) && (x.EndDate is null || d <= x.EndDate)))
            .WithErrorCode("INVALID_EFFECTIVE_DATE").WithMessage("Ngày hiệu lực không được trước ngày ký hoặc sau ngày kết thúc.");
        RuleFor(x => x.SignedPlace).OptionalText(200);
        RuleFor(x => x.MonthlyRent).OptionalMoney().Must(v => v is null || v > 0)
            .WithErrorCode("INVALID_AMOUNT").WithMessage("Giá thuê phải lớn hơn 0.");
        RuleFor(x => x.DepositAmount).OptionalMoney();
        RuleFor(x => x.DepositTerms).OptionalText(5000);
        When(x => x.Billing is not null, () =>
        {
            RuleFor(x => x.Billing!.AnchorDay).InclusiveBetween(1, 31).OverridePropertyName("billing.anchorDay").WithErrorCode("OUT_OF_RANGE");
            RuleFor(x => x.Billing!.PaymentDueDays).InclusiveBetween(0, 60).OverridePropertyName("billing.paymentDueDays").WithErrorCode("OUT_OF_RANGE");
            RuleFor(x => x.Billing!.ChargeMode).IsInEnum().OverridePropertyName("billing.chargeMode");
            RuleFor(x => x.Billing!.ProrationMode).IsInEnum().OverridePropertyName("billing.prorationMode");
        });
        RuleFor(x => x.NoticeDays).InclusiveBetween(0, 180).When(x => x.NoticeDays is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.PaymentMethods)
            .Must(m => m is null || (m.Count > 0 && m.All(Enum.IsDefined)))
            .WithErrorCode("INVALID_PAYMENT_METHODS").WithMessage("Chọn ít nhất 1 phương thức thanh toán hợp lệ.");
        RuleFor(x => x.CopiesCount).InclusiveBetween(1, 10).When(x => x.CopiesCount is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.TermsText).OptionalText(50_000);
        RuleFor(x => x.Note).OptionalText(2000);
        RuleFor(x => x.Occupants)
            .Must(o => o is null || o.Count <= MaxOccupantsInRequest).WithErrorCode("OUT_OF_RANGE")
            .Must(o => o is null || o.Select(x => x.RenterId).Distinct().Count() == o.Count)
            .WithErrorCode("DUPLICATE_OCCUPANT").WithMessage("Một người chỉ xuất hiện 1 lần trong danh sách người ở.");
        RuleForEach(x => x.Occupants).ChildRules(o =>
        {
            o.RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
            o.RuleFor(x => x.Relationship).OptionalText(50);
            o.RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
            o.RuleFor(x => x.Note).OptionalText(500);
        });
        RuleFor(x => x.ContractType).IsInEnum().When(x => x.ContractType is not null);
        RuleFor(x => x.Title).OptionalText(200);
        this.ClausesRules(x => x.Clauses, "clauses");
        RuleFor(x => x.Fees)
            .Must(f => f is null || f.Count <= ContractFeeRules.MaxFees).WithErrorCode("OUT_OF_RANGE")
            .WithMessage($"Tối đa {ContractFeeRules.MaxFees} khoản thu.")
            .Must(f => f is null || f.Select(x => x.FeeTypeId).Distinct().Count() == f.Count)
            .WithErrorCode("DUPLICATE_FEE").WithMessage("Mỗi khoản thu chỉ gắn 1 lần.");
        RuleForEach(x => x.Fees).ChildRules(f =>
        {
            f.RuleFor(x => x.Quantity).Must(ContractFeeRules.IsValidQuantity).WithErrorCode("OUT_OF_RANGE")
                .WithMessage("Số lượng 0–100, tối đa 2 số lẻ.");
            f.RuleFor(x => x.UnitPriceOverride).Must(ContractFeeRules.IsValidOverride).WithErrorCode("INVALID_AMOUNT")
                .WithMessage("Giá riêng 0–50.000.000đ, tối đa 2 số lẻ.");
        });
        RuleFor(x => x.CustomFields).Must(f => f is null || f.Count <= ContractDocumentRules.MaxFields)
            .WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {ContractDocumentRules.MaxFields} trường.");
        RuleForEach(x => x.Occupants)
            .Must((x, o) => o.MoveInDate is null || o.MoveInDate >= x.StartDate)
            .WithErrorCode("DATE_OUTSIDE_CONTRACT").WithMessage("Ngày vào ở không được trước ngày bắt đầu hợp đồng.");
    }
}

/// <summary>Áp mặc định từ khu/phòng và kiểm tra người thuê thuộc tổ chức.</summary>
internal static class ContractDraftBuilder
{
    public static readonly PaymentMethod[] DefaultPaymentMethods = [PaymentMethod.Cash, PaymentMethod.BankTransfer];
    public const int DefaultCopies = 2;
    public const int MaxDepositMonths = 12;

    /// <param name="currentTemplateId">Mẫu đang gắn với bản nháp (sửa nháp) — được phép giữ dù mẫu đã ngừng dùng.</param>
    public static async Task<Result<ContractDraftData>> BuildAsync(
        IAppDbContext db, ContractInput input, Room room, Property property, Guid? currentTemplateId, CancellationToken ct)
    {
        var template = await LoadTemplateAsync(db, input.TemplateId, currentTemplateId, ct);
        if (template.IsFailure)
            return template.Error!;

        // CT-BR-27: mẫu không cọc ⇒ cọc luôn 0, không lấy cọc mặc định của phòng.
        var noDeposit = template.Value?.NoDeposit == true;
        if (noDeposit && input.DepositAmount is > 0)
            return ContractTemplateErrors.DepositNotAllowed;
        var deposit = noDeposit ? 0 : input.DepositAmount ?? room.DefaultDeposit ?? 0;
        var document = BuildDocument(input, template.Value);

        // CT-BR-36: quan hệ khai so với chủ hộ (mặc định người đứng tên); chủ hộ phải là người ở.
        var reference = input.HouseholdHeadRenterId ?? input.RepresentativeRenterId;
        if (input.HouseholdHeadRenterId is { } head && input.Occupants?.Any(o => o.RenterId == head) != true)
            OccupantChecks.ThrowIfInvalid(
                [new OccupantRuleViolation(-1, "householdHeadRenterId", ContractErrors.HouseholdHeadNotOccupant)],
                v => $"contract.{v.Field}");

        var occupants = (input.Occupants ?? [])
            .Select(o => new OccupantInput(o.RenterId, o.MoveInDate ?? input.StartDate, o.ExpectedEndDate, o.Relationship, o.Note,
                o.RenterId == reference ? null : o.RelationshipType, o.GuardianConsent ?? false))
            .ToList();
        var people = await OccupantChecks.LoadPeopleAsync(db, occupants.Select(o => o.RenterId).Append(input.RepresentativeRenterId), ct);
        if (occupants.Any(o => !people.ContainsKey(o.RenterId)) || !people.ContainsKey(input.RepresentativeRenterId))
            return RenterErrors.RenterNotFound;

        // CT-BR-28..30: quan hệ của từng người ở với người đứng tên.
        OccupantChecks.ThrowIfInvalid(
            OccupantRelationshipRules.Check(people[reference].Facts,
                occupants.Select(o => (o, people[o.RenterId].Facts)).ToList()),
            v => $"contract.occupants[{v.Index}].{v.Field}");

        var rent = input.MonthlyRent ?? room.ListedRent;
        if (rent is null or <= 0)
            return Error.Validation("MONTHLY_RENT_REQUIRED", "Cần nhập giá thuê (phòng chưa có giá niêm yết).");
        if (deposit > rent.Value * MaxDepositMonths)
            return ContractErrors.DepositTooHigh;

        // CT-UC-06: khoản thu — null ⇒ tự gắn theo khu; khoản phải cùng khu, chưa ngừng dùng.
        var fees = await ContractFeeRules.ResolveAsync(db, property.Id, input.Fees, ct);
        if (fees.IsFailure)
            return fees.Error!;

        var defaults = property.BillingDefaults;
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
            input.Billing?.AnchorDay ?? defaults.AnchorDay,
            input.Billing?.ChargeMode ?? defaults.ChargeMode,
            input.Billing?.ProrationMode ?? defaults.ProrationMode,
            input.Billing?.PaymentDueDays ?? defaults.PaymentDueDays,
            input.NoticeDays ?? defaults.NoticeDays,
            input.PaymentMethods ?? DefaultPaymentMethods,
            input.CopiesCount ?? DefaultCopies,
            input.TermsText,
            input.Note,
            occupants,
            document,
            input.HouseholdHeadRenterId,
            fees.Value!);
    }

    private static async Task<Result<ContractTemplate?>> LoadTemplateAsync(
        IAppDbContext db, Guid? templateId, Guid? currentTemplateId, CancellationToken ct)
    {
        if (templateId is null)
            return Result.Success<ContractTemplate?>(null);

        var template = await db.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return ContractTemplateErrors.NotFound;
        if (template.IsArchived && templateId != currentTemplateId)
            return ContractTemplateErrors.Archived;
        return template;
    }

    /// <summary>
    /// CT-BR-25/26: chép văn bản từ mẫu vào hợp đồng. Mẫu quyết định loại + bộ trường; tiêu đề / điều khoản
    /// gửi kèm ghi đè nội dung của mẫu. Không dùng mẫu ⇒ không có trường tùy biến.
    /// </summary>
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

// ============================================================ Tạo nháp

public sealed record CreateContractCommand(Guid RoomId, string? ContractNo, ContractInput Contract) : IRequest<Result<CreatedWithWarnings>>;

public sealed class CreateContractCommandValidator : AbstractValidator<CreateContractCommand>
{
    public CreateContractCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.RoomId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.ContractNo!).Code(30, "Số hợp đồng").When(x => x.ContractNo is not null);
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator(clock));
    }
}

public sealed class CreateContractHandler(
    IAppDbContext db, ICurrentUser currentUser, IContractNumberGenerator numberGenerator, TimeProvider clock)
    : IRequestHandler<CreateContractCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(CreateContractCommand request, CancellationToken cancellationToken)
    {
        // Khóa hàng phòng: không tạo được nháp đồng thời với lệnh ngừng dùng phòng (PR-BR-05).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Room>(request.RoomId, cancellationToken);

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;
        if (room.IsArchived)
            return PropertyErrors.RoomArchived;

        var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == room.PropertyId, cancellationToken);
        var data = await ContractDraftBuilder.BuildAsync(db, request.Contract, room, property, currentTemplateId: null, cancellationToken);
        if (data.IsFailure)
            return data.Error!;

        string contractNo;
        if (request.ContractNo is not null)
        {
            contractNo = TextNormalizer.NormalizeCode(request.ContractNo);
            if (await db.Contracts.AnyAsync(c => c.ContractNo == contractNo, cancellationToken))
                return ContractErrors.ContractNoTaken;
        }
        else
        {
            contractNo = await ContractNumbers.NextAsync(db, numberGenerator, currentUser, clock, cancellationToken);
        }

        var contract = Contract.CreateDraft(property.Id, room.Id, contractNo, data.Value!);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreatedWithWarnings(contract.Id, ContractWarnings.For(contract));
    }
}

internal static class ContractNumbers
{
    private const int MaxAttempts = 5;

    /// <summary>Số tự sinh có thể trùng số người dùng đã tự nhập trước đó (VD tự gõ "HD2026-0005") ⇒ lấy số kế tiếp.</summary>
    public static async Task<string> NextAsync(
        IAppDbContext db, IContractNumberGenerator generator, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        string contractNo;
        var attempt = 0;
        do
        {
            contractNo = await generator.NextAsync(currentUser.OrganizationId!.Value, clock.GetUtcNow().ToBusinessDate().Year, ct);
        }
        while (++attempt < MaxAttempts && await db.Contracts.AnyAsync(c => c.ContractNo == contractNo, ct));
        return contractNo;
    }
}

// ============================================================ Sửa nháp / hủy nháp

public sealed record UpdateContractDraftCommand(Guid Id, ContractInput Contract, uint Version) : IRequest<Result>;

public sealed class UpdateContractDraftCommandValidator : AbstractValidator<UpdateContractDraftCommand>
{
    public UpdateContractDraftCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator(clock));
}

public sealed class UpdateContractDraftHandler(IAppDbContext db) : IRequestHandler<UpdateContractDraftCommand, Result>
{
    public ValueTask<Result> Handle(UpdateContractDraftCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.Id, async contract =>
        {
            db.SetExpectedVersion(contract, request.Version);
            var room = await db.Rooms.AsNoTracking().FirstAsync(r => r.Id == contract.RoomId, cancellationToken);
            var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == contract.PropertyId, cancellationToken);
            var data = await ContractDraftBuilder.BuildAsync(db, request.Contract, room, property, contract.TemplateId, cancellationToken);
            return data.IsFailure ? Result.Failure(data.Error!) : contract.UpdateDraft(data.Value!);
        }, cancellationToken));
}

public sealed record CancelContractCommand(Guid Id, string Reason) : IRequest<Result>;

public sealed class CancelContractCommandValidator : AbstractValidator<CancelContractCommand>
{
    public CancelContractCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do hủy");
}

public sealed class CancelContractHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelContractCommand, Result>
{
    public ValueTask<Result> Handle(CancelContractCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.Id,
            contract => Task.FromResult(contract.Cancel(request.Reason, clock.GetUtcNow())), cancellationToken));
}

// ============================================================ Kích hoạt (bàn giao phòng)

/// <param name="OverrideCapacity">Chủ ý vượt sức chứa phòng (VD gia đình có con nhỏ) — ghi log kiểm toán.</param>
/// <param name="HandoverReadings">MT-BR-13: chỉ số nhận phòng cho mỗi công tơ của phòng; value null = "Dùng số mới nhất".</param>
public sealed record ActivateContractCommand(
    Guid Id, bool OverrideCapacity = false, IReadOnlyList<MeterReadingInput>? HandoverReadings = null) : IRequest<Result>;

/// <summary>
/// CT-BR-01/02/18/19. Khóa theo thứ tự rooms → contracts (C-07). Hai hợp đồng cùng phòng kích hoạt song song:
/// khóa hàng phòng tuần tự hóa, EXCLUDE constraint trong DB là chốt chặn cuối (→ 409 ROOM_PERIOD_OVERLAP).
/// </summary>
public sealed class ActivateContractHandler(
    IAppDbContext db, TimeProvider clock, IAuditTrail auditTrail)
    : IRequestHandler<ActivateContractCommand, Result>
{
    public async ValueTask<Result> Handle(ActivateContractCommand request, CancellationToken cancellationToken)
    {
        var roomId = await db.Contracts.Where(c => c.Id == request.Id).Select(c => (Guid?)c.RoomId).FirstOrDefaultAsync(cancellationToken);
        if (roomId is null)
            return Result.Failure(ContractErrors.NotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Room>(roomId.Value, cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.Id, cancellationToken);

        var contract = (await ContractMutation.LoadAsync(db, request.Id, cancellationToken))!;
        var room = await db.Rooms.FirstAsync(r => r.Id == contract.RoomId, cancellationToken);
        var property = await db.Properties.FirstAsync(p => p.Id == contract.PropertyId, cancellationToken);
        var representative = await db.Renters.FirstAsync(r => r.Id == contract.RepresentativeRenterId, cancellationToken);

        var occupantError = await CheckOccupantsAsync(contract, cancellationToken);
        if (occupantError is not null)
            return Result.Failure(occupantError);

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        var lessor = property.Lessor;
        var feeTypes = await ContractFeeRules.LoadTypesAsync(db, contract, cancellationToken);
        var context = new ActivationContext(
            today,
            now,
            RoomAvailable: !room.IsArchived && !room.IsUnderMaintenance && !property.IsArchived,
            RoomMaxOccupants: room.MaxOccupants,
            LessorComplete: lessor?.IsComplete(today) == true,
            SigningSnapshotJson: lessor is null ? "{}" : SigningSnapshot.From(property, lessor, room, representative, now).ToJson(),
            HouseRulesSnapshot: property.HouseRulesText,
            RepresentativeDateOfBirth: representative.DateOfBirth,
            RepresentativeHasPhone: representative.Phone is not null,
            OverrideCapacity: request.OverrideCapacity,
            UtilityPriceSnapshotJson: UtilityPriceSnapshotJson.ToJson(UtilityPriceSnapshotJson.Capture(contract, feeTypes)));

        var activated = contract.Activate(context);
        if (activated.IsFailure)
            return activated;
        var handover = await ContractMeterReadings.RecordAsync(
            db, contract, ReadingKind.Handover, contract.StartDate, request.HandoverReadings, cancellationToken);
        if (handover.IsFailure)
            return handover;
        if (request.OverrideCapacity && contract.ExceedsCapacity(room.MaxOccupants))
            auditTrail.Record(AuditActions.OverrideCapacity, nameof(Contract), contract.Id,
                new { operation = "Activate", maxOccupants = room.MaxOccupants });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>CT-BR-28..31 tại thời điểm bàn giao: quan hệ vẫn hợp lệ với hồ sơ hiện tại, không ai đang ở phòng khác.</summary>
    private async Task<Error?> CheckOccupantsAsync(Contract contract, CancellationToken ct)
    {
        var people = await OccupantChecks.LoadPeopleAsync(
            db, contract.Occupants.Select(o => o.RenterId).Append(contract.ReferenceRenterId), ct);
        var occupants = contract.Occupants.ToList();
        var violation = OccupantRelationshipRules.Check(
                people[contract.ReferenceRenterId].Facts,
                occupants.Select(o => (o.ToInput(), people[o.RenterId].Facts)).ToList())
            .FirstOrDefault();
        if (violation is not null)
            return Error.BusinessRule(violation.Error.Code,
                $"{people[occupants[violation.Index].RenterId].Name}: {violation.Error.Message}");

        return await OccupantChecks.FindLivingElsewhereAsync(db, contract,
            occupants.Select(o => (o.RenterId, o.MoveInDate, o.MoveOutDate)).ToList(),
            people.ToDictionary(p => p.Key, p => p.Value.Name), ct);
    }
}
