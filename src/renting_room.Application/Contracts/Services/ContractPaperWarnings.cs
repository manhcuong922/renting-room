using renting_room.Application.Identity.Organizations;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

public sealed class ContractPaperWarnings(
    OccupantChecks occupantChecks, IPropertyQueries properties, IOrganizationQueries organizations, IRenterQueries renters)
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public async Task<IReadOnlyList<Warning>> ForAsync(Contract contract, DateOnly today, CancellationToken ct)
    {
        if (!OpenStatuses.Contains(contract.Status))
            return [];

        var warnings = new List<Warning>();
        if (contract.Status == ContractStatus.Draft && await occupantChecks.FindRoleConflictAsync(contract, ct) is { } conflict)
            warnings.Add(new Warning(conflict.Code, conflict.Message));
        var property = (await properties.GetAsync(contract.PropertyId, ct))!;
        if ((await LessorSource.EffectiveAsync(organizations, property, ct)).Lessor?.IsComplete(today) != true)
            warnings.Add(new Warning("LESSOR_INFO_INCOMPLETE",
                "Chưa đủ thông tin bên cho thuê (thông tin chủ trọ) — chưa in được hợp đồng đầy đủ. Không ảnh hưởng thu tiền."));

        var representative = await renters.GetContactAsync(contract.RepresentativeRenterId, ct);
        if (representative is { IsAnonymized: false })
        {
            if (representative.Phone is null)
                warnings.Add(new Warning("REPRESENTATIVE_PHONE_MISSING", "Người đứng tên chưa có số điện thoại — khó liên hệ khi nhắc tiền."));
            var signedOn = contract.SignedDate ?? (contract.StartDate < today ? contract.StartDate : today);
            if (representative.DateOfBirth.AgeOn(signedOn) < LessorDetails.MinimumAge)
                warnings.Add(new Warning(ContractErrors.RepresentativeUnderage.Code,
                    "Người đứng tên chưa đủ 18 tuổi tại ngày ký — hợp đồng giấy cần người giám hộ ký thay (BLDS Điều 117)."));
        }

        var staying = contract.Occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate >= today).ToList();
        var stayingIds = staying.Select(o => o.RenterId).ToList();
        var missingId = await renters.ListMissingIdNumberAsync(stayingIds, ct);
        warnings.AddRange(missingId.Where(r => r.DateOfBirth.AgeOn(today) >= Renter.IdRequiredAge)
            .Select(r => new Warning("OCCUPANT_ID_MISSING", $"{r.FullName}: từ {Renter.IdRequiredAge} tuổi phải có số giấy tờ — bổ sung ở hồ sơ người thuê.")));
        var people = await occupantChecks.LoadPeopleAsync(staying.Select(o => o.RenterId).Append(contract.ReferenceRenterId), ct);
        if (people.TryGetValue(contract.ReferenceRenterId, out var reference))
        {
            var known = staying.Where(o => people.ContainsKey(o.RenterId)).ToList();
            warnings.AddRange(OccupantRelationshipRules.Check(reference.Facts, known.Select(o => (o.ToInput(), people[o.RenterId].Facts)).ToList())
                .Select(v => new Warning(v.Error.Code, $"{people[known[v.Index].RenterId].Name}: {v.Error.Message}")));
        }
        return warnings;
    }
}
