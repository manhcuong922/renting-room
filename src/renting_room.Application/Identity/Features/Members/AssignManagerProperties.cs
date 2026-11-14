using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

/// <summary>M01 ID-UC-19 / ID-BR-24 (08/11/2026): chủ trọ giao khu cho quản lý khu — thay cả tập; <c>[]</c> = không giao khu nào.</summary>
public sealed record AssignManagerPropertiesCommand(Guid Id, IReadOnlyList<Guid> PropertyIds) : IRequest<Result<MemberDto>>;

public sealed class AssignManagerPropertiesCommandValidator : AbstractValidator<AssignManagerPropertiesCommand>
{
    public AssignManagerPropertiesCommandValidator() => RuleFor(x => x.PropertyIds).NotNull().AssignedProperties();
}

public sealed class AssignManagerPropertiesHandler(
    IUserRepository users, IUserQueries userQueries, IPropertyAccessRepository access, IPropertyScopeStore scopes, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, IAuditTrail auditTrail)
    : IRequestHandler<AssignManagerPropertiesCommand, Result<MemberDto>>
{
    public async ValueTask<Result<MemberDto>> Handle(AssignManagerPropertiesCommand request, CancellationToken cancellationToken)
    {
        var manager = await ManagerLookup.FindAsync(users, currentUser, request.Id, cancellationToken);
        if (manager.IsFailure)
            return manager.Error!;
        var ids = await PropertyAssignment.ResolveAsync(access, request.PropertyIds, cancellationToken);
        if (ids.IsFailure)
            return ids.Error!;

        var before = (await access.ListAsync([request.Id], cancellationToken)).GetValueOrDefault(request.Id) ?? [];
        await access.ReplaceAsync(request.Id, ids.Value!, cancellationToken);
        auditTrail.Record(AuditActions.PropertiesAssigned, "User", request.Id, new { before, after = ids.Value });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        scopes.Invalidate(request.Id);
        return (await userQueries.GetMemberAsync(request.Id, currentUser.OrganizationId, cancellationToken))!;
    }
}

internal static class PropertyAssignment
{
    public const int MaxProperties = 200;

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> AssignedProperties<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> rule) =>
        rule.Must(ids => ids is null || ids.Count <= MaxProperties).WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {MaxProperties} khu.");

    /// <summary>null ⇒ mọi khu đang dùng (ID-BR-29); có id lạ / khu tổ chức khác / khu đã ngừng ⇒ 422 <c>PROPERTY_ASSIGNMENT_INVALID</c>.</summary>
    public static async Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        IPropertyAccessRepository access, IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        if (requested is null)
            return Result.Success(await access.ListActivePropertyIdsAsync(null, ct));
        var distinct = requested.Distinct().ToList();
        var active = await access.ListActivePropertyIdsAsync(distinct, ct);
        return active.Count == distinct.Count
            ? Result.Success<IReadOnlyList<Guid>>(distinct)
            : IdentityErrors.PropertyAssignmentInvalid;
    }
}
