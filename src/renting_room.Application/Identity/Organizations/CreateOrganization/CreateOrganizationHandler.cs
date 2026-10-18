using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Organizations.CreateOrganization;

public sealed class CreateOrganizationHandler(
    IAppDbContext db,
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
        if (await db.Organizations.AnyAsync(o => o.Code == code, cancellationToken))
            return IdentityErrors.OrganizationCodeTaken;
        if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNormalized == phone, cancellationToken))
            return IdentityErrors.PhoneTaken;
        if (email is not null && await db.Users.AnyAsync(u => u.EmailNormalized == email, cancellationToken))
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
        db.Organizations.Add(organization);
        db.Users.Add(owner);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Organization {OrganizationId} ({Code}) created with owner {UserId}", organization.Id, code, owner.Id);

        return new CreateOrganizationResult(organization.Id, owner.Id, owner.Username, temporaryPassword);
    }
}
