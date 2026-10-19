using renting_room.Domain.Identity;

namespace renting_room.Requests;

public sealed record ListOrganizationsRequest(string? Search, OrganizationStatus? Status, int? Page, int? PageSize);

public sealed record SuspendOrganizationRequest(string Reason);
