using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations;

/// <summary>ID-UC-03: thông tin tổ chức của người đang đăng nhập — chủ trọ tự sửa, mã tổ chức không đổi.</summary>
public sealed record OrganizationInfoDto(
    Guid Id, string Code, string Name, string? ContactName, string? ContactPhone, string? ContactEmail, string? TaxCode, string? Address);

public sealed record GetOrganizationInfoQuery : IRequest<OrganizationInfoDto>;

public sealed record UpdateOrganizationInfoCommand(
    string Name, string? ContactName, string? ContactPhone, string? ContactEmail, string? TaxCode, string? Address) : IRequest<OrganizationInfoDto>;

public sealed class UpdateOrganizationInfoCommandValidator : AbstractValidator<UpdateOrganizationInfoCommand>
{
    public UpdateOrganizationInfoCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(200, "Tên tổ chức");
        RuleFor(x => x.ContactName).OptionalText(200);
        RuleFor(x => x.ContactPhone).VietnamPhone();
        RuleFor(x => x.ContactEmail).ValidEmail();
        RuleFor(x => x.TaxCode).OptionalText(14);
        RuleFor(x => x.Address).OptionalText(500);
    }
}

internal static class OrganizationInfoMapping
{
    public static OrganizationInfoDto ToInfo(this Organization o) =>
        new(o.Id, o.Code, o.Name, o.ContactName, o.ContactPhone, o.ContactEmail, o.TaxCode, o.Address);
}

public sealed class GetOrganizationInfoHandler(IOrganizationQueries organizations, ICurrentUser currentUser)
    : IRequestHandler<GetOrganizationInfoQuery, OrganizationInfoDto>
{
    public async ValueTask<OrganizationInfoDto> Handle(GetOrganizationInfoQuery request, CancellationToken cancellationToken) =>
        (await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken))!.ToInfo();
}

public sealed class UpdateOrganizationInfoHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<UpdateOrganizationInfoCommand, OrganizationInfoDto>
{
    public async ValueTask<OrganizationInfoDto> Handle(UpdateOrganizationInfoCommand request, CancellationToken cancellationToken)
    {
        var organization = (await organizations.GetAsync(currentUser.OrganizationId!.Value, cancellationToken))!;
        organization.UpdateInfo(request.Name, request.ContactName, request.ContactPhone, request.ContactEmail, request.TaxCode, request.Address);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return organization.ToInfo();
    }
}
