namespace renting_room.Domain.Renters;

public sealed record RenterProfile(
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? Phone,
    string? Email,
    string Nationality,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    string? PermanentAddress,
    string? Occupation,
    string? Workplace,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Note);
