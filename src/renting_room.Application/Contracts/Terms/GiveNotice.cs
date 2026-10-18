using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

// Phụ lục: đổi giá, gia hạn, báo trả phòng
public sealed record GiveNoticeCommand(Guid ContractId, DateOnly NoticeDate, DateOnly PlannedMoveOutDate) : IRequest<Result<NoticeResult>>;

public sealed class GiveNoticeHandler(IAppDbContext db) : IRequestHandler<GiveNoticeCommand, Result<NoticeResult>>
{
    public ValueTask<Result<NoticeResult>> Handle(GiveNoticeCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.GiveNotice(request.NoticeDate, request.PlannedMoveOutDate)), cancellationToken));
}
