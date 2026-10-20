using renting_room.Application.Identity.Organizations;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

internal static class LessorSource
{
    public static async Task<(LessorDetails? Lessor, bool Inherited)> EffectiveAsync(
        IOrganizationQueries organizations, Property property, CancellationToken ct) =>
        property.Lessor is { } own ? (own, false) : (await organizations.GetDefaultLessorAsync(property.OrganizationId, ct), true);
}
