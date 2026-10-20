namespace renting_room.Application.Properties;

public sealed record OrganizationLessorDto(LessorDto? Lessor, LessorPrefillDto Prefill);

public sealed record LessorPrefillDto(string? Name, string? Phone, string? Address);
