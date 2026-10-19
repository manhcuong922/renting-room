using FluentValidation;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

// RT-UC-10: import người thuê đang ở — chỉ tạo mới khi chuyển từ sổ sang phần mềm
/// <param name="Errors">Lỗi của cả phòng (phòng không có / đang có HĐ, thiếu người đứng tên…); lỗi từng người nằm ở dòng.</param>
public sealed record TenancyRoomDto(
    string RoomCode, IReadOnlyList<int> RowNumbers, bool IsValid, DateOnly? StartDate, DateOnly? BillingStartDate,
    IReadOnlyList<ImportIssue> Errors, IReadOnlyList<ImportIssue> Warnings);

/// <summary>Kế hoạch tạo 1 lượt thuê — chỉ có khi cả phòng hợp lệ.</summary>
internal sealed record TenancyPlan(
    string RoomCode, Guid RoomId, IReadOnlyList<TenancyImportRow> Rows, DateOnly StartDate, DateOnly BillingStartDate, decimal MonthlyRent,
    decimal DepositAmount);

internal sealed record TenancyCheck(
    IReadOnlyList<ImportRowResult<TenancyImportRow>> Rows, IReadOnlyList<TenancyRoomDto> Rooms, IReadOnlyList<TenancyPlan> Plans);

/// <summary>Kiểm các dòng theo dữ liệu hiện tại — dùng chung cho xem trước và lưu (RT-UC-10).</summary>
internal sealed class TenancyImportChecker(
    IRenterQueries renters, IContractQueries contracts, IRoomQueries roomQueries, IMeterQueries meters, ICurrentUser currentUser,
    IPersonalDataProtector protector, IValidator<CreateRenterCommand> renterValidator)
{
    public async Task<TenancyCheck> CheckAsync(
        Property property, IReadOnlyList<TenancyImportRow> rows, IReadOnlyDictionary<int, List<ImportIssue>> parseErrors, DateOnly today,
        CancellationToken ct)
    {
        var errors = rows.ToDictionary(r => r.RowNumber, r => new List<ImportIssue>(parseErrors.GetValueOrDefault(r.RowNumber) ?? []));
        var warnings = rows.ToDictionary(r => r.RowNumber, _ => new List<ImportIssue>());
        void Error(TenancyImportRow r, string key, string code, string message) => errors[r.RowNumber].Add(new(code, message, TenancyImportTemplate.Headers[key]));
        void Warn(TenancyImportRow r, string? key, string code, string message) =>
            warnings[r.RowNumber].Add(new(code, message, key is null ? null : TenancyImportTemplate.Headers[key]));

        // RT-UC-10 (11/10/2026): phòng có nhiều dòng "Đứng tên" ⇒ dòng đầu là người đứng tên, các dòng sau là đồng thuê (M05 CT-BR-49).
        var mainRepresentatives = rows.Where(r => r.RoomCode is not null && TenancyImportTemplate.IsRepresentative(r.Role))
            .GroupBy(r => r.RoomCode!).Select(g => g.Min(r => r.RowNumber)).ToHashSet();
        bool IsMainRepresentative(TenancyImportRow r) =>
            TenancyImportTemplate.IsRepresentative(r.Role) && (r.RoomCode is null || mainRepresentatives.Contains(r.RowNumber));

        // 1. Từng dòng.
        foreach (var row in rows)
        {
            if (row.RoomCode is null)
                Error(row, "room", "REQUIRED", "Mã phòng là bắt buộc.");
            if (row.Role is null && !errors[row.RowNumber].Any(e => e.Column == TenancyImportTemplate.Headers["role"]))
                Error(row, "role", "REQUIRED", "Vai trò là bắt buộc.");
            var renterCheck = await renterValidator.ValidateAsync(new CreateRenterCommand(TenancyImportTemplate.Renter(row)), ct);
            errors[row.RowNumber].AddRange(ImportIssues.From(renterCheck, TenancyImportTemplate.RenterPropertyToKey, TenancyImportTemplate.Headers));
            if (TenancyImportTemplate.IsRepresentative(row.Role) && row.IdNumber is null)
                Error(row, "idNumber", "REPRESENTATIVE_ID_REQUIRED", "Người đứng tên phải có số giấy tờ.");
            if (row.MoveInDate is null)
                Error(row, "moveIn", "REQUIRED", "Ngày vào ở là bắt buộc.");
            else if (row.MoveInDate > today)
                Error(row, "moveIn", "START_DATE_IN_FUTURE", "Chỉ nhập người đang ở (ngày vào ở ≤ hôm nay) — người sắp vào thì tạo HĐ nháp ở màn HĐ.");
            else if (row.MoveInDate < today.AddYears(-10))
                Error(row, "moveIn", "INVALID_START_DATE", "Ngày vào ở không được trước hôm nay quá 10 năm.");
            if (IsMainRepresentative(row))
            {
                if (row.MonthlyRent is not > 0)
                    Error(row, "rent", "REQUIRED", "Dòng đứng tên phải có giá thuê > 0.");
                else if (row.MonthlyRent > 1_000_000_000)
                    Error(row, "rent", "OUT_OF_RANGE", "Giá thuê quá lớn.");
                if (row.DepositAmount < 0)
                    Error(row, "deposit", "OUT_OF_RANGE", "Tiền cọc không được âm.");
                else if (row.DepositAmount > row.MonthlyRent * ContractDraftBuilder.MaxDepositMonths)
                    Error(row, "deposit", "DEPOSIT_TOO_HIGH", $"Tiền cọc lớn hơn {ContractDraftBuilder.MaxDepositMonths} tháng tiền thuê.");
            }
            else if (row.MonthlyRent is not null || row.DepositAmount is not null)
                Warn(row, "rent", "IGNORED_FOR_OCCUPANT", "Giá thuê / tiền cọc chỉ đọc ở dòng đứng tên — dòng này bị bỏ qua.");
            if (row.Role == ImportRole.Occupant && row.Relationship is null)
                Warn(row, "relationship", "RELATIONSHIP_REQUIRED", "Chưa khai quan hệ với người đứng tên (khai sau trên HĐ được).");
        }

        // 2. Cùng một người trong file (theo số giấy tờ).
        foreach (var person in rows.Where(r => TenancyImportTemplate.PersonKey(r) is not null).GroupBy(r => TenancyImportTemplate.PersonKey(r)!))
        {
            var list = person.ToList();
            if (list.Select(r => TextNormalizer.ToSearchText(r.FullName ?? "")).Distinct().Count() > 1 || list.Select(r => r.DateOfBirth).Distinct().Count() > 1)
                list.ForEach(r => Error(r, "idNumber", "PERSON_MISMATCH", "Cùng số giấy tờ nhưng khác họ tên / ngày sinh ở các dòng."));
            if (list.Count(r => TenancyImportTemplate.IsLiving(r.Role)) > 1)
                list.Where(r => TenancyImportTemplate.IsLiving(r.Role)).ToList()
                    .ForEach(r => Error(r, "idNumber", "OCCUPANT_LIVES_ELSEWHERE",
                        "Một người chỉ ở 1 phòng — người thuê nhiều phòng dùng vai trò \"Đứng tên (không ở)\" ở các phòng còn lại."));
            foreach (var sameRoom in list.GroupBy(r => r.RoomCode).Where(g => g.Count() > 1))
                sameRoom.ToList().ForEach(r => Error(r, "idNumber", "DUPLICATE_IN_ROOM", "Một người xuất hiện 2 lần trong cùng phòng."));
        }

        // 3. So với dữ liệu đang có: hồ sơ cùng số giấy tờ, người đang ở HĐ khác.
        var organizationId = currentUser.OrganizationId!.Value;
        var hashes = rows.Where(r => r is { IdType: not null, IdNumber: not null })
            .ToDictionary(r => r.RowNumber, r => protector.ProtectIdNumber(organizationId, r.IdType!.Value, r.IdNumber!).Hash);
        var hashList = hashes.Values.Distinct().Select(h => (string?)h).ToList();
        var existing = await renters.ListByIdNumberHashesAsync(hashList, ct);
        var existingIds = existing.Select(e => e.Id).ToList();
        var stayingElsewhere = (await contracts.ListOpenStaysAsync(existingIds, today, ct))
            .GroupBy(x => x.RenterId).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in rows.Where(r => hashes.ContainsKey(r.RowNumber)))
        {
            var match = existing.FirstOrDefault(e => e.IdNumberHash == hashes[row.RowNumber]);
            if (match is null)
                continue;
            Warn(row, "idNumber", "RENTER_EXISTS", "Đã có hồ sơ cùng số giấy tờ — dùng lại hồ sơ đó, không sửa.");
            // RT-UC-10 (17/10/2026): khác họ tên / ngày sinh thường là gõ nhầm số giấy tờ ⇒ chặn, tránh gắn nhầm người vào HĐ.
            if (TextNormalizer.ToSearchText(match.FullName) != TextNormalizer.ToSearchText(row.FullName ?? "") || match.DateOfBirth != row.DateOfBirth)
                Error(row, "idNumber", "RENTER_IDENTITY_MISMATCH",
                    $"Số giấy tờ trùng hồ sơ {match.FullName} ({match.DateOfBirth:dd/MM/yyyy}) nhưng họ tên / ngày sinh trong file khác — kiểm tra lại số giấy tờ.");
            else if (row.Phone is not null && RenterProjection.NormalizePhone(row.Phone) != match.Phone)
                Warn(row, "phone", "RENTER_DATA_DIFFERS",
                    $"SĐT trong file khác hồ sơ đang có ({match.Phone ?? "không SĐT"}) — giữ hồ sơ, sửa ở màn người thuê nếu cần.");
            if (TenancyImportTemplate.IsLiving(row.Role) && stayingElsewhere.TryGetValue(match.Id, out var stay))
                Error(row, "idNumber", "OCCUPANT_LIVES_ELSEWHERE", $"Người này đang ở phòng {stay.RoomCode} (HĐ {stay.ContractNo}) — thanh lý / chuyển phòng ở màn HĐ trước.");
        }

        // 4. Theo phòng.
        var periodStart = property.BillingSchedule.StandardPeriodContaining(today).Start;
        var roomCodes = rows.Where(r => r.RoomCode is not null).Select(r => r.RoomCode!).Distinct().ToList();
        var roomsByCode = await roomQueries.ListByCodesAsync(property.Id, roomCodes, ct);
        var roomIds = roomsByCode.Values.Select(r => r.Id).ToList();
        var openContracts = (await contracts.ListOpenInRoomsAsync(roomIds, ct))
            .GroupBy(c => c.RoomId).ToDictionary(g => g.Key, g => g.First());
        // Phòng đã có HĐ khác ở tới ngày nào: HĐ đã kết thúc (ngày trả phòng) / đã chuyển đi (ngày chuyển — CT-BR-14). Bước kích hoạt chặn nếu
        // ngày đó ≥ ngày bắt đầu HĐ mới ⇒ xem trước phải báo cùng điều kiện.
        var occupiedUntil = (await contracts.ListOccupiedUntilAsync(roomIds, ct))
            .GroupBy(x => x.RoomId).ToDictionary(g => g.Key, g => g.Max(x => x.Until));
        var lastReadings = (await meters.ListLastReadingsAsync(roomIds, ct)).ToLookup(m => m.RoomId);

        var rooms = new List<TenancyRoomDto>();
        var plans = new List<TenancyPlan>();
        foreach (var group in rows.Where(r => r.RoomCode is not null).GroupBy(r => r.RoomCode!))
        {
            var members = group.ToList();
            var roomErrors = new List<ImportIssue>();
            var roomWarnings = new List<ImportIssue>();
            var room = roomsByCode.GetValueOrDefault(group.Key);
            if (room is null || room.ArchivedAt is not null)
                roomErrors.Add(new("ROOM_NOT_FOUND", $"Khu không có phòng {group.Key} (hoặc đã ngừng dùng) — import phòng trước."));
            else if (openContracts.TryGetValue(room.Id, out var open))
                roomErrors.Add(new("ROOM_HAS_CONTRACTS",
                    $"Phòng {group.Key} đang có HĐ {open.ContractNo} của {open.Name} ({open.Status}) — import chỉ cho phòng trống; thêm người / ký lại / thanh lý ở màn HĐ."));
            var representatives = members.Where(r => TenancyImportTemplate.IsRepresentative(r.Role)).OrderBy(r => r.RowNumber).ToList();
            if (representatives.Count == 0)
                roomErrors.Add(new("REPRESENTATIVE_COUNT", "Phòng chưa có người đứng tên."));
            else if (representatives.Skip(1).Any(r => r.Role != ImportRole.Representative))
                roomErrors.Add(new("REPRESENTATIVE_COUNT",
                    "Phòng có nhiều hơn 1 người đứng tên — người thứ hai trở đi được nhập là đồng thuê nên phải ở trong phòng (vai trò \"Đứng tên\")."));
            else
                foreach (var coTenant in representatives.Skip(1))
                    Warn(coTenant, "role", "IMPORTED_AS_CO_TENANT",
                        $"Phòng đã có người đứng tên ở dòng {representatives[0].RowNumber} — người này được nhập là đồng thuê (có tên trên hợp đồng).");
            var living = members.Where(r => TenancyImportTemplate.IsLiving(r.Role)).ToList();
            if (living.Count == 0)
                roomErrors.Add(new("NO_OCCUPANT", "Phòng không có ai ở (vai trò \"Đứng tên\" hoặc \"Ở cùng\")."));

            DateOnly? start = living.Select(r => r.MoveInDate).Where(d => d is not null).Min();
            if (room is not null && start is { } movedIn && occupiedUntil.TryGetValue(room.Id, out var until) && until >= movedIn)
                roomErrors.Add(new(ContractErrors.RoomPeriodOverlap.Code,
                    $"Phòng {group.Key} đã có hợp đồng khác ở tới {until:dd/MM/yyyy} — ngày vào ở phải sau ngày đó."));
            DateOnly? billingStart = start is { } s ? (s > periodStart ? s : periodStart) : null;
            if (room is not null && billingStart is { } from)
                // Chỉ số nhận phòng = số mới nhất; ghi trước ngày tính tiền ⇒ phiếu đầu tính cả điện nước trước đó (đã thu trên sổ cũ).
                foreach (var meter in lastReadings[room.Id].Where(m => m.Last is null || m.Last < from))
                    roomWarnings.Add(new("STALE_METER_READING",
                        $"Chỉ số công tơ {meter.SerialNo ?? ""} ghi lần cuối {meter.Last:dd/MM/yyyy}, trước ngày tính tiền {from:dd/MM/yyyy} — ghi lại chỉ số tại ngày tính tiền, nếu không phiếu đầu sẽ tính cả điện nước trước đó."));

            var isValid = roomErrors.Count == 0 && members.All(r => errors[r.RowNumber].Count == 0);
            rooms.Add(new TenancyRoomDto(group.Key, members.Select(r => r.RowNumber).ToList(), isValid, start, billingStart, roomErrors, roomWarnings));
            if (isValid)
            {
                var representative = representatives[0];
                plans.Add(new TenancyPlan(group.Key, room!.Id, members, start!.Value, billingStart!.Value, representative.MonthlyRent!.Value,
                    representative.DepositAmount ?? 0));
            }
        }

        var results = rows.Select(r => new ImportRowResult<TenancyImportRow>(r, errors[r.RowNumber], warnings[r.RowNumber])).ToList();
        return new TenancyCheck(results, rooms, plans);
    }
}
