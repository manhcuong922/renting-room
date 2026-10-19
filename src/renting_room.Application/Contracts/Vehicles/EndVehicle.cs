using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record EndVehicleCommand(Guid ContractId, Guid VehicleId, DateOnly EndDate) : IRequest<Result>;

public sealed class EndVehicleHandler(ContractMutation mutation) : IRequestHandler<EndVehicleCommand, Result>
{
    public ValueTask<Result> Handle(EndVehicleCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.EndVehicle(request.VehicleId, request.EndDate)), cancellationToken));
}
