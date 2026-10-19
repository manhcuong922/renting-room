using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations.CreateOrganization;

public sealed class CreateOrganizationHandler(
    IOrganizationRepository organizations,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    TimeProvider clock,
    ILogger<CreateOrganizationHandler> logger)
    : IRequestHandler<CreateOrganizationCommand, Result<CreateOrganizationResult>>
{
    public async ValueTask<Result<CreateOrganizationResult>> Handle(
        CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var code = Organization.NormalizeCode(request.Code);
        var phone = ContactNormalizer.NormalizePhone(request.Owner.Phone);
        var email = ContactNormalizer.NormalizeEmail(request.Owner.Email);

        // Kiểm tra trước để trả mã lỗi rõ ràng; unique index trong DB vẫn là chốt chặn cuối khi chạy song song.
        if (await organizations.IsCodeTakenAsync(code, cancellationToken))
            return IdentityErrors.OrganizationCodeTaken;
        if (phone is not null && await users.IsPhoneTakenAsync(phone, excludeId: null, cancellationToken))
            return IdentityErrors.PhoneTaken;
        if (email is not null && await users.IsEmailTakenAsync(email, excludeId: null, cancellationToken))
            return IdentityErrors.EmailTaken;

        var organization = Organization.Create(
            code,
            request.Name,
            request.ContactName,
            request.ContactPhone,
            request.ContactEmail,
            request.TaxCode,
            request.Note,
            request.Address);

        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var owner = User.CreateOrgOwner(
            organization.Id,
            request.Owner.FullName,
            phone,
            email,
            passwordHasher.Hash(temporaryPassword),
            clock.GetUtcNow());

        // Một SaveChanges = một transaction: không bao giờ có tổ chức thiếu chủ (M01 §10).
        organizations.Add(organization);
        users.Add(owner);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Organization {OrganizationId} ({Code}) created with owner {UserId}", organization.Id, code, owner.Id);

        return new CreateOrganizationResult(organization.Id, owner.Id, owner.Username, temporaryPassword);
    }
}
