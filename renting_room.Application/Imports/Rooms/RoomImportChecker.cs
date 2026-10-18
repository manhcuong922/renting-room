using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Meters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

/// <summary>Kiểm các dòng nhập phòng theo dữ liệu hiện tại — dùng chung cho xem trước và lưu (PR-UC-11).</summary>
internal sealed class RoomImportChecker(IAppDbContext db, IValidator<CreateRoomCommand> roomValidator, IValidator<InstallMeterCommand> meterValidator)
{
    public async Task<IReadOnlyList<ImportRowResult<RoomImportRow>>> CheckAsync(
        Property property, IReadOnlyList<RoomImportRow> rows, IReadOnlyDictionary<int, List<ImportIssue>> parseErrors, DateOnly meterDate,
        CancellationToken ct)
    {
        var existing = (await db.Rooms.Where(r => r.PropertyId == property.Id).Select(r => r.Code).ToListAsync(ct)).ToHashSet();
        var meterFees = await MeterFeesAsync(db, property.Id, ct);
        var duplicates = rows.Where(r => r.Code is not null).GroupBy(r => TextNormalizer.NormalizeCode(r.Code!))
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var results = new List<ImportRowResult<RoomImportRow>>();
        foreach (var row in rows)
        {
            var errors = new List<ImportIssue>(parseErrors.GetValueOrDefault(row.RowNumber) ?? []);
            var code = row.Code is null ? null : TextNormalizer.NormalizeCode(row.Code);
            if (code is null)
                errors.Add(new ImportIssue("REQUIRED", "Mã phòng là bắt buộc.", RoomImportTemplate.Headers["code"]));
            else
            {
                var check = await roomValidator.ValidateAsync(new CreateRoomCommand(property.Id, code, RoomImportTemplate.Spec(row)), ct);
                errors.AddRange(ImportIssues.From(check, RoomImportTemplate.PropertyToKey, RoomImportTemplate.Headers));
                if (duplicates.Contains(code))
                    errors.Add(new ImportIssue("DUPLICATE_IN_FILE", $"Mã phòng {code} lặp lại trong file.", RoomImportTemplate.Headers["code"]));
                else if (existing.Contains(code))
                    errors.Add(new ImportIssue("ROOM_CODE_TAKEN", $"Khu đã có phòng {code}.", RoomImportTemplate.Headers["code"]));
            }
            Meter(row.ElectricitySerial, row.ElectricityReading, "electricitySerial", "electricityReading", FeeSystemCodes.Electricity, "điện");
            Meter(row.WaterSerial, row.WaterReading, "waterSerial", "waterReading", FeeSystemCodes.Water, "nước");
            results.Add(new ImportRowResult<RoomImportRow>(row with { Code = code }, errors, []));

            void Meter(string? serial, decimal? reading, string serialKey, string readingKey, string systemCode, string label)
            {
                if (serial is not null && reading is null)
                    errors.Add(new ImportIssue("REQUIRED", $"Có số seri công tơ {label} thì phải có chỉ số đầu kỳ.", RoomImportTemplate.Headers[readingKey]));
                if (reading is null)
                    return;
                if (!meterFees.TryGetValue(systemCode, out var feeTypeId))
                {
                    errors.Add(new ImportIssue("FEE_NOT_FOUND", $"Khu chưa có khoản {label} theo công tơ.", RoomImportTemplate.Headers[readingKey]));
                    return;
                }
                foreach (var failure in meterValidator.Validate(new InstallMeterCommand(Guid.NewGuid(), feeTypeId, serial, meterDate, reading.Value, null)).Errors)
                    errors.Add(new ImportIssue(failure.ErrorCode, failure.ErrorMessage,
                        RoomImportTemplate.Headers[failure.PropertyName == nameof(InstallMeterCommand.SerialNo) ? serialKey : readingKey]));
            }
        }
        return results;
    }

    public static Task<Dictionary<string, Guid>> MeterFeesAsync(IAppDbContext db, Guid propertyId, CancellationToken ct) =>
        db.FeeTypes.Where(f => f.PropertyId == propertyId && f.SystemCode != null && f.ArchivedAt == null)
            .ToDictionaryAsync(f => f.SystemCode!, f => f.Id, ct);

    /// <summary>Công tơ khai qua import tính là lắp từ ngày chốt kỳ hiện tại của khu (chỉ số đầu kỳ chủ trọ vẫn ghi sổ).</summary>
    public static DateOnly MeterDate(Property property, DateOnly today) => property.BillingSchedule.StandardPeriodContaining(today).Start;
}
