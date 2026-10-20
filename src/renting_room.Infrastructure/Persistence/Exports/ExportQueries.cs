using Microsoft.EntityFrameworkCore;
using renting_room.Application.Exports;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class ExportQueries(AppDbContext db) : IExportQueries
{
    private static readonly ContractStatus[] LivedStatuses = [ContractStatus.Active, ContractStatus.Liquidating, ContractStatus.Ended];

    public Task<int> CountRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Renters(request, from, to).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<RenterExportRow>> ListRentersAsync(
        ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await Renters(request, from, to).ToListAsync(cancellationToken);

    private IQueryable<RenterExportRow> Renters(ExportRentersQuery request, DateOnly fromDate, DateOnly toDate)
    {
        var query =
            from c in db.Contracts
            where LivedStatuses.Contains(c.Status)
            from o in c.Occupants
            // Đang thanh lý: ngày chuyển đi chỉ ghi khi hoàn tất ⇒ dùng ngày trả phòng của HĐ.
            let moveOut = o.MoveOutDate ?? c.ActualEndDate
            where o.MoveInDate <= toDate && (moveOut == null || moveOut >= fromDate)
            join r in db.Renters on o.RenterId equals r.Id
            join room in db.Rooms on c.RoomId equals room.Id
            join p in db.Properties on c.PropertyId equals p.Id
            select new RenterExportRow
            {
                PropertyId = p.Id, PropertyCode = p.Code, PropertyName = p.Name,
                RoomId = room.Id, RoomCode = room.Code, Floor = room.Floor,
                ContractNo = c.ContractNo, ContractStatus = c.Status, IsRepresentative = c.RepresentativeRenterId == r.Id,
                IsCoTenant = o.Role == OccupantRole.CoTenant,
                IsHouseholdHead = (c.HouseholdHeadRenterId ?? c.RepresentativeRenterId) == r.Id,
                FullName = r.FullName, DateOfBirth = r.DateOfBirth, Gender = r.Gender, Phone = r.Phone,
                IdType = r.IdType, IdNumberLast4 = r.IdNumberLast4, IdNumberEncrypted = r.IdNumberEncrypted,
                IdIssueDate = r.IdIssueDate, IdIssuePlace = r.IdIssuePlace, Nationality = r.Nationality,
                PermanentAddress = r.PermanentAddress, Occupation = r.Occupation, Workplace = r.Workplace,
                Relationship = o.Relationship, RelationshipType = o.RelationshipType, GuardianConsent = o.GuardianConsent,
                MoveInDate = o.MoveInDate, MoveOutDate = moveOut,
                EmergencyContactName = r.EmergencyContactName, EmergencyContactPhone = r.EmergencyContactPhone
            };

        if (request.PropertyIds is { Count: > 0 } propertyIds)
            query = query.Where(x => propertyIds.Contains(x.PropertyId));
        if (request.RoomIds is { Count: > 0 } roomIds)
            query = query.Where(x => roomIds.Contains(x.RoomId));
        if (request.Floors is { Count: > 0 } floors)
        {
            var normalized = floors.Select(f => f.Trim()).ToList();
            query = query.Where(x => x.Floor != null && normalized.Contains(x.Floor));
        }
        if (request.RoomGroupIds is { Count: > 0 } groupIds)
            query = query.Where(x => db.RoomGroups.Any(g => groupIds.Contains(g.Id) && g.Members.Any(m => m.RoomId == x.RoomId)));

        return query;
    }
}
