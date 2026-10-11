using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

// ============================================================ Đồng thuê (CT-BR-49)

public sealed record ChangeOccupantRoleCommand(Guid ContractId, Guid OccupantId, OccupantRole Role) : IRequest<Result>;

public sealed class ChangeOccupantRoleCommandValidator : AbstractValidator<ChangeOccupantRoleCommand>
{
    public ChangeOccupantRoleCommandValidator() => RuleFor(x => x.Role).IsInEnum();
}

/// <summary>CT-BR-49: đổi người ở ↔ đồng thuê. Đồng thuê phải có giấy tờ; về người ở thường thì kiểm CT-BR-50.</summary>
public sealed class ChangeOccupantRoleHandler(IAppDbContext db) : IRequestHandler<ChangeOccupantRoleCommand, Result>
{
    public ValueTask<Result> Handle(ChangeOccupantRoleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var occupant = contract.Occupants.FirstOrDefault(o => o.Id == request.OccupantId);
            if (occupant is not null && request.Role == OccupantRole.CoTenant && occupant.RenterId != contract.RepresentativeRenterId
                && !await OccupantChecks.HasIdNumberAsync(db, occupant.RenterId, cancellationToken))
                return Result.Failure(ContractErrors.CoTenantIdRequired);

            var changed = contract.ChangeOccupantRole(request.OccupantId, request.Role);
            if (changed.IsFailure || contract.Status != ContractStatus.Active)
                return changed;
            return await OccupantChecks.FindRoleConflictAsync(db, contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : changed;
        }, cancellationToken));
}

// ============================================================ Đổi người đứng tên (CT-BR-51)

/// <param name="HouseholdHeadRenterId">Chủ hộ mới (phải đang ở); null = giữ chủ hộ hiện tại, không có thì người đứng tên mới.</param>
/// <param name="OldRepresentativeMovesOut">Người đứng tên cũ rời đi cùng ngày ⇒ ghi chuyển đi + kết thúc xe của họ.</param>
public sealed record ChangeRepresentativeCommand(
    Guid ContractId, Guid RenterId, DateOnly Date, Guid? HouseholdHeadRenterId, bool OldRepresentativeMovesOut) : IRequest<Result>;

public sealed class ChangeRepresentativeCommandValidator : AbstractValidator<ChangeRepresentativeCommand>
{
    public ChangeRepresentativeCommandValidator() => RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
}

/// <summary>
/// CT-UC-25 / CT-BR-51: đổi người đứng tên trên cùng HĐ — nợ, cọc, khoản phát sinh, chuỗi chỉ số giữ nguyên (người mới nhận nợ cũ theo thỏa thuận
/// bên ngoài của người thuê). Bản ký về "Thiếu tài liệu", bản chụp lúc ký thay phần bên thuê.
/// </summary>
public sealed class ChangeRepresentativeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeRepresentativeCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeRepresentativeCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RenterId && r.AnonymizedAt == null, cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);

        return await ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            if (renter.Id != contract.RepresentativeRenterId && renter.IdNumberHash is null)
                return Result.Failure(ContractErrors.RepresentativeIdRequired);

            var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot)?.WithRepresentative(renter).ToJson();
            var changed = contract.ChangeRepresentative(renter.Id, request.Date, clock.GetUtcNow().ToBusinessDate(), request.HouseholdHeadRenterId,
                request.OldRepresentativeMovesOut, snapshot);
            if (changed.IsFailure)
                return changed;
            return await OccupantChecks.FindRoleConflictAsync(db, contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : changed;
        }, cancellationToken);
    }
}

// ============================================================ Chuyển người (CT-BR-52)

public sealed record MoveOccupantCommand(
    Guid ContractId, Guid OccupantId, Guid ToContractId, DateOnly Date, OccupantRole Role, OccupantRelationship? RelationshipType,
    string? Relationship, bool GuardianConsent, string? Note) : IRequest<Result>;

public sealed class MoveOccupantCommandValidator : AbstractValidator<MoveOccupantCommand>
{
    public MoveOccupantCommandValidator()
    {
        RuleFor(x => x.ToContractId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
        RuleFor(x => x.Relationship).OptionalText(50);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-UC-26 / CT-BR-52: người đi, HĐ ở lại — 1 transaction, khóa 2 HĐ theo id (C-07): ghi chuyển đi ở HĐ nguồn, thêm vào HĐ đích đang hiệu lực
/// cùng ngày, xe của người đó chuyển theo. Khoản thu chi không đi theo người.
/// </summary>
public sealed class MoveOccupantHandler(IAppDbContext db, ISender sender) : IRequestHandler<MoveOccupantCommand, Result>
{
    public async ValueTask<Result> Handle(MoveOccupantCommand request, CancellationToken cancellationToken)
    {
        if (request.ToContractId == request.ContractId)
            return Result.Failure(ContractErrors.MoveSameContract);
        var source = await db.Contracts.Where(c => c.Id == request.ContractId)
            .Select(c => new { RenterId = c.Occupants.Where(o => o.Id == request.OccupantId).Select(o => (Guid?)o.RenterId).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        var targetStatus = await db.Contracts.Where(c => c.Id == request.ToContractId).Select(c => (ContractStatus?)c.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (source is null || targetStatus is null)
            return Result.Failure(ContractErrors.NotFound);
        if (source.RenterId is not { } renterId)
            return Result.Failure(ContractErrors.OccupantNotFound);
        if (targetStatus != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { request.ContractId, request.ToContractId }.Order())
            await db.LockForUpdateAsync<Contract>(id, cancellationToken);

        var left = await sender.Send(new EndOccupancyCommand(request.ContractId, request.OccupantId, request.Date), cancellationToken);
        if (left.IsFailure)
            return left;
        var arrived = await sender.Send(new AddOccupantCommand(request.ToContractId, renterId, request.Date, null, request.Relationship, request.Note,
            request.RelationshipType, request.GuardianConsent, request.Role), cancellationToken);
        if (arrived.IsFailure)
            return arrived;

        var vehicles = await db.Contracts.Where(c => c.Id == request.ContractId).SelectMany(c => c.Vehicles)
            .Where(v => v.RenterId == renterId && v.RegisteredTo == null)
            .Select(v => new { v.Id, v.VehicleType, v.PlateNumber, v.BrandColor, v.Note }).ToListAsync(cancellationToken);
        foreach (var vehicle in vehicles)
        {
            var ended = await sender.Send(new EndVehicleCommand(request.ContractId, vehicle.Id, request.Date), cancellationToken);
            if (ended.IsFailure)
                return ended;
            var registered = await sender.Send(new RegisterVehicleCommand(request.ToContractId, renterId, vehicle.VehicleType, vehicle.PlateNumber,
                vehicle.BrandColor, request.Date, vehicle.Note), cancellationToken);
            if (registered.IsFailure)
                return Result.Failure(registered.Error!);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
