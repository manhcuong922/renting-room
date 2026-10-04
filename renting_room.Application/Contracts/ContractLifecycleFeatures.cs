using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

// ============================================================ Người ở

public sealed record AddOccupantCommand(
    Guid ContractId, Guid RenterId, DateOnly MoveInDate, DateOnly? ExpectedEndDate, string? Relationship, string? Note,
    bool OverrideCapacity, OccupantRelationship? RelationshipType = null, bool GuardianConsent = false) : IRequest<Result>;

public sealed class AddOccupantCommandValidator : AbstractValidator<AddOccupantCommand>
{
    public AddOccupantCommandValidator()
    {
        RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.ExpectedEndDate).Must((x, d) => d is null || d >= x.MoveInDate).WithErrorCode("INVALID_DATE");
        RuleFor(x => x.Relationship).OptionalText(50);
        RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-BR-09: vượt sức chứa ⇒ 422, trừ khi chủ ý vượt (overrideCapacity — ghi log).
/// CT-BR-28..30: quan hệ với người đứng tên hợp lệ; CT-BR-31: HĐ đang hiệu lực ⇒ người mới không đang ở phòng khác.
/// </summary>
public sealed class AddOccupantHandler(IAppDbContext db, ICurrentUser currentUser, ILogger<AddOccupantHandler> logger)
    : IRequestHandler<AddOccupantCommand, Result>
{
    public ValueTask<Result> Handle(AddOccupantCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var people = await OccupantChecks.LoadPeopleAsync(db, [request.RenterId, contract.ReferenceRenterId], cancellationToken);
            if (!people.TryGetValue(request.RenterId, out var person))
                return Result.Failure(RenterErrors.RenterNotFound);

            var isReference = request.RenterId == contract.ReferenceRenterId;
            var input = new OccupantInput(request.RenterId, request.MoveInDate, request.ExpectedEndDate, request.Relationship, request.Note,
                isReference ? null : request.RelationshipType, request.GuardianConsent);
            CheckRelationship(contract, input, person.Facts, people[contract.ReferenceRenterId].Facts);

            if (contract.Status == ContractStatus.Active)
            {
                var elsewhere = await OccupantChecks.FindLivingElsewhereAsync(db, contract,
                    [(request.RenterId, request.MoveInDate, null)], people.ToDictionary(p => p.Key, p => p.Value.Name), cancellationToken);
                if (elsewhere is not null)
                    return Result.Failure(elsewhere);
            }

            var maxOccupants = await db.Rooms.Where(r => r.Id == contract.RoomId).Select(r => r.MaxOccupants).FirstAsync(cancellationToken);
            var added = contract.AddOccupant(input, maxOccupants, request.OverrideCapacity);
            if (added.IsSuccess && request.OverrideCapacity && contract.ExceedsCapacity(maxOccupants))
                logger.LogWarning("AUDIT: user {UserId} added occupant to contract {ContractId} over room capacity {MaxOccupants}",
                    currentUser.UserId, contract.Id, maxOccupants);
            return added;
        }, cancellationToken));

    /// <summary>Kiểm tra người mới cùng những người đang ở trùng thời gian (để bắt trùng vợ/chồng); chỉ báo lỗi của người mới.</summary>
    private static void CheckRelationship(Contract contract, OccupantInput input, PersonFacts person, PersonFacts reference)
    {
        var existing = contract.Occupants.Where(o => o.OverlapsFrom(input.MoveInDate) && o.RenterId != input.RenterId)
            .Select(o => (o.ToInput(), new PersonFacts(o.RenterId, DateOnly.MinValue, Gender.Other)));
        var list = existing.Append((input, person)).ToList();
        var violations = OccupantRelationshipRules.Check(reference, list).Where(v => v.Index == list.Count - 1);
        OccupantChecks.ThrowIfInvalid(violations, v => v.Field);
    }
}

public sealed record UpdateContractNoteCommand(Guid ContractId, string? Note) : IRequest<Result>;

public sealed class UpdateContractNoteCommandValidator : AbstractValidator<UpdateContractNoteCommand>
{
    public UpdateContractNoteCommandValidator() => RuleFor(x => x.Note).OptionalText(2000);
}

public sealed class UpdateContractNoteHandler(IAppDbContext db) : IRequestHandler<UpdateContractNoteCommand, Result>
{
    public ValueTask<Result> Handle(UpdateContractNoteCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.UpdateNote(request.Note)), cancellationToken));
}

public sealed record EndOccupancyCommand(Guid ContractId, Guid OccupantId, DateOnly MoveOutDate) : IRequest<Result>;

public sealed class EndOccupancyHandler(IAppDbContext db) : IRequestHandler<EndOccupancyCommand, Result>
{
    public ValueTask<Result> Handle(EndOccupancyCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.EndOccupancy(request.OccupantId, request.MoveOutDate)), cancellationToken));
}

// ============================================================ Phụ lục: đổi giá, gia hạn, báo trả phòng

public sealed record ChangeRentCommand(Guid ContractId, DateOnly EffectiveFrom, decimal MonthlyRent, string? AddendumNo, string? Note)
    : IRequest<Result>;

public sealed class ChangeRentCommandValidator : AbstractValidator<ChangeRentCommand>
{
    public ChangeRentCommandValidator()
    {
        RuleFor(x => x.MonthlyRent).Money(allowZero: false);
        RuleFor(x => x.AddendumNo).OptionalText(30);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

public sealed class ChangeRentHandler(IAppDbContext db, IInvoiceLockReader invoiceLocks) : IRequestHandler<ChangeRentCommand, Result>
{
    public ValueTask<Result> Handle(ChangeRentCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var firstOpenPeriod = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, cancellationToken);
            return contract.ChangeRent(request.EffectiveFrom, request.MonthlyRent, request.AddendumNo, request.Note, firstOpenPeriod);
        }, cancellationToken));
}

public sealed record ExtendContractCommand(Guid ContractId, DateOnly NewEndDate) : IRequest<Result>;

public sealed class ExtendContractCommandValidator : AbstractValidator<ExtendContractCommand>
{
    public ExtendContractCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.NewEndDate)
            .Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddYears(10))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Gia hạn tối đa 10 năm kể từ hôm nay.");
}

public sealed class ExtendContractHandler(IAppDbContext db) : IRequestHandler<ExtendContractCommand, Result>
{
    public ValueTask<Result> Handle(ExtendContractCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.Extend(request.NewEndDate)), cancellationToken));
}

/// <summary>CT-UC-22: HĐ quá hạn, chủ trọ cho ở tiếp chưa ký lại (CT-BR-45).</summary>
public sealed record StartHoldoverCommand(Guid ContractId, string? Note) : IRequest<Result>;

public sealed class StartHoldoverCommandValidator : AbstractValidator<StartHoldoverCommand>
{
    public StartHoldoverCommandValidator() => RuleFor(x => x.Note).OptionalText(500);
}

public sealed class StartHoldoverHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<StartHoldoverCommand, Result>
{
    public ValueTask<Result> Handle(StartHoldoverCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.StartHoldover(clock.GetUtcNow().ToBusinessDate(), request.Note)), cancellationToken));
}

/// <summary>
/// CT-UC-21: ký lại cho người còn ở — trong 1 transaction: HĐ cũ bắt đầu thanh lý tại ngày bàn giao X, tạo HĐ nháp mới từ X+1
/// (chép điều khoản, dịch vụ, người ở còn lại, xe của họ). Chủ trọ xem lại nháp rồi kích hoạt như bình thường.
/// </summary>
public sealed record ResignContractCommand(Guid ContractId, DateOnly HandoverDate, Guid RepresentativeRenterId, DateOnly? EndDate)
    : IRequest<Result<CreatedWithWarnings>>;

public sealed class ResignContractCommandValidator : AbstractValidator<ResignContractCommand>
{
    public ResignContractCommandValidator()
    {
        RuleFor(x => x.RepresentativeRenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end is null || end <= x.HandoverDate.AddYears(10))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Thời hạn hợp đồng mới tối đa 10 năm.");
    }
}

public sealed class ResignContractHandler(
    IAppDbContext db, ICurrentUser currentUser, IContractNumberGenerator numberGenerator, TimeProvider clock)
    : IRequestHandler<ResignContractCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(ResignContractCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync<CreatedWithWarnings>(db, request.ContractId, async old =>
        {
            var data = old.ResignDraft(request.HandoverDate, request.RepresentativeRenterId, request.EndDate);
            if (data.IsFailure)
                return data.Error!;

            if (await LiquidationGuards.HasInvoiceAfterAsync(db, old.Id, request.HandoverDate, cancellationToken))
                return ContractErrors.InvoiceAfterEndDate;
            var contractNo = await ContractNumbers.NextAsync(db, numberGenerator, currentUser, clock, cancellationToken);
            var liquidation = old.StartLiquidation(request.HandoverDate, TerminationReason.MutualAgreement, null,
                $"Ký lại cho người còn ở — hợp đồng mới {contractNo}", clock.GetUtcNow().ToBusinessDate());
            if (liquidation.IsFailure)
                return liquidation.Error!;

            var next = Contract.CreateDraft(old.PropertyId, old.RoomId, contractNo, data.Value!, previousContractId: old.Id);
            var renterIds = data.Value!.Occupants.Select(o => o.RenterId).ToList();
            foreach (var vehicle in old.ResignVehicles(renterIds, next.StartDate))
            {
                var registered = next.RegisterVehicle(vehicle);
                if (registered.IsFailure)
                    return registered.Error!;
            }

            db.Contracts.Add(next);
            return new CreatedWithWarnings(next.Id, ContractWarnings.For(next));
        }, cancellationToken));
}

public sealed record GiveNoticeCommand(Guid ContractId, DateOnly NoticeDate, DateOnly PlannedMoveOutDate) : IRequest<Result<NoticeResult>>;

public sealed class GiveNoticeHandler(IAppDbContext db) : IRequestHandler<GiveNoticeCommand, Result<NoticeResult>>
{
    public ValueTask<Result<NoticeResult>> Handle(GiveNoticeCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.GiveNotice(request.NoticeDate, request.PlannedMoveOutDate)), cancellationToken));
}

// ============================================================ Thanh lý

public sealed record StartLiquidationCommand(
    Guid ContractId, DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note) : IRequest<Result>;

public sealed class StartLiquidationCommandValidator : AbstractValidator<StartLiquidationCommand>
{
    public StartLiquidationCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Ground).IsInEnum().When(x => x.Ground is not null);
        RuleFor(x => x.Note).OptionalText(1000);
        RuleFor(x => x.Note)
            .NotEmpty().When(x => x.Ground == TerminationGround.Other)
            .WithErrorCode("REQUIRED").WithMessage("Căn cứ 'Khác' cần mô tả cụ thể.");
    }
}

public sealed class StartLiquidationHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<StartLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(StartLiquidationCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
            await LiquidationGuards.HasInvoiceAfterAsync(db, contract.Id, request.ActualEndDate, cancellationToken)
                ? Result.Failure(ContractErrors.InvoiceAfterEndDate)
                : contract.StartLiquidation(request.ActualEndDate, request.Reason, request.Ground, request.Note, clock.GetUtcNow().ToBusinessDate()),
            cancellationToken));
}

internal static class LiquidationGuards
{
    /// <summary>CT-BR-11: còn phiếu (chưa hủy) của kỳ bắt đầu sau ngày trả phòng ⇒ phải hủy / xóa trước (tránh thu tiền kỳ sau khi đã trả phòng).</summary>
    public static Task<bool> HasInvoiceAfterAsync(IAppDbContext db, Guid contractId, DateOnly actualEndDate, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void && i.PeriodStart > actualEndDate, ct);
}

/// <summary>Quay lại Active ⇒ ngày kết thúc thực tế bỏ trống; nếu phòng đã có HĐ mới sau đó, EXCLUDE trong DB trả 409.</summary>
public sealed record CancelLiquidationCommand(Guid ContractId) : IRequest<Result>;

public sealed class CancelLiquidationHandler(IAppDbContext db) : IRequestHandler<CancelLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(CancelLiquidationCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var cancelled = contract.CancelLiquidation();
            if (cancelled.IsFailure)
                return cancelled;

            // CT-BR-31: người ở chưa ghi chuyển đi lại thành "đang ở" vô thời hạn — có thể đã chuyển sang phòng khác.
            var staying = contract.Occupants.Where(o => o.MoveOutDate is null).ToList();
            if (staying.Count == 0)
                return Result.Success();
            var people = await OccupantChecks.LoadPeopleAsync(db, staying.Select(o => o.RenterId), cancellationToken);
            var elsewhere = await OccupantChecks.FindLivingElsewhereAsync(db, contract,
                staying.Select(o => (o.RenterId, o.MoveInDate, (DateOnly?)null)).ToList(),
                people.ToDictionary(p => p.Key, p => p.Value.Name), cancellationToken);
            return elsewhere is null ? Result.Success() : Result.Failure(elsewhere);
        }, cancellationToken));
}

/// <param name="FinalReadings">CT-BR-12: chỉ số cuối (ngày trả phòng) cho mỗi công tơ của phòng — bắt buộc nhập số.</param>
public sealed record CompleteLiquidationCommand(Guid ContractId, IReadOnlyList<MeterReadingInput>? FinalReadings = null) : IRequest<Result>;

public sealed class CompleteLiquidationHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CompleteLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(CompleteLiquidationCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var completed = contract.CompleteLiquidation(clock.GetUtcNow());
            if (completed.IsFailure)
                return completed;
            return await ContractMeterReadings.RecordAsync(
                db, contract, ReadingKind.Final, contract.ActualEndDate!.Value, request.FinalReadings, cancellationToken);
        }, cancellationToken));
}

// ============================================================ Tài sản bàn giao

public sealed record AssetRequest(string Name, int Quantity, string? ConditionAtHandover, decimal? ValueEstimate, string? Note)
{
    public AssetInput ToDomain() => new(Name, Quantity, ConditionAtHandover, ValueEstimate, Note);
}

internal sealed class AssetRequestValidator : AbstractValidator<AssetRequest>
{
    public AssetRequestValidator()
    {
        RuleFor(x => x.Name).RequiredText(200, "Tên tài sản");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.ConditionAtHandover).OptionalText(500);
        RuleFor(x => x.ValueEstimate).OptionalMoney();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

public sealed record AddAssetCommand(Guid ContractId, AssetRequest Asset) : IRequest<Result<Guid>>;

public sealed class AddAssetCommandValidator : AbstractValidator<AddAssetCommand>
{
    public AddAssetCommandValidator()
    {
        RuleFor(x => x.Asset).NotNull();
        RuleFor(x => x.Asset).FlattenedValidator(new AssetRequestValidator());
    }
}

public sealed class AddAssetHandler(IAppDbContext db) : IRequestHandler<AddAssetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(AddAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, contract =>
        {
            var added = contract.AddAsset(request.Asset.ToDomain());
            return Task.FromResult(added.IsSuccess ? Result.Success(added.Value!.Id) : Result.Failure<Guid>(added.Error!));
        }, cancellationToken));
}

public sealed record UpdateAssetCommand(Guid ContractId, Guid AssetId, AssetRequest Asset) : IRequest<Result>;

public sealed class UpdateAssetCommandValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetCommandValidator()
    {
        RuleFor(x => x.Asset).NotNull();
        RuleFor(x => x.Asset).FlattenedValidator(new AssetRequestValidator());
    }
}

public sealed class UpdateAssetHandler(IAppDbContext db) : IRequestHandler<UpdateAssetCommand, Result>
{
    public ValueTask<Result> Handle(UpdateAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.UpdateAsset(request.AssetId, request.Asset.ToDomain())), cancellationToken));
}

public sealed record RemoveAssetCommand(Guid ContractId, Guid AssetId) : IRequest<Result>;

public sealed class RemoveAssetHandler(IAppDbContext db) : IRequestHandler<RemoveAssetCommand, Result>
{
    public ValueTask<Result> Handle(RemoveAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.RemoveAsset(request.AssetId)), cancellationToken));
}

public sealed record RecordAssetReturnCommand(Guid ContractId, Guid AssetId, string? ConditionAtReturn, decimal? CompensationValue)
    : IRequest<Result>;

public sealed class RecordAssetReturnCommandValidator : AbstractValidator<RecordAssetReturnCommand>
{
    public RecordAssetReturnCommandValidator()
    {
        RuleFor(x => x.ConditionAtReturn).OptionalText(500);
        RuleFor(x => x.CompensationValue).OptionalMoney();
    }
}

public sealed class RecordAssetReturnHandler(IAppDbContext db) : IRequestHandler<RecordAssetReturnCommand, Result>
{
    public ValueTask<Result> Handle(RecordAssetReturnCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, contract => Task.FromResult(
            contract.RecordAssetReturn(request.AssetId, request.ConditionAtReturn, request.CompensationValue)), cancellationToken));
}

// ============================================================ Xe gửi

public sealed record RegisterVehicleCommand(
    Guid ContractId, Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly? RegisteredFrom, string? Note)
    : IRequest<Result<CreatedWithWarnings>>;

public sealed class RegisterVehicleCommandValidator : AbstractValidator<RegisterVehicleCommand>
{
    public RegisterVehicleCommandValidator()
    {
        RuleFor(x => x.VehicleType).IsInEnum();
        RuleFor(x => x.PlateNumber).OptionalText(20);
        RuleFor(x => x.PlateNumber)
            .NotEmpty().When(x => x.VehicleType is VehicleType.Motorbike or VehicleType.Car)
            .WithErrorCode("REQUIRED").WithMessage("Xe máy / ô tô phải có biển số.");
        RuleFor(x => x.BrandColor).OptionalText(100);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>Một biển số chỉ đăng ký giữ ở 1 nơi trong tổ chức (unique index là chốt chặn khi gửi song song).</summary>
public sealed class RegisterVehicleHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<RegisterVehicleCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(RegisterVehicleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var plate = ContractVehicle.NormalizePlate(request.PlateNumber);
            if (plate is not null && await db.Contracts.SelectMany(c => c.Vehicles)
                    .AnyAsync(v => v.PlateNumber == plate && v.RegisteredTo == null, cancellationToken))
                return Result.Failure<CreatedWithWarnings>(ContractErrors.PlateAlreadyRegistered);
            if (request.RenterId is { } renterId && !contract.Occupants.Any(o => o.RenterId == renterId)
                && contract.RepresentativeRenterId != renterId)
                return Result.Failure<CreatedWithWarnings>(RenterErrors.RenterNotFound);

            var registered = contract.RegisterVehicle(new VehicleInput(
                request.RenterId, request.VehicleType, request.PlateNumber, request.BrandColor,
                request.RegisteredFrom ?? Max(contract.StartDate, clock.GetUtcNow().ToBusinessDate()), request.Note));
            if (registered.IsFailure)
                return Result.Failure<CreatedWithWarnings>(registered.Error!);

            var vehicle = registered.Value!;
            var warnings = new List<Warning>();
            if (vehicle.PlateNumber is { } normalized && !ContractWarnings.IsUsualPlate(normalized))
                warnings.Add(ContractWarnings.UnusualPlate(normalized));
            var parkingFees = await ContractFeeRules.LoadParkingFeesAsync(db, contract.PropertyId, cancellationToken);
            warnings.AddRange(ContractWarnings.For(contract, parkingFees, clock.GetUtcNow().ToBusinessDate())
                .Where(w => w.Code == "PARKING_QUANTITY_MISMATCH"));
            return Result.Success(new CreatedWithWarnings(vehicle.Id, warnings));
        }, cancellationToken));

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}

public sealed record EndVehicleCommand(Guid ContractId, Guid VehicleId, DateOnly EndDate) : IRequest<Result>;

public sealed class EndVehicleHandler(IAppDbContext db) : IRequestHandler<EndVehicleCommand, Result>
{
    public ValueTask<Result> Handle(EndVehicleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.EndVehicle(request.VehicleId, request.EndDate)), cancellationToken));
}
