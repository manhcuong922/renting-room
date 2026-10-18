using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>Khai bên cho thuê <b>riêng</b> cho khu — chỉ khi khác chủ trọ (công ty, người được ủy quyền…). Không khai ⇒ dùng thông tin chủ trọ.</summary>
/// <param name="IdNumber">Null = giữ nguyên số giấy tờ đang lưu (client chỉ thấy 4 số cuối nên không gửi lại được).</param>
public sealed record UpdateLessorCommand(
    Guid PropertyId,
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
    DateOnly? AuthorizationDocDate) : IRequest<Result<PropertyDetailDto>>, ILessorFields;

public sealed class UpdateLessorCommandValidator : AbstractValidator<UpdateLessorCommand>
{
    public UpdateLessorCommandValidator(TimeProvider clock) => this.Lessor(clock);
}

public sealed class UpdateLessorHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var lessor = LessorRules.Build(request, property.Lessor, protector, currentUser.OrganizationId!.Value);
        if (lessor.IsFailure)
            return lessor.Error!;
        property.UpdateLessor(lessor.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}
