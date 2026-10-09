using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

/// <summary>
/// Phần giấy tờ / pháp lý của HĐ (chốt 09/10/2026): chỉ <b>cảnh báo</b>, không chặn tạo / kích hoạt / thu tiền — HĐ trong phần mềm là hồ sơ
/// thuê để tính tiền, bản có giá trị pháp lý là bản giấy. Gồm: bên cho thuê chưa đủ thông tin (chưa in được HĐ đầy đủ), người đứng tên thiếu
/// SĐT / chưa đủ 18 tuổi tại ngày ký, quan hệ người ở chưa khai / chưa hợp lý, người chưa thành niên thiếu đồng ý của người giám hộ
/// (CT-BR-18, 19, 28–30). "Chưa có bản HĐ ký" là cờ <see cref="ContractFlag.MissingSignedDocument"/>.
/// </summary>
internal static class ContractPaperWarnings
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public static async Task<IReadOnlyList<Warning>> ForAsync(IAppDbContext db, Contract contract, DateOnly today, CancellationToken ct)
    {
        if (!OpenStatuses.Contains(contract.Status))
            return [];

        var warnings = new List<Warning>();
        var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == contract.PropertyId, ct);
        if ((await LessorSource.EffectiveAsync(db, property, ct)).Lessor?.IsComplete(today) != true)
            warnings.Add(new Warning("LESSOR_INFO_INCOMPLETE",
                "Chưa đủ thông tin bên cho thuê (thông tin chủ trọ) — chưa in được hợp đồng đầy đủ. Không ảnh hưởng thu tiền."));

        var representative = await db.Renters.AsNoTracking().Where(r => r.Id == contract.RepresentativeRenterId)
            .Select(r => new { r.Phone, r.DateOfBirth, r.AnonymizedAt }).FirstOrDefaultAsync(ct);
        if (representative is { AnonymizedAt: null })
        {
            if (representative.Phone is null)
                warnings.Add(new Warning("REPRESENTATIVE_PHONE_MISSING", "Người đứng tên chưa có số điện thoại — khó liên hệ khi nhắc tiền."));
            var signedOn = contract.SignedDate ?? (contract.StartDate < today ? contract.StartDate : today);
            if (representative.DateOfBirth.AgeOn(signedOn) < LessorDetails.MinimumAge)
                warnings.Add(new Warning(ContractErrors.RepresentativeUnderage.Code,
                    "Người đứng tên chưa đủ 18 tuổi tại ngày ký — hợp đồng giấy cần người giám hộ ký thay (BLDS Điều 117)."));
        }

        var staying = contract.Occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate >= today).ToList();
        // RT-BR-01: người ở từ 14 tuổi chưa có giấy tờ (VD vừa qua 14 tuổi) ⇒ nhắc bổ sung.
        var stayingIds = staying.Select(o => o.RenterId).ToList();
        var missingId = await db.Renters.AsNoTracking()
            .Where(r => stayingIds.Contains(r.Id) && r.IdNumberHash == null && r.AnonymizedAt == null)
            .Select(r => new { r.FullName, r.DateOfBirth }).ToListAsync(ct);
        warnings.AddRange(missingId.Where(r => r.DateOfBirth.AgeOn(today) >= Renter.IdRequiredAge)
            .Select(r => new Warning("OCCUPANT_ID_MISSING", $"{r.FullName}: từ {Renter.IdRequiredAge} tuổi phải có số giấy tờ — bổ sung ở hồ sơ người thuê.")));
        var people = await OccupantChecks.LoadPeopleAsync(db, staying.Select(o => o.RenterId).Append(contract.ReferenceRenterId), ct);
        if (people.TryGetValue(contract.ReferenceRenterId, out var reference))
        {
            var known = staying.Where(o => people.ContainsKey(o.RenterId)).ToList();
            warnings.AddRange(OccupantRelationshipRules.Check(reference.Facts, known.Select(o => (o.ToInput(), people[o.RenterId].Facts)).ToList())
                .Select(v => new Warning(v.Error.Code, $"{people[known[v.Index].RenterId].Name}: {v.Error.Message}")));
        }
        return warnings;
    }
}
