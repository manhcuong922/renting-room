namespace renting_room.Requests;

public sealed record UpdateOrganizationInfoRequest(
    string Name, string? ContactName, string? ContactPhone, string? ContactEmail, string? TaxCode, string? Address);
