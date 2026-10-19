using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record EndOccupancyCommand(Guid ContractId, Guid OccupantId, DateOnly MoveOutDate) : IRequest<Result>;

public sealed class EndOccupancyHandler(ContractMutation mutation) : IRequestHandler<EndOccupancyCommand, Result>
{
    public ValueTask<Result> Handle(EndOccupancyCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.EndOccupancy(request.OccupantId, request.MoveOutDate)), cancellationToken));
}
