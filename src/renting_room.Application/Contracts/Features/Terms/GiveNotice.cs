using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record GiveNoticeCommand(Guid ContractId, DateOnly NoticeDate, DateOnly PlannedMoveOutDate) : IRequest<Result<NoticeResult>>;

public sealed class GiveNoticeHandler(ContractMutation mutation) : IRequestHandler<GiveNoticeCommand, Result<NoticeResult>>
{
    public ValueTask<Result<NoticeResult>> Handle(GiveNoticeCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.GiveNotice(request.NoticeDate, request.PlannedMoveOutDate)), cancellationToken));
}
