using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>PR-BR-17: bên cho thuê hiệu lực của khu = bên cho thuê riêng của khu, không có thì lấy thông tin chủ trọ (tổ chức).</summary>
internal static class LessorSource
{
    public static async Task<(LessorDetails? Lessor, bool Inherited)> EffectiveAsync(
        IOrganizationQueries organizations, Property property, CancellationToken ct) =>
        property.Lessor is { } own ? (own, false) : (await organizations.GetDefaultLessorAsync(property.OrganizationId, ct), true);
}
