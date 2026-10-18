using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>PR-BR-17: bên cho thuê hiệu lực của khu = bên cho thuê riêng của khu, không có thì lấy thông tin chủ trọ (tổ chức).</summary>
internal static class LessorSource
{
    public static async Task<(LessorDetails? Lessor, bool Inherited)> EffectiveAsync(IAppDbContext db, Property property, CancellationToken ct) =>
        property.Lessor is { } own ? (own, false) : (await OrganizationLessorAsync(db, property.OrganizationId, ct), true);

    public static Task<LessorDetails?> OrganizationLessorAsync(IAppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.DefaultLessor).FirstOrDefaultAsync(ct);
}
