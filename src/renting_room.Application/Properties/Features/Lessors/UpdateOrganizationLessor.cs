using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>PR-BR-17: chủ trọ khai thông tin của mình một lần — mọi khu không khai riêng dùng thông tin này.</summary>
/// <param name="IdNumber">Null = giữ nguyên số giấy tờ đang lưu.</param>
public sealed record UpdateOrganizationLessorCommand(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumber,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate) : IRequest<Result<OrganizationLessorDto>>, ILessorFields;

public sealed class UpdateOrganizationLessorCommandValidator : AbstractValidator<UpdateOrganizationLessorCommand>
{
    public UpdateOrganizationLessorCommandValidator(TimeProvider clock) => this.Lessor(clock);
}

public sealed class UpdateOrganizationLessorHandler(
    IOrganizationRepository organizations, IUnitOfWork unitOfWork, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateOrganizationLessorCommand, Result<OrganizationLessorDto>>
{
    public async ValueTask<Result<OrganizationLessorDto>> Handle(UpdateOrganizationLessorCommand request, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("Organization of the current user not found.");
        var lessor = LessorRules.Build(request, organization.DefaultLessor, protector, organization.Id);
        if (lessor.IsFailure)
            return lessor.Error!;
        organization.UpdateDefaultLessor(lessor.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return organization.ToLessorDto(clock.GetUtcNow().ToBusinessDate());
    }
}
