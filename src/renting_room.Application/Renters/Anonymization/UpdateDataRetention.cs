using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Application.Renters;

// Cài đặt lưu giữ của tổ chức
public sealed record UpdateDataRetentionCommand(int RetentionMonths, bool AutoAnonymize) : IRequest<DataRetentionDto>;

public sealed class UpdateDataRetentionCommandValidator : AbstractValidator<UpdateDataRetentionCommand>
{
    public UpdateDataRetentionCommandValidator() =>
        RuleFor(x => x.RetentionMonths).InclusiveBetween(Organization.MinRetentionMonths, Organization.MaxRetentionMonths)
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Thời gian giữ dữ liệu từ 36 đến 120 tháng.");
}

/// <summary>RT-BR-06: chủ trọ đặt thời gian giữ / tắt ẩn danh tự động — thay đổi được audit (ai, lúc nào, giá trị cũ → mới).</summary>
public sealed class UpdateDataRetentionHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateDataRetentionCommand, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(UpdateDataRetentionCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        organization.UpdateDataRetention(request.RetentionMonths, request.AutoAnonymize);
        await db.SaveChangesAsync(cancellationToken);
        return DataRetentionMapping.ToDto(organization);
    }
}
