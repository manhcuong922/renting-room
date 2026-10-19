using renting_room.Application.Common.Models;
using renting_room.Application.Renters;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Queries;

internal static class RenterDtoProjection
{
    public static IQueryable<RenterDto> ToDto(this IQueryable<Renter> renters) =>
        renters.Select(r => new RenterDto(
            r.Id, r.FullName, r.DateOfBirth, r.Gender, r.Phone, r.Email, r.Nationality, r.IdType,
            r.IdNumberLast4 == null ? null : "********" + r.IdNumberLast4,
            r.IdIssueDate, r.IdIssuePlace, r.PermanentAddress, r.Occupation, r.Workplace,
            r.EmergencyContactName, r.EmergencyContactPhone, r.Note, r.CreatedAt, r.Version.ToString(), r.AnonymizedAt, r.ArchivedAt));
}
