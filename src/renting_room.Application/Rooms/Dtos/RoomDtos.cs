using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record RoomSpecInput(
    string? Floor,
    decimal? AreaM2,
    int? MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string>? Amenities,
    string? Description)
{
    public RoomSpec ToDomain() => new(Floor, AreaM2, MaxOccupants, ListedRent, DefaultDeposit, Amenities ?? [], Description);
}

public sealed record CurrentContractDto(
    Guid Id, string ContractNo, string RepresentativeName, DateOnly StartDate, DateOnly? EndDate, int OccupantCount,
    IReadOnlyList<ContractFlag>? Flags = null);

public sealed record RoomDto(
    Guid Id,
    Guid PropertyId,
    string PropertyCode,
    string Code,
    string? Floor,
    decimal? AreaM2,
    int? MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    string[] Amenities,
    string? Description,
    RoomDisplayStatus Status,
    string? MaintenanceNote,
    CurrentContractDto? CurrentContract,
    string Version,
    decimal OutstandingAmount = 0,
    decimal OverdueAmount = 0,
    decimal DepositHeld = 0)
{
    public bool IsOverdue => OverdueAmount > 0;
}

internal static class RoomSpecRules
{
    public static void SpecRules<T>(this AbstractValidator<T> v, Func<T, RoomSpecInput> spec, string prefix = "")
    {
        v.RuleFor(x => spec(x).Floor).OptionalText(10).OverridePropertyName($"{prefix}floor");
        v.RuleFor(x => spec(x).AreaM2)
            .Must(a => a is null || (a > 0 && a <= 1000 && decimal.Round(a.Value, 2) == a))
            .OverridePropertyName($"{prefix}areaM2").WithErrorCode("OUT_OF_RANGE").WithMessage("Diện tích từ 0 đến 1000 m², tối đa 2 số lẻ.");
        v.RuleFor(x => spec(x).MaxOccupants).InclusiveBetween(1, Room.MaxOccupantsLimit).When(x => spec(x).MaxOccupants is not null)
            .OverridePropertyName($"{prefix}maxOccupants").WithErrorCode("OUT_OF_RANGE");
        v.RuleFor(x => spec(x).ListedRent).OptionalMoney().OverridePropertyName($"{prefix}listedRent");
        v.RuleFor(x => spec(x).DefaultDeposit).OptionalMoney().OverridePropertyName($"{prefix}defaultDeposit");
        v.RuleFor(x => spec(x).Amenities)
            .Must(a => a is null || (a.Count <= 30 && a.All(t => System.Text.RegularExpressions.Regex.IsMatch(t, "^[a-z0-9_]{1,30}$"))))
            .OverridePropertyName($"{prefix}amenities").WithErrorCode("INVALID_FORMAT")
            .WithMessage("Tối đa 30 tiện ích, mỗi mã gồm chữ thường, số, '_' (VD: air_con).");
        v.RuleFor(x => spec(x).Description).OptionalText(2000).OverridePropertyName($"{prefix}description");
    }
}
