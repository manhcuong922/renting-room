using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record EndVehicleCommand(Guid ContractId, Guid VehicleId, DateOnly EndDate) : IRequest<Result>;

public sealed class EndVehicleHandler(IAppDbContext db) : IRequestHandler<EndVehicleCommand, Result>
{
    public ValueTask<Result> Handle(EndVehicleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.EndVehicle(request.VehicleId, request.EndDate)), cancellationToken));
}
