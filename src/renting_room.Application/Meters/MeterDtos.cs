using FluentValidation;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record MeterReadingDto(
    Guid Id, ReadingKind Kind, DateOnly ReadingDate, decimal Value, Guid? ContractId, string? Note, string Version);

/// <param name="LatestReading">Chỉ số mới nhất — UI dùng cho nút "Dùng số mới nhất" (MT-BR-13).</param>
public sealed record MeterDto(
    Guid Id,
    Guid RoomId,
    Guid FeeTypeId,
    string FeeTypeName,
    string Unit,
    string? SerialNo,
    DateOnly InstalledDate,
    DateOnly? RemovedDate,
    Guid? ReplacedByMeterId,
    bool IsActive,
    MeterReadingDto? LatestReading,
    string? Note,
    string Version);

internal static class MeterMapping
{
    public static MeterReadingDto ToDto(this MeterReading r) =>
        new(r.Id, r.Kind, r.ReadingDate, r.Value, r.ContractId, r.Note, r.Version.ToString());

    public static MeterDto ToDto(this Meter m, FeeType? fee) =>
        new(m.Id, m.RoomId, m.FeeTypeId, fee?.Name ?? string.Empty, fee?.Unit ?? string.Empty, m.SerialNo, m.InstalledDate, m.RemovedDate,
            m.ReplacedByMeterId, m.IsActive, m.Latest?.ToDto(), m.Note, m.Version.ToString());

    public static bool IsValidValue(decimal value) => value >= 0 && value <= Meter.MaxValue && decimal.Round(value, 2) == value;

    public static IRuleBuilderOptions<T, decimal> ReadingValue<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(IsValidValue).WithErrorCode("INVALID_READING").WithMessage("Chỉ số 0–99.999.999,99, tối đa 2 số lẻ.");

    public static IRuleBuilderOptions<T, DateOnly> ReadingDate<T>(this IRuleBuilder<T, DateOnly> rule, TimeProvider clock) =>
        rule.Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddDays(1))
            .WithErrorCode("INVALID_READING_DATE").WithMessage("Ngày ghi chỉ số không được quá hôm nay + 1.");
}
