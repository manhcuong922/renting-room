using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

/// <summary>
/// Chủ trọ sửa giá niêm yết của phòng rồi chọn "áp cho người đang thuê": HĐ đang hiệu lực của phòng đổi giá từ kỳ chưa chốt đầu tiên
/// (lịch sử giữ nguyên). Không tự đổi khi sửa giá phòng — giá thỏa thuận có thể khác giá niêm yết.
/// </summary>
public sealed record ApplyListedRentCommand(Guid RoomId) : IRequest<Result>;

public sealed class ApplyListedRentHandler(IAppDbContext db, ISender sender, TimeProvider clock) : IRequestHandler<ApplyListedRentCommand, Result>
{
    public async ValueTask<Result> Handle(ApplyListedRentCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().Where(r => r.Id == request.RoomId).Select(r => new { r.ListedRent }).FirstOrDefaultAsync(cancellationToken);
        if (room is null)
            return Result.Failure(PropertyErrors.RoomNotFound);
        if (room.ListedRent is not { } rent || rent <= 0)
            return Result.Failure(Error.Validation("LISTED_RENT_REQUIRED", "Phòng chưa có giá niêm yết."));

        var today = clock.GetUtcNow().ToBusinessDate();
        var contractId = await db.Contracts.Where(c => c.RoomId == request.RoomId && c.Status == ContractStatus.Active && c.StartDate <= today)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        if (contractId is null)
            return Result.Failure(ContractErrors.NotActive);
        return await sender.Send(new ChangeRentCommand(contractId.Value, null, rent, null, "Áp giá niêm yết mới của phòng"), cancellationToken);
    }
}
