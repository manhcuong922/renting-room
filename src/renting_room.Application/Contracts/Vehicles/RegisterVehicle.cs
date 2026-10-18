using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

// Xe gửi
public sealed record RegisterVehicleCommand(
    Guid ContractId, Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly? RegisteredFrom, string? Note)
    : IRequest<Result<CreatedWithWarnings>>;

public sealed class RegisterVehicleCommandValidator : AbstractValidator<RegisterVehicleCommand>
{
    public RegisterVehicleCommandValidator()
    {
        RuleFor(x => x.VehicleType).IsInEnum();
        RuleFor(x => x.PlateNumber).OptionalText(20);
        RuleFor(x => x.PlateNumber)
            .NotEmpty().When(x => x.VehicleType is VehicleType.Motorbike or VehicleType.Car)
            .WithErrorCode("REQUIRED").WithMessage("Xe máy / ô tô phải có biển số.");
        RuleFor(x => x.BrandColor).OptionalText(100);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>Một biển số chỉ đăng ký giữ ở 1 nơi trong tổ chức (unique index là chốt chặn khi gửi song song).</summary>
public sealed class RegisterVehicleHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<RegisterVehicleCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(RegisterVehicleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var plate = ContractVehicle.NormalizePlate(request.PlateNumber);
            if (plate is not null && await db.Contracts.SelectMany(c => c.Vehicles)
                    .AnyAsync(v => v.PlateNumber == plate && v.RegisteredTo == null, cancellationToken))
                return Result.Failure<CreatedWithWarnings>(ContractErrors.PlateAlreadyRegistered);
            if (request.RenterId is { } renterId && !contract.Occupants.Any(o => o.RenterId == renterId)
                && contract.RepresentativeRenterId != renterId)
                return Result.Failure<CreatedWithWarnings>(RenterErrors.RenterNotFound);

            var registered = contract.RegisterVehicle(new VehicleInput(
                request.RenterId, request.VehicleType, request.PlateNumber, request.BrandColor,
                request.RegisteredFrom ?? Max(contract.StartDate, clock.GetUtcNow().ToBusinessDate()), request.Note));
            if (registered.IsFailure)
                return Result.Failure<CreatedWithWarnings>(registered.Error!);

            var vehicle = registered.Value!;
            var warnings = new List<Warning>();
            if (vehicle.PlateNumber is { } normalized && !ContractWarnings.IsUsualPlate(normalized))
                warnings.Add(ContractWarnings.UnusualPlate(normalized));
            var parkingFees = await ContractFeeRules.LoadParkingFeesAsync(db, contract.PropertyId, cancellationToken);
            warnings.AddRange(ContractWarnings.For(contract, parkingFees, clock.GetUtcNow().ToBusinessDate())
                .Where(w => w.Code == "PARKING_QUANTITY_MISMATCH"));
            return Result.Success(new CreatedWithWarnings(vehicle.Id, warnings));
        }, cancellationToken));

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
