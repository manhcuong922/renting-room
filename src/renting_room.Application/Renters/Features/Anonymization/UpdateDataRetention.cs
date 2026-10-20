using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Identity;

namespace renting_room.Application.Renters;

public sealed record UpdateDataRetentionCommand(int RetentionMonths, bool AutoAnonymize) : IRequest<DataRetentionDto>;

public sealed class UpdateDataRetentionCommandValidator : AbstractValidator<UpdateDataRetentionCommand>
{
    public UpdateDataRetentionCommandValidator() =>
        RuleFor(x => x.RetentionMonths).InclusiveBetween(Organization.MinRetentionMonths, Organization.MaxRetentionMonths)
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Thời gian giữ dữ liệu từ 36 đến 120 tháng.");
}

public sealed class UpdateDataRetentionHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<UpdateDataRetentionCommand, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(UpdateDataRetentionCommand request, CancellationToken cancellationToken)
    {
        var organization = (await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken))!;
        organization.UpdateDataRetention(request.RetentionMonths, request.AutoAnonymize);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DataRetentionMapping.ToDto(organization);
    }
}
